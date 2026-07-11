using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Instant Smelt" , "Krungh Crow/Updated" , "2.1.4-gather-compatible")]
    [Description("Smelt resources as soon as they are mined")]
    public class InstantSmelt : RustPlugin
    {
        #region Variables

        private const string permUse = "instantsmelt.use";
        private const string charcoalItemName = "charcoal";
        private const string woodItemName = "wood";

        #endregion

        #region Configuration

        private ConfigData configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Command")]
            public string command = "ismelt";

            [JsonProperty(PropertyName = "Resource (true/false)")]
            public Dictionary<string , bool> autoSmeltEnabled = new Dictionary<string , bool>
            {
                {"wood", true},
                {"metal.ore", true},
                {"hq.metal.ore", true},
                {"sulfur.ore", true}
            };
        }

        private bool LoadConfigVariables()
        {
            try
            {
                configData = Config.ReadObject<ConfigData>();
            }
            catch (Exception ex)
            {
                PrintError($"[Error] Failed to load config: {ex.Message}");
                return false;
            }

            SaveConfig();
            return true;
        }

        protected override void LoadDefaultConfig()
        {
            configData = new ConfigData();
            SaveConfig();
        }

        private void SaveConfig() => Config.WriteObject(configData , true);

        #endregion

        #region Data 1.0.0

        private const string filename = "Temp/InstantSmelt/Playes";
        private List<ulong> data = new List<ulong>();

        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<List<ulong>>(filename);
            }
            catch (Exception e)
            {
                PrintWarning(e.Message);
            }

            SaveData();
            timer.Every(Core.Random.Range(500 , 700f) , SaveData);
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(filename , data);
        }

        #endregion

        #region Language API

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string , string>
            {
                {"Permission", "You don't have permission to use that!"},
                {"Enabled", "You enabled instant smelt!"},
                {"Disabled", "You disabled instant smelt!"},
            } , this);
        }

        private void Message(BasePlayer player , string messageKey , params object[] args)
        {
            if (player == null)
            {
                return;
            }

            var message = GetMessage(messageKey , player.UserIDString , args);
            player.ChatMessage(message);
        }

        private string GetMessage(string messageKey , string playerID , params object[] args)
        {
            return string.Format(lang.GetMessage(messageKey , this , playerID) , args);
        }

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            LoadConfigVariables();

            permission.RegisterPermission(permUse , this);
            cmd.AddChatCommand(configData.command , this , nameof(cmdToggleChat));
            LoadData();
        }

        private void Unload()
        {
            SaveData();
        }

        private object OnCollectiblePickup(Item item , BasePlayer player)
        {
            return OnGather(player , item , false , true);
        }

        private object OnDispenserGather(ResourceDispenser dispenser , BasePlayer player , Item item)
        {
            return OnGather(player , item);
        }

        private object OnDispenserBonus(ResourceDispenser dispenser , BasePlayer player , Item item)
        {
            return OnGather(player , item , true);
        }

        #endregion

        #region Commands

        private void cmdToggleChat(BasePlayer player)
        {
            if (!HasPermission(player))
            {
                Message(player , "Permission");
                return;
            }

            var key = string.Empty;

            if (data.Contains(player.userID))
            {
                key = "Enabled";
                data.Remove(player.userID);
            }
            else
            {
                key = "Disabled";
                data.Add(player.userID);
            }

            Message(player , key);
        }

        #endregion

        #region Core

        private object OnGather(BasePlayer player , Item item , bool bonus = false , bool pickup = false)
        {
            if (!HasPermission(player)) return null;
            if (player == null || item == null || item.info == null) return null;

            var shortname = item.info.shortname;

            if (!configData.autoSmeltEnabled.GetValueOrDefault(shortname , false))
                return null;

            if (data.Contains(player.userID))
                return null;

            object overrideSmelt = Interface.CallHook("CanSmelt" , player , item);
            if (overrideSmelt is bool result && result == false)
                return null;

            // Delay the smelt until the next server tick so gather-rate plugins
            // such as Gather Manager can finish multiplying item.amount first.
            NextTick(() => SmeltGatheredItem(player , item));
            return null;
        }

        private void SmeltGatheredItem(BasePlayer player , Item item)
        {
            if (player == null || item == null || item.info == null || item.amount <= 0) return;

            var shortname = item.info.shortname;

            if (!configData.autoSmeltEnabled.GetValueOrDefault(shortname , false))
                return;

            if (data.Contains(player.userID))
                return;

            object overrideSmelt = Interface.CallHook("CanSmelt" , player , item);
            if (overrideSmelt is bool result && result == false)
                return;

            Item newItem;
            if (shortname == woodItemName)
            {
                newItem = ItemManager.CreateByName(charcoalItemName , item.amount);
            }
            else
            {
                var cookable = item.info.GetComponent<ItemModCookable>();
                if (cookable == null) return;
                newItem = ItemManager.Create(cookable.becomeOnCooked , item.amount);
            }

            if (newItem == null) return;

            Interface.CallHook("OnSmelted" , player , item , newItem);

            item.RemoveFromContainer();
            item.Remove();
            player.GiveItem(newItem , BaseEntity.GiveItemReason.ResourceHarvested);
        }

        private bool HasPermission(BasePlayer player)
        {
            return permission.UserHasPermission(player.UserIDString , permUse);
        }

        #endregion

        #region API

        [HookMethod("CanSmelt")]
        public object CanSmelt(BasePlayer player , Item item)
        {
            var shortname = item.info.shortname;

            if (!configData.autoSmeltEnabled.GetValueOrDefault(shortname , false))
                return false;

            if (data.Contains(player.userID))
                return false;

            return null;
        }

        #endregion
    }
}