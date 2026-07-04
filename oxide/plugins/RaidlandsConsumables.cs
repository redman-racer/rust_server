using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("RaidlandsConsumables", "Raidlands", "1.0.7")]
    [Description("Provides Raidlands Super Serum consumables with persistent tea and pie style buffs until death.")]
    public class RaidlandsConsumables : RustPlugin
    {
        private const string AdminPermission = "raidlands.consumables.admin";

        private Configuration config;
        private StoredData storedData;
        private Timer refreshTimer;
        private readonly Dictionary<global::Modifier.ModifierType, float> modifierValues = new Dictionary<global::Modifier.ModifierType, float>();

        private class Configuration
        {
            [JsonProperty("Chat Prefix")]
            public string ChatPrefix = "<color=#ce422b>[Raidlands]</color>";

            [JsonProperty("Super Serum Item")]
            public SerumItem SuperSerumItem = new SerumItem();

            [JsonProperty("Use Actions")]
            public string[] UseActions = { "consume", "eat", "drink", "use" };

            [JsonProperty("Refresh Interval Seconds")]
            public float RefreshIntervalSeconds = 30f;

            [JsonProperty("Native Modifier Duration Seconds")]
            public float NativeModifierDurationSeconds = 90f;

            [JsonProperty("Modifier Values")]
            public Dictionary<string, float> ModifierValues = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["Max_Health"] = 0.2f,
                ["Ore_Yield"] = 1f,
                ["Wood_Yield"] = 1f,
                ["Scrap_Yield"] = 1f,
                ["Radiation_Resistance"] = 1f,
                ["Radiation_Exposure_Resistance"] = 1f,
                ["Comfort"] = 1f,
                ["Crafting_Quality"] = 1f,
                ["Warming"] = 1f,
                ["Cooling"] = 1f,
                ["CoreTemperatureMinAdjustment"] = 1f,
                ["CoreTemperatureMaxAdjustment"] = 1f,
                ["VisionCare"] = 1f,
                ["MetabolismBooster"] = 1f,
                ["Harvesting"] = 1f,
                ["DigestionBoost"] = 1f,
                ["Farming_BetterGenes"] = 1f,
                ["Clotting"] = 1f,
                ["HunterVision"] = 1f,
                ["DigestionBoostTimeMod"] = 1f
            };

            [JsonProperty("Metabolism Refresh")]
            public MetabolismRefresh MetabolismRefresh = new MetabolismRefresh();

            [JsonProperty("Source Buff Items")]
            public string[] SourceBuffItems =
            {
                "maxhealthtea.pure",
                "oretea.pure",
                "woodtea.pure",
                "scraptea.pure",
                "pureharvestingtea",
                "purecoolingtea",
                "purewarmingtea",
                "radiationresisttea.pure",
                "radiationremovetea.pure",
                "healingtea.pure",
                "purecraftingtea_quality",
                "pie.apple",
                "pie.bear",
                "pie.bigcat",
                "pie.chicken",
                "pie.crocodile",
                "pie.fish",
                "pie.hunters",
                "pie.pork",
                "pie.pumpkin",
                "pie.survivors"
            };

            [JsonProperty("Kit Grants")]
            public Dictionary<string, int> KitGrants = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["custom.pending.super_serum"] = 1,
                ["pack_super_serum"] = 1
            };
        }

        private class SerumItem
        {
            [JsonProperty("Shortname")]
            public string Shortname = "supertea";

            [JsonProperty("Display Name")]
            public string DisplayName = "Super Serum";

            [JsonProperty("Skin")]
            public ulong Skin;

            [JsonProperty("Require Display Name Match")]
            public bool RequireDisplayNameMatch = true;
        }

        private class MetabolismRefresh
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Minimum Calories")]
            public float MinimumCalories = 500f;

            [JsonProperty("Minimum Hydration")]
            public float MinimumHydration = 250f;

            [JsonProperty("Minimum Oxygen")]
            public float MinimumOxygen = 1f;

            [JsonProperty("Clear Radiation")]
            public bool ClearRadiation = true;

            [JsonProperty("Clear Bleeding")]
            public bool ClearBleeding = true;

            [JsonProperty("Instant Heal On Consume")]
            public float InstantHealOnConsume = 100f;
        }

        private class StoredData
        {
            [JsonProperty("Active Players")]
            public HashSet<ulong> ActivePlayers = new HashSet<ulong>();
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
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

            if (config.SuperSerumItem == null)
            {
                config.SuperSerumItem = new SerumItem();
            }

            if (config.UseActions == null || config.UseActions.Length == 0)
            {
                config.UseActions = new Configuration().UseActions;
            }

            if (config.ModifierValues == null)
            {
                config.ModifierValues = new Configuration().ModifierValues;
            }
            else
            {
                foreach (var entry in new Configuration().ModifierValues)
                {
                    if (!config.ModifierValues.ContainsKey(entry.Key))
                    {
                        config.ModifierValues[entry.Key] = entry.Value;
                    }
                }
            }

            if (config.RefreshIntervalSeconds < 5f)
            {
                config.RefreshIntervalSeconds = 30f;
            }

            float maxHealthValue;
            if (config.ModifierValues.TryGetValue("Max_Health", out maxHealthValue) && (maxHealthValue > 0.25f || maxHealthValue < 0f))
            {
                config.ModifierValues["Max_Health"] = 0.2f;
                PrintWarning("Corrected Super Serum Max_Health modifier to 0.2 for the intended 120 HP cap.");
            }

            if (config.NativeModifierDurationSeconds < config.RefreshIntervalSeconds + 5f)
            {
                config.NativeModifierDurationSeconds = Math.Max(30f, config.RefreshIntervalSeconds * 3f);
            }

            if (config.MetabolismRefresh == null)
            {
                config.MetabolismRefresh = new MetabolismRefresh();
            }

            if (config.SourceBuffItems == null)
            {
                config.SourceBuffItems = new Configuration().SourceBuffItems;
            }

            if (config.KitGrants == null)
            {
                config.KitGrants = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void Init()
        {
            permission.RegisterPermission(AdminPermission, this);
            LoadData();
            BuildModifierCache();
        }

        private void OnServerInitialized()
        {
            StartRefreshTimer();
            foreach (var player in BasePlayer.activePlayerList)
            {
                RefreshPlayerSerum(player);
            }
        }

        private void Unload()
        {
            refreshTimer?.Destroy();
            SaveData();
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void OnNewSave(string filename)
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                ClearPlayerEffects(player);
            }

            storedData.ActivePlayers.Clear();
            SaveData();
        }

        [ChatCommand("superserum")]
        private void CmdSuperSerum(BasePlayer player, string command, string[] args)
        {
            if (player == null)
            {
                return;
            }

            var item = FindSerum(player);
            if (item == null)
            {
                Reply(player, "You need a Super Serum to do that.");
                return;
            }

            ConsumeOne(item);
            ActivateSerum(player);
        }

        [ConsoleCommand("raidlands.serum.give")]
        private void CCmdGiveSerum(ConsoleSystem.Arg arg)
        {
            if (!CanUseAdminCommand(arg))
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                arg.ReplyWith("Usage: raidlands.serum.give <playerNameOrSteamId> [amount]");
                return;
            }

            var targetName = arg.GetString(0);
            var target = FindPlayer(targetName);
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

            var given = GiveSerum(target, Math.Max(1, amount));
            arg.ReplyWith($"Gave {given} Super Serum item(s) to {target.displayName}.");
        }

        public int GiveSuperSerum(ulong playerId, int amount)
        {
            var player = BasePlayer.FindAwakeOrSleeping(playerId.ToString());
            if (player == null)
            {
                return 0;
            }

            return GiveSerum(player, Math.Max(1, amount));
        }

        public bool IsSuperSerumActive(ulong playerId)
        {
            return storedData.ActivePlayers.Contains(playerId);
        }

        private void OnKitRedeemed(BasePlayer player, string kitName)
        {
            if (player == null || string.IsNullOrWhiteSpace(kitName) || config.KitGrants == null)
            {
                return;
            }

            int amount;
            if (!config.KitGrants.TryGetValue(kitName, out amount) || amount <= 0)
            {
                return;
            }

            var given = GiveSerum(player, amount);
            if (given > 0)
            {
                Reply(player, $"Granted {given} Super Serum item(s) from kit '{kitName}'.");
            }
        }

        private void OnPlayerInit(BasePlayer player)
        {
            timer.Once(2f, () => RefreshPlayerSerum(player));
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            DeactivateSerum(player);
        }

        private void OnItemUse(Item item, int amountToUse)
        {
            if (!IsSerum(item))
            {
                return;
            }

            var player = item?.parent?.playerOwner;
            if (player != null)
            {
                ActivateSerum(player);
            }
        }

        private object OnItemAction(Item item, string action, BasePlayer player)
        {
            if (player == null || item == null || !IsConfiguredUseAction(action) || !IsSerum(item))
            {
                return null;
            }

            ConsumeOne(item);
            ActivateSerum(player);
            return true;
        }

        private void ActivateSerum(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            storedData.ActivePlayers.Add(player.userID);
            ApplySerumEffects(player, true);
            SaveData();
            Reply(player, "Super Serum active. Buffs will persist until death.");
        }

        private void DeactivateSerum(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            if (storedData.ActivePlayers.Remove(player.userID))
            {
                ClearPlayerEffects(player);
                SaveData();
            }
        }

        private void RefreshPlayerSerum(BasePlayer player)
        {
            if (player == null || !storedData.ActivePlayers.Contains(player.userID))
            {
                return;
            }

            ApplySerumEffects(player, false);
        }

        private void StartRefreshTimer()
        {
            refreshTimer?.Destroy();
            refreshTimer = timer.Every(Math.Max(5f, config.RefreshIntervalSeconds), RefreshActivePlayers);
        }

        private void RefreshActivePlayers()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                RefreshPlayerSerum(player);
            }
        }

        private void ApplySerumEffects(BasePlayer player, bool firstApply)
        {
            if (player == null || player.IsDead())
            {
                return;
            }

            if (player.modifiers != null)
            {
                foreach (var entry in modifierValues)
                {
                    player.modifiers.RemoveVariable(entry.Key);
                }

                player.modifiers.RemoveFromSource(global::Modifier.ModifierSource.Tea);

                foreach (var entry in modifierValues)
                {
                    if (Math.Abs(entry.Value) <= 0.001f)
                    {
                        continue;
                    }

                    var modifier = new global::Modifier();
                    modifier.Init(
                        entry.Key,
                        global::Modifier.ModifierSource.Tea,
                        entry.Value,
                        config.NativeModifierDurationSeconds,
                        config.NativeModifierDurationSeconds);
                    player.modifiers.Add(modifier);
                }

                player.modifiers.SendChangesToClient();
            }

            if (config.MetabolismRefresh.Enabled && player.metabolism != null)
            {
                var meta = player.metabolism;
                meta.calories.value = Math.Max(meta.calories.value, config.MetabolismRefresh.MinimumCalories);
                meta.hydration.value = Math.Max(meta.hydration.value, config.MetabolismRefresh.MinimumHydration);
                meta.oxygen.value = Math.Max(meta.oxygen.value, config.MetabolismRefresh.MinimumOxygen);

                if (config.MetabolismRefresh.ClearRadiation)
                {
                    meta.radiation_level.value = 0f;
                    meta.radiation_poison.value = 0f;
                }

                if (config.MetabolismRefresh.ClearBleeding)
                {
                    meta.bleeding.value = 0f;
                }

                meta.SendChanges();
            }

            if (firstApply && config.MetabolismRefresh.InstantHealOnConsume > 0f)
            {
                player.Heal(config.MetabolismRefresh.InstantHealOnConsume);
            }

            player.SendNetworkUpdateImmediate();
        }

        private void ClearPlayerEffects(BasePlayer player)
        {
            if (player == null || player.modifiers == null)
            {
                return;
            }

            foreach (var entry in modifierValues)
            {
                player.modifiers.RemoveVariable(entry.Key);
            }

            player.modifiers.RemoveFromSource(global::Modifier.ModifierSource.Tea);
            player.modifiers.SendChangesToClient();

            player.SendNetworkUpdateImmediate();
        }

        private int GiveSerum(BasePlayer player, int amount)
        {
            var given = 0;
            for (var i = 0; i < amount; i++)
            {
                var item = CreateSerum();
                if (item == null)
                {
                    return given;
                }

                player.GiveItem(item);
                given++;
            }

            return given;
        }

        private Item CreateSerum()
        {
            var item = ItemManager.CreateByName(config.SuperSerumItem.Shortname, 1, config.SuperSerumItem.Skin);
            if (item == null)
            {
                PrintWarning($"Could not create Super Serum item '{config.SuperSerumItem.Shortname}'.");
                return null;
            }

            if (!string.IsNullOrWhiteSpace(config.SuperSerumItem.DisplayName))
            {
                item.name = config.SuperSerumItem.DisplayName;
            }

            item.MarkDirty();
            return item;
        }

        private Item FindSerum(BasePlayer player)
        {
            return FindSerumInContainer(player.inventory.containerBelt)
                ?? FindSerumInContainer(player.inventory.containerMain)
                ?? FindSerumInContainer(player.inventory.containerWear);
        }

        private Item FindSerumInContainer(ItemContainer container)
        {
            if (container?.itemList == null)
            {
                return null;
            }

            foreach (var item in container.itemList)
            {
                if (IsSerum(item))
                {
                    return item;
                }
            }

            return null;
        }

        private bool IsSerum(Item item)
        {
            if (item?.info == null || config.SuperSerumItem == null)
            {
                return false;
            }

            if (!string.Equals(item.info.shortname, config.SuperSerumItem.Shortname, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (config.SuperSerumItem.Skin != 0 && item.skin != config.SuperSerumItem.Skin)
            {
                return false;
            }

            return !config.SuperSerumItem.RequireDisplayNameMatch
                || string.Equals(item.name ?? "", config.SuperSerumItem.DisplayName ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsConfiguredUseAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            foreach (var useAction in config.UseActions)
            {
                if (string.Equals(action, useAction, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void ConsumeOne(Item item)
        {
            if (item.amount > 1)
            {
                item.amount -= 1;
                item.MarkDirty();
                return;
            }

            item.RemoveFromContainer();
            item.Remove();
        }

        private void BuildModifierCache()
        {
            modifierValues.Clear();
            foreach (var entry in config.ModifierValues)
            {
                global::Modifier.ModifierType type;
                if (Enum.TryParse(entry.Key, true, out type))
                {
                    modifierValues[type] = entry.Value;
                }
                else
                {
                    PrintWarning($"Unknown Rust modifier '{entry.Key}' in config; skipping it.");
                }
            }
        }

        private bool CanUseAdminCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Connection == null || arg.Connection.authLevel > 0 || arg.IsAdmin)
            {
                return true;
            }

            var player = arg.Connection.player as BasePlayer;
            return player != null && permission.UserHasPermission(player.UserIDString, AdminPermission);
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

        private void LoadData()
        {
            try
            {
                storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("RaidlandsConsumables/super_serum_players") ?? new StoredData();
            }
            catch
            {
                storedData = new StoredData();
            }

            if (storedData.ActivePlayers == null)
            {
                storedData.ActivePlayers = new HashSet<ulong>();
            }
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject("RaidlandsConsumables/super_serum_players", storedData ?? new StoredData());
        }

        private void Reply(BasePlayer player, string message)
        {
            player.ChatMessage($"{config.ChatPrefix} {message}");
        }
    }
}
