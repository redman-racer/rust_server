
using System;
using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("AutoCodeLock", "Raidlands", "0.4.2")]
    [Description("Auto-generates, places, codes, locks, and shares code locks.")]
    public class AutoCodeLock : RustPlugin
    {
        private const string PermUse = "autocodelock.use";
        private const string CodeLockShortName = "lock.code";

        private StoredData data;
        private Configuration config;
        private readonly System.Random random = new System.Random();

        private class Configuration
        {
            public bool AutoGenerateCodes = true;
            public bool DefaultEnabled = true;
            public bool DefaultShareWithTeam = true;
            public bool ShowCodeToPlayerOnDeploy = true;
            public bool LockAfterCodeSet = true;

            public bool AutoPlaceCodeLocks = true;
            public bool RequireCodeLockInInventory = true;
            public bool ConsumeCodeLockFromInventory = true;

            public bool AutoPlaceOnDoors = true;
            public bool AutoPlaceOnBoxes = true;
            public bool AutoPlaceOnToolCupboards = true;

            public string ChatPrefix = "<color=#8ecae6>AutoCodeLock</color>:";
        }

        private class StoredData
        {
            public Dictionary<ulong, PlayerData> Players = new Dictionary<ulong, PlayerData>();
        }

        private class PlayerData
        {
            public string Code = "";
            public bool Enabled = true;
            public bool ShareWithTeam = true;
            public List<ulong> SharedPlayers = new List<ulong>();
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
                config = Config.ReadObject<Configuration>();
                if (config == null) throw new Exception();
            }
            catch
            {
                PrintWarning("Config file invalid; generating a new one.");
                LoadDefaultConfig();
            }

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData();

            if (config == null)
                LoadConfig();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, data);
        }

        private PlayerData GetPlayerData(ulong userId)
        {
            if (!data.Players.TryGetValue(userId, out var playerData))
            {
                playerData = new PlayerData
                {
                    Code = config.AutoGenerateCodes ? GenerateCode() : "",
                    Enabled = config.DefaultEnabled,
                    ShareWithTeam = config.DefaultShareWithTeam
                };

                data.Players[userId] = playerData;
                SaveData();
            }

            if (config.AutoGenerateCodes && string.IsNullOrEmpty(playerData.Code))
            {
                playerData.Code = GenerateCode();
                SaveData();
            }

            return playerData;
        }

        private string GenerateCode()
        {
            return random.Next(1000, 9999).ToString();
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, PermUse)) return;

            GetPlayerData(player.userID);
        }

        private void OnEntityBuilt(Planner planner, GameObject gameObject)
        {
            var player = planner?.GetOwnerPlayer();
            var entity = gameObject?.ToBaseEntity();

            if (player == null || entity == null) return;

            timer.Once(0.25f, () =>
            {
                if (entity == null || entity.IsDestroyed) return;
                TryAutoPlaceCodeLock(player, entity);
            });
        }

        private void OnItemDeployed(Deployer deployer, BaseEntity entity)
        {
            var player = deployer?.GetOwnerPlayer();
            if (player == null || entity == null) return;

            timer.Once(0.25f, () =>
            {
                if (entity == null || entity.IsDestroyed) return;

                var placedCodeLock = entity as CodeLock;
                if (placedCodeLock != null)
                {
                    TryConfigureCodeLock(player, placedCodeLock);
                    return;
                }

                TryAutoPlaceCodeLock(player, entity);
            });
        }

        private void TryAutoPlaceCodeLock(BasePlayer player, BaseEntity target)
        {
            if (!config.AutoPlaceCodeLocks) return;
            if (!permission.UserHasPermission(player.UserIDString, PermUse)) return;

            var playerData = GetPlayerData(player.userID);
            if (!playerData.Enabled) return;
            if (string.IsNullOrEmpty(playerData.Code)) return;

            if (!IsEligibleTarget(target)) return;

            if (target.GetSlot(BaseEntity.Slot.Lock) != null)
                return;

            if (config.RequireCodeLockInInventory && !HasCodeLock(player))
                return;

            var itemDefinition = ItemManager.FindItemDefinition(CodeLockShortName);
            var deployable = itemDefinition?.GetComponent<ItemModDeployable>();

            if (deployable == null || deployable.entityPrefab == null)
            {
                Reply(player, "Could not find code lock deployable prefab.");
                return;
            }

            var codeLock = GameManager.server.CreateEntity(
                deployable.entityPrefab.resourcePath,
                target.transform.position,
                target.transform.rotation
            ) as CodeLock;

            if (codeLock == null)
            {
                Reply(player, $"Failed to create code lock from prefab: {deployable.entityPrefab.resourcePath}");
                return;
            }

            if (config.ConsumeCodeLockFromInventory && !TakeCodeLock(player))
            {
                codeLock.Kill();
                return;
            }

            codeLock.OwnerID = player.userID;

            string anchor = target.GetSlotAnchorName(BaseEntity.Slot.Lock);
            codeLock.SetParent(target, anchor);

            target.SetSlot(BaseEntity.Slot.Lock, codeLock);

            codeLock.Spawn();
            codeLock.transform.localPosition = Vector3.zero;
            codeLock.transform.localRotation = Quaternion.identity;

            TryConfigureCodeLock(player, codeLock);

            target.SendNetworkUpdateImmediate();
            codeLock.SendNetworkUpdateImmediate();

            Reply(player, "Code lock automatically attached.");
        }

        private void TryConfigureCodeLock(BasePlayer player, CodeLock codeLock)
        {
            if (player == null || codeLock == null || codeLock.IsDestroyed) return;
            if (!permission.UserHasPermission(player.UserIDString, PermUse)) return;

            var playerData = GetPlayerData(player.userID);
            if (!playerData.Enabled) return;
            if (string.IsNullOrEmpty(playerData.Code)) return;

            codeLock.code = playerData.Code;
            codeLock.hasCode = true;

            if (config.LockAfterCodeSet)
                codeLock.SetFlag(BaseEntity.Flags.Locked, true);

            AddAccess(codeLock, player.userID);

            foreach (var sharedId in GetSharedPlayers(player, playerData))
                AddAccess(codeLock, sharedId);

            codeLock.SendNetworkUpdateImmediate();

            if (config.ShowCodeToPlayerOnDeploy)
                Reply(player, $"Code lock set to {playerData.Code}.");
        }

        private bool IsEligibleTarget(BaseEntity entity)
        {
            if (entity == null) return false;

            if (config.AutoPlaceOnDoors && entity is Door)
                return true;

            if (config.AutoPlaceOnToolCupboards && entity is BuildingPrivlidge)
                return true;

            if (config.AutoPlaceOnBoxes && entity is StorageContainer)
            {
                string prefab = entity.ShortPrefabName.ToLower();

                if (prefab.Contains("box") ||
                    prefab.Contains("locker") ||
                    prefab.Contains("fridge") ||
                    prefab.Contains("vendingmachine"))
                    return true;
            }

            return false;
        }

        private bool HasCodeLock(BasePlayer player)
        {
            var itemDefinition = ItemManager.FindItemDefinition(CodeLockShortName);
            if (itemDefinition == null) return false;

            return player.inventory.GetAmount(itemDefinition.itemid) > 0;
        }

        private bool TakeCodeLock(BasePlayer player)
        {
            var itemDefinition = ItemManager.FindItemDefinition(CodeLockShortName);
            if (itemDefinition == null) return false;

            if (player.inventory.GetAmount(itemDefinition.itemid) <= 0)
                return false;

            player.inventory.Take(null, itemDefinition.itemid, 1);
            player.Command("note.inv", itemDefinition.itemid, -1);

            return true;
        }

        private IEnumerable<ulong> GetSharedPlayers(BasePlayer owner, PlayerData playerData)
        {
            var result = new HashSet<ulong>();

            foreach (var id in playerData.SharedPlayers)
                result.Add(id);

            if (playerData.ShareWithTeam)
            {
                var team = RelationshipManager.ServerInstance.FindPlayersTeam(owner.userID);
                if (team?.members != null)
                {
                    foreach (var id in team.members)
                        result.Add(id);
                }
            }

            result.Remove(owner.userID);
            return result;
        }

        private void AddAccess(CodeLock codeLock, ulong userId)
        {
            if (!codeLock.whitelistPlayers.Contains(userId))
                codeLock.whitelistPlayers.Add(userId);
        }

        [ChatCommand("acl")]
        private void CmdAcl(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermUse))
            {
                Reply(player, "You do not have permission.");
                return;
            }

            var playerData = GetPlayerData(player.userID);

            if (args.Length == 0)
            {
                Reply(player, $"Enabled: {playerData.Enabled}");
                Reply(player, $"Code: {playerData.Code}");
                Reply(player, $"Team sharing: {playerData.ShareWithTeam}");
                Reply(player, "Commands: /acl regen | /acl on | /acl off | /acl team on/off | /acl share <steamid64> | /acl unshare <steamid64>");
                return;
            }

            switch (args[0].ToLower())
            {
                case "regen":
                    playerData.Code = GenerateCode();
                    SaveData();
                    Reply(player, $"New code generated: {playerData.Code}");
                    return;

                case "on":
                    playerData.Enabled = true;
                    SaveData();
                    Reply(player, "AutoCodeLock enabled.");
                    return;

                case "off":
                    playerData.Enabled = false;
                    SaveData();
                    Reply(player, "AutoCodeLock disabled.");
                    return;

                case "team":
                    if (args.Length < 2)
                    {
                        Reply(player, "Usage: /acl team on/off");
                        return;
                    }

                    playerData.ShareWithTeam = args[1].ToLower() == "on";
                    SaveData();
                    Reply(player, $"Team sharing {(playerData.ShareWithTeam ? "enabled" : "disabled")}.");
                    return;

                case "share":
                    if (args.Length < 2 || !ulong.TryParse(args[1], out var shareId))
                    {
                        Reply(player, "Usage: /acl share <steamid64>");
                        return;
                    }

                    if (!playerData.SharedPlayers.Contains(shareId))
                        playerData.SharedPlayers.Add(shareId);

                    SaveData();
                    Reply(player, $"Shared locks with {shareId}.");
                    return;

                case "unshare":
                    if (args.Length < 2 || !ulong.TryParse(args[1], out var unshareId))
                    {
                        Reply(player, "Usage: /acl unshare <steamid64>");
                        return;
                    }

                    playerData.SharedPlayers.Remove(unshareId);
                    SaveData();
                    Reply(player, $"Removed share for {unshareId}.");
                    return;
            }

            Reply(player, "Unknown command.");
        }

        private void Reply(BasePlayer player, string message)
        {
            player.ChatMessage($"{config.ChatPrefix} {message}");
        }
    }
}