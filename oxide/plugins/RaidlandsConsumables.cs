using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("RaidlandsConsumables", "Raidlands", "1.0.13")]
    [Description("Provides Raidlands Super Serum consumables with persistent tea and pie style buffs until death.")]
    public class RaidlandsConsumables : RustPlugin
    {
        private const string AdminPermission = "raidlands.consumables.admin";

        private Configuration config;
        private StoredData storedData;
        private Timer refreshTimer;
        private readonly Dictionary<global::Modifier.ModifierType, float> modifierValues = new Dictionary<global::Modifier.ModifierType, float>();
        private readonly List<global::ModifierDefintion> nativeSourceModifiers = new List<global::ModifierDefintion>();

        private class Configuration
        {
            [JsonProperty("Chat Prefix")]
            public string ChatPrefix = "<color=#ce422b>[Raidlands]</color>";

            [JsonProperty("Super Serum Item")]
            public SerumItem SuperSerumItem = new SerumItem();

            [JsonProperty("Use Actions")]
            public string[] UseActions = { "consume", "eat", "drink", "use" };

            [JsonProperty("Consumption Effect")]
            public ConsumptionEffect ConsumptionEffect = new ConsumptionEffect();

            [JsonProperty("Refresh Interval Seconds")]
            public float RefreshIntervalSeconds = 30f;

            [JsonProperty("Native Modifier Duration Seconds")]
            public float NativeModifierDurationSeconds = 90f;

            [JsonProperty("Use Native Source Item Modifiers")]
            public bool UseNativeSourceItemModifiers = true;

            [JsonProperty("Force Target Max Health")]
            public bool ForceTargetMaxHealth = true;

            [JsonProperty("Target Max Health")]
            public float TargetMaxHealth = 120f;

            [JsonProperty("Modifier Values")]
            public Dictionary<string, float> ModifierValues = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

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

        private class ConsumptionEffect
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Effect Prefab")]
            public string EffectPrefab = "assets/bundled/prefabs/fx/gestures/drink_tea.prefab";
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

            if (config.ConsumptionEffect == null)
            {
                config.ConsumptionEffect = new ConsumptionEffect();
            }

            if (string.IsNullOrWhiteSpace(config.ConsumptionEffect.EffectPrefab))
            {
                config.ConsumptionEffect.EffectPrefab = new ConsumptionEffect().EffectPrefab;
            }

            if (config.ModifierValues == null || config.UseNativeSourceItemModifiers)
            {
                config.ModifierValues = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            }

            if (config.RefreshIntervalSeconds < 5f)
            {
                config.RefreshIntervalSeconds = 30f;
            }

            float maxHealthValue;
            if (!config.UseNativeSourceItemModifiers
                && config.ModifierValues.TryGetValue("Max_Health", out maxHealthValue)
                && (maxHealthValue > 0.25f || maxHealthValue < 0f))
            {
                config.ModifierValues["Max_Health"] = 0.2f;
                PrintWarning("Corrected Super Serum Max_Health modifier to 0.2 for the intended 120 HP cap.");
            }

            if (config.NativeModifierDurationSeconds < config.RefreshIntervalSeconds + 5f)
            {
                config.NativeModifierDurationSeconds = Math.Max(30f, config.RefreshIntervalSeconds * 3f);
            }

            if (config.TargetMaxHealth < 100f || config.TargetMaxHealth > 200f)
            {
                config.TargetMaxHealth = 120f;
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
            BuildModifierCache();
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

        private object OnRaidlandsCreateKitItem(string shortname, int amount, ulong skin, string displayName)
        {
            return CreateSerumForShortname(shortname, amount);
        }

        private object OnRaidlandsCreateLootItem(string shortname, int amount, ulong skin, string displayName)
        {
            return CreateSerumForShortname(shortname, amount);
        }

        private Item CreateSerumForShortname(string shortname, int amount)
        {
            if (!IsSuperSerumKitShortname(shortname))
            {
                return null;
            }

            return CreateSerum(Math.Max(1, amount));
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
            PlayConsumptionEffect(player);
            ApplySerumEffects(player, true);
            SaveData();
            Reply(player, "Super Serum active. Buffs will persist until death.");
        }

        private void PlayConsumptionEffect(BasePlayer player)
        {
            if (player == null
                || config.ConsumptionEffect == null
                || !config.ConsumptionEffect.Enabled
                || string.IsNullOrWhiteSpace(config.ConsumptionEffect.EffectPrefab))
            {
                return;
            }

            try
            {
                Effect.server.Run(config.ConsumptionEffect.EffectPrefab, player.transform.position);
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not play Super Serum consume effect '{config.ConsumptionEffect.EffectPrefab}': {ex.Message}");
            }
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
                player.modifiers.RemoveFromSource(global::Modifier.ModifierSource.Tea);

                if (config.UseNativeSourceItemModifiers)
                {
                    ApplyNativeSourceModifiers(player);
                }
                else
                {
                    ApplyManualModifierValues(player);
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
                ClampSerumHealth(player);
                timer.Once(0.1f, () => ClampSerumHealth(player));
            }
            else
            {
                ClampSerumHealth(player);
                timer.Once(0.1f, () => ClampSerumHealth(player));
            }

            player.SendNetworkUpdateImmediate();
        }

        private void TopOffSerumHealth(BasePlayer player)
        {
            NormalizeSerumHealth(player, true);
        }

        private void ClampSerumHealth(BasePlayer player)
        {
            NormalizeSerumHealth(player, false);
        }

        private void NormalizeSerumHealth(BasePlayer player, bool allowHeal)
        {
            if (player == null || player.IsDead())
            {
                return;
            }

            var targetHealth = TargetSerumHealth(player);
            if (targetHealth <= 0f)
            {
                return;
            }

            var currentHealth = player.Health();
            if (currentHealth > targetHealth + 0.01f)
            {
                player.SetHealth(targetHealth);
                player.SendNetworkUpdateImmediate();
                return;
            }

            if (!allowHeal)
            {
                return;
            }

            var missingHealth = targetHealth - currentHealth;
            if (missingHealth <= 0.01f)
            {
                return;
            }

            player.Heal(missingHealth);
            player.SendNetworkUpdateImmediate();
        }

        private float TargetSerumHealth(BasePlayer player)
        {
            if (player == null)
            {
                return 0f;
            }

            if (config.ForceTargetMaxHealth && config.TargetMaxHealth > 0f)
            {
                return config.TargetMaxHealth;
            }

            return player.MaxHealth();
        }

        private void ApplyNativeSourceModifiers(BasePlayer player)
        {
            if (nativeSourceModifiers.Count == 0)
            {
                BuildNativeSourceModifierCache();
            }

            var definitions = nativeSourceModifiers;
            var targetMaxHealthModifier = CreateTargetMaxHealthModifier(player);
            if (targetMaxHealthModifier != null)
            {
                definitions = new List<global::ModifierDefintion>(nativeSourceModifiers.Count + 1);
                definitions.AddRange(nativeSourceModifiers);
                definitions.Add(targetMaxHealthModifier);
            }

            if (definitions.Count == 0)
            {
                PrintWarning("No native Super Serum source modifiers were available; check Source Buff Items.");
                return;
            }

            global::PlayerModifiers.AddToPlayer(player, definitions, 1f, 1f);
        }

        private global::ModifierDefintion CreateTargetMaxHealthModifier(BasePlayer player)
        {
            if (!config.ForceTargetMaxHealth || config.TargetMaxHealth <= 0f || player == null)
            {
                return null;
            }

            var baseMaxHealth = Math.Max(1f, player.StartMaxHealth());
            var value = (config.TargetMaxHealth / baseMaxHealth) - 1f;
            if (value <= 0.001f)
            {
                return null;
            }

            return new global::ModifierDefintion
            {
                type = global::Modifier.ModifierType.Max_Health,
                source = global::Modifier.ModifierSource.Tea,
                value = value,
                duration = config.NativeModifierDurationSeconds
            };
        }

        private void ApplyManualModifierValues(BasePlayer player)
        {
            foreach (var entry in modifierValues)
            {
                player.modifiers.RemoveVariable(entry.Key);
            }

            var definitions = new List<global::ModifierDefintion>();
            foreach (var entry in modifierValues)
            {
                if (Math.Abs(entry.Value) <= 0.001f)
                {
                    continue;
                }

                definitions.Add(new global::ModifierDefintion
                {
                    type = entry.Key,
                    source = global::Modifier.ModifierSource.Tea,
                    value = entry.Value,
                    duration = config.NativeModifierDurationSeconds
                });
            }

            if (definitions.Count > 0)
            {
                player.modifiers.Add(definitions, 1f, 1f);
            }
        }

        private void ClearPlayerEffects(BasePlayer player)
        {
            if (player == null || player.modifiers == null)
            {
                return;
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
            return CreateSerum(1);
        }

        private Item CreateSerum(int amount)
        {
            var item = ItemManager.CreateByName(config.SuperSerumItem.Shortname, Math.Max(1, amount), config.SuperSerumItem.Skin);
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

        private bool IsSuperSerumKitShortname(string shortname)
        {
            return string.Equals(shortname, config.SuperSerumItem.Shortname, StringComparison.OrdinalIgnoreCase)
                || string.Equals(shortname, "maxhealthtea.pure", StringComparison.OrdinalIgnoreCase);
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
            if (config.UseNativeSourceItemModifiers)
            {
                BuildNativeSourceModifierCache();
                return;
            }

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

        private void BuildNativeSourceModifierCache()
        {
            nativeSourceModifiers.Clear();
            if (config.SourceBuffItems == null)
            {
                return;
            }

            foreach (var shortname in config.SourceBuffItems)
            {
                if (string.IsNullOrWhiteSpace(shortname))
                {
                    continue;
                }

                var itemDefinition = ItemManager.FindItemDefinition(shortname);
                if (itemDefinition == null)
                {
                    PrintWarning($"Could not find Super Serum source item '{shortname}'.");
                    continue;
                }

                var itemMods = itemDefinition.itemMods;
                if (itemMods == null)
                {
                    continue;
                }

                foreach (var itemMod in itemMods)
                {
                    var consume = itemMod as global::ItemModConsume;
                    var consumable = consume?.GetConsumable();
                    if (consumable?.modifiers == null)
                    {
                        continue;
                    }

                    foreach (var sourceModifier in consumable.modifiers)
                    {
                        if (sourceModifier == null || Math.Abs(sourceModifier.value) <= 0.001f)
                        {
                            continue;
                        }

                        if (config.ForceTargetMaxHealth && sourceModifier.type == global::Modifier.ModifierType.Max_Health)
                        {
                            continue;
                        }

                        nativeSourceModifiers.Add(new global::ModifierDefintion
                        {
                            type = sourceModifier.type,
                            source = sourceModifier.source,
                            value = sourceModifier.value,
                            duration = sourceModifier.duration
                        });
                    }
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
