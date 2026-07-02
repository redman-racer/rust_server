using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("WebsiteVipBridge", "Raidlands", "1.2.0")]
    [Description("Syncs website VIP entitlements and player stats between Raidlands.net and the Rust server.")]
    public class WebsiteVipBridge : CovalencePlugin
    {
        private Configuration config;
        private Timer syncTimer;
        private Timer statsTimer;
        private Timer pendingStatsTimer;
        private long cursor;
        private Dictionary<string, string> secrets;
        private const string SecretsConfigName = "Secrets.local";

        private class Configuration
        {
            public string ApiBaseUrl = "https://raidlands.net";
            [JsonProperty("Website Asset Base Url")]
            public string WebsiteAssetBaseUrl = "https://raidlands.net";
            [JsonProperty("Assets")]
            public AssetPaths Assets = new AssetPaths();
            public string ServerId = "raidlands-main";
            public string SharedSecret = "";
            public int SyncIntervalSeconds = 120;
            public string FailMode = "log_only";
            public bool StatsEnabled = true;
            public int StatsSyncIntervalSeconds = 300;
            public int StatsDebounceSeconds = 30;
            public string WipeKey = "";
            public string WipeStartedAt = "";
            public List<string> ManagedGroups = new List<string>
            {
                "vip_bronze",
                "vip_gold",
                "vip_elite",
                "perk_personal_mini",
                "perk_skinbox",
                "perk_raid_kit",
                "perk_queue_priority",
                "perk_supporter_badge"
            };
        }

        private class AssetPaths
        {
            public string Logo = "/assets/media/raidlands-logo.png";
            public string NavLogo = "/assets/media/nav-logo.png";
            public string Hero = "/assets/media/website-hero-raid-overlook-v4.webp";
            public string Header = "/assets/media/header-bg-rust-v2.png";
            public string WipePanel = "/assets/media/wipe-countdown-panel-v2.jpg";
            public string BackpacksIcon = "/assets/media/feature-icons/backpacks.png";
            public string KitsIcon = "/assets/media/feature-icons/kit.png";
            public string TeleportIcon = "/assets/media/feature-icons/teleport.png";
            public string ClanIcon = "/assets/media/feature-icons/clan.png";
            public string SkinboxIcon = "/assets/media/feature-icons/skinbox.png";
            public string FastRaidsIcon = "/assets/media/feature-icons/fast-raids.png";
            public string GatherIcon = "/assets/media/feature-icons/gather.png";
            public string StatsIcon = "/assets/media/feature-icons/stats.png";
            public string SearchIcon = "/assets/media/feature-icons/search.png";
        }

        private class PlayerResponse
        {
            public bool ok;
            public string error;
            public string steam_id64;
            public List<string> managed_groups;
            public List<string> groups;
            public long cursor;
        }

        private class ChangesResponse
        {
            public bool ok;
            public string error;
            public List<string> managed_groups;
            public List<PlayerState> players;
            public long cursor;
        }

        private class PlayerState
        {
            public string steam_id64;
            public List<string> groups;
        }

        private class StatsResponse
        {
            public bool ok;
            public string error;
        }

        private class StatsSnapshot
        {
            public string wipe_key;
            public string wipe_started_at;
            public string generated_at;
            public List<StatsPlayer> players = new List<StatsPlayer>();
        }

        private class StatsPlayer
        {
            public string steam_id64;
            public string display_name;
            public int kills;
            public int deaths;
            public int playtime_seconds;
            public int afk_seconds;
            public int reward_points;
        }

        private class KdrData
        {
            public ulong id;
            public string name;
            public int kills;
            public int deaths;
        }

        private class PlaytimeData
        {
            public Dictionary<string, PlaytimeUser> _userData = new Dictionary<string, PlaytimeUser>();
        }

        private class PlaytimeUser
        {
            public double playtime;
            public double afkTime;
            public string displayName;
            public double PlayTime;
            public double AFKTime;
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
                PrintWarning("Configuration was invalid; writing defaults.");
                config = new Configuration();
            }

            var defaults = new Configuration();

            if (string.IsNullOrWhiteSpace(config.WebsiteAssetBaseUrl))
            {
                config.WebsiteAssetBaseUrl = defaults.WebsiteAssetBaseUrl;
            }

            if (config.Assets == null)
            {
                config.Assets = new AssetPaths();
            }

            ApplyAssetDefaults(config.Assets, defaults.Assets);

            if (config.ManagedGroups == null)
            {
                config.ManagedGroups = defaults.ManagedGroups;
            }

            if (config.StatsSyncIntervalSeconds <= 0)
            {
                config.StatsSyncIntervalSeconds = defaults.StatsSyncIntervalSeconds;
            }

            if (config.StatsDebounceSeconds <= 0)
            {
                config.StatsDebounceSeconds = defaults.StatsDebounceSeconds;
            }

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void OnServerInitialized()
        {
            EnsureManagedGroups(config.ManagedGroups);
            SyncBrandConfigs();
            SyncChanges();

            var interval = Math.Max(30, config.SyncIntervalSeconds);
            syncTimer = timer.Every(interval, SyncChanges);

            if (config.StatsEnabled)
            {
                var statsInterval = Math.Max(60, config.StatsSyncIntervalSeconds);
                timer.Once(10f, SyncStatsSnapshot);
                statsTimer = timer.Every(statsInterval, SyncStatsSnapshot);
                Puts($"WebsiteVipBridge syncing VIP every {interval} seconds and stats every {statsInterval} seconds.");
                return;
            }

            Puts($"WebsiteVipBridge syncing VIP every {interval} seconds. Stats sync is disabled.");
        }

        private void Unload()
        {
            syncTimer?.Destroy();
            statsTimer?.Destroy();
            pendingStatsTimer?.Destroy();
        }

        private void OnUserConnected(IPlayer player)
        {
            if (player == null || string.IsNullOrWhiteSpace(player.Id))
            {
                return;
            }

            SyncPlayer(player.Id);
            QueueStatsSync();
        }

        private void OnUserDisconnected(IPlayer player)
        {
            QueueStatsSync();
        }

        private void OnPointsUpdated(ulong userId, int balance)
        {
            QueueStatsSync();
        }

        private void SyncPlayer(string steamId)
        {
            if (!CanRequest())
            {
                return;
            }

            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/vip-player.php?steam_id64={Uri.EscapeDataString(steamId)}";
            SendGet(url, (code, response) =>
            {
                if (!IsSuccess(code, response, out var error))
                {
                    PrintWarning($"VIP player sync failed for {steamId}: {error}");
                    return;
                }

                var payload = JsonConvert.DeserializeObject<PlayerResponse>(response);

                if (payload == null || !payload.ok)
                {
                    PrintWarning($"VIP player sync failed for {steamId}: {payload?.error ?? "invalid response"}");
                    return;
                }

                ApplyDesiredGroups(steamId, payload.groups, payload.managed_groups);

                if (payload.cursor > cursor)
                {
                    cursor = payload.cursor;
                }
            });
        }

        private void SyncChanges()
        {
            if (!CanRequest())
            {
                return;
            }

            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/vip-changes.php?since={cursor}";
            SendGet(url, (code, response) =>
            {
                if (!IsSuccess(code, response, out var error))
                {
                    PrintWarning($"VIP change sync failed: {error}");
                    return;
                }

                var payload = JsonConvert.DeserializeObject<ChangesResponse>(response);

                if (payload == null || !payload.ok)
                {
                    PrintWarning($"VIP change sync failed: {payload?.error ?? "invalid response"}");
                    return;
                }

                EnsureManagedGroups(payload.managed_groups);

                foreach (var player in payload.players ?? new List<PlayerState>())
                {
                    if (string.IsNullOrWhiteSpace(player.steam_id64))
                    {
                        continue;
                    }

                    ApplyDesiredGroups(player.steam_id64, player.groups, payload.managed_groups);
                }

                if (payload.cursor > cursor)
                {
                    cursor = payload.cursor;
                }
            });
        }

        private void QueueStatsSync()
        {
            if (!config.StatsEnabled || !CanRequest())
            {
                return;
            }

            pendingStatsTimer?.Destroy();
            pendingStatsTimer = timer.Once(Math.Max(5, config.StatsDebounceSeconds), SyncStatsSnapshot);
        }

        private void SyncStatsSnapshot()
        {
            pendingStatsTimer?.Destroy();
            pendingStatsTimer = null;

            if (!config.StatsEnabled || !CanRequest())
            {
                return;
            }

            var snapshot = BuildStatsSnapshot();
            var body = JsonConvert.SerializeObject(snapshot);
            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/stats-snapshot.php";

            SendPost(url, body, (code, response) =>
            {
                if (!IsSuccess(code, response, out var error))
                {
                    PrintWarning($"Stats snapshot sync failed: {error}");
                    return;
                }

                var payload = JsonConvert.DeserializeObject<StatsResponse>(response);

                if (payload == null || !payload.ok)
                {
                    PrintWarning($"Stats snapshot sync failed: {payload?.error ?? "invalid response"}");
                    return;
                }

                Puts($"Stats snapshot synced for {snapshot.players.Count} players.");
            });
        }

        private StatsSnapshot BuildStatsSnapshot()
        {
            var playersById = new Dictionary<string, StatsPlayer>();
            var wipeStartedAt = ResolveSecretValue(config.WipeStartedAt);

            AddKdrStats(playersById);
            AddPlaytimeStats(playersById);
            AddRewardPoints(playersById);
            AddConnectedPlayers(playersById);

            return new StatsSnapshot
            {
                wipe_key = ResolveWipeKey(),
                wipe_started_at = string.IsNullOrWhiteSpace(wipeStartedAt) ? null : wipeStartedAt,
                generated_at = DateTime.UtcNow.ToString("o"),
                players = playersById.Values
                    .OrderByDescending(player => player.kills)
                    .ThenByDescending(player => player.playtime_seconds)
                    .ThenBy(player => player.steam_id64)
                    .ToList()
            };
        }

        private void AddKdrStats(Dictionary<string, StatsPlayer> playersById)
        {
            var directory = Path.Combine(Interface.Oxide.DataFileSystem.Directory, "KDRScoreboard");

            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var path in Directory.GetFiles(directory, "*.json"))
            {
                try
                {
                    var data = JsonConvert.DeserializeObject<KdrData>(File.ReadAllText(path));

                    if (data == null || data.id == 0)
                    {
                        continue;
                    }

                    var steamId = data.id.ToString();

                    if (!IsSteamId64(steamId))
                    {
                        continue;
                    }

                    var player = EnsureStatsPlayer(playersById, steamId);
                    player.display_name = FirstNonEmpty(player.display_name, data.name);
                    player.kills = Math.Max(0, data.kills);
                    player.deaths = Math.Max(0, data.deaths);
                }
                catch (Exception ex)
                {
                    PrintWarning($"Could not read KDR stats from {Path.GetFileName(path)}: {ex.Message}");
                }
            }
        }

        private void AddPlaytimeStats(Dictionary<string, StatsPlayer> playersById)
        {
            var data = ReadDataFile<PlaytimeData>("PlaytimeTracker/user_data");

            if (data?._userData == null)
            {
                return;
            }

            foreach (var entry in data._userData)
            {
                if (!IsSteamId64(entry.Key) || entry.Value == null)
                {
                    continue;
                }

                var player = EnsureStatsPlayer(playersById, entry.Key);
                player.display_name = FirstNonEmpty(player.display_name, entry.Value.displayName);
                player.playtime_seconds = Math.Max(0, ToInt(Math.Max(entry.Value.PlayTime, entry.Value.playtime)));
                player.afk_seconds = Math.Max(0, ToInt(Math.Max(entry.Value.AFKTime, entry.Value.afkTime)));
            }
        }

        private void AddRewardPoints(Dictionary<string, StatsPlayer> playersById)
        {
            var balances = ReadDataFile<Dictionary<string, int>>("ServerRewards/player_balances");

            if (balances == null)
            {
                return;
            }

            foreach (var entry in balances)
            {
                if (!IsSteamId64(entry.Key))
                {
                    continue;
                }

                var player = EnsureStatsPlayer(playersById, entry.Key);
                player.reward_points = Math.Max(0, entry.Value);
            }
        }

        private void AddConnectedPlayers(Dictionary<string, StatsPlayer> playersById)
        {
            foreach (var player in players.Connected)
            {
                if (player == null || !IsSteamId64(player.Id))
                {
                    continue;
                }

                var statsPlayer = EnsureStatsPlayer(playersById, player.Id);
                statsPlayer.display_name = FirstNonEmpty(statsPlayer.display_name, player.Name);
            }
        }

        private StatsPlayer EnsureStatsPlayer(Dictionary<string, StatsPlayer> playersById, string steamId)
        {
            StatsPlayer player;

            if (!playersById.TryGetValue(steamId, out player))
            {
                player = new StatsPlayer
                {
                    steam_id64 = steamId,
                    display_name = ""
                };
                playersById[steamId] = player;
            }

            return player;
        }

        private T ReadDataFile<T>(string fileName)
        {
            try
            {
                if (!Interface.Oxide.DataFileSystem.ExistsDatafile(fileName))
                {
                    return default(T);
                }

                return Interface.Oxide.DataFileSystem.ReadObject<T>(fileName);
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not read data file {fileName}: {ex.Message}");
                return default(T);
            }
        }

        private string ResolveWipeKey()
        {
            var wipeKey = ResolveSecretValue(config.WipeKey);

            if (!string.IsNullOrWhiteSpace(wipeKey))
            {
                return wipeKey.Trim();
            }

            return $"{config.ServerId}-current";
        }

        public string GetRaidlandsBrandAssetUrl(string key)
        {
            return BrandAssetUrl(key);
        }

        public Dictionary<string, string> GetRaidlandsBrandAssets()
        {
            return BrandAssetUrls();
        }

        public string GetRaidlandsBrandValue(string key)
        {
            string value;
            return TryGetBrandValue(key, out value) ? value : "";
        }

        private void SyncBrandConfigs()
        {
            var updated = 0;

            updated += SyncJsonConfig("SimpleLogo", ApplySimpleLogoBrand);
            updated += SyncJsonConfig("ServerInfo", ApplyServerInfoBrand);
            updated += SyncJsonConfig("ServerPop", ApplyServerPopBrand);
            updated += SyncJsonConfig("SmartChatBot", ApplySmartChatBotBrand);
            updated += SyncJsonConfig("Kits", ApplyKitsBrand);
            updated += SyncJsonConfig("DiscordWipe", ApplyDiscordWipeBrand);
            updated += SyncJsonConfig("Scoreboards", ApplyScoreboardsBrand);

            if (updated > 0)
            {
                Puts($"Raidlands brand config sync updated {updated} config file(s).");
            }
        }

        private int SyncJsonConfig(string configName, Action<JObject> apply)
        {
            var path = Path.Combine(Interface.Oxide.ConfigDirectory, $"{configName}.json");

            if (!File.Exists(path))
            {
                return 0;
            }

            try
            {
                var json = JObject.Parse(File.ReadAllText(path));
                var before = json.ToString(Formatting.None);

                apply(json);

                if (before == json.ToString(Formatting.None))
                {
                    return 0;
                }

                File.WriteAllText(path, json.ToString(Formatting.Indented));
                return 1;
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not sync Raidlands brand config for {configName}: {ex.Message}");
                return 0;
            }
        }

        private void ApplySimpleLogoBrand(JObject json)
        {
            var ui = EnsureObject(json, "UI");
            ui["BackgroundMainURL"] = new JArray(BrandAssetUrl("NavLogo"));
        }

        private void ApplyServerInfoBrand(JObject json)
        {
            var settings = EnsureObject(json, "settings");
            var backgroundImage = EnsureObject(settings, "BackgroundImage");

            backgroundImage["Enabled"] = true;
            backgroundImage["Url"] = BrandAssetUrl("Header");
            backgroundImage["TransparencyInPercent"] = 40;

            settings["ActiveButtonColor"] = BrandValue("PrimaryRed");
            settings["InactiveButtonColor"] = BrandValue("MutedButton");
            settings["CloseButtonColor"] = BrandValue("PrimaryRed");
            settings["NextPageButtonColor"] = BrandValue("PrimaryRed");
            settings["PrevPageButtonColor"] = BrandValue("PrimaryRed");
            settings["BackgroundColor"] = BrandValue("DarkPanel");

            var helpButton = EnsureObject(settings, "HelpButton");
            helpButton["Color"] = BrandValue("PrimaryRed");

            var tabs = settings["Tabs"] as JArray;
            SetServerInfoTab(tabs, 0, "Briefing", "RAIDLANDS OPERATION BRIEFING", new[]
            {
                "<color=#ff3b3b><b>WELCOME OPERATIONAL BRIEF</b></color>",
                "",
                "<color=#d0d0d0>Welcome to Raidlands, a high-intensity Rust combat environment.</color>",
                "<color=#b0b0b0>Expect fast progression, constant pressure, and direct player conflict.</color>",
                "",
                "<color=#ff3b3b><b>MISSION PARAMETERS</b></color>",
                "<color=#d0d0d0>- High-risk PvP environment</color>",
                "<color=#d0d0d0>- Accelerated resource gathering</color>",
                "<color=#d0d0d0>- Rapid crafting and deployment systems</color>",
                "<color=#d0d0d0>- Clan-based warfare enabled</color>",
                "",
                "<color=#ff3b3b><b>OBJECTIVE</b></color>",
                "<color=#ffffff>Build fast. Raid smart. Hold your ground.</color>"
            });

            SetServerInfoTab(tabs, 1, "Systems", "ACTIVE SERVER SYSTEMS", new[]
            {
                "<color=#ff3b3b><b>SERVER SYSTEMS ONLINE</b></color>",
                "",
                "<color=#d0d0d0>Raidlands runs a fast, combat-focused ruleset with quality-of-life systems.</color>",
                "",
                "<color=#ff3b3b><b>CORE SYSTEMS</b></color>",
                "<color=#d0d0d0>- Instant crafting</color>",
                "<color=#d0d0d0>- Starter kit access</color>",
                "<color=#d0d0d0>- Teleportation network</color>",
                "<color=#d0d0d0>- Clan warfare tools</color>",
                "<color=#d0d0d0>- Skin and loadout customization</color>",
                "",
                "<color=#ff3b3b><b>COMBAT ENVIRONMENT</b></color>",
                "<color=#d0d0d0>- PvP enabled</color>",
                "<color=#d0d0d0>- Raiding unrestricted</color>",
                "<color=#d0d0d0>- Staff support active</color>"
            });

            SetServerInfoTab(tabs, 2, "Orders", "FIELD COMMANDS", new[]
            {
                "<color=#ff3b3b><b>AUTHORIZED COMMANDS</b></color>",
                "",
                "<color=#ffd166>/bgrade 0-4</color> - Upgrade structures automatically",
                "<color=#ffd166>/mymini</color> - Deploy personal air transport",
                "<color=#ffd166>/fmini</color> - Recall deployed air unit",
                "<color=#ffd166>/nomini</color> - Decommission air unit",
                "<color=#ffd166>/outpost</color> - Teleport to safe trading zone",
                "<color=#ffd166>/bandit</color> - Teleport to hostile trading zone",
                "<color=#ffd166>/tpr</color> - Start a player teleport request",
                "<color=#ffd166>/kit</color> - Access equipment loadouts",
                "<color=#ffd166>/skins</color> - Modify equipment appearance",
                "<color=#ffd166>/remove</color> - Deconstruct placed structures",
                "<color=#ffd166>/clanhelp</color> - View clan system help",
                "<color=#ffd166>/s</color> - Access the supply marketplace",
                "<color=#ffd166>/bskin</color> - Configure building skins",
                "<color=#ffd166>/stats</color> - View combat performance",
                "<color=#ffd166>/auth</color> - Begin Discord account linking"
            });
        }

        private void ApplyServerPopBrand(JObject json)
        {
            var chatSettings = EnsureObject(json, "Chat Settings");
            var messageSettings = EnsureObject(json, "Messgae Settings");

            chatSettings["Chat Prefix"] = "<size=16><color=#ff3b3b>| Raidlands |</color></size>";
            messageSettings["Value Color (HEX)"] = BrandValue("AccentGold");
        }

        private void ApplySmartChatBotBrand(JObject json)
        {
            json["Chat Prefix"] = "<color=#ff3b3b>Raidlands</color> ";
            json["Show Chat Prefix"] = true;
            json["Auto Messages"] = new JArray(
                new JObject
                {
                    ["Permission"] = "smartchatbot.messages",
                    ["Message Frequency"] = "5m",
                    ["Auto Messages"] = new JArray(
                        new JObject
                        {
                            ["Is Enabled"] = true,
                            ["Message"] = "Visit https://raidlands.net/ for store perks, Discord, and live stats."
                        })
                });
        }

        private void ApplyKitsBrand(JObject json)
        {
            var uiOptions = EnsureObject(json, "UI Options");
            uiOptions["Default kit image URL"] = BrandAssetUrl("KitsIcon");
            uiOptions["View kit icon URL"] = FirstNonEmpty(BrandAssetUrl("SearchIcon"), BrandAssetUrl("KitsIcon"));
        }

        private void ApplyDiscordWipeBrand(JObject json)
        {
            ApplyDiscordMessageBrands(json["Wipe messages"] as JArray,
                "Raidlands has wiped. Drop in, build fast, and claim the map.");
            ApplyDiscordMessageBrands(json["Protocol messages"] as JArray,
                "Raidlands server protocol changed. Update Rust before reconnecting.");
        }

        private void ApplyScoreboardsBrand(JObject json)
        {
            json["Background Color"] = "0.105 0.118 0.122 0.82";
            json["Content Color"] = "0.067 0.067 0.067 0.88";
            json["Header Color"] = "1 0.231 0.231 1";
            json["Title Color"] = "1 0.82 0.4 1";
        }

        private void ApplyDiscordMessageBrands(JArray messages, string description)
        {
            if (messages == null)
            {
                return;
            }

            foreach (var item in messages.OfType<JObject>())
            {
                var embed = EnsureObject(item, "Embed");
                var footer = EnsureObject(embed, "Footer");

                embed["Description"] = description;
                embed["Url"] = BrandValue("WebsiteUrl");
                embed["Embed Color"] = BrandValue("PrimaryRed");
                embed["Thumbnail Url"] = BrandAssetUrl("Logo");
                footer["Icon Url"] = BrandAssetUrl("Logo");
                footer["Text"] = BrandValue("Name");
                footer["Enabled"] = true;
            }
        }

        private void SetServerInfoTab(JArray tabs, int index, string buttonText, string headerText, IEnumerable<string> lines)
        {
            if (tabs == null || index < 0 || index >= tabs.Count)
            {
                return;
            }

            var tab = tabs[index] as JObject;

            if (tab == null)
            {
                return;
            }

            tab["ButtonText"] = buttonText;
            tab["HeaderText"] = headerText;

            var pages = tab["Pages"] as JArray;

            if (pages == null)
            {
                pages = new JArray();
                tab["Pages"] = pages;
            }

            if (pages.Count == 0)
            {
                pages.Add(new JObject
                {
                    ["ImageSettings"] = new JArray()
                });
            }

            var page = pages[0] as JObject;

            if (page == null)
            {
                page = new JObject();
                pages[0] = page;
            }

            var textLines = new JArray();

            foreach (var line in lines ?? new string[0])
            {
                textLines.Add(line);
            }

            page["TextLines"] = textLines;

            if (page["ImageSettings"] == null)
            {
                page["ImageSettings"] = new JArray();
            }
        }

        private JObject EnsureObject(JObject parent, string key)
        {
            var current = parent[key] as JObject;

            if (current != null)
            {
                return current;
            }

            current = new JObject();
            parent[key] = current;
            return current;
        }

        private Dictionary<string, string> BrandAssetUrls()
        {
            var assets = config?.Assets ?? new AssetPaths();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            values["Logo"] = AssetUrl(assets.Logo);
            values["NavLogo"] = AssetUrl(assets.NavLogo);
            values["Hero"] = AssetUrl(assets.Hero);
            values["Header"] = AssetUrl(assets.Header);
            values["WipePanel"] = AssetUrl(assets.WipePanel);
            values["BackpacksIcon"] = AssetUrl(assets.BackpacksIcon);
            values["KitsIcon"] = AssetUrl(assets.KitsIcon);
            values["TeleportIcon"] = AssetUrl(assets.TeleportIcon);
            values["ClanIcon"] = AssetUrl(assets.ClanIcon);
            values["SkinboxIcon"] = AssetUrl(assets.SkinboxIcon);
            values["FastRaidsIcon"] = AssetUrl(assets.FastRaidsIcon);
            values["GatherIcon"] = AssetUrl(assets.GatherIcon);
            values["StatsIcon"] = AssetUrl(assets.StatsIcon);
            values["SearchIcon"] = AssetUrl(assets.SearchIcon);

            return values;
        }

        private string BrandAssetUrl(string key)
        {
            var normalized = NormalizeBrandKey(key);

            foreach (var asset in BrandAssetUrls())
            {
                if (NormalizeBrandKey(asset.Key) == normalized)
                {
                    return asset.Value;
                }
            }

            return "";
        }

        private Dictionary<string, string> BrandValues()
        {
            var websiteUrl = TrimSlash(config?.ApiBaseUrl);

            if (string.IsNullOrWhiteSpace(websiteUrl))
            {
                websiteUrl = "https://raidlands.net";
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            values["Name"] = "Raidlands";
            values["WebsiteUrl"] = websiteUrl;
            values["PrimaryRed"] = "#ff3b3b";
            values["AccentGold"] = "#ffd166";
            values["DarkPanel"] = "#1b1e1f";
            values["MutedButton"] = "#3a3d3f";

            return values;
        }

        private string BrandValue(string key)
        {
            string value;
            return TryGetBrandValue(key, out value) ? value : "";
        }

        private bool TryGetBrandValue(string key, out string value)
        {
            var normalized = NormalizeBrandKey(key);

            foreach (var item in BrandValues())
            {
                if (NormalizeBrandKey(item.Key) == normalized)
                {
                    value = item.Value;
                    return true;
                }
            }

            value = "";
            return false;
        }

        private string AssetUrl(string configuredAssetPath)
        {
            return ResolveAssetUrl(configuredAssetPath, config?.WebsiteAssetBaseUrl);
        }

        private static string ResolveAssetUrl(string configuredAssetPath, string websiteAssetBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(configuredAssetPath))
            {
                return "";
            }

            var assetPath = configuredAssetPath.Trim();

            if (assetPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || assetPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return assetPath;
            }

            var baseUrl = TrimSlash(websiteAssetBaseUrl);
            var normalizedPath = NormalizeAssetPath(assetPath).TrimStart('/');

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return normalizedPath;
            }

            return $"{baseUrl}/{normalizedPath}";
        }

        private bool CanRequest()
        {
            if (string.IsNullOrWhiteSpace(config.ApiBaseUrl))
            {
                PrintWarning("ApiBaseUrl is not configured.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(ResolveSecretValue(config.SharedSecret)))
            {
                PrintWarning("SharedSecret is not configured.");
                return false;
            }

            return true;
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
            var signature = HmacSha256(payload, ResolveSecretValue(config.SharedSecret));

            return new Dictionary<string, string>
            {
                ["X-Raidlands-Server"] = config.ServerId,
                ["X-Raidlands-Timestamp"] = timestamp,
                ["X-Raidlands-Signature"] = signature,
                ["Accept"] = "application/json"
            };
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
                return value;
            }

            var key = trimmed.Substring(2, trimmed.Length - 3).Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                return "";
            }

            string secret;

            if (LoadSecrets().TryGetValue(key, out secret))
            {
                return secret;
            }

            PrintWarning($"Secret variable {key} is not configured in oxide/config/{SecretsConfigName}.json.");
            return "";
        }

        private Dictionary<string, string> LoadSecrets()
        {
            if (secrets != null)
            {
                return secrets;
            }

            secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var path = Path.Combine(Interface.Oxide.ConfigDirectory, $"{SecretsConfigName}.json");

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

        private void ApplyDesiredGroups(string steamId, List<string> desiredGroups, List<string> apiManagedGroups)
        {
            var managed = new HashSet<string>((config.ManagedGroups ?? new List<string>()).Where(IsGroupName), StringComparer.OrdinalIgnoreCase);

            foreach (var group in apiManagedGroups ?? new List<string>())
            {
                if (IsGroupName(group))
                {
                    managed.Add(group);
                }
            }

            var desired = new HashSet<string>((desiredGroups ?? new List<string>()).Where(IsGroupName), StringComparer.OrdinalIgnoreCase);

            EnsureManagedGroups(managed.ToList());

            foreach (var group in managed)
            {
                var hasGroup = permission.UserHasGroup(steamId, group);
                var shouldHaveGroup = desired.Contains(group);

                if (shouldHaveGroup && !hasGroup)
                {
                    permission.AddUserGroup(steamId, group);
                    Puts($"Granted {group} to {steamId}.");
                    continue;
                }

                if (!shouldHaveGroup && hasGroup)
                {
                    permission.RemoveUserGroup(steamId, group);
                    Puts($"Removed {group} from {steamId}.");
                }
            }
        }

        private void EnsureManagedGroups(IEnumerable<string> groups)
        {
            foreach (var group in groups ?? new List<string>())
            {
                if (!IsGroupName(group))
                {
                    continue;
                }

                if (!permission.GroupExists(group))
                {
                    permission.CreateGroup(group, group, 0);
                    Puts($"Created Oxide group {group}.");
                }
            }
        }

        private bool IsGroupName(string group)
        {
            return !string.IsNullOrWhiteSpace(group) && group.All(character =>
                char.IsLetterOrDigit(character) || character == '_' || character == '-');
        }

        private bool IsSteamId64(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length == 17
                && value.StartsWith("7656119")
                && value.All(char.IsDigit);
        }

        private string FirstNonEmpty(string current, string next)
        {
            return string.IsNullOrWhiteSpace(current) ? (next ?? "").Trim() : current;
        }

        private static string ConfiguredOrDefault(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static void ApplyAssetDefaults(AssetPaths assets, AssetPaths defaults)
        {
            assets.Logo = ConfiguredOrDefault(assets.Logo, defaults.Logo);
            assets.NavLogo = ConfiguredOrDefault(assets.NavLogo, defaults.NavLogo);
            assets.Hero = ConfiguredOrDefault(assets.Hero, defaults.Hero);
            assets.Header = ConfiguredOrDefault(assets.Header, defaults.Header);
            assets.WipePanel = ConfiguredOrDefault(assets.WipePanel, defaults.WipePanel);
            assets.BackpacksIcon = ConfiguredOrDefault(assets.BackpacksIcon, defaults.BackpacksIcon);
            assets.KitsIcon = ConfiguredOrDefault(assets.KitsIcon, defaults.KitsIcon);
            assets.TeleportIcon = ConfiguredOrDefault(assets.TeleportIcon, defaults.TeleportIcon);
            assets.ClanIcon = ConfiguredOrDefault(assets.ClanIcon, defaults.ClanIcon);
            assets.SkinboxIcon = ConfiguredOrDefault(assets.SkinboxIcon, defaults.SkinboxIcon);
            assets.FastRaidsIcon = ConfiguredOrDefault(assets.FastRaidsIcon, defaults.FastRaidsIcon);
            assets.GatherIcon = ConfiguredOrDefault(assets.GatherIcon, defaults.GatherIcon);
            assets.StatsIcon = ConfiguredOrDefault(assets.StatsIcon, defaults.StatsIcon);
            assets.SearchIcon = ConfiguredOrDefault(assets.SearchIcon, defaults.SearchIcon);
        }

        private int ToInt(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            {
                return 0;
            }

            return (int)Math.Min(int.MaxValue, Math.Round(value));
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

        private static string TrimSlash(string value)
        {
            return (value ?? "").Trim().TrimEnd('/');
        }

        private static string NormalizeAssetPath(string value)
        {
            return string.Join("/", (value ?? "")
                .Replace('\\', '/')
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string NormalizeBrandKey(string value)
        {
            return string.Concat((value ?? "").Where(char.IsLetterOrDigit)).ToLowerInvariant();
        }

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
            }
        }

        private static string HmacSha256(string value, string secret)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                return Hex(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
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
    }
}
