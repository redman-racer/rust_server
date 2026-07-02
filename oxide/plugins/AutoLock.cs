using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cronos;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("AutoWipe", "Converted", "2.0.0")]
    [Description("Automates server wiping based on cron expressions.")]
    public class AutoWipe : RustPlugin
    {
        public static AutoWipe Singleton;

        private AutoWipeConfig ConfigInstance;
        private AutoWipeData DataInstance;

        private readonly char[] splitter = new[] { '|' };
        private readonly float wipeCooldown = 60 * 60;
        private readonly float wipeTick = 30;
        private Timer wipeTimer;

        public bool InCooldown() => (DateTime.UtcNow - new DateTime(DataInstance.LastWipeTime)).TotalSeconds <= wipeCooldown;

        #region Oxide Hooks

        private void Init()
        {
            Singleton = this;
            LoadConfigVariables();
            LoadDataVariables();
        }

        private void OnServerInitialized()
        {
            if (InCooldown())
            {
                PrintWarning("Initialized world config [WIPE_COOLDOWN]");
                DataInstance.Wipe?.InitWorld(ConfigInstance.Maps, DataInstance.LastWipeTime);
                return;
            }

            if (DataInstance.NextWipe == null)
            {
                DataInstance.NextWipe = GetUpcomingAvailableWipeImpl();
            }

            var currentWipe = DataInstance.Wipe;
            var wipe = DataInstance.NextWipe ?? currentWipe;
            var justWiped = wipe != null && !wipe.Equals(currentWipe);

            if (justWiped)
            {
                var config = ConfigInstance.GetWipeConfig(wipe);
                DataInstance.LastWipeTime = DateTime.UtcNow.Ticks;
                DataInstance.NextWipe = null;
                ConVar.Server.autoUploadMap = false;

                if (wipe.Temp)
                {
                    ConfigInstance.AvailableWipes.Remove(wipe);
                    PrintWarning("Removed map from list");
                }

                DataInstance.Wipe ??= new Wipe();
                wipe.CopyTo(DataInstance.Wipe);
                PrintWarning("New wipe detected!");
                DataInstance.Wipe?.InitWorld(ConfigInstance.Maps, DataInstance.LastWipeTime);

                if (config.PostWipeCommands != null)
                {
                    for (int i = 0; i < config.PostWipeCommands.Length; i++)
                    {
                        var command = config.PostWipeCommands[i];
                        if (string.IsNullOrEmpty(command))
                            continue;
                        ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), command);
                    }
                }

                if (config.PostWipeDeletes != null)
                {
                    for (int i = 0; i < config.PostWipeDeletes.Length; i++)
                    {
                        var delete = config.PostWipeDeletes[i];
                        if (string.IsNullOrEmpty(delete))
                            continue;

                        if (delete.Contains("*"))
                        {
                            var directoryPath = Path.GetDirectoryName(delete);
                            var searchPattern = Path.GetFileName(delete);

                            if (string.IsNullOrEmpty(directoryPath))
                            {
                                directoryPath = ".";
                            }

                            if (Directory.Exists(directoryPath))
                            {
                                try
                                {
                                    var matchingFiles = Directory.GetFiles(directoryPath, searchPattern);
                                    for (int o = 0; o < matchingFiles.Length; o++)
                                    {
                                        var file = matchingFiles[o];
                                        File.Delete(file);
                                        PrintWarning($"Deleting scheduled file '{file}'");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    PrintError($"Error deleting files matching pattern '{delete}': {ex.Message}");
                                }
                            }
                            continue;
                        }

                        if (File.Exists(delete))
                        {
                            File.Delete(delete);
                            PrintWarning($"Deleting scheduled file '{delete}'");
                            continue;
                        }

                        if (Directory.Exists(delete))
                        {
                            Directory.Delete(delete, true);
                            PrintWarning($"Deleting scheduled directory '{delete}'");
                        }
                    }
                }

                SaveDataVariables();
            }
            else
            {
                PrintWarning("Initialized world config");
                DataInstance.Wipe?.InitWorld(ConfigInstance.Maps, DataInstance.LastWipeTime);
            }

            // Start the tick timer
            if (wipeTimer != null) wipeTimer.Destroy();
            wipeTimer = timer.Every(wipeTick, WipeTickImpl);
        }

        private void Unload()
        {
            if (wipeTimer != null)
            {
                wipeTimer.Destroy();
                wipeTimer = null;
            }
            SaveDataVariables();
        }

        private void OnServerInformationUpdated()
        {
            RefreshHostName();
        }

        #endregion

        #region Chat Commands

        [ChatCommand("wipe")]
        private void WipeChat(BasePlayer player, string cmd, string[] args)
        {
            // Optional check if you want dynamic commands using ConfigInstance.WipeChatCommand:
            if (!string.IsNullOrEmpty(ConfigInstance.WipeChatCommand) && !cmd.Equals(ConfigInstance.WipeChatCommand, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var nextWipe = GetUpcomingWipeImpl();
            if (nextWipe.wipe == null)
            {
                player.ChatMessage("No available wipe found");
                return;
            }

            var result = (nextWipe.next.GetValueOrDefault() - DateTime.UtcNow).TotalSeconds;
            player.ChatMessage($"Next wipe happens in <color=orange>{FormatTime(result).ToLower()}</color>");
        }

        #endregion

        #region Core Core Logic

        private void RefreshHostName()
        {
            if (DataInstance == null) return;

            var lastWipeDate = new DateTime(DataInstance.LastWipeTime);

            if (!string.IsNullOrEmpty(ConVar.Server.hostname) && HasReplacements(ConVar.Server.hostname))
            {
                ConVar.Server.hostname = ProcessString(ConVar.Server.hostname, lastWipeDate);
                PrintWarning("Updated server hostname replacements");
            }
            if (!string.IsNullOrEmpty(ConVar.Server.description) && HasReplacements(ConVar.Server.description))
            {
                ConVar.Server.description = ProcessString(ConVar.Server.description, lastWipeDate);
                PrintWarning("Updated server description replacements");
            }

            return;

            static string ProcessString(string source, DateTime time)
            {
                return source
                    .Replace("[WIPE_DAY]", $"{time.Day}")
                    .Replace("[WIPE_MONTH]", $"{time.Month}")
                    .Replace("[WIPE_YEAR]", $"{time.Year}")
                    .Replace("[WIPE_HOUR]", $"{time.Hour}")
                    .Replace("[WIPE_MINUTE]", $"{time.Minute}");
            }

            static bool HasReplacements(string source)
            {
                return source.Contains("[WIPE_DAY]") ||
                       source.Contains("[WIPE_MONTH]") ||
                       source.Contains("[WIPE_YEAR]") ||
                       source.Contains("[WIPE_HOUR]") ||
                       source.Contains("[WIPE_MINUTE]");
            }
        }

        private void WipeTickImpl()
        {
            if (InCooldown()) return;

            DataInstance.NextWipe = GetUpcomingAvailableWipeImpl();

            if (DataInstance.NextWipe == null) return;

            if (DataInstance.NextWipe.Commands != null)
            {
                for (int i = 0; i < DataInstance.NextWipe.Commands.Length; i++)
                {
                    var command = DataInstance.NextWipe.Commands[i];
                    if (string.IsNullOrEmpty(command))
                        continue;
                    ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), command);
                }
            }

            wipeTimer?.Destroy();
            SaveDataVariables();
        }

        private Wipe GetUpcomingAvailableWipeImpl()
        {
            for (int i = 0; i < ConfigInstance.AvailableWipes.Count; i++)
            {
                var wipe = ConfigInstance.AvailableWipes[i];
                if (wipe.ShouldWipe())
                {
                    return wipe;
                }
            }
            return null;
        }

        private (Wipe wipe, DateTime? next) GetUpcomingWipeImpl()
        {
            var now = DateTime.UtcNow;
            var nextRun = ConfigInstance.AvailableWipes.Select(job => (job, CronExpression.Parse(job.Cron).GetNextOccurrence(now, TimeZoneInfo.Utc)))
                .Where(x => x.Item2.HasValue)
                .OrderBy(x => x.Item2)
                .FirstOrDefault();

