using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Rust;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RaidlandsVehicleTokens", "Raidlands", "1.0.6")]
    [Description("Provides Raidlands vehicle token items backed by SpawnHeli and VehicleLicence spawns.")]
    public class RaidlandsVehicleTokens : RustPlugin
    {
        private const string AdminPermission = "raidlandsvehicletokens.admin";
        private const string BypassPermission = "raidlandsvehicletokens.bypass";
        private const string VehicleHp125Permission = "raidlandsvehicletokens.vehicle.hp.125";
        private const string VehicleHp150Permission = "raidlandsvehicletokens.vehicle.hp.150";
        private const string LegacyAdminPermission = "raidlands.vehicletokens.admin";
        private const string LegacyBypassPermission = "raidlands.vehicletokens.bypass";
        private const string LegacyVehicleHp125Permission = "raidlands.vehicle.hp.125";
        private const string LegacyVehicleHp150Permission = "raidlands.vehicle.hp.150";
        private const string DefaultLegacyTokenShortname = "wrappedgift";
        private const string DefaultParentTokenShortname = "scrap";
        private const string CustomTokenShortnamePrefix = "raidlands.vehicle.token.";
        private const string DefaultTokenIconDataFolder = "RaidlandsVehicleTokens";
        private const int DefaultTokenMaxStackSize = 100;
        private const int MaximumTokenMaxStackSize = 65535;
        private const float DefaultDroppedTokenOwnerSearchRadius = 8f;
        private const float DefaultTokenSpawnMinForwardDistance = 5f;
        private const float DefaultTokenSpawnMaxForwardDistance = 15f;
        private const float DefaultTokenSpawnYawOffsetDegrees = -90f;
        private const int GroundLayerMask = Layers.Solid | Layers.Mask.Water | Layers.Construction;

        [PluginReference]
        private Plugin SpawnHeli;

        [PluginReference]
        private Plugin VehicleLicence;

        [PluginReference]
        private Plugin CustomItemDefinitions;

        private Configuration config;
        private StoredData storedData;
        private readonly HashSet<string> pendingVehicleLicenceSpawns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ItemDefinition> customTokenDefinitions = new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, uint> tokenIconFileIds = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> warnedMissingIconPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool warnedCIDUnavailable;

        private class Configuration
        {
            [JsonProperty("Chat Prefix")]
            public string ChatPrefix = "<color=#ce422b>[Raidlands]</color>";

            [JsonProperty("Use Actions")]
            public string[] UseActions = { "unwrap", "open", "use" };

            [JsonProperty("Redeem On Item Drop")]
            public bool RedeemOnItemDrop = true;

            [JsonProperty("Item Drop Redeem Delay Seconds")]
            public float ItemDropRedeemDelaySeconds = 0.1f;

            [JsonProperty("Dropped Spawn Vertical Offset")]
            public float DroppedSpawnVerticalOffset = 1f;

            [JsonProperty("Token Spawn Min Forward Distance")]
            public float TokenSpawnMinForwardDistance = DefaultTokenSpawnMinForwardDistance;

            [JsonProperty("Token Spawn Max Forward Distance")]
            public float TokenSpawnMaxForwardDistance = DefaultTokenSpawnMaxForwardDistance;

            [JsonProperty("Token Spawn Yaw Offset Degrees")]
            public float TokenSpawnYawOffsetDegrees = DefaultTokenSpawnYawOffsetDegrees;

            [JsonProperty("Dropped Token Owner Search Radius")]
            public float DroppedTokenOwnerSearchRadius = DefaultDroppedTokenOwnerSearchRadius;

            [JsonProperty("Refresh Custom Item Definitions On Load")]
            public bool RefreshCustomItemDefinitionsOnLoad = true;

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
            public string TokenShortname = DefaultLegacyTokenShortname;

            [JsonProperty("Token Display Name")]
            public string TokenDisplayName;

            [JsonProperty("Token Skin")]
            public ulong TokenSkin;

            [JsonProperty("Require Display Name Match")]
            public bool RequireDisplayNameMatch = true;

            [JsonProperty("Use Custom Item Definition")]
            public bool UseCustomItemDefinition = true;

            [JsonProperty("Allow Legacy Fallback If CID Missing")]
            public bool AllowLegacyFallbackIfCIDMissing;

            [JsonProperty("Custom Shortname")]
            public string CustomShortname;

            [JsonProperty("Custom Item ID")]
            public int CustomItemId;

            [JsonProperty("Parent Shortname")]
            public string ParentShortname = DefaultParentTokenShortname;

            [JsonProperty("Icon File ID")]
            public uint IconFileId;

            [JsonProperty("Icon PNG Data Path")]
            public string IconPngDataPath;

            [JsonProperty("Default Description")]
            public string DefaultDescription;

            [JsonProperty("Import Parent Item Mods")]
            public bool ImportParentItemMods;

            [JsonProperty("Max Stack Size")]
            public int MaxStackSize = 100;

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

            config.ItemDropRedeemDelaySeconds = Mathf.Clamp(config.ItemDropRedeemDelaySeconds <= 0f ? 0.1f : config.ItemDropRedeemDelaySeconds, 0.01f, 2f);
            config.DroppedSpawnVerticalOffset = Mathf.Clamp(config.DroppedSpawnVerticalOffset, 0f, 10f);
            config.TokenSpawnMinForwardDistance = Mathf.Clamp(config.TokenSpawnMinForwardDistance, 0f, 100f);
            config.TokenSpawnMaxForwardDistance = Mathf.Clamp(config.TokenSpawnMaxForwardDistance, config.TokenSpawnMinForwardDistance, 150f);
            config.TokenSpawnYawOffsetDegrees = Mathf.Clamp(config.TokenSpawnYawOffsetDegrees, -360f, 360f);
            config.DroppedTokenOwnerSearchRadius = Mathf.Clamp(config.DroppedTokenOwnerSearchRadius <= 0f ? DefaultDroppedTokenOwnerSearchRadius : config.DroppedTokenOwnerSearchRadius, 1f, 30f);

            if (config.VehicleTokens == null || config.VehicleTokens.Count == 0)
            {
                config.VehicleTokens = DefaultVehicleTokens();
            }

            config.VehicleTokens = DeduplicateVehicleTokens(config.VehicleTokens);
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

        private void OnServerInitialized()
        {
            RefreshCustomTokenDefinitions();
            TryRegisterCustomTokenDefinitions();
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin == null)
            {
                return;
            }

            if (string.Equals(plugin.Name, "CustomItemDefinitions", StringComparison.OrdinalIgnoreCase))
            {
                CustomItemDefinitions = plugin;
                warnedCIDUnavailable = false;
                RefreshCustomTokenDefinitions();
                TryRegisterCustomTokenDefinitions();
            }
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin == null)
            {
                return;
            }

            if (string.Equals(plugin.Name, "CustomItemDefinitions", StringComparison.OrdinalIgnoreCase) && CustomItemDefinitions == plugin)
            {
                CustomItemDefinitions = null;
                customTokenDefinitions.Clear();
                tokenIconFileIds.Clear();
                warnedCIDUnavailable = false;
                PrintWarning("CustomItemDefinitions unloaded. Vehicle tokens will use legacy fallback item creation when configured.");
            }
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

        private object OnRaidlandsCreateKitItem(string shortname, int amount, ulong skin, string displayName)
        {
            VehicleTokenDefinition definition;
            if (!TryGetDefinitionByKitItem(shortname, skin, displayName, out definition))
            {
                return null;
            }

            return CreateToken(definition, Math.Max(1, amount));
        }

        private void OnItemDropped(Item item, BaseEntity entity)
        {
            HandleItemDropped(item, entity, null);
        }

        private void OnItemDropped(Item item, BaseEntity entity, BasePlayer player)
        {
            HandleItemDropped(item, entity, player);
        }

        private void HandleItemDropped(Item item, BaseEntity entity, BasePlayer explicitPlayer)
        {
            VehicleTokenDefinition definition;
            if (!config.RedeemOnItemDrop || item == null || entity == null || !TryGetDefinitionByToken(item, out definition))
            {
                return;
            }

            var playerId = ResolveDroppedTokenOwnerId(item, entity, explicitPlayer);
            var definitionKey = definition.Key;
            timer.Once(config.ItemDropRedeemDelaySeconds, () => TryRedeemDroppedToken(playerId, item, entity, definitionKey));
        }

        private object CanLicensedVehicleSpawn(BasePlayer player, string vehicleType, Vector3 position, Quaternion rotation)
        {
            if (!config.BlockDirectVehicleLicenceSpawns || player == null || string.IsNullOrWhiteSpace(vehicleType))
            {
                return null;
            }

            if (player.IsAdmin || HasPlayerPermission(player, BypassPermission, LegacyBypassPermission))
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
            Vector3 spawnPosition;
            Quaternion spawnRotation;
            GetTokenSpawnTransform(player, definition, out spawnPosition, out spawnRotation);

            if (!TrySpawnVehicle(player, definition, spawnPosition, spawnRotation))
            {
                return false;
            }

            ConsumeOneInventoryItem(item);
            Reply(player, $"{definition.DisplayName} spawned. One {definition.TokenDisplayName} was consumed.");
            return true;
        }

        private bool TryRedeemDroppedToken(ulong playerId, Item item, BaseEntity entity, string definitionKey)
        {
            if (item == null || entity == null || entity.IsDestroyed)
            {
                return false;
            }

            var player = playerId == 0UL
                ? FindNearestPlayer(entity.transform.position, config.DroppedTokenOwnerSearchRadius)
                : BasePlayer.FindAwakeOrSleeping(playerId.ToString());
            if (player == null)
            {
                return false;
            }

            VehicleTokenDefinition definition;
            if (!TryGetDefinitionByToken(item, out definition) || !string.Equals(definition.Key, definitionKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Vector3 spawnPosition;
            Quaternion spawnRotation;
            GetTokenSpawnTransform(player, definition, out spawnPosition, out spawnRotation);

            if (!TrySpawnVehicle(player, definition, spawnPosition, spawnRotation))
            {
                Reply(player, $"{definition.DisplayName} could not be spawned. Your dropped {definition.TokenDisplayName} was left where you threw it.");
                return false;
            }

            ConsumeOneDroppedItem(item, entity);
            Reply(player, $"{definition.DisplayName} spawned from dropped token. One {definition.TokenDisplayName} was consumed.");
            return true;
        }

        private void GetTokenSpawnTransform(BasePlayer player, VehicleTokenDefinition definition, out Vector3 spawnPosition, out Quaternion spawnRotation)
        {
            var forward = GetPlayerFlatForward(player);
            var distance = config.TokenSpawnMaxForwardDistance > config.TokenSpawnMinForwardDistance
                ? UnityEngine.Random.Range(config.TokenSpawnMinForwardDistance, config.TokenSpawnMaxForwardDistance)
                : config.TokenSpawnMinForwardDistance;

            spawnPosition = GetGroundPosition(player.transform.position + forward * distance);
            if (string.Equals(definition.Backend, "SpawnHeli", StringComparison.OrdinalIgnoreCase))
            {
                spawnPosition += Vector3.up * config.DroppedSpawnVerticalOffset;
            }

            var yaw = Quaternion.LookRotation(forward).eulerAngles.y + config.TokenSpawnYawOffsetDegrees;
            spawnRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private Vector3 GetPlayerFlatForward(BasePlayer player)
        {
            var forward = player.eyes != null ? player.eyes.HeadForward() : player.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.001f)
            {
                forward = player.transform.forward;
                forward.y = 0f;
            }

            return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
        }

        private Vector3 GetGroundPosition(Vector3 position)
        {
            RaycastHit hitInfo;
            var rayOrigin = position + Vector3.up * 250f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out hitInfo, 400f, GroundLayerMask))
            {
                position.y = hitInfo.point.y;
                return position;
            }

            if (TerrainMeta.HeightMap != null)
            {
                position.y = TerrainMeta.HeightMap.GetHeight(position);
            }

            return position;
        }

        private ulong ResolveDroppedTokenOwnerId(Item item, BaseEntity entity, BasePlayer explicitPlayer)
        {
            if (explicitPlayer != null)
            {
                return explicitPlayer.userID.Get();
            }

            var owner = item.GetOwnerPlayer() ?? item.parent?.playerOwner;
            if (owner != null)
            {
                return owner.userID.Get();
            }

            if (entity != null && entity.OwnerID != 0UL)
            {
                return entity.OwnerID;
            }

            return 0UL;
        }

        private BasePlayer FindNearestPlayer(Vector3 position, float radius)
        {
            BasePlayer closest = null;
            var closestDistance = radius * radius;
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected || player.IsSleeping())
                {
                    continue;
                }

                var distance = (player.transform.position - position).sqrMagnitude;
                if (distance > closestDistance)
                {
                    continue;
                }

                closest = player;
                closestDistance = distance;
            }

            return closest;
        }

        private bool TrySpawnVehicle(BasePlayer player, VehicleTokenDefinition definition, Vector3? position, Quaternion? rotation)
        {
            if (definition == null || !definition.Enabled)
            {
                Reply(player, "That vehicle token is not enabled.");
                return false;
            }

            var spawned = string.Equals(definition.Backend, "SpawnHeli", StringComparison.OrdinalIgnoreCase)
                ? TrySpawnHeliVehicle(player, definition, position, rotation)
                : TrySpawnVehicleLicenceVehicle(player, definition, position, rotation);

            return spawned;
        }

        private bool TrySpawnHeliVehicle(BasePlayer player, VehicleTokenDefinition definition, Vector3? position, Quaternion? rotation)
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

            Dictionary<string, object> options = new Dictionary<string, object>
            {
                ["AllowMultipleForPlayer"] = true,
                ["AutoFetch"] = false,
                ["EnforceHelicopterLimit"] = false
            };

            if (position.HasValue)
            {
                options["Position"] = position.Value;
                options["Rotation"] = rotation ?? player.transform.rotation;
                options["AutoMount"] = false;
            }

            var spawned = SpawnHeli.Call(definition.SpawnHeliApiHook, player, options) as BaseEntity;
            if (spawned == null || spawned.IsDestroyed)
            {
                Reply(player, $"{definition.DisplayName} could not be spawned. Your token was not consumed.");
                return false;
            }

            ApplyVehicleHealthBonus(spawned, player);
            return true;
        }

        private bool TrySpawnVehicleLicenceVehicle(BasePlayer player, VehicleTokenDefinition definition, Vector3? position, Quaternion? rotation)
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

            bool tokenApiSpawned;
            if (TrySpawnRaidlandsTokenVehicle(player, definition, position, rotation, out tokenApiSpawned))
            {
                return tokenApiSpawned;
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
                var result = position.HasValue
                    ? VehicleLicence.Call("SpawnLicensedVehicleAt", player, definition.VehicleLicenceType, position.Value, rotation ?? player.transform.rotation, "raidlands.vehicle.token", false)
                    : VehicleLicence.Call("SpawnLicensedVehicle", player, definition.VehicleLicenceType, "raidlands.vehicle.token", false);
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

        private bool TrySpawnRaidlandsTokenVehicle(BasePlayer player, VehicleTokenDefinition definition, Vector3? position, Quaternion? rotation, out bool spawned)
        {
            spawned = false;
            var spawnIntent = SpawnIntentKey(player.userID, definition.VehicleLicenceType);
            pendingVehicleLicenceSpawns.Add(spawnIntent);

            object result;
            try
            {
                result = position.HasValue
                    ? VehicleLicence.Call("SpawnRaidlandsTokenVehicleAt", player, definition.VehicleLicenceType, position.Value, rotation ?? player.transform.rotation, "raidlands.vehicle.token", false)
                    : VehicleLicence.Call("SpawnRaidlandsTokenVehicle", player, definition.VehicleLicenceType, "raidlands.vehicle.token", false);
            }
            finally
            {
                pendingVehicleLicenceSpawns.Remove(spawnIntent);
            }

            if (!(result is bool))
            {
                return false;
            }

            spawned = (bool)result;
            if (!spawned)
            {
                Reply(player, $"{definition.DisplayName} could not be spawned. Your token was not consumed.");
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
            var remaining = Math.Max(1, amount);
            while (remaining > 0)
            {
                var stackAmount = Math.Min(remaining, GetTokenMaxStackSize(definition));
                var item = CreateToken(definition, stackAmount);
                if (item == null)
                {
                    return given;
                }

                if (!player.inventory.GiveItem(item))
                {
                    item.Drop(player.GetDropPosition(), player.GetDropVelocity());
                }

                given += stackAmount;
                remaining -= stackAmount;
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
            return CreateToken(definition, 1);
        }

        private Item CreateToken(VehicleTokenDefinition definition, int amount)
        {
            TryRegisterCustomTokenDefinition(definition);

            var shortname = GetTokenCreateShortname(definition);
            var item = ItemManager.CreateByName(shortname, Math.Max(1, amount), GetTokenCreateSkin(definition));
            if (item == null && definition.AllowLegacyFallbackIfCIDMissing && !string.Equals(shortname, definition.TokenShortname, StringComparison.OrdinalIgnoreCase))
            {
                item = ItemManager.CreateByName(definition.TokenShortname, Math.Max(1, amount), definition.TokenSkin);
            }

            if (item == null)
            {
                PrintWarning($"Could not create vehicle token item '{shortname}' for {definition.Key}.");
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

                if (IsCustomTokenItem(item, candidate) || IsLegacyTokenItem(item, candidate))
                {
                    definition = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetDefinitionByKitItem(string shortname, ulong skin, string displayName, out VehicleTokenDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(shortname))
            {
                return false;
            }

            foreach (var candidate in config.VehicleTokens)
            {
                if (!candidate.Enabled)
                {
                    continue;
                }

                if (WantsCustomTokenDefinition(candidate)
                    && string.Equals(shortname, candidate.CustomShortname, StringComparison.OrdinalIgnoreCase))
                {
                    definition = candidate;
                    return true;
                }

                if (!string.Equals(shortname, candidate.TokenShortname, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (candidate.TokenSkin != 0 && skin != candidate.TokenSkin)
                {
                    continue;
                }

                if (candidate.RequireDisplayNameMatch && !string.Equals(displayName ?? "", candidate.TokenDisplayName ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                definition = candidate;
                return true;
            }

            return false;
        }

        private bool IsCustomTokenItem(Item item, VehicleTokenDefinition definition)
        {
            if (item?.info == null || !WantsCustomTokenDefinition(definition))
            {
                return false;
            }

            TryRegisterCustomTokenDefinition(definition);
            ItemDefinition customDefinition;
            if (customTokenDefinitions.TryGetValue(definition.Key, out customDefinition) && ReferenceEquals(item.info, customDefinition))
            {
                return true;
            }

            return string.Equals(item.info.shortname, definition.CustomShortname, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsLegacyTokenItem(Item item, VehicleTokenDefinition definition)
        {
            if (item?.info == null || definition == null)
            {
                return false;
            }

            if (!string.Equals(item.info.shortname, definition.TokenShortname, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (definition.TokenSkin != 0 && item.skin != definition.TokenSkin)
            {
                return false;
            }

            return !definition.RequireDisplayNameMatch
                || string.Equals(item.name ?? "", definition.TokenDisplayName ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private bool WantsCustomTokenDefinition(VehicleTokenDefinition definition)
        {
            return definition != null
                && definition.Enabled
                && definition.UseCustomItemDefinition
                && !string.IsNullOrWhiteSpace(definition.CustomShortname)
                && definition.CustomItemId != 0;
        }

        private bool IsCustomTokenDefinitionActive(VehicleTokenDefinition definition)
        {
            return WantsCustomTokenDefinition(definition)
                && customTokenDefinitions.ContainsKey(definition.Key ?? "");
        }

        private string GetTokenCreateShortname(VehicleTokenDefinition definition)
        {
            if (IsCustomTokenDefinitionActive(definition))
            {
                return definition.CustomShortname;
            }

            if (WantsCustomTokenDefinition(definition) && !definition.AllowLegacyFallbackIfCIDMissing)
            {
                return definition.CustomShortname;
            }

            return string.IsNullOrWhiteSpace(definition.TokenShortname) ? DefaultLegacyTokenShortname : definition.TokenShortname;
        }

        private ulong GetTokenCreateSkin(VehicleTokenDefinition definition)
        {
            return string.Equals(GetTokenCreateShortname(definition), definition.CustomShortname, StringComparison.OrdinalIgnoreCase)
                ? 0UL
                : definition.TokenSkin;
        }

        private int GetTokenMaxStackSize(VehicleTokenDefinition definition)
        {
            var configured = definition == null ? DefaultTokenMaxStackSize : definition.MaxStackSize;
            return Math.Max(1, Math.Min(configured > 0 ? configured : DefaultTokenMaxStackSize, MaximumTokenMaxStackSize));
        }

        private bool TryRegisterCustomTokenDefinitions()
        {
            var registeredAny = false;
            if (config?.VehicleTokens == null)
            {
                return false;
            }

            foreach (var definition in config.VehicleTokens)
            {
                registeredAny |= TryRegisterCustomTokenDefinition(definition);
            }

            return registeredAny;
        }

        private void RefreshCustomTokenDefinitions()
        {
            customTokenDefinitions.Clear();
            tokenIconFileIds.Clear();

            if (config?.VehicleTokens == null || !config.RefreshCustomItemDefinitionsOnLoad)
            {
                return;
            }

            if (CustomItemDefinitions == null || !CustomItemDefinitions.IsLoaded)
            {
                return;
            }

            foreach (var definition in config.VehicleTokens)
            {
                if (!WantsCustomTokenDefinition(definition))
                {
                    continue;
                }

                var existing = ItemManager.FindItemDefinition(definition.CustomShortname);
                if (existing == null)
                {
                    continue;
                }

                if (!(CustomItemDefinitions.Call("IsCustomDefinition", existing) is bool isCustomDefinition) || !isCustomDefinition)
                {
                    continue;
                }

                try
                {
                    if (CustomItemDefinitions.Call("Unregister", existing, this) is bool unregistered && unregistered)
                    {
                        Puts("Refreshed stale CID vehicle token definition '" + definition.CustomShortname + "'.");
                    }
                }
                catch (Exception ex)
                {
                    PrintWarning("Could not refresh CID vehicle token definition '" + definition.CustomShortname + "': " + ex.Message);
                }
            }
        }

        private bool TryRegisterCustomTokenDefinition(VehicleTokenDefinition definition)
        {
            if (!WantsCustomTokenDefinition(definition))
            {
                return false;
            }

            var key = definition.Key ?? "";
            ItemDefinition registeredDefinition;
            if (customTokenDefinitions.TryGetValue(key, out registeredDefinition) && registeredDefinition != null)
            {
                return true;
            }

            if (CustomItemDefinitions == null || !CustomItemDefinitions.IsLoaded)
            {
                WarnCIDUnavailableOnce();
                return false;
            }

            var customShortname = definition.CustomShortname.Trim();
            var existing = ItemManager.FindItemDefinition(customShortname);
            if (existing != null)
            {
                if (CustomItemDefinitions.Call("IsCustomDefinition", existing) is bool isCustomDefinition && isCustomDefinition)
                {
                    customTokenDefinitions[key] = existing;
                    ApplyCustomTokenRuntimeFields(definition, existing);
                    Puts("Using existing CID vehicle token '" + existing.shortname + "' itemId=" + existing.itemid + " maxStackSize=" + existing.stackable + ".");
                    return true;
                }

                PrintWarning("CID vehicle token registration skipped: item shortname '" + customShortname + "' already exists but is not a CustomItemDefinitions item.");
                return false;
            }

            var parentShortname = string.IsNullOrWhiteSpace(definition.ParentShortname) ? DefaultParentTokenShortname : definition.ParentShortname.Trim();
            var parent = ItemManager.FindItemDefinition(parentShortname);
            if (parent == null)
            {
                PrintWarning("CID vehicle token registration failed for '" + definition.Key + "': parent item definition '" + parentShortname + "' was not found.");
                return false;
            }

            var description = string.IsNullOrWhiteSpace(definition.DefaultDescription)
                ? "Drop this token to spawn a " + definition.DisplayName + "."
                : definition.DefaultDescription.Trim();

            try
            {
                var dto = new
                {
                    parentItemId = parent.itemid,
                    shortname = customShortname,
                    itemId = definition.CustomItemId,
                    iconFileId = ResolveTokenIconFileId(definition),
                    defaultName = definition.TokenDisplayName,
                    defaultDescription = description,
                    defaultSkinId = definition.TokenSkin,
                    maxStackSize = GetTokenMaxStackSize(definition),
                    category = parent.category,
                    itemMods = definition.ImportParentItemMods ? parent.itemMods : null,
                    repairable = false,
                    craftable = false,
                    defaultBlueprintUnlocked = false
                };

                var registered = CustomItemDefinitions.Call("Register", dto, this) as ItemDefinition;
                if (registered == null)
                {
                    PrintWarning("CID vehicle token registration failed for '" + definition.Key + "': CustomItemDefinitions.Register returned no ItemDefinition.");
                    return false;
                }

                registered.stackable = GetTokenMaxStackSize(definition);
                ApplyCustomTokenRuntimeFields(definition, registered);
                customTokenDefinitions[key] = registered;
                warnedCIDUnavailable = false;
                Puts("Registered CID vehicle token '" + registered.shortname + "' itemId=" + registered.itemid + " parent=" + parent.shortname + " maxStackSize=" + registered.stackable + ".");
                return true;
            }
            catch (Exception ex)
            {
                PrintWarning("CID vehicle token registration failed for '" + definition.Key + "': " + ex.Message);
                return false;
            }
        }

        private void ApplyCustomTokenRuntimeFields(VehicleTokenDefinition definition, ItemDefinition itemDefinition)
        {
            if (definition == null || itemDefinition == null)
            {
                return;
            }

            itemDefinition.stackable = GetTokenMaxStackSize(definition);
            itemDefinition.displayName = new Translate.Phrase(definition.CustomShortname, definition.TokenDisplayName);
            itemDefinition.displayDescription = new Translate.Phrase(definition.CustomShortname + ".desc", GetTokenDescription(definition));

            if (!definition.ImportParentItemMods)
            {
                itemDefinition.itemMods = null;
                foreach (var mod in itemDefinition.GetComponentsInChildren<ItemMod>(true))
                {
                    UnityEngine.Object.DestroyImmediate(mod);
                }

                itemDefinition.Initialize(ItemManager.itemList);
            }
        }

        private string GetTokenDescription(VehicleTokenDefinition definition)
        {
            return string.IsNullOrWhiteSpace(definition?.DefaultDescription)
                ? "Drop this token to spawn a " + (definition?.DisplayName ?? "vehicle") + "."
                : definition.DefaultDescription.Trim();
        }

        private uint ResolveTokenIconFileId(VehicleTokenDefinition definition)
        {
            if (definition == null)
            {
                return 0;
            }

            if (definition.IconFileId != 0)
            {
                tokenIconFileIds[definition.Key ?? ""] = definition.IconFileId;
                return definition.IconFileId;
            }

            uint cachedFileId;
            if (tokenIconFileIds.TryGetValue(definition.Key ?? "", out cachedFileId) && cachedFileId != 0)
            {
                return cachedFileId;
            }

            var iconPath = ResolveTokenIconPath(definition);
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                return 0;
            }

            if (!File.Exists(iconPath))
            {
                WarnIconMissingOnce(iconPath);
                return 0;
            }

            if (FileStorage.server == null)
            {
                PrintWarning("CID vehicle token icon FileStorage is unavailable; '" + definition.Key + "' will register without a custom icon.");
                return 0;
            }

            try
            {
                var bytes = File.ReadAllBytes(iconPath);
                if (bytes == null || bytes.Length == 0)
                {
                    PrintWarning("CID vehicle token icon file is empty: " + iconPath);
                    return 0;
                }

                var fileId = FileStorage.server.Store(bytes, FileStorage.Type.png, default);
                tokenIconFileIds[definition.Key ?? ""] = fileId;
                return fileId;
            }
            catch (Exception ex)
            {
                PrintWarning("Could not load CID vehicle token icon from '" + iconPath + "': " + ex.Message);
                return 0;
            }
        }

        private string ResolveTokenIconPath(VehicleTokenDefinition definition)
        {
            var path = definition?.IconPngDataPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            path = path.Trim();
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            path = path.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.Combine(Interface.Oxide.DataDirectory, path);
        }

        private void WarnCIDUnavailableOnce()
        {
            if (warnedCIDUnavailable)
            {
                return;
            }

            warnedCIDUnavailable = true;
            PrintWarning("CustomItemDefinitions is not loaded. Raidlands vehicle tokens will use legacy wrappedgift fallback where configured.");
        }

        private void WarnIconMissingOnce(string iconPath)
        {
            if (!warnedMissingIconPaths.Add(iconPath))
            {
                return;
            }

            PrintWarning("CID vehicle token icon file was not found at '" + iconPath + "'. The custom token will register without that PNG icon unless Icon File ID is configured.");
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
            if (HasPlayerPermission(player, VehicleHp150Permission, LegacyVehicleHp150Permission))
            {
                return 1.5f;
            }

            return HasPlayerPermission(player, VehicleHp125Permission, LegacyVehicleHp125Permission) ? 1.25f : 1f;
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

        private void ConsumeOneInventoryItem(Item item)
        {
            if (item == null)
            {
                return;
            }

            if (item.amount > 1)
            {
                item.amount -= 1;
                item.MarkDirty();
                return;
            }

            item.RemoveFromContainer();
            item.Remove();
        }

        private void ConsumeOneDroppedItem(Item item, BaseEntity entity)
        {
            if (item == null)
            {
                return;
            }

            if (item.amount > 1)
            {
                item.amount -= 1;
                item.MarkDirty();
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.SendNetworkUpdate();
                }
                return;
            }

            item.RemoveFromWorld();
            item.RemoveFromContainer();
            item.Remove();

            if (entity != null && !entity.IsDestroyed)
            {
                entity.Kill();
            }
        }

        private bool CanUseAdminCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Connection == null || arg.Connection.authLevel > 0 || arg.IsAdmin)
            {
                return true;
            }

            var player = arg.Connection.player as BasePlayer;
            return HasPlayerPermission(player, AdminPermission, LegacyAdminPermission);
        }

        private bool HasPlayerPermission(BasePlayer player, string permissionName, string legacyPermissionName = null)
        {
            if (player == null)
            {
                return false;
            }

            var userId = player.UserIDString;
            return permission.UserHasPermission(userId, permissionName)
                || (!string.IsNullOrEmpty(legacyPermissionName) && permission.UserHasPermission(userId, legacyPermissionName));
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

        private List<VehicleTokenDefinition> DeduplicateVehicleTokens(List<VehicleTokenDefinition> definitions)
        {
            var result = new List<VehicleTokenDefinition>();
            if (definitions == null || definitions.Count == 0)
            {
                return result;
            }

            var byKey = new Dictionary<string, VehicleTokenDefinition>(StringComparer.OrdinalIgnoreCase);
            var removed = 0;
            var conflictKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var definition in definitions)
            {
                if (definition == null)
                {
                    continue;
                }

                var key = (definition.Key ?? "").Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    result.Add(definition);
                    continue;
                }

                definition.Key = key;

                VehicleTokenDefinition existing;
                if (!byKey.TryGetValue(key, out existing))
                {
                    byKey[key] = definition;
                    result.Add(definition);
                    continue;
                }

                removed++;
                if (DefinitionsConflict(existing, definition) && conflictKeys.Add(key))
                {
                    PrintWarning("Duplicate vehicle token config for key '" + key + "' differs from the first definition. Preserving the first definition and dropping later duplicates.");
                }
            }

            if (removed > 0)
            {
                Puts("Normalized RaidlandsVehicleTokens config by removing " + removed + " duplicate vehicle token definition(s); preserved the first definition per key.");
            }

            return result;
        }

        private static bool DefinitionsConflict(VehicleTokenDefinition first, VehicleTokenDefinition second)
        {
            if (first == null || second == null)
            {
                return first != second;
            }

            return first.Enabled != second.Enabled
                || !StringEquals(first.DisplayName, second.DisplayName)
                || !StringEquals(first.Backend, second.Backend)
                || !StringEquals(first.SpawnHeliApiHook, second.SpawnHeliApiHook)
                || !StringEquals(first.VehicleLicenceType, second.VehicleLicenceType)
                || !StringEquals(first.TokenShortname, second.TokenShortname)
                || !StringEquals(first.TokenDisplayName, second.TokenDisplayName)
                || first.TokenSkin != second.TokenSkin
                || first.RequireDisplayNameMatch != second.RequireDisplayNameMatch
                || first.UseCustomItemDefinition != second.UseCustomItemDefinition
                || first.AllowLegacyFallbackIfCIDMissing != second.AllowLegacyFallbackIfCIDMissing
                || !StringEquals(first.CustomShortname, second.CustomShortname)
                || first.CustomItemId != second.CustomItemId
                || !StringEquals(first.ParentShortname, second.ParentShortname)
                || first.IconFileId != second.IconFileId
                || !StringEquals(first.IconPngDataPath, second.IconPngDataPath)
                || !StringEquals(first.DefaultDescription, second.DefaultDescription)
                || first.ImportParentItemMods != second.ImportParentItemMods
                || first.MaxStackSize != second.MaxStackSize
                || !StringArrayEquals(first.Aliases, second.Aliases);
        }

        private static bool StringEquals(string first, string second)
        {
            return string.Equals(first ?? "", second ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private static bool StringArrayEquals(string[] first, string[] second)
        {
            first = first ?? new string[0];
            second = second ?? new string[0];
            if (first.Length != second.Length)
            {
                return false;
            }

            for (var i = 0; i < first.Length; i++)
            {
                if (!StringEquals(first[i], second[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static void NormalizeDefinition(VehicleTokenDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            definition.Key = (definition.Key ?? "").Trim();
            definition.DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.Key : definition.DisplayName.Trim();

            if (string.IsNullOrWhiteSpace(definition.TokenShortname))
            {
                definition.TokenShortname = DefaultLegacyTokenShortname;
            }

            if (string.IsNullOrWhiteSpace(definition.TokenDisplayName))
            {
                definition.TokenDisplayName = $"{definition.DisplayName} Token";
            }

            if (string.IsNullOrWhiteSpace(definition.CustomShortname))
            {
                definition.CustomShortname = CustomTokenShortnamePrefix + definition.Key;
            }

            if (definition.CustomItemId == 0)
            {
                definition.CustomItemId = DefaultCustomItemId(definition.Key);
            }

            if (string.IsNullOrWhiteSpace(definition.ParentShortname))
            {
                definition.ParentShortname = DefaultParentTokenShortname;
            }

            if (string.IsNullOrWhiteSpace(definition.IconPngDataPath))
            {
                definition.IconPngDataPath = DefaultTokenIconDataFolder + "/" + definition.Key + ".png";
            }

            if (string.IsNullOrWhiteSpace(definition.DefaultDescription))
            {
                definition.DefaultDescription = "Drop this token to spawn a " + definition.DisplayName + ".";
            }

            definition.MaxStackSize = Math.Max(1, Math.Min(definition.MaxStackSize <= 0 ? DefaultTokenMaxStackSize : definition.MaxStackSize, MaximumTokenMaxStackSize));

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
                SpawnHeliToken("minicopter", "Minicopter", "API_SpawnMinicopter", -395118501, new[] { "mini", "minicopter" }),
                SpawnHeliToken("scrap_transport_helicopter", "Scrap Transport Helicopter", "API_SpawnScrapTransportHelicopter", -395118502, new[] { "scrapheli", "scrap", "scraptransport" }),
                SpawnHeliToken("attack_helicopter", "Attack Helicopter", "API_SpawnAttackHelicopter", -395118503, new[] { "attackheli", "attack" }),
                VehicleLicenceToken("rhib", "RHIB", "RHIB", -395118504, new[] { "rhib" }),
                VehicleLicenceToken("tugboat", "Tugboat", "Tugboat", -395118505, new[] { "tug", "tugboat" }),
                VehicleLicenceToken("solo_submarine", "Solo Submarine", "SubmarineSolo", -395118506, new[] { "subsolo", "solo", "solosub" }),
                VehicleLicenceToken("duo_submarine", "Duo Submarine", "SubmarineDuo", -395118507, new[] { "subduo", "duo", "duosub" }),
                VehicleLicenceToken("snowmobile", "Snowmobile", "Snowmobile", -395118508, new[] { "snow", "snowmobile" }),
                VehicleLicenceToken("hot_air_balloon", "Hot Air Balloon", "HotAirBalloon", -395118509, new[] { "hab", "hotairballoon", "balloon" })
            };
        }

        private static VehicleTokenDefinition SpawnHeliToken(string key, string displayName, string hook, int customItemId, string[] aliases)
        {
            var definition = new VehicleTokenDefinition
            {
                Key = key,
                DisplayName = displayName,
                Backend = "SpawnHeli",
                SpawnHeliApiHook = hook,
                TokenDisplayName = $"{displayName} Token",
                Aliases = aliases
            };

            ApplyCustomDefaults(definition, customItemId);
            return definition;
        }

        private static VehicleTokenDefinition VehicleLicenceToken(string key, string displayName, string vehicleLicenceType, int customItemId, string[] aliases)
        {
            var definition = new VehicleTokenDefinition
            {
                Key = key,
                DisplayName = displayName,
                Backend = "VehicleLicence",
                VehicleLicenceType = vehicleLicenceType,
                TokenDisplayName = $"{displayName} Token",
                Aliases = aliases
            };

            ApplyCustomDefaults(definition, customItemId);
            return definition;
        }

        private static void ApplyCustomDefaults(VehicleTokenDefinition definition, int customItemId)
        {
            definition.UseCustomItemDefinition = true;
            definition.AllowLegacyFallbackIfCIDMissing = false;
            definition.CustomShortname = CustomTokenShortnamePrefix + definition.Key;
            definition.CustomItemId = customItemId;
            definition.ParentShortname = DefaultParentTokenShortname;
            definition.IconPngDataPath = DefaultTokenIconDataFolder + "/" + definition.Key + ".png";
            definition.DefaultDescription = "Drop this token to spawn a " + definition.DisplayName + ".";
            definition.ImportParentItemMods = false;
            definition.MaxStackSize = DefaultTokenMaxStackSize;
        }

        private static int DefaultCustomItemId(string key)
        {
            switch (key ?? "")
            {
                case "minicopter": return -395118501;
                case "scrap_transport_helicopter": return -395118502;
                case "attack_helicopter": return -395118503;
                case "rhib": return -395118504;
                case "tugboat": return -395118505;
                case "solo_submarine": return -395118506;
                case "duo_submarine": return -395118507;
                case "snowmobile": return -395118508;
                case "hot_air_balloon": return -395118509;
                default: return 0;
            }
        }
    }
}
