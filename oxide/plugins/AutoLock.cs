using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;
using Random = Oxide.Core.Random;

namespace Oxide.Plugins
{
    [Info("Auto Lock", "birthdates", "2.4.7")]
    [Description("Automatically adds a codelock to a lockable entity with a set pin")]
    public class AutoLock : RustPlugin
    {
        #region Variables

        private const string PermissionUse = "autolock.use";
        private const string PermissionItemBypass = "autolock.item.bypass";

        [UsedImplicitly] [PluginReference("NoEscape")]
        private Plugin _noEscape;

        #endregion

        #region Hooks

        [UsedImplicitly]
        private void Init()
        {
            LoadConfig();
            permission.RegisterPermission(PermissionUse, this);
            permission.RegisterPermission(PermissionItemBypass, this);
            _data = Interface.Oxide.DataFileSystem.ReadObject<Data>(Name);

            cmd.AddChatCommand("autolock", this, ChatCommand);
            cmd.AddChatCommand("al", this, ChatCommand);
        }

        [UsedImplicitly]
        private void OnEntityBuilt(HeldEntity plan, GameObject go)
        {
            var player = plan.GetOwnerPlayer();
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, PermissionUse)) return;
            var entity = go.ToBaseEntity() as DecayEntity;
            if (entity == null || _config.Disabled.Contains(entity.PrefabName)) return;
            var container = entity as StorageContainer;
            if (entity.IsLocked() || container != null && container.inventorySlots < 12 ||
                !container && !(entity is AnimatedBuildingBlock)) return;
            var playerData = CreateDataIfAbsent(player.UserIDString);
            if (_noEscape != null)
            {
                if (_config.NoEscapeSettings.BlockRaid && _noEscape.Call<bool>("IsRaidBlocked", player.UserIDString))
                {
                    if (!playerData.Hidden)
                        player.ChatMessage(lang.GetMessage("RaidBlocked", this, player.UserIDString));
                    return;
                }

                if (_config.NoEscapeSettings.BlockCombat &&
                    _noEscape.Call<bool>("IsCombatBlocked", player.UserIDString))
                {
                    if (!playerData.Hidden)
                        player.ChatMessage(lang.GetMessage("CombatBlocked", this, player.UserIDString));
                    return;
                }
            }


            if (!playerData.Enabled || !HasCodeLock(player)) return;
            var code = GameManager.server.CreateEntity("assets/prefabs/locks/keypad/lock.code.prefab") as CodeLock;
            if (code != null)
            {
                code.gameObject.Identity();
                code.SetParent(entity, entity.GetSlotAnchorName(BaseEntity.Slot.Lock));
                code.Spawn();
                code.code = playerData.Code;
                code.hasCode = true;
                entity.SetSlot(BaseEntity.Slot.Lock, code);
                Effect.server.Run("assets/prefabs/locks/keypad/effects/lock-code-deploy.prefab",
                    code.transform.position);
                if (!code.whitelistPlayers.Contains(player.userID))
                {
                    code.whitelistPlayers.Add(player.userID);
                }
                code.SetFlag(BaseEntity.Flags.Locked, true);
            }

            TakeCodeLock(player);
            if (!playerData.Hidden)
                player.ChatMessage(string.Format(lang.GetMessage("CodeAdded", this, player.UserIDString),
                    player.net.connection.info.GetBool("global.streamermode") ? "****" : playerData.Code));
        }

        private static string GetRandomCode()
        {
            return Random.Range(1000, 9999).ToString();
        }

        [UsedImplicitly]
        private void OnServerShutdown()
        {
            Unload();
        }

        private void Unload()
        {
            SaveData();
        }

        private PlayerData CreateDataIfAbsent(string id)
        {
            PlayerData playerData;
            if (_data.Codes.TryGetValue(id, out playerData)) return playerData;
            _data.Codes.Add(id, playerData = new PlayerData
            {
                Code = GetRandomCode(),
                Enabled = true
            });
            return playerData;
        }

        #endregion

        #region Command

        private void ChatCommand(BasePlayer player, string label, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                player.ChatMessage(lang.GetMessage("NoPermission", this, player.UserIDString));
                return;
            }

            if (args.Length < 1)
            {
                player.ChatMessage(string.Format(lang.GetMessage("InvalidArgs", this, player.UserIDString), label));
                return;
            }

            CreateDataIfAbsent(player.UserIDString);
            switch (args[0].ToLower())
            {
                case "code":
                    SetCode(player, args.Length > 1 ? args[1] : null);
                    break;
                case "toggle":
                    player.ChatMessage(lang.GetMessage(Toggle(player) ? "Enabled" : "Disabled", this,
                        player.UserIDString));
                    break;
                case "hide":
                    player.ChatMessage(lang.GetMessage(ToggleHide(player) ? "HideEnabled" : "HideDisabled", this,
                        player.UserIDString));
                    break;
                default:
                    player.ChatMessage(string.Format(lang.GetMessage("InvalidArgs", this, player.UserIDString), label));
                    break;
            }
        }

        private static bool HasCodeLock(BasePlayer player)
        {
            return player.IPlayer.HasPermission(PermissionItemBypass) || player.inventory.FindItemByItemID(1159991980) != null;
        }

        private static void TakeCodeLock(BasePlayer player)
        {
            if (!player.IPlayer.HasPermission(PermissionItemBypass))
                player.inventory.Take(null, 1159991980, 1);
        }

        private void SetCode(BasePlayer player, string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length != 4 || !code.All(char.IsDigit))
            {
                player.ChatMessage(lang.GetMessage("InvalidCode", this, player.UserIDString));
                return;
            }

            var pData = _data.Codes[player.UserIDString];
            pData.Code = code;
            if (!pData.Hidden)
                player.ChatMessage(string.Format(lang.GetMessage("CodeUpdated", this, player.UserIDString),
                    player.net.connection.info.GetBool("global.streamermode") ? "****" : code));
        }

        private bool Toggle(BasePlayer player)
        {
            var data = _data.Codes[player.UserIDString];
            var newToggle = !data.Enabled;
            data.Enabled = newToggle;
            return newToggle;
        }

        private bool ToggleHide(BasePlayer player)
        {
            var data = _data.Codes[player.UserIDString];
            data.Hidden = !data.Hidden;
            return data.Hidden;
        }

        #endregion

        #region Configuration & Language

        private ConfigFile _config;
        private Data _data;

        private class PlayerData
        {
            public string Code;
            public bool Enabled;
            public bool Hidden;
        }

        private class Data
        {
            public readonly Dictionary<string, PlayerData> Codes = new Dictionary<string, PlayerData>();
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"CodeAdded", "Codelock placed with code {0}."},
                {"Disabled", "You have disabled auto locks."},
                {"Enabled", "You have enabled auto locks."},
                {"HideEnabled", "You have hidden auto lock notifications."},
                {"HideDisabled", "You have shown auto lock notifications."},
                {"CodeUpdated", "Your new code is {0}."},
                {"InvalidCode", "Please enter a valid 4-digit code, e.g. /autolock code 1234"},
                {"NoPermission", "You don't have permission."},
                {"InvalidArgs", "/{0} code <1234>|toggle|hide"},
                {"RaidBlocked", "The codelock wasn't automatically locked due to you being raid blocked!"},
                {"CombatBlocked", "The codelock wasn't automatically locked due to you being combat blocked!"}
            }, this);
        }

        public class ConfigFile
        {
            [JsonProperty("Disabled Items (Prefabs)")]
            public List<string> Disabled;

            [JsonProperty("No Escape")] public NoEscapeSettings NoEscapeSettings;

            public static ConfigFile DefaultConfig()
            {
                return new ConfigFile
                {
                    Disabled = new List<string>
                    {
                        "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab"
                    },
                    NoEscapeSettings = new NoEscapeSettings
                    {
                        BlockCombat = true,
                        BlockRaid = true
                    }
                };
            }
        }

        public class NoEscapeSettings
        {
            [JsonProperty("Block Auto Lock whilst in Combat?")]
            public bool BlockCombat;

            [JsonProperty("Block Auto Lock whilst Raid Blocked?")]
            public bool BlockRaid;
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, _data);
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<ConfigFile>();
            if (_config == null) LoadDefaultConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = ConfigFile.DefaultConfig();
            PrintWarning("Default configuration has been loaded.");
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config);
        }

        #endregion
    }
}
//Generated with birthdates' Plugin Maker