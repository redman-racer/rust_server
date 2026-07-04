using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RaidlandsVehicleTokens", "Raidlands", "1.0.3")]
    [Description("Provides Raidlands vehicle token items backed by SpawnHeli and VehicleLicence spawns.")]
    public class RaidlandsVehicleTokens : RustPlugin
    {
        private const string AdminPermission = "raidlands.vehicletokens.admin";
        private const string BypassPermission = "raidlands.vehicletokens.bypass";
        private const string VehicleHp125Permission = "raidlands.vehicle.hp.125";
        private const string VehicleHp150Permission = "raidlands.vehicle.hp.150";

        [PluginReference]
        private Plugin SpawnHeli;

        [PluginReference]
        private Plugin VehicleLicence;

        private Configuration config;
        private StoredData storedData;
        private readonly HashSet<string> pendingVehicleLicenceSpawns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private class Configuration
        {
            [JsonProperty("Chat Prefix")]
            public string ChatPrefix = "<color=#ce422b>[Raidlands]</color>";

            [JsonProperty("Use Actions")]
            public string[] UseActions = { "unwrap", "open", "use" };

            [JsonProperty("Block Direct VehicleLicence Spawns For Token Vehicles")]
            public bool BlockDirectVehicleLicenceSpawns = true;

            [JsonProperty("Direct VehicleLicence Block Message")]
            public string DirectVehicleLicenceBlockMessage = "That vehicle is token-only on Raidlands. Redeem a vehicle token instead.";

            [JsonProperty("Vehicle Tokens")]
            public List<VehicleTokenDefinition> VehicleTokens = DefaultVehicleTokens();

            [JsonProperty("Kit Grants")]
            public Dictionary<string, Dictionary<string, int>> KitGrants = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase)
            {
                ["pack_vehicle"] = DefaultVehiclePackGrant()
            };
        }

        private class VehicleTokenDefinition
        {
            [JsonProperty("Key")]
            public string Key;

            [JsonProperty("Display Name")]
            public string DisplayName;

            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Backend")]
            public string Backend;

            [JsonProperty("SpawnHeli API Hook")]
            public string SpawnHeliApiHook;

            [JsonProperty("VehicleLicence Type")]
            public string VehicleLicenceType;

            [JsonProperty("Token Shortname")]
            public string TokenShortname = "wrappedgift";

            [JsonProperty("Token Display Name")]
            public string TokenDisplayName;

            [JsonProperty("Token Skin")]
            public ulong TokenSkin;

            [JsonProperty("Require Display Name Match")]
            public bool RequireDisplayNameMatch = true;

            [JsonProperty("Aliases")]
            public string[] Aliases = new string[0];
        }

        private class StoredData
        {
            [JsonProperty("Temporary VehicleLicence Keys")]
            public HashSet<string> TemporaryVehicleLicenceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

            if (config.UseActions == null || config.UseActions.Length == 0)
            {
                config.UseActions = new Configuration().UseActions;
            }

            if (config.VehicleTokens == null || config.VehicleTokens.Count == 0)
            {
                config.VehicleTokens = DefaultVehicleTokens();
            }

            foreach (var definition in config.VehicleTokens)
            {
                NormalizeDefinition(definition);
            }

            if (config.KitGrants == null)
            {
                config.KitGrants = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
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
            permission.RegisterPermission(BypassPermission, this);
            permission.RegisterPermission(VehicleHp125Permission, this);
            permission.RegisterPermission(VehicleHp150Permission, this);
            LoadData();
        }

        private void Unload()
        {
            SaveData();
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void OnNewSave(string filename)
        {
            ClearTemporaryVehicleLicences();
        }

        [ChatCommand("raidvehicle")]
        private void CmdRaidVehicle(BasePlayer player, string command, string[] args)
        {
            HandleVehicleChatCommand(player, args);
        }

        [ChatCommand("vtoken")]
        private void CmdVehicleToken(BasePlayer player, string command, string[] args)
        {
            HandleVehicleChatCommand(player, args);
        }

        [ConsoleCommand("raidlands.vehicle.give")]
        private void CCmdGiveVehicleToken(ConsoleSystem.Arg arg)
        {
            if (!CanUseAdminCommand(arg))
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 2)
            {
                arg.ReplyWith("Usage: raidlands.vehicle.give <playerNameOrSteamId> <vehicleKey|all> [amount]");
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
            if (arg.Args.Length >= 3)
            {
                int.TryParse(arg.GetString(2), out amount);
            }

            amount = Math.Max(1, amount);
            var vehicleKey = arg.GetString(1);
            int given;
            if (string.Equals(vehicleKey, "all", StringComparison.OrdinalIgnoreCase))
            {
                given = GiveVehiclePack(target, amount);
            }
            else
            {
                given = GiveVehicleToken(target, vehicleKey, amount);
            }

            arg.ReplyWith($"Gave {given} vehicle token item(s) to {target.displayName}.");
        }

        [ConsoleCommand("raidlands.vehicle.givepack")]
        private void CCmdGiveVehiclePack(ConsoleSystem.Arg arg)
        {
            if (!CanUseAdminCommand(arg))
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                arg.ReplyWith("Usage: raidlands.vehicle.givepack <playerNameOrSteamId> [amountEach]");
                return;
            }

            var targetName = arg.GetString(0);
            var target = FindPlayer(targetName);
            if (target == null)
            {
                arg.ReplyWith("Player not found.");
                return;
            }

            var amountEach = 5;
            if (arg.Args.Length >= 2)
            {
                int.TryParse(arg.GetString(1), out amountEach);
            }

            var given = GiveVehiclePack(target, Math.Max(1, amountEach));
            arg.ReplyWith($"Gave {given} vehicle token item(s) to {target.displayName}.");
        }

        public int GiveVehicleToken(ulong playerId, string vehicleKey, int amount)
        {
            var player = BasePlayer.FindAwakeOrSleeping(playerId.ToString());
            if (player == null)
            {
                return 0;
            }

            return GiveVehicleToken(player, vehicleKey, Math.Max(1, amount));
        }

        public int GiveVehiclePack(ulong playerId, int amountEach)
        {
            var player = BasePlayer.FindAwakeOrSleeping(playerId.ToString());
            if (player == null)
            {
                return 0;
            }

            return GiveVehiclePack(player, Math.Max(1, amountEach));
        }

        private void OnKitRedeemed(BasePlayer player, string kitName)
        {
            if (player == null || string.IsNullOrWhiteSpace(kitName) || config.KitGrants == null)
            {
                return;
            }

            Dictionary<string, int> grant;
            if (!config.KitGrants.TryGetValue(kitName, out grant) || grant == null)
            {
                return;
            }

            var given = 0;
            foreach (var entry in grant)
            {
                if (entry.Value <= 0)
                {
                    continue;
                }

                given += GiveVehicleToken(player, entry.Key, entry.Value);
            }

            if (given > 0)
            {
                Reply(player, $"Granted {given} vehicle token item(s) from kit '{kitName}'.");
            }
        }

        private object OnItemAction(Item item, string action, BasePlayer player)
        {
            VehicleTokenDefinition definition;
            if (player == null || item == null || !IsConfiguredUseAction(action) || !TryGetDefinitionByToken(item, out definition))
            {
                return null;
            }

            TryRedeemToken(player, item, definition);
            return true;
        }

        private object CanLicensedVehicleSpawn(BasePlayer player, string vehicleType, Vector3 position, Quaternion rotation)
        {
            if (!config.BlockDirectVehicleLicenceSpawns || player == null || string.IsNullOrWhiteSpace(vehicleType))
            {
                return null;
            }

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, BypassPermission))
            {
                return null;
            }

            var definition = FindDefinitionByVehicleLicenceType(vehicleType);
            if (definition == null)
            {
                return null;
            }

            return pendingVehicleLicenceSpawns.Contains(SpawnIntentKey(player.userID, vehicleType))
                ? null
                : config.DirectVehicleLicenceBlockMessage;
        }

        private void OnLicensedVehicleSpawned(BaseEntity entity, BasePlayer player, string vehicleType)
        {
            if (entity == null || player == null || FindDefinitionByVehicleLicenceType(vehicleType) == null)
            {
                return;
            }

            ApplyVehicleHealthBonus(entity, player);
        }

        private void OnLicensedVehicleDeath(ulong playerId, string vehicleType)
        {
            RemoveTemporaryLicence(playerId, vehicleType);
        }

        private void OnLicensedVehicleKilled(BasePlayer player, string vehicleType, bool response)
        {
            if (player != null)
            {
                RemoveTemporaryLicence(player.userID, vehicleType);
            }
        }

        private void OnLicensedVehicleRemoved(ulong playerId, string vehicleType)
        {
            ForgetTemporaryLicence(playerId, vehicleType);
        }

        private void HandleVehicleChatCommand(BasePlayer player, string[] args)
        {
            if (player == null)
            {
                return;
            }

            if (args != null && args.Length > 0 && string.Equals(args[0], "list", StringComparison.OrdinalIgnoreCase))
            {
                Reply(player, $"Token vehicles: {string.Join(", ", EnabledVehicleKeys())}");
                return;
            }

            VehicleTokenDefinition definition = null;
            Item item = null;
            if (args != null && args.Length > 0)
            {
                definition = FindDefinition(args[0]);
                if (definition == null)
                {
                    Reply(player, $"Unknown vehicle token '{args[0]}'. Use /raidvehicle list.");
                    return;
                }

                item = FindToken(player, definition);
            }
            else
            {
                var active = player.GetActiveItem();
                if (active != null)
                {
                    TryGetDefinitionByToken(active, out definition);
                    item = definition == null ? null : active;
                }

                if (definition == null)
                {
                    item = FindAnyToken(player, out definition);
                }
            }

            if (definition == null || item == null)
            {
                Reply(player, "You need a matching vehicle token to do that.");
                return;
            }

            TryRedeemToken(player, item, definition);
        }

        private bool TryRedeemToken(BasePlayer player, Item item, VehicleTokenDefinition definition)
        {
            if (definition == null || !definition.Enabled)
            {
                Reply(player, "That vehicle token is not enabled.");
                return false;
            }

            var spawned = string.Equals(definition.Backend, "SpawnHeli", StringComparison.OrdinalIgnoreCase)
                ? TrySpawnHeliVehicle(player, definition)
                : TrySpawnVehicleLicenceVehicle(player, definition);

            if (!spawned)
            {
                return false;
            }

            ConsumeOne(item);
            Reply(player, $"{definition.DisplayName} spawned. One {definition.TokenDisplayName} was consumed.");
            return true;
        }

        private bool TrySpawnHeliVehicle(BasePlayer player, VehicleTokenDefinition definition)
        {
            if (SpawnHeli == null || !SpawnHeli.IsLoaded)
            {
                Reply(player, "Vehicle tokens are not ready yet. SpawnHeli is not loaded.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.SpawnHeliApiHook))
            {
                Reply(player, "That vehicle token is missing its SpawnHeli API hook.");
                return false;
            }

            var spawned = SpawnHeli.Call(definition.SpawnHeliApiHook, player, null) as BaseEntity;
            if (spawned == null || spawned.IsDestroyed)
            {
                Reply(player, $"{definition.DisplayName} could not be spawned. Your token was not consumed.");
                return false;
            }

            ApplyVehicleHealthBonus(spawned, player);
            return true;
        }

        private bool TrySpawnVehicleLicenceVehicle(BasePlayer player, VehicleTokenDefinition definition)
        {
            if (VehicleLicence == null || !VehicleLicence.IsLoaded)
            {
                Reply(player, "Vehicle tokens are not ready yet. VehicleLicence is not loaded.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.VehicleLicenceType))
            {
                Reply(player, "That vehicle token is missing its VehicleLicence type.");
                return false;
            }

            var alreadyLicensed = HasVehicleLicence(player.userID, definition.VehicleLicenceType);
            var tempKey = TemporaryLicenceKey(player.userID, definition.VehicleLicenceType);
            if (alreadyLicensed && !storedData.TemporaryVehicleLicenceKeys.Contains(tempKey))
            {
                Reply(player, $"You already have a normal {definition.DisplayName} license. Your token was not consumed.");
                return false;
            }

            var addedTemporaryLicence = false;
            if (!alreadyLicensed)
            {
                addedTemporaryLicence = AddVehicleLicence(player.userID, definition.VehicleLicenceType);
                if (!addedTemporaryLicence)
                {
                    Reply(player, $"Could not prepare a {definition.DisplayName} token spawn. Your token was not consumed.");
                    return false;
                }

                storedData.TemporaryVehicleLicenceKeys.Add(tempKey);
                SaveData();
            }

            var spawnIntent = SpawnIntentKey(player.userID, definition.VehicleLicenceType);
            pendingVehicleLicenceSpawns.Add(spawnIntent);

            bool spawned;
            try
            {
                var result = VehicleLicence.Call("SpawnLicensedVehicle", player, definition.VehicleLicenceType, "raidlands.vehicle.token", false);
                spawned = result is bool && (bool)result;
            }
            finally
            {
                pendingVehicleLicenceSpawns.Remove(spawnIntent);
            }

            if (!spawned)
            {
                if (addedTemporaryLicence)
                {
                    RemoveVehicleLicence(player.userID, definition.VehicleLicenceType);
                    ForgetTemporaryLicence(player.userID, definition.VehicleLicenceType);
                }

                Reply(player, $"{definition.DisplayName} could not be spawned. Your token was not consumed.");
                return false;
            }

            var entity = VehicleLicence.Call("GetLicensedVehicle", player.userID, definition.VehicleLicenceType) as BaseEntity;
            if (entity != null && !entity.IsDestroyed)
            {
                ApplyVehicleHealthBonus(entity, player);
            }

            return true;
        }

        private int GiveVehicleToken(BasePlayer player, string vehicleKey, int amount)
        {
            var definition = FindDefinition(vehicleKey);
            if (definition == null || !definition.Enabled)
            {
                return 0;
            }

            var given = 0;
            for (var i = 0; i < amount; i++)
            {
                var item = CreateToken(definition);
                if (item == null)
                {
                    return given;
                }

                player.GiveItem(item);
                given++;
            }

            return given;
        }

        private int GiveVehiclePack(BasePlayer player, int amountEach)
        {
            var given = 0;
            foreach (var definition in config.VehicleTokens)
            {
                if (definition.Enabled)
                {
                    given += GiveVehicleToken(player, definition.Key, amountEach);
                }
            }

            return given;
        }

        private Item CreateToken(VehicleTokenDefinition definition)
        {
            var item = ItemManager.CreateByName(definition.TokenShortname, 1, definition.TokenSkin);
            if (item == null)
            {
                PrintWarning($"Could not create vehicle token item '{definition.TokenShortname}' for {definition.Key}.");
                return null;
            }

            if (!string.IsNullOrWhiteSpace(definition.TokenDisplayName))
            {
                item.name = definition.TokenDisplayName;
            }

            item.MarkDirty();
            return item;
        }

        private bool TryGetDefinitionByToken(Item item, out VehicleTokenDefinition definition)
        {
            definition = null;
            if (item?.info == null)
            {
                return false;
            }

            foreach (var candidate in config.VehicleTokens)
            {
                if (!candidate.Enabled)
                {
                    continue;
                }

                if (!string.Equals(item.info.shortname, candidate.TokenShortname, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (candidate.TokenSkin != 0 && item.skin != candidate.TokenSkin)
                {
                    continue;
                }

                if (candidate.RequireDisplayNameMatch && !string.Equals(item.name ?? "", candidate.TokenDisplayName ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                definition = candidate;
                return true;
            }

            return false;
        }

        private Item FindAnyToken(BasePlayer player, out VehicleTokenDefinition definition)
        {
            definition = null;
            var item = FindAnyTokenInContainer(player.inventory.containerBelt, out definition)
                ?? FindAnyTokenInContainer(player.inventory.containerMain, out definition)
                ?? FindAnyTokenInContainer(player.inventory.containerWear, out definition);

            return item;
        }

        private Item FindAnyTokenInContainer(ItemContainer container, out VehicleTokenDefinition definition)
        {
            definition = null;
            if (container?.itemList == null)
            {
                return null;
            }

            foreach (var item in container.itemList)
            {
                if (TryGetDefinitionByToken(item, out definition))
                {
                    return item;
                }
            }

            return null;
        }

        private Item FindToken(BasePlayer player, VehicleTokenDefinition definition)
        {
            return FindTokenInContainer(player.inventory.containerBelt, definition)
                ?? FindTokenInContainer(player.inventory.containerMain, definition)
                ?? FindTokenInContainer(player.inventory.containerWear, definition);
        }

        private Item FindTokenInContainer(ItemContainer container, VehicleTokenDefinition definition)
        {
            if (container?.itemList == null)
            {
                return null;
            }

            foreach (var item in container.itemList)
            {
                VehicleTokenDefinition candidate;
                if (TryGetDefinitionByToken(item, out candidate) && candidate == definition)
                {
                    return item;
                }
            }

            return null;
        }

        private VehicleTokenDefinition FindDefinition(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            foreach (var definition in config.VehicleTokens)
            {
                if (string.Equals(definition.Key, value, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(definition.DisplayName, value, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(definition.TokenDisplayName, value, StringComparison.OrdinalIgnoreCase))
                {
                    return definition;
                }

                if (definition.Aliases == null)
                {
                    continue;
                }

                foreach (var alias in definition.Aliases)
                {
                    if (string.Equals(alias, value, StringComparison.OrdinalIgnoreCase))
                    {
                        return definition;
                    }
                }
            }

            return null;
        }

        private VehicleTokenDefinition FindDefinitionByVehicleLicenceType(string vehicleType)
        {
            foreach (var definition in config.VehicleTokens)
            {
                if (definition.Enabled
                    && string.Equals(definition.Backend, "VehicleLicence", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(definition.VehicleLicenceType, vehicleType, StringComparison.OrdinalIgnoreCase))
                {
                    return definition;
                }
            }

            return null;
        }

        private string[] EnabledVehicleKeys()
        {
            var keys = new List<string>();
            foreach (var definition in config.VehicleTokens)
            {
                if (definition.Enabled)
                {
                    keys.Add(definition.Key);
                }
            }

            return keys.ToArray();
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

        private void ApplyVehicleHealthBonus(BaseEntity entity, BasePlayer player)
        {
            var combat = entity as BaseCombatEntity;
            if (combat == null || player == null || entity.IsDestroyed)
            {
                return;
            }

            var multiplier = GetVehicleHealthMultiplier(player);
            if (multiplier <= 1f)
            {
                return;
            }

            var baseHealth = combat.MaxHealth();
            if (baseHealth <= 0f)
            {
                return;
            }

            var targetHealth = baseHealth * multiplier;
            combat.InitializeHealth(targetHealth, targetHealth);
        }

        private float GetVehicleHealthMultiplier(BasePlayer player)
        {
            if (permission.UserHasPermission(player.UserIDString, VehicleHp150Permission))
            {
                return 1.5f;
            }

            return permission.UserHasPermission(player.UserIDString, VehicleHp125Permission) ? 1.25f : 1f;
        }

        private bool HasVehicleLicence(ulong playerId, string vehicleType)
        {
            var result = VehicleLicence.Call("HasVehicleLicense", playerId, vehicleType);
            return result is bool && (bool)result;
        }

        private bool AddVehicleLicence(ulong playerId, string vehicleType)
        {
            var result = VehicleLicence.Call("AddVehicleLicense", playerId, vehicleType);
            return result is bool && (bool)result;
        }

        private bool RemoveVehicleLicence(ulong playerId, string vehicleType)
        {
            var result = VehicleLicence.Call("RemoveVehicleLicense", playerId, vehicleType);
            return result is bool && (bool)result;
        }

        private void RemoveTemporaryLicence(ulong playerId, string vehicleType)
        {
            var key = TemporaryLicenceKey(playerId, vehicleType);
            if (!storedData.TemporaryVehicleLicenceKeys.Contains(key))
            {
                return;
            }

            if (VehicleLicence != null && VehicleLicence.IsLoaded)
            {
                RemoveVehicleLicence(playerId, vehicleType);
            }

            ForgetTemporaryLicence(playerId, vehicleType);
        }

        private void ForgetTemporaryLicence(ulong playerId, string vehicleType)
        {
            var key = TemporaryLicenceKey(playerId, vehicleType);
            if (storedData.TemporaryVehicleLicenceKeys.Remove(key))
            {
                SaveData();
            }
        }

        private void ClearTemporaryVehicleLicences()
        {
            if (storedData == null)
            {
                LoadData();
            }

            if (VehicleLicence != null && VehicleLicence.IsLoaded)
            {
                foreach (var key in new List<string>(storedData.TemporaryVehicleLicenceKeys))
                {
                    ulong playerId;
                    string vehicleType;
                    if (TryParseTemporaryLicenceKey(key, out playerId, out vehicleType))
                    {
                        RemoveVehicleLicence(playerId, vehicleType);
                    }
                }
            }

            storedData.TemporaryVehicleLicenceKeys.Clear();
            SaveData();
        }

        private string TemporaryLicenceKey(ulong playerId, string vehicleType)
        {
            return $"{playerId}:{vehicleType}".ToLowerInvariant();
        }

        private string SpawnIntentKey(ulong playerId, string vehicleType)
        {
            return $"{playerId}:{vehicleType}".ToLowerInvariant();
        }

        private bool TryParseTemporaryLicenceKey(string key, out ulong playerId, out string vehicleType)
        {
            playerId = 0;
            vehicleType = null;
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            var parts = key.Split(new[] { ':' }, 2);
            if (parts.Length != 2 || !ulong.TryParse(parts[0], out playerId))
            {
                return false;
            }

            vehicleType = parts[1];
            return !string.IsNullOrWhiteSpace(vehicleType);
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
                storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("RaidlandsVehicleTokens/temp_licenses") ?? new StoredData();
            }
            catch
            {
                storedData = new StoredData();
            }

            if (storedData.TemporaryVehicleLicenceKeys == null)
            {
                storedData.TemporaryVehicleLicenceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject("RaidlandsVehicleTokens/temp_licenses", storedData ?? new StoredData());
        }

        private void Reply(BasePlayer player, string message)
        {
            player.ChatMessage($"{config.ChatPrefix} {message}");
        }

        private static void NormalizeDefinition(VehicleTokenDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(definition.TokenShortname))
            {
                definition.TokenShortname = "wrappedgift";
            }

            if (string.IsNullOrWhiteSpace(definition.TokenDisplayName))
            {
                definition.TokenDisplayName = $"{definition.DisplayName} Token";
            }

            if (definition.Aliases == null)
            {
                definition.Aliases = new string[0];
            }
        }

        private static Dictionary<string, int> DefaultVehiclePackGrant()
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["minicopter"] = 5,
                ["scrap_transport_helicopter"] = 5,
                ["attack_helicopter"] = 5,
                ["rhib"] = 5,
                ["tugboat"] = 5,
                ["solo_submarine"] = 5,
                ["duo_submarine"] = 5,
                ["snowmobile"] = 5,
                ["hot_air_balloon"] = 5
            };
        }

        private static List<VehicleTokenDefinition> DefaultVehicleTokens()
        {
            return new List<VehicleTokenDefinition>
            {
                SpawnHeliToken("minicopter", "Minicopter", "API_SpawnMinicopter", new[] { "mini", "minicopter" }),
                SpawnHeliToken("scrap_transport_helicopter", "Scrap Transport Helicopter", "API_SpawnScrapTransportHelicopter", new[] { "scrapheli", "scrap", "scraptransport" }),
                SpawnHeliToken("attack_helicopter", "Attack Helicopter", "API_SpawnAttackHelicopter", new[] { "attackheli", "attack" }),
                VehicleLicenceToken("rhib", "RHIB", "RHIB", new[] { "rhib" }),
                VehicleLicenceToken("tugboat", "Tugboat", "Tugboat", new[] { "tug", "tugboat" }),
                VehicleLicenceToken("solo_submarine", "Solo Submarine", "SubmarineSolo", new[] { "subsolo", "solo", "solosub" }),
                VehicleLicenceToken("duo_submarine", "Duo Submarine", "SubmarineDuo", new[] { "subduo", "duo", "duosub" }),
                VehicleLicenceToken("snowmobile", "Snowmobile", "Snowmobile", new[] { "snow", "snowmobile" }),
                VehicleLicenceToken("hot_air_balloon", "Hot Air Balloon", "HotAirBalloon", new[] { "hab", "hotairballoon", "balloon" })
            };
        }

        private static VehicleTokenDefinition SpawnHeliToken(string key, string displayName, string hook, string[] aliases)
        {
            return new VehicleTokenDefinition
            {
                Key = key,
                DisplayName = displayName,
                Backend = "SpawnHeli",
                SpawnHeliApiHook = hook,
                TokenDisplayName = $"{displayName} Token",
                Aliases = aliases
            };
        }

        private static VehicleTokenDefinition VehicleLicenceToken(string key, string displayName, string vehicleLicenceType, string[] aliases)
        {
            return new VehicleTokenDefinition
            {
                Key = key,
                DisplayName = displayName,
                Backend = "VehicleLicence",
                VehicleLicenceType = vehicleLicenceType,
                TokenDisplayName = $"{displayName} Token",
                Aliases = aliases
            };
        }
    }
}
