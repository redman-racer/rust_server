using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MultiHotbars", "OpenAI", "1.0.0")]
    [Description("Provides up to five switchable, MMO-style hotbar layouts using commands, key binds, or a GUI.")]
    public class MultiHotbars : RustPlugin
    {
        private const string PermissionUse = "multihotbars.use";
        private const string UiName = "MultiHotbars.UI";
        private const int BeltSlots = 6;

        private PluginConfig _config;
        private DynamicConfigFile _dataFile;
        private StoredData _storedData;
        private readonly HashSet<ulong> _openUi = new HashSet<ulong>();

        #region Configuration

        private class PluginConfig
        {
            [JsonProperty("Maximum hotbars per player (1-5)")]
            public int MaximumHotbars = 5;

            [JsonProperty("Automatically save the active bar before switching")]
            public bool AutoSaveActiveBar = true;

            [JsonProperty("Show switch confirmation in chat")]
            public bool ShowSwitchMessages = true;

            [JsonProperty("GUI anchor minimum")]
            public string GuiAnchorMin = "0.385 0.105";

            [JsonProperty("GUI anchor maximum")]
            public string GuiAnchorMax = "0.615 0.155";

            [JsonProperty("GUI background color")]
            public string GuiBackgroundColor = "0.08 0.08 0.08 0.88";

            [JsonProperty("GUI active button color")]
            public string GuiActiveColor = "0.20 0.55 0.95 0.95";

            [JsonProperty("GUI inactive button color")]
            public string GuiInactiveColor = "0.22 0.22 0.22 0.95";
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null)
                    throw new Exception("Configuration was empty.");
            }
            catch (Exception ex)
            {
                PrintWarning($"Invalid configuration; creating a new one. Error: {ex.Message}");
                LoadDefaultConfig();
            }

            _config.MaximumHotbars = Mathf.Clamp(_config.MaximumHotbars, 1, 5);
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        #endregion

        #region Data

        private class StoredData
        {
            [JsonProperty("Players")]
            public Dictionary<ulong, PlayerData> Players = new Dictionary<ulong, PlayerData>();
        }

        private class PlayerData
        {
            [JsonProperty("ActiveBar")]
            public int ActiveBar;

            [JsonProperty("Bars")]
            public Dictionary<int, HotbarData> Bars = new Dictionary<int, HotbarData>();
        }

        private class HotbarData
        {
            [JsonProperty("Slots")]
            public List<HotbarSlot> Slots = new List<HotbarSlot>();
        }

        private class HotbarSlot
        {
            [JsonProperty("Slot")]
            public int Slot;

            [JsonProperty("ItemUid")]
            public string ItemUid;

            [JsonProperty("ShortName")]
            public string ShortName;

            [JsonProperty("DisplayName")]
            public string DisplayName;
        }

        private void LoadData()
        {
            _dataFile = Interface.Oxide.DataFileSystem.GetFile(Name);
            try
            {
                _storedData = _dataFile.ReadObject<StoredData>() ?? new StoredData();
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not read data; starting with an empty data file. Error: {ex.Message}");
                _storedData = new StoredData();
            }
        }

        private void SaveData() => _dataFile.WriteObject(_storedData);

        private PlayerData GetPlayerData(ulong userId)
        {
            PlayerData data;
            if (!_storedData.Players.TryGetValue(userId, out data))
            {
                data = new PlayerData();
                _storedData.Players[userId] = data;
            }

            return data;
        }

        private HotbarData GetBar(PlayerData playerData, int barNumber, bool create)
        {
            HotbarData bar;
            if (!playerData.Bars.TryGetValue(barNumber, out bar) && create)
            {
                bar = new HotbarData();
                playerData.Bars[barNumber] = bar;
            }

            return bar;
        }

        #endregion

        #region Lifecycle

        private void Init()
        {
            permission.RegisterPermission(PermissionUse, this);
            LoadData();
        }

        private void OnServerSave() => SaveData();

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            _openUi.Remove(player.userID);
            DestroyUi(player);
        }

        private void Unload()
        {
            SaveData();
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                DestroyUi(player);
        }

        #endregion

        #region Chat Commands

        [ChatCommand("hotbar")]
        private void HotbarChatCommand(BasePlayer player, string command, string[] args)
        {
            if (!CanUse(player))
                return;

            if (args == null || args.Length == 0)
            {
                ToggleUi(player);
                return;
            }

            HandleAction(player, args[0], args.Skip(1).ToArray());
        }

        [ChatCommand("hb")]
        private void HotbarAliasCommand(BasePlayer player, string command, string[] args)
        {
            HotbarChatCommand(player, command, args);
        }

        #endregion

        #region Console / Bind Commands

        [ConsoleCommand("hotbar.load")]
        private void ConsoleLoad(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !CanUse(player))
                return;

            int barNumber;
            if (!TryGetBarNumber(arg.Args, 0, out barNumber))
            {
                SendReply(player, $"Usage: hotbar.load <1-{_config.MaximumHotbars}>");
                return;
            }

            SwitchToBar(player, barNumber);
        }

        [ConsoleCommand("hotbar.save")]
        private void ConsoleSave(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !CanUse(player))
                return;

            int barNumber;
            if (!TryGetBarNumber(arg.Args, 0, out barNumber))
            {
                SendReply(player, $"Usage: hotbar.save <1-{_config.MaximumHotbars}>");
                return;
            }

            SaveBar(player, barNumber, true);
        }

        [ConsoleCommand("hotbar.next")]
        private void ConsoleNext(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !CanUse(player))
                return;

            SwitchRelative(player, 1);
        }

        [ConsoleCommand("hotbar.prev")]
        private void ConsolePrevious(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !CanUse(player))
                return;

            SwitchRelative(player, -1);
        }

        [ConsoleCommand("hotbar.ui")]
        private void ConsoleUi(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !CanUse(player))
                return;

            ToggleUi(player);
        }

        [ConsoleCommand("hotbar.close")]
        private void ConsoleClose(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            _openUi.Remove(player.userID);
            DestroyUi(player);
        }

        #endregion

        #region Command Handling

        private void HandleAction(BasePlayer player, string action, string[] args)
        {
            switch ((action ?? string.Empty).ToLowerInvariant())
            {
                case "load":
                case "use":
                case "switch":
                {
                    int barNumber;
                    if (!TryGetBarNumber(args, 0, out barNumber))
                    {
                        SendReply(player, $"Usage: /hotbar load <1-{_config.MaximumHotbars}>");
                        return;
                    }

                    SwitchToBar(player, barNumber);
                    break;
                }

                case "save":
                {
                    int barNumber;
                    if (!TryGetBarNumber(args, 0, out barNumber))
                    {
                        SendReply(player, $"Usage: /hotbar save <1-{_config.MaximumHotbars}>");
                        return;
                    }

                    SaveBar(player, barNumber, true);
                    break;
                }

                case "clear":
                {
                    int barNumber;
                    if (!TryGetBarNumber(args, 0, out barNumber))
                    {
                        SendReply(player, $"Usage: /hotbar clear <1-{_config.MaximumHotbars}>");
                        return;
                    }

                    ClearBar(player, barNumber);
                    break;
                }

                case "next":
                    SwitchRelative(player, 1);
                    break;

                case "prev":
                case "previous":
                    SwitchRelative(player, -1);
                    break;

                case "ui":
                    ToggleUi(player);
                    break;

                case "help":
                default:
                    ShowHelp(player);
                    break;
            }
        }

        private bool TryGetBarNumber(string[] args, int index, out int barNumber)
        {
            barNumber = 0;
            return args != null && args.Length > index &&
                   int.TryParse(args[index], out barNumber) &&
                   barNumber >= 1 && barNumber <= _config.MaximumHotbars;
        }

        private bool CanUse(BasePlayer player)
        {
            if (permission.UserHasPermission(player.UserIDString, PermissionUse))
                return true;

            SendReply(player, "You do not have permission to use multiple hotbars.");
            return false;
        }

        private void ShowHelp(BasePlayer player)
        {
            SendReply(player,
                $"<color=#74b9ff>MultiHotbars</color>\n" +
                $"/hotbar save <1-{_config.MaximumHotbars}> - Save your current belt\n" +
                $"/hotbar load <1-{_config.MaximumHotbars}> - Recall a saved belt\n" +
                "/hotbar next | prev - Cycle bars\n" +
                $"/hotbar clear <1-{_config.MaximumHotbars}> - Delete a saved bar\n" +
                "/hotbar - Open or close the selector GUI");
        }

        #endregion

        #region Hotbar Logic

        private void SaveBar(BasePlayer player, int barNumber, bool notify)
        {
            PlayerData playerData = GetPlayerData(player.userID);
            HotbarData bar = GetBar(playerData, barNumber, true);
            bar.Slots.Clear();

            for (int slot = 0; slot < BeltSlots; slot++)
            {
                Item item = player.inventory.containerBelt.GetSlot(slot);
                if (item == null)
                    continue;

                bar.Slots.Add(new HotbarSlot
                {
                    Slot = slot,
                    ItemUid = item.uid.ToString(),
                    ShortName = item.info.shortname,
                    DisplayName = item.info.displayName.english
                });
            }

            playerData.ActiveBar = barNumber;
            SaveData();

            if (notify)
                SendReply(player, $"Saved the current belt to hotbar {barNumber}.");

            RefreshUi(player);
        }

        private void ClearBar(BasePlayer player, int barNumber)
        {
            PlayerData playerData = GetPlayerData(player.userID);
            playerData.Bars.Remove(barNumber);
            if (playerData.ActiveBar == barNumber)
                playerData.ActiveBar = 0;

            SaveData();
            SendReply(player, $"Hotbar {barNumber} was cleared.");
            RefreshUi(player);
        }

        private void SwitchRelative(BasePlayer player, int direction)
        {
            PlayerData data = GetPlayerData(player.userID);
            int current = data.ActiveBar;
            int target = current <= 0
                ? (direction > 0 ? 1 : _config.MaximumHotbars)
                : ((current - 1 + direction + _config.MaximumHotbars) % _config.MaximumHotbars) + 1;

            SwitchToBar(player, target);
        }

        private void SwitchToBar(BasePlayer player, int barNumber)
        {
            PlayerData playerData = GetPlayerData(player.userID);

            if (_config.AutoSaveActiveBar && playerData.ActiveBar > 0 && playerData.ActiveBar != barNumber)
                SaveBar(player, playerData.ActiveBar, false);

            HotbarData bar = GetBar(playerData, barNumber, false);
            if (bar == null)
            {
                SendReply(player, $"Hotbar {barNumber} has not been saved yet. Use /hotbar save {barNumber}.");
                return;
            }

            Item[] allItems = player.inventory.AllItems();
            Dictionary<string, Item> itemsByUid = allItems
                .Where(item => item != null)
                .GroupBy(item => item.uid.ToString())
                .ToDictionary(group => group.Key, group => group.First());

            int removedMissing = bar.Slots.RemoveAll(slot =>
                string.IsNullOrEmpty(slot.ItemUid) || !itemsByUid.ContainsKey(slot.ItemUid));

            // A temporary server-side container allows cyclic belt rearrangement without
            // duplicating, dropping, or deleting items when the main inventory is full.
            ItemContainer temporary = new ItemContainer();
            temporary.ServerInitialize(null, BeltSlots);
            temporary.GiveUID();

            List<Item> originalBeltItems = player.inventory.containerBelt.itemList.ToList();
            foreach (Item item in originalBeltItems)
            {
                if (!item.MoveToContainer(temporary, -1, false))
                {
                    temporary.Kill();
                    SendReply(player, "Could not prepare the belt for switching. No hotbar changes were made.");
                    return;
                }
            }

            int failedMoves = 0;
            foreach (HotbarSlot slot in bar.Slots.OrderBy(entry => entry.Slot).ToList())
            {
                Item item;
                if (!itemsByUid.TryGetValue(slot.ItemUid, out item) || item == null)
                {
                    bar.Slots.Remove(slot);
                    removedMissing++;
                    continue;
                }

                if (!item.MoveToContainer(player.inventory.containerBelt, slot.Slot, false))
                {
                    bar.Slots.Remove(slot);
                    failedMoves++;
                }
            }

            // Return anything that belonged to the previous belt. Main inventory is
            // preferred; any remaining item goes into a free belt slot.
            foreach (Item item in temporary.itemList.ToList())
            {
                if (item.MoveToContainer(player.inventory.containerMain, -1, false))
                    continue;

                if (item.MoveToContainer(player.inventory.containerBelt, -1, false))
                    continue;

                // Capacity should be conserved, but this is a final safety fallback.
                item.Drop(player.GetDropPosition(), player.GetDropVelocity());
            }

            temporary.Kill();
            playerData.ActiveBar = barNumber;
            SaveData();
            player.inventory.ServerUpdate(0f);

            if (_config.ShowSwitchMessages)
            {
                string cleanup = removedMissing > 0
                    ? $" Removed {removedMissing} missing item{(removedMissing == 1 ? string.Empty : "s")} from the saved bar."
                    : string.Empty;
                string failures = failedMoves > 0
                    ? $" {failedMoves} item{(failedMoves == 1 ? string.Empty : "s")} could not be moved and were removed from the saved bar."
                    : string.Empty;

                SendReply(player, $"Switched to hotbar {barNumber}.{cleanup}{failures}");
            }

            RefreshUi(player);
        }

        #endregion

        #region GUI

        private void ToggleUi(BasePlayer player)
        {
            if (_openUi.Contains(player.userID))
            {
                _openUi.Remove(player.userID);
                DestroyUi(player);
                return;
            }

            _openUi.Add(player.userID);
            DrawUi(player);
        }

        private void RefreshUi(BasePlayer player)
        {
            if (_openUi.Contains(player.userID))
                DrawUi(player);
        }

        private void DestroyUi(BasePlayer player) => CuiHelper.DestroyUi(player, UiName);

        private void DrawUi(BasePlayer player)
        {
            DestroyUi(player);

            PlayerData playerData = GetPlayerData(player.userID);
            CuiElementContainer elements = new CuiElementContainer();

            elements.Add(new CuiPanel
            {
                Image = { Color = _config.GuiBackgroundColor },
                RectTransform = { AnchorMin = _config.GuiAnchorMin, AnchorMax = _config.GuiAnchorMax },
                CursorEnabled = false
            }, "Hud", UiName);

            float closeWidth = 0.12f;
            float usableWidth = 1f - closeWidth;
            float buttonWidth = usableWidth / _config.MaximumHotbars;

            for (int index = 0; index < _config.MaximumHotbars; index++)
            {
                int barNumber = index + 1;
                float minX = index * buttonWidth;
                float maxX = minX + buttonWidth;
                bool active = playerData.ActiveBar == barNumber;
                bool saved = playerData.Bars.ContainsKey(barNumber);

                elements.Add(new CuiButton
                {
                    Button =
                    {
                        Color = active ? _config.GuiActiveColor : _config.GuiInactiveColor,
                        Command = $"hotbar.load {barNumber}"
                    },
                    RectTransform =
                    {
                        AnchorMin = $"{minX + 0.006f} 0.10",
                        AnchorMax = $"{maxX - 0.006f} 0.90"
                    },
                    Text =
                    {
                        Text = saved ? barNumber.ToString() : $"{barNumber}*",
                        FontSize = 14,
                        Align = TextAnchor.MiddleCenter
                    }
                }, UiName);
            }

            elements.Add(new CuiButton
            {
                Button = { Color = "0.65 0.18 0.18 0.95", Command = "hotbar.close" },
                RectTransform = { AnchorMin = "0.89 0.10", AnchorMax = "0.99 0.90" },
                Text = { Text = "X", FontSize = 13, Align = TextAnchor.MiddleCenter }
            }, UiName);

            CuiHelper.AddUi(player, elements);
        }

        #endregion
    }
}
