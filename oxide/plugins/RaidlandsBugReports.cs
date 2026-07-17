using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Raidlands Bug Reports", "Raidlands", "1.0.2")]
    [Description("Lets players submit /bug reports locally, to the Raidlands website, and optionally to Discord.")]
    public class RaidlandsBugReports : RustPlugin
    {
        private PluginConfig _config;
        private Dictionary<string, string> _secrets;
        private const string SecretsConfigName = "Secrets.local";
        private const string AdminPermission = "raidlandsbugreports.admin";
        private const string AdminUiName = "RaidlandsBugReports.Admin";

        private class PluginConfig
        {
            [JsonProperty("Command name")]
            public string CommandName = "bug";

            [JsonProperty("Minimum report characters")]
            public int MinimumReportCharacters = 8;

            [JsonProperty("Maximum report characters")]
            public int MaximumReportCharacters = 1200;

            [JsonProperty("Save local reports")]
            public bool SaveLocalReports = true;

            [JsonProperty("Website API enabled")]
            public bool WebsiteApiEnabled = true;

            [JsonProperty("Website API base URL")]
            public string WebsiteApiBaseUrl = "https://raidlands.net";

            [JsonProperty("Website API bug report path")]
            public string WebsiteApiBugReportPath = "/api/server/bug-report.php";

            [JsonProperty("Website API server id")]
            public string WebsiteApiServerId = "raidlands-main";

            [JsonProperty("Website API shared secret")]
            public string WebsiteApiSharedSecret = "${WEBSITE_VIP_SHARED_SECRET}";

            [JsonProperty("Website API timeout seconds")]
            public float WebsiteApiTimeoutSeconds = 15f;

            [JsonProperty("Website API retry attempts")]
            public int WebsiteApiRetryAttempts = 2;

            [JsonProperty("Retry delay seconds")]
            public float RetryDelaySeconds = 5f;

            [JsonProperty("Discord webhook enabled")]
            public bool DiscordWebhookEnabled = false;

            [JsonProperty("Discord webhook URL")]
            public string DiscordWebhookUrl = "${DISCORD_DEFAULT_WEBHOOK_URL}";

            [JsonProperty("Discord username")]
            public string DiscordUsername = "Raidlands Bug Reports";

            [JsonProperty("Discord avatar URL")]
            public string DiscordAvatarUrl = "";

            [JsonProperty("Discord mention text (optional)")]
            public string DiscordMentionText = "";

            [JsonProperty("Admin command name")]
            public string AdminCommandName = "bugs";

            [JsonProperty("Admin list size")]
            public int AdminListSize = 10;
        }

        private class BugReport
        {
            public string IncidentId;
            public DateTime SubmittedAtUtc;
            public string Summary;
            public string Details;
            public PlayerInfo Player;
            public ServerInfo Server;
            public string LocalDataFile;
            public bool WebsiteQueued;
            public bool DiscordQueued;
            public string Status;
        }

        private class PlayerInfo
        {
            public string SteamId64;
            public string DisplayName;
            public string Position;
            public bool Online;
        }

        private class ServerInfo
        {
            public string ServerId;
            public string Name;
            public string Address;
            public int Seed;
            public int WorldSize;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            PrintWarning("Creating a new configuration file.");
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null) throw new Exception("Configuration deserialized to null.");
            }
            catch (Exception ex)
            {
                PrintError($"Invalid configuration: {ex.Message}");
                LoadDefaultConfig();
            }

            NormalizeConfig();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private void NormalizeConfig()
        {
            _config.CommandName = SafeCommandName(_config.CommandName, "bug");
            _config.MinimumReportCharacters = Mathf.Clamp(_config.MinimumReportCharacters, 4, 200);
            _config.MaximumReportCharacters = Mathf.Clamp(_config.MaximumReportCharacters, _config.MinimumReportCharacters, 3000);
            _config.WebsiteApiTimeoutSeconds = Mathf.Clamp(_config.WebsiteApiTimeoutSeconds, 3f, 60f);
            _config.WebsiteApiRetryAttempts = Mathf.Clamp(_config.WebsiteApiRetryAttempts, 0, 10);
            _config.RetryDelaySeconds = Mathf.Clamp(_config.RetryDelaySeconds, 1f, 60f);
            _config.AdminCommandName = SafeCommandName(_config.AdminCommandName, "bugs");
            _config.AdminListSize = Mathf.Clamp(_config.AdminListSize, 3, 25);
        }

        private void OnServerInitialized()
        {
            cmd.AddChatCommand(_config.CommandName, this, nameof(BugCommand));
            cmd.AddChatCommand(_config.AdminCommandName, this, nameof(AdminBugsCommand));

            if (_config.WebsiteApiEnabled && string.IsNullOrWhiteSpace(ResolveSecretValue(_config.WebsiteApiSharedSecret)))
                PrintWarning("Website API is enabled, but the shared secret is not configured.");

            if (_config.DiscordWebhookEnabled && !IsDiscordWebhookConfigured())
                PrintWarning("Discord webhook is enabled, but the webhook URL is not configured.");

            Puts($"Loaded. Players can submit reports with /{_config.CommandName}; admins can open the local report list with /{_config.AdminCommandName}.");
        }

        private void Init()
        {
            // Register before the server finishes initializing so groups can be granted
            // access immediately after a plugin reload.
            permission.RegisterPermission(AdminPermission, this);
        }

        private void AdminBugsCommand(BasePlayer player, string command, string[] args)
        {
            if (!CanUseAdminCommand(player))
            {
                Reply(player, "You do not have permission to view bug reports.");
                return;
            }

            string subcommand = (args != null && args.Length > 0 ? args[0] : "list").ToLowerInvariant();
            if (subcommand == "close")
            {
                CloseAdminUi(player);
                return;
            }

            ShowAdminUi(player, 0);
        }

        [ConsoleCommand("raidbugs.ui")]
        private void AdminUiCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !CanUseAdminCommand(player))
                return;

            string action = arg.GetString(0, "open").ToLowerInvariant();
            if (action == "close")
            {
                CloseAdminUi(player);
                return;
            }

            if (action == "refresh")
            {
                ShowAdminUi(player, 0);
                return;
            }

            if (action == "select")
            {
                ShowAdminUi(player, Mathf.Max(0, arg.GetInt(1, 0)), arg.GetString(2, "submitted"));
                return;
            }

            if (action == "tab")
            {
                ShowAdminUi(player, 0, arg.GetString(1, "submitted"));
                return;
            }

            if (action == "status")
            {
                SetReportStatus(player, arg.GetString(1, ""), arg.GetString(2, ""));
                return;
            }

            if (action == "delete")
            {
                DeleteReport(player, arg.GetString(1, ""), arg.GetString(2, "submitted"));
                return;
            }

            ShowAdminUi(player, 0);
        }

        private void BugCommand(BasePlayer player, string command, string[] args)
        {
            string details = SafeText(string.Join(" ", args ?? new string[0]), _config.MaximumReportCharacters);
            if (details.Length < _config.MinimumReportCharacters)
            {
                Reply(player, $"Use /{_config.CommandName} <what happened>. Please include at least {_config.MinimumReportCharacters} characters.");
                return;
            }

            BugReport report = BuildReport(player, details);

            if (_config.SaveLocalReports)
                SaveReport(report);

            SendWebsite(report, 0);
            SendDiscord(report, 0);

            Reply(player, $"Thanks. Your bug report was saved for staff review. ID: {report.IncidentId}");
            Puts($"Captured /bug report {report.IncidentId}: {report.Player.DisplayName} submitted `{report.Summary}`. Website enabled={_config.WebsiteApiEnabled}; Discord enabled={_config.DiscordWebhookEnabled}.");
        }

        [ConsoleCommand("raidbugs.test")]
        private void TestCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && arg.Connection.authLevel < 2) return;
            BasePlayer player = arg.Player() ?? BasePlayer.activePlayerList.FirstOrDefault();
            BugReport report = BuildReport(player, "This is a generated test bug report from raidbugs.test.");

            if (_config.SaveLocalReports)
                SaveReport(report);

            SendWebsite(report, 0);
            SendDiscord(report, 0);
            Puts($"Generated test bug report {report.IncidentId}.");
        }

        private BugReport BuildReport(BasePlayer player, string details)
        {
            DateTime now = DateTime.UtcNow;
            string steamId = player?.UserIDString ?? "server";
            string incidentId = $"{now:yyyyMMdd-HHmmss}-{steamId}";

            return new BugReport
            {
                IncidentId = incidentId,
                SubmittedAtUtc = now,
                Summary = BuildSummary(details),
                Details = SafeText(details, _config.MaximumReportCharacters),
                Player = new PlayerInfo
                {
                    SteamId64 = player?.UserIDString ?? "",
                    DisplayName = SafeName(player?.displayName ?? "Server Console"),
                    Position = player != null ? FormatPosition(player.transform.position) : "unknown",
                    Online = player != null && player.IsConnected
                },
                Server = new ServerInfo
                {
                    ServerId = _config.WebsiteApiServerId,
                    Name = ConVar.Server.hostname,
                    Address = $"{ConVar.Server.ip}:{ConVar.Server.port}",
                    Seed = ConVar.Server.seed,
                    WorldSize = ConVar.Server.worldsize
                },
                LocalDataFile = $"RaidlandsBugReports/reports/{incidentId}.json",
                WebsiteQueued = _config.WebsiteApiEnabled,
                DiscordQueued = _config.DiscordWebhookEnabled,
                Status = "Submitted"
            };
        }

        private void SaveReport(BugReport report)
        {
            try
            {
                Interface.Oxide.DataFileSystem.WriteObject($"RaidlandsBugReports/reports/{report.IncidentId}", report, true);
            }
            catch (Exception ex)
            {
                PrintError($"Could not save bug report {report.IncidentId}: {ex.Message}");
            }
        }

        private void ShowAdminUi(BasePlayer player, int selectedIndex, string requestedStatusFilter = "submitted")
        {
            string statusFilter = NormalizeStatusFilter(requestedStatusFilter);
            var allReports = LoadRecentReports(int.MaxValue);
            var filteredReports = allReports.Where(report => StatusMatchesFilter(report, statusFilter)).ToList();
            var reports = filteredReports.Take(_config.AdminListSize).ToList();
            selectedIndex = reports.Count == 0 ? -1 : Mathf.Clamp(selectedIndex, 0, reports.Count - 1);
            BugReport selected = selectedIndex >= 0 ? reports[selectedIndex] : null;

            CuiHelper.DestroyUi(player, AdminUiName);

            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel
            {
                Image = { Color = "0.035 0.040 0.048 0.96", Material = "assets/content/ui/uibackgroundblur.mat" },
                RectTransform = { AnchorMin = "0.13 0.12", AnchorMax = "0.87 0.88" },
                CursorEnabled = true
            }, "Overlay", AdminUiName);

            AddUiPanel(container, root, "0.000 0.000", "1.000 1.000", "0.035 0.040 0.048 0.96");
            AddUiLabel(container, root, "Raidlands Bug Reports", 18, TextAnchor.MiddleLeft, "0.035 0.925", "0.500 0.982", "1 0.86 0.58 1");
            AddUiLabel(container, root, $"{allReports.Count} local reports", 10, TextAnchor.MiddleLeft, "0.035 0.885", "0.300 0.925", "0.72 0.80 0.86 1");
            AddUiButton(container, root, "Refresh", "raidbugs.ui refresh", "0.760 0.925", "0.865 0.972", "0.12 0.18 0.24 0.95", 10);
            AddUiButton(container, root, "Close", "raidbugs.ui close", "0.875 0.925", "0.965 0.972", "0.40 0.15 0.12 0.95", 10);

            string list = container.Add(new CuiPanel
            {
                Image = { Color = "0.055 0.064 0.078 0.95" },
                RectTransform = { AnchorMin = "0.030 0.065", AnchorMax = "0.375 0.865" }
            }, root);

            string detail = container.Add(new CuiPanel
            {
                Image = { Color = "0.060 0.069 0.083 0.95" },
                RectTransform = { AnchorMin = "0.395 0.065", AnchorMax = "0.970 0.865" }
            }, root);

            AddStatusTab(container, list, "Submitted", "submitted", statusFilter, CountReportsByStatus(allReports, "submitted"), "0.035 0.845", "0.258 0.905");
            AddStatusTab(container, list, "Investigating", "investigating", statusFilter, CountReportsByStatus(allReports, "investigating"), "0.267 0.845", "0.490 0.905");
            AddStatusTab(container, list, "Development", "development", statusFilter, CountReportsByStatus(allReports, "development"), "0.500 0.845", "0.723 0.905");
            AddStatusTab(container, list, "Fixed", "fixed", statusFilter, CountReportsByStatus(allReports, "fixed"), "0.732 0.845", "0.965 0.905");

            if (reports.Count == 0)
            {
                AddUiLabel(container, list, allReports.Count == 0 ? "No local bug reports are saved yet." : $"No {StatusFilterLabel(statusFilter).ToLowerInvariant()} reports.", 12, TextAnchor.MiddleCenter, "0.06 0.45", "0.94 0.55", "0.78 0.84 0.88 1");
                AddUiLabel(container, detail, allReports.Count == 0 ? "Submit a test report with /bug <description>." : "Choose another status tab to review its reports.", 13, TextAnchor.MiddleCenter, "0.08 0.45", "0.92 0.55", "0.78 0.84 0.88 1");
                CuiHelper.AddUi(player, container);
                return;
            }

            AddUiLabel(container, list, $"{StatusFilterLabel(statusFilter)} Reports", 13, TextAnchor.MiddleLeft, "0.05 0.925", "0.85 0.980", "1 0.86 0.58 1");
            for (int i = 0; i < reports.Count; i++)
            {
                BugReport report = reports[i];
                float top = 0.815f - (i * 0.068f);
                float bottom = top - 0.058f;
                if (bottom < 0.035f)
                    break;

                string color = i == selectedIndex ? "0.18 0.13 0.10 0.98" : "0.075 0.086 0.102 0.95";
                // Keep the row text inside the button. Sibling labels can intercept CUI clicks,
                // leaving only the uncovered edge of a report row selectable.
                string rowButton = AddUiButton(container, list, "", $"raidbugs.ui select {i} {statusFilter}", $"0.035 {FormatUiFloat(bottom)}", $"0.965 {FormatUiFloat(top)}", color, 9);
                AddUiLabel(container, rowButton, SafeText(report.Summary, 44), 9, TextAnchor.MiddleLeft, "0.025 0.500", "0.975 0.985", "0.92 0.96 1 1");
                AddUiLabel(container, rowButton, $"{report.Player?.DisplayName ?? "Unknown"}  |  {report.SubmittedAtUtc:MM-dd HH:mm} UTC", 8, TextAnchor.MiddleLeft, "0.025 0.015", "0.975 0.500", "0.58 0.66 0.72 1");
            }

            if (selected == null)
            {
                AddUiLabel(container, detail, "Select a report from the list.", 13, TextAnchor.MiddleCenter, "0.08 0.45", "0.92 0.55", "0.78 0.84 0.88 1");
                CuiHelper.AddUi(player, container);
                return;
            }

            AddUiLabel(container, detail, "Bug Content", 14, TextAnchor.MiddleLeft, "0.040 0.925", "0.500 0.980", "1 0.86 0.58 1");
            AddUiLabel(container, detail, selected.Summary, 13, TextAnchor.MiddleLeft, "0.040 0.865", "0.960 0.925", "0.92 0.96 1 1");
            AddUiLabel(container, detail, $"Player: {selected.Player?.DisplayName ?? "Unknown"} ({selected.Player?.SteamId64 ?? "unknown"})", 10, TextAnchor.MiddleLeft, "0.040 0.815", "0.960 0.858", "0.70 0.78 0.84 1");
            AddUiLabel(container, detail, $"Time: {selected.SubmittedAtUtc:yyyy-MM-dd HH:mm:ss} UTC    Position: {selected.Player?.Position ?? "unknown"}", 10, TextAnchor.MiddleLeft, "0.040 0.775", "0.960 0.815", "0.70 0.78 0.84 1");
            AddUiLabel(container, detail, $"Status: {NormalizeStatus(selected.Status)}    Website queued: {selected.WebsiteQueued}    Discord queued: {selected.DiscordQueued}", 10, TextAnchor.MiddleLeft, "0.040 0.735", "0.960 0.775", "0.70 0.78 0.84 1");

            string incidentId = SafeFileName(selected.IncidentId);
            AddUiButton(container, detail, "Investigating", $"raidbugs.ui status investigating {incidentId}", "0.040 0.675", "0.235 0.715", "0.20 0.30 0.42 0.95", 9);
            AddUiButton(container, detail, "In Development", $"raidbugs.ui status development {incidentId}", "0.245 0.675", "0.440 0.715", "0.28 0.22 0.42 0.95", 9);
            AddUiButton(container, detail, "Fixed", $"raidbugs.ui status fixed {incidentId}", "0.450 0.675", "0.645 0.715", "0.16 0.38 0.24 0.95", 9);
            AddUiButton(container, detail, "Delete", $"raidbugs.ui delete {incidentId}", "0.765 0.675", "0.960 0.715", "0.42 0.15 0.12 0.95", 9);

            AddUiPanel(container, detail, "0.040 0.090", "0.960 0.655", "0.032 0.038 0.048 0.95");
            AddUiLabel(container, detail, WrapForUi(selected.Details, 92, 16), 10, TextAnchor.UpperLeft, "0.060 0.110", "0.940 0.630", "0.88 0.92 0.96 1");

            CuiHelper.AddUi(player, container);
        }

        private void CloseAdminUi(BasePlayer player)
        {
            if (player != null)
                CuiHelper.DestroyUi(player, AdminUiName);
        }

        private List<BugReport> LoadRecentReports(int limit)
        {
            var results = new List<BugReport>();
            string folder = Path.Combine(Interface.Oxide.DataDirectory, "RaidlandsBugReports", "reports");
            if (!Directory.Exists(folder))
                return results;

            foreach (string file in Directory.GetFiles(folder, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(limit))
            {
                try
                {
                    BugReport report = JsonConvert.DeserializeObject<BugReport>(File.ReadAllText(file));
                    if (report != null)
                        results.Add(report);
                }
                catch (Exception ex)
                {
                    PrintWarning($"Could not read bug report {Path.GetFileNameWithoutExtension(file)}: {ex.Message}");
                }
            }

            return results;
        }

        private BugReport LoadReport(string incidentId)
        {
            string safeId = SafeFileName(incidentId);
            if (string.IsNullOrWhiteSpace(safeId))
                return null;

            string path = Path.Combine(Interface.Oxide.DataDirectory, "RaidlandsBugReports", "reports", safeId + ".json");
            if (!File.Exists(path))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<BugReport>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not read bug report {safeId}: {ex.Message}");
                return null;
            }
        }

        private void SetReportStatus(BasePlayer player, string requestedStatus, string incidentId)
        {
            BugReport report = LoadReport(incidentId);
            if (report == null)
            {
                Reply(player, "That bug report no longer exists. Refreshing the list.");
                ShowAdminUi(player, 0);
                return;
            }

            report.Status = NormalizeStatus(requestedStatus);
            SaveReport(report);
            Reply(player, $"Bug report {report.IncidentId} marked {report.Status}.");
            ShowAdminUi(player, 0, NormalizeStatusFilter(requestedStatus));
        }

        private void DeleteReport(BasePlayer player, string incidentId, string statusFilter)
        {
            string safeId = SafeFileName(incidentId);
            string path = Path.Combine(Interface.Oxide.DataDirectory, "RaidlandsBugReports", "reports", safeId + ".json");
            if (string.IsNullOrWhiteSpace(safeId) || !File.Exists(path))
            {
                Reply(player, "That bug report no longer exists. Refreshing the list.");
                ShowAdminUi(player, 0);
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not delete bug report {safeId}: {ex.Message}");
                Reply(player, "Could not delete that bug report. Check the server console.");
                return;
            }

            Reply(player, $"Deleted local bug report {safeId}.");
            ShowAdminUi(player, 0, statusFilter);
        }

        private void SendWebsite(BugReport report, int attempt)
        {
            if (!_config.WebsiteApiEnabled)
                return;

            string secret = ResolveSecretValue(_config.WebsiteApiSharedSecret);
            if (string.IsNullOrWhiteSpace(secret))
            {
                PrintWarning($"Bug report {report.IncidentId} was saved locally but not sent to website: shared secret is not configured.");
                return;
            }

            string url = BuildWebsiteUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                PrintWarning($"Bug report {report.IncidentId} was saved locally but not sent to website: API URL is invalid.");
                return;
            }

            string body = BuildWebsitePayload(report);
            var headers = BuildWebsiteHeaders("POST", url, body, secret);
            headers["Content-Type"] = "application/json";

            webrequest.Enqueue(url, body, (code, response) =>
            {
                if (code >= 200 && code < 300)
                {
                    Puts($"Sent bug report {report.IncidentId} to website.");
                    return;
                }

                if (attempt < _config.WebsiteApiRetryAttempts)
                {
                    int nextAttempt = attempt + 1;
                    PrintWarning($"Website API returned HTTP {code} for bug report {report.IncidentId}; retry {nextAttempt}/{_config.WebsiteApiRetryAttempts}.");
                    timer.Once(_config.RetryDelaySeconds * nextAttempt, () => SendWebsite(report, nextAttempt));
                    return;
                }

                PrintWarning($"Website delivery failed for bug report {report.IncidentId}. HTTP {code}: {SafeText(response, 500)}");
            }, this, RequestMethod.POST, headers, _config.WebsiteApiTimeoutSeconds);
        }

        private string BuildWebsitePayload(BugReport report)
        {
            var payload = new
            {
                incident_id = report.IncidentId,
                submitted_at = report.SubmittedAtUtc.ToString("o"),
                type = "bug",
                summary = report.Summary,
                details = report.Details,
                source = "chat /bug",
                player = new
                {
                    steam_id64 = report.Player.SteamId64,
                    display_name = report.Player.DisplayName,
                    position = report.Player.Position,
                    online = report.Player.Online
                },
                server = new
                {
                    id = report.Server.ServerId,
                    name = report.Server.Name,
                    address = report.Server.Address,
                    seed = report.Server.Seed,
                    world_size = report.Server.WorldSize
                },
                local = new
                {
                    data_file = report.LocalDataFile,
                    plugin = "RaidlandsBugReports",
                    plugin_version = "1.0.0"
                }
            };

            return JsonConvert.SerializeObject(payload);
        }

        private string BuildWebsiteUrl()
        {
            string baseUrl = TrimSlash(_config.WebsiteApiBaseUrl);
            string path = "/" + (_config.WebsiteApiBugReportPath ?? "").Trim().TrimStart('/');

            if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.IsWellFormedUriString(baseUrl, UriKind.Absolute))
                return "";

            return baseUrl + path;
        }

        private Dictionary<string, string> BuildWebsiteHeaders(string method, string url, string body, string secret)
        {
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            string pathAndQuery = new Uri(url).PathAndQuery;
            string bodyHash = Sha256(body ?? "");
            string payload = $"{method.ToUpperInvariant()}\n{pathAndQuery}\n{timestamp}\n{bodyHash}";

            return new Dictionary<string, string>
            {
                ["X-Raidlands-Server"] = _config.WebsiteApiServerId,
                ["X-Raidlands-Timestamp"] = timestamp,
                ["X-Raidlands-Signature"] = HmacSha256(payload, secret),
                ["Accept"] = "application/json"
            };
        }

        private void SendDiscord(BugReport report, int attempt)
        {
            if (!_config.DiscordWebhookEnabled)
                return;

            string webhookUrl = ResolveSecretValue(_config.DiscordWebhookUrl);
            if (!IsDiscordWebhookUrl(webhookUrl))
            {
                PrintWarning($"Bug report {report.IncidentId} was saved locally but not sent to Discord: webhook is not configured.");
                return;
            }

            string payload = BuildDiscordPayload(report);
            var headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" };

            webrequest.Enqueue(webhookUrl, payload, (code, response) =>
            {
                if (code == 200 || code == 204)
                {
                    Puts($"Sent bug report {report.IncidentId} to Discord.");
                    return;
                }

                if (attempt < _config.WebsiteApiRetryAttempts)
                {
                    int nextAttempt = attempt + 1;
                    PrintWarning($"Discord returned HTTP {code} for bug report {report.IncidentId}; retry {nextAttempt}/{_config.WebsiteApiRetryAttempts}.");
                    timer.Once(_config.RetryDelaySeconds * nextAttempt, () => SendDiscord(report, nextAttempt));
                    return;
                }

                PrintWarning($"Discord delivery failed for bug report {report.IncidentId}. HTTP {code}: {SafeText(response, 500)}");
            }, this, RequestMethod.POST, headers, 15f);
        }

        private string BuildDiscordPayload(BugReport report)
        {
            var fields = new List<object>
            {
                new { name = "Player", value = PlayerDiscordValue(report.Player), inline = true },
                new { name = "Bug report", value = $"**Subject:** {DiscordEscape(report.Summary)}\n{DiscordEscape(report.Details)}", inline = false },
                new { name = "Website", value = "Queued to the Raidlands Admin -> Feedback inbox.", inline = false },
                new { name = "Server", value = $"{DiscordEscape(report.Server.Name)}\nSeed `{report.Server.Seed}` - Size `{report.Server.WorldSize}`", inline = false }
            };

            var payload = new
            {
                username = _config.DiscordUsername,
                avatar_url = string.IsNullOrWhiteSpace(_config.DiscordAvatarUrl) ? null : _config.DiscordAvatarUrl,
                content = string.IsNullOrWhiteSpace(_config.DiscordMentionText) ? null : _config.DiscordMentionText,
                allowed_mentions = new { parse = new[] { "roles", "users" } },
                embeds = new[]
                {
                    new
                    {
                        title = $"/bug report - {report.IncidentId}",
                        color = 15105570,
                        timestamp = report.SubmittedAtUtc.ToString("o"),
                        fields,
                        footer = new { text = "Raidlands Bug Reports v1.0.0" }
                    }
                }
            };

            return JsonConvert.SerializeObject(payload);
        }

        private string ResolveSecretValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            string trimmed = value.Trim();
            if (!trimmed.StartsWith("${", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
                return trimmed;

            string key = trimmed.Substring(2, trimmed.Length - 3).Trim();
            if (string.IsNullOrWhiteSpace(key))
                return "";

            string secret;
            if (LoadSecrets().TryGetValue(key, out secret))
                return (secret ?? "").Trim();

            PrintWarning($"Secret variable {key} is not configured in oxide/config/{SecretsConfigName}.json.");
            return "";
        }

        private Dictionary<string, string> LoadSecrets()
        {
            if (_secrets != null)
                return _secrets;

            _secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string path = Path.Combine(Interface.Oxide.ConfigDirectory, $"{SecretsConfigName}.json");

            if (!File.Exists(path))
            {
                PrintWarning($"Optional secrets file not found: oxide/config/{SecretsConfigName}.json.");
                return _secrets;
            }

            try
            {
                var loadedSecrets = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (loadedSecrets != null)
                    _secrets = new Dictionary<string, string>(loadedSecrets, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not read oxide/config/{SecretsConfigName}.json: {ex.Message}");
            }

            return _secrets;
        }

        private bool IsDiscordWebhookConfigured()
        {
            return IsDiscordWebhookUrl(ResolveSecretValue(_config.DiscordWebhookUrl));
        }

        private void AddUiPanel(CuiElementContainer container, string parent, string anchorMin, string anchorMax, string color)
        {
            container.Add(new CuiPanel
            {
                Image = { Color = color },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);
        }

        private void AddUiLabel(CuiElementContainer container, string parent, string text, int size, TextAnchor align, string anchorMin, string anchorMax, string color)
        {
            container.Add(new CuiLabel
            {
                Text = { Text = text ?? string.Empty, FontSize = size, Align = align, Color = color },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);
        }

        private string AddUiButton(CuiElementContainer container, string parent, string text, string command, string anchorMin, string anchorMax, string color, int size)
        {
            return container.Add(new CuiButton
            {
                Button = { Command = command ?? string.Empty, Color = color },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                Text = { Text = text ?? string.Empty, FontSize = size, Align = TextAnchor.MiddleCenter, Color = "0.92 0.96 1 1" }
            }, parent);
        }

        private static string WrapForUi(string value, int lineLength, int maxLines)
        {
            value = SafeText(value ?? "", 1500).Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = new List<string>();

            foreach (string raw in value.Split('\n'))
            {
                string line = raw.Trim();
                while (line.Length > lineLength)
                {
                    int split = line.LastIndexOf(' ', Math.Min(lineLength, line.Length - 1));
                    if (split < 20) split = lineLength;
                    lines.Add(line.Substring(0, split).Trim());
                    line = line.Substring(Math.Min(split, line.Length)).Trim();
                    if (lines.Count >= maxLines)
                        return string.Join("\n", lines) + "\n...";
                }

                lines.Add(line);
                if (lines.Count >= maxLines)
                    return string.Join("\n", lines) + "\n...";
            }

            return string.Join("\n", lines);
        }

        private static string FormatUiFloat(float value)
        {
            return value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
        }

        private void AddStatusTab(CuiElementContainer container, string parent, string label, string filter, string activeFilter, int count, string anchorMin, string anchorMax)
        {
            bool active = string.Equals(filter, activeFilter, StringComparison.OrdinalIgnoreCase);
            string color = active ? "0.30 0.22 0.12 0.98" : "0.090 0.110 0.135 0.95";
            AddUiButton(container, parent, $"{label}\n{count}", $"raidbugs.ui tab {filter}", anchorMin, anchorMax, color, 8);
        }

        private static int CountReportsByStatus(IEnumerable<BugReport> reports, string statusFilter)
        {
            return reports.Count(report => StatusMatchesFilter(report, statusFilter));
        }

        private static bool StatusMatchesFilter(BugReport report, string statusFilter)
        {
            return string.Equals(NormalizeStatusFilter(report?.Status), NormalizeStatusFilter(statusFilter), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeStatusFilter(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "investigating": return "investigating";
                case "development":
                case "in development": return "development";
                case "fixed": return "fixed";
                default: return "submitted";
            }
        }

        private static string StatusFilterLabel(string statusFilter)
        {
            switch (NormalizeStatusFilter(statusFilter))
            {
                case "investigating": return "Investigating";
                case "development": return "In Development";
                case "fixed": return "Fixed";
                default: return "Submitted";
            }
        }

        private bool CanUseAdminCommand(BasePlayer player)
        {
            if (player == null)
                return true;

            return player.IsAdmin || permission.UserHasPermission(player.UserIDString, AdminPermission);
        }

        private static string NormalizeStatus(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "investigating": return "Investigating";
                case "development": return "In Development";
                case "fixed": return "Fixed";
                default: return "Submitted";
            }
        }

        private static bool IsDiscordWebhookUrl(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.StartsWith("https://discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildSummary(string details)
        {
            string clean = SafeText(details, 120);
            if (clean.Length <= 90)
                return clean;

            return clean.Substring(0, 87).TrimEnd() + "...";
        }

        private static string SafeCommandName(string value, string fallback)
        {
            string cleaned = new string((value ?? "").Trim().TrimStart('/').ToLowerInvariant()
                .Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
                .ToArray());

            return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
        }

        private static string SafeFileName(string value)
        {
            return new string((value ?? "").Trim()
                .Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.')
                .ToArray());
        }

        private static string SafeName(string value) => SafeText(string.IsNullOrWhiteSpace(value) ? "Unknown" : value, 80);

        private static string SafeText(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            value = value.Replace("\0", string.Empty).Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        private static string FormatPosition(Vector3 position)
        {
            return $"{position.x:0},{position.y:0},{position.z:0}";
        }

        private static string PlayerDiscordValue(PlayerInfo player)
        {
            string id = string.IsNullOrWhiteSpace(player.SteamId64) ? "unknown" : player.SteamId64;
            string profile = id.Length >= 17 ? $"[Steam profile](https://steamcommunity.com/profiles/{id})" : "Steam profile unavailable";
            return $"**{DiscordEscape(player.DisplayName)}**\n`{id}` - {profile}\n{(player.Online ? "Online" : "Offline")} - `{player.Position}`";
        }

        private static string DiscordEscape(string value)
        {
            return SafeText(value, 1500).Replace("\\", "\\\\").Replace("`", "'").Replace("@", "@\u200B");
        }

        private static void Reply(BasePlayer player, string message)
        {
            if (player == null)
                return;

            player.ChatMessage(message);
        }

        private static string TrimSlash(string value)
        {
            return (value ?? "").Trim().TrimEnd('/');
        }

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")));
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
            foreach (byte value in bytes)
                builder.Append(value.ToString("x2"));
            return builder.ToString();
        }
    }
}
