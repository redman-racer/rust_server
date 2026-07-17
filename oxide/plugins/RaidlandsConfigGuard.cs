using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("RaidlandsConfigGuard", "Raidlands", "1.0.0")]
    [Description("Creates content-addressed Oxide config snapshots and audits known unique arrays without automatically restoring or rewriting files.")]
    public class RaidlandsConfigGuard : CovalencePlugin
    {
        private const string SnapshotRootName = "RaidlandsConfigGuard";
        private GuardConfig config;
        private Timer snapshotTimer;
        private DateTime lastSnapshotUtc = DateTime.MinValue;
        private string lastSummary = "No snapshot has run yet.";

        private class GuardConfig
        {
            public bool Enabled = true;
            public bool WatchAllConfigJson = true;
            public int SnapshotIntervalMinutes = 30;
            public bool SnapshotOnServerSave = true;
            public bool SnapshotOnUnload = true;

            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> WatchedConfigFiles = new List<string>
            {
                "WebsiteVipBridge.json",
                "LiveAdmin.json",
                "RaidlandsRoamBots.json",
                "ServerInfo.json",
                "RustReportDiscord.json",
                "SmartChatBot.json",
                "SimpleLogo.json",
                "ServerPop.json",
                "Kits.json",
                "DiscordWipe.json",
                "Scoreboards.json"
            };

            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> ExcludedConfigFiles = new List<string>
            {
                "Secrets.local.json"
            };
        }

        private class SnapshotResult
        {
            public int Created;
            public int Unchanged;
            public int Missing;
            public int Invalid;
            public int Failed;

            public override string ToString()
            {
                return $"created={Created}, unchanged={Unchanged}, missing={Missing}, invalid_json={Invalid}, failed={Failed}";
            }
        }

        protected override void LoadDefaultConfig()
        {
            config = new GuardConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<GuardConfig>() ?? new GuardConfig();
            }
            catch (Exception ex)
            {
                PrintError($"Invalid configuration; using safe defaults. {ex.Message}");
                config = new GuardConfig();
            }

            config.SnapshotIntervalMinutes = Math.Max(5, config.SnapshotIntervalMinutes);
            config.WatchedConfigFiles = DistinctFileNames(config.WatchedConfigFiles);
            config.ExcludedConfigFiles = DistinctFileNames(config.ExcludedConfigFiles);

            if (!config.ExcludedConfigFiles.Contains("Secrets.local.json", StringComparer.OrdinalIgnoreCase))
            {
                config.ExcludedConfigFiles.Add("Secrets.local.json");
            }

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void OnServerInitialized()
        {
            if (!config.Enabled)
            {
                Puts("Config snapshots are disabled by configuration.");
                return;
            }

            timer.Once(5f, () => RunSnapshot("startup"));
            snapshotTimer = timer.Every(config.SnapshotIntervalMinutes * 60f, () => RunSnapshot("interval"));
            Puts($"Config guard active. Snapshots run every {config.SnapshotIntervalMinutes} minute(s); automatic restore is not implemented.");
        }

        private void OnServerSave()
        {
            if (config.Enabled && config.SnapshotOnServerSave)
            {
                RunSnapshot("server-save");
            }
        }

        private void Unload()
        {
            snapshotTimer?.Destroy();
            snapshotTimer = null;

            if (config != null && config.Enabled && config.SnapshotOnUnload)
            {
                RunSnapshot("unload");
            }
        }

        [Command("raidlands.configguard.snapshot")]
        private void SnapshotCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRun(player))
            {
                return;
            }

            Reply(player, RunSnapshot("manual"));
        }

        [Command("raidlands.configguard.status")]
        private void StatusCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRun(player))
            {
                return;
            }

            var last = lastSnapshotUtc == DateTime.MinValue ? "never" : lastSnapshotUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
            Reply(player, $"enabled={config.Enabled}, watch_all={config.WatchAllConfigJson}, interval={config.SnapshotIntervalMinutes}m, last={last}; {lastSummary}");
        }

        [Command("raidlands.configguard.verify")]
        private void VerifyCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRun(player))
            {
                return;
            }

            Reply(player, VerifyConfigs());
        }

        private bool CanRun(IPlayer player)
        {
            if (player == null || player.IsServer || player.IsAdmin)
            {
                return true;
            }

            player.Reply("You must be a server admin to run this command.");
            return false;
        }

        private void Reply(IPlayer player, string message)
        {
            if (player == null || player.IsServer)
            {
                Puts(message);
                return;
            }

            player.Reply(message);
        }

        private string RunSnapshot(string reason)
        {
            if (config == null || !config.Enabled)
            {
                return "Config snapshot skipped because the guard is disabled.";
            }

            var result = new SnapshotResult();
            var root = Path.Combine(Interface.Oxide.DataDirectory, SnapshotRootName);
            Directory.CreateDirectory(root);

            foreach (var sourcePath in ResolveConfigPaths())
            {
                SnapshotFile(sourcePath, root, result);
            }

            lastSnapshotUtc = DateTime.UtcNow;
            lastSummary = $"reason={reason}; {result}";
            Puts($"Config snapshot complete: {lastSummary}");
            return lastSummary;
        }

        private IEnumerable<string> ResolveConfigPaths()
        {
            var excluded = new HashSet<string>(config.ExcludedConfigFiles ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            if (config.WatchAllConfigJson)
            {
                return Directory.GetFiles(Interface.Oxide.ConfigDirectory, "*.json", SearchOption.TopDirectoryOnly)
                    .Where(path => !excluded.Contains(Path.GetFileName(path)))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return (config.WatchedConfigFiles ?? new List<string>())
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name) && !excluded.Contains(name))
                .Select(name => Path.Combine(Interface.Oxide.ConfigDirectory, name))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void SnapshotFile(string sourcePath, string root, SnapshotResult result)
        {
            if (!File.Exists(sourcePath))
            {
                result.Missing++;
                return;
            }

            try
            {
                var bytes = File.ReadAllBytes(sourcePath);
                var hash = ComputeSha256(bytes);
                var configName = Path.GetFileNameWithoutExtension(sourcePath);
                var destinationDirectory = Path.Combine(root, SafeDirectoryName(configName));
                Directory.CreateDirectory(destinationDirectory);

                if (Directory.GetFiles(destinationDirectory, $"*-{hash}.json", SearchOption.TopDirectoryOnly).Length > 0)
                {
                    result.Unchanged++;
                    return;
                }

                var destinationName = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{hash}.json";
                File.WriteAllBytes(Path.Combine(destinationDirectory, destinationName), bytes);
                result.Created++;

                try
                {
                    JToken.Parse(File.ReadAllText(sourcePath));
                }
                catch (Exception ex)
                {
                    result.Invalid++;
                    PrintWarning($"Snapshotted invalid JSON from {Path.GetFileName(sourcePath)}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                result.Failed++;
                PrintError($"Could not snapshot {sourcePath}: {ex.Message}");
            }
        }

        private string VerifyConfigs()
        {
            var missing = 0;
            var invalid = 0;
            var duplicateArrays = new List<string>();

            foreach (var path in ResolveConfigPaths())
            {
                if (!File.Exists(path))
                {
                    missing++;
                    continue;
                }

                try
                {
                    var root = JToken.Parse(File.ReadAllText(path));
                    AuditKnownUniqueArrays(Path.GetFileName(path), root, duplicateArrays);
                }
                catch (Exception ex)
                {
                    invalid++;
                    PrintWarning($"Invalid JSON in {Path.GetFileName(path)}: {ex.Message}");
                }
            }

            var duplicateSummary = duplicateArrays.Count == 0
                ? "known_unique_arrays=clean"
                : $"known_unique_arrays_with_duplicates={duplicateArrays.Count}: {string.Join(", ", duplicateArrays.Take(8))}";
            return $"Config verification complete: missing={missing}, invalid_json={invalid}, {duplicateSummary}. No files were changed.";
        }

        private void AuditKnownUniqueArrays(string fileName, JToken root, List<string> warnings)
        {
            if (fileName.Equals("WebsiteVipBridge.json", StringComparison.OrdinalIgnoreCase))
            {
                AuditUniqueArray(fileName, root["HeatmapMetrics"], "HeatmapMetrics", warnings);
                AuditUniqueArray(fileName, root["KitPermissionManagedGroups"], "KitPermissionManagedGroups", warnings);
                AuditUniqueArray(fileName, root["KitPermissionPrefixes"], "KitPermissionPrefixes", warnings);
                AuditUniqueArray(fileName, root["ManagedGroups"], "ManagedGroups", warnings);
            }
            else if (fileName.Equals("LiveAdmin.json", StringComparison.OrdinalIgnoreCase))
            {
                AuditUniqueArray(fileName, root["ChatQuickReplies"], "ChatQuickReplies", warnings);
            }
            else if (fileName.Equals("RaidlandsRoamBots.json", StringComparison.OrdinalIgnoreCase))
            {
                var kits = root["Kit Selection"];
                AuditUniqueArray(fileName, kits?["Eligible Kit Names"], "Kit Selection/Eligible Kit Names", warnings);
                AuditUniqueArray(fileName, kits?["Rare High Tier Kit Names"], "Kit Selection/Rare High Tier Kit Names", warnings);
                AuditUniqueArray(fileName, kits?["Weapon Shortnames"], "Kit Selection/Weapon Shortnames", warnings);
            }
            else if (fileName.Equals("ServerInfo.json", StringComparison.OrdinalIgnoreCase))
            {
                var settings = root["settings"];
                AuditUniqueArray(fileName, settings?["StaffGroups"], "settings/StaffGroups", warnings);
                AuditUniqueArray(fileName, settings?["AdminGroups"], "settings/AdminGroups", warnings);
            }
            else if (fileName.Equals("RustReportDiscord.json", StringComparison.OrdinalIgnoreCase))
            {
                AuditUniqueArray(fileName, root["Demo search folders relative to server root"], "Demo search folders", warnings);
            }
        }

        private void AuditUniqueArray(string fileName, JToken token, string path, List<string> warnings)
        {
            var array = token as JArray;

            if (array == null)
            {
                return;
            }

            var values = array
                .Select(item => item.Type == JTokenType.String ? item.Value<string>().Trim() : item.ToString(Formatting.None))
                .ToList();
            var uniqueCount = values.Distinct(StringComparer.OrdinalIgnoreCase).Count();

            if (uniqueCount < values.Count)
            {
                warnings.Add($"{fileName}:{path}={values.Count}/{uniqueCount}");
            }
        }

        private static List<string> DistinctFileNames(IEnumerable<string> values)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var value in values ?? Enumerable.Empty<string>())
            {
                var name = Path.GetFileName((value ?? string.Empty).Trim());

                if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && seen.Add(name))
                {
                    result.Add(name);
                }
            }

            return result;
        }

        private static string SafeDirectoryName(string value)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            return new string((value ?? "config").Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
