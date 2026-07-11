using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Hotbars", "Codex", "1.0.6")]
    [Description("Lets players save up to five MMO-style hotbars and switch between them with commands, binds, or a GUI.")]
    public class Hotbars : RustPlugin
    {
        private const int MaxHotbars = 5;
        private const string PermissionUse = "hotbars.use";
        private const string UiMenu = "Hotbars.Menu";

        private PluginConfig config;
        private StoredData storedData;
        private readonly Dictionary<ulong, int> activeHotbar = new Dictionary<ulong, int>();

        #region Oxide Hooks

        private void Init()
        {
            permission.RegisterPermission(PermissionUse, this);

            for (var index = 1; index <= MaxHotbars; index++)
            {
                permission.RegisterPermission($"hotbars.{index}", this);
            }

            LoadData();
        }

        private void Unload()
        {
            SaveData();

            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyUi(player);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            DestroyUi(player);
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player == null || config.RestoreHotbarOnDeath)
            {
                return;
            }

            activeHotbar.Remove(player.userID);
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            if (!config.RestoreHotbarOnDeath || !activeHotbar.TryGetValue(player.userID, out var hotbarNumber))
            {
                return;
            }

            timer.Once(0.25f, () => RestoreHotbar(player, hotbarNumber, true));
        }

        #endregion

        #region Commands

        [ChatCommand("hotbar")]
        private void HotbarChatCommand(BasePlayer player, string command, string[] args)
        {
            if (!CanUse(player))
            {
                return;
            }

            if (args.Length == 0)
            {
                SendHelp(player);
                return;
            }

            HandleHotbarCommand(player, args);
        }

        [ConsoleCommand("hotbar")]
        private void HotbarConsoleCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !CanUse(player))
            {
                return;
            }

            HandleHotbarCommand(player, GetArgs(arg));
        }

        [ConsoleCommand("hotbar.switch")]
        private void HotbarSwitchConsoleCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !CanUse(player))
            {
                return;
            }

            if (!TryReadHotbarNumber(GetArgs(arg), 0, out var hotbarNumber))
            {
                Reply(player, "Usage: hotbar.switch 1-5");
                return;
            }

            RestoreHotbar(player, hotbarNumber);
        }

        [ConsoleCommand("hotbar.save")]
        private void HotbarSaveConsoleCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !CanUse(player))
            {
                return;
            }

            if (TryReadHotbarNumber(GetArgs(arg), 0, out var hotbarNumber))
            {
                SaveHotbar(player, hotbarNumber, true);
                return;
            }

            SavePlayerHotbar(player, true);
        }

        [ConsoleCommand("hotbar.menu")]
        private void HotbarMenuConsoleCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !CanUse(player))
            {
                return;
            }

        }

        [ConsoleCommand("hotbar.close")]
        private void HotbarCloseConsoleCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player != null)
            {
                CuiHelper.DestroyUi(player, UiMenu);
            }
        }

        [ConsoleCommand("hotbar.delete")]
        private void HotbarDeleteConsoleCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (player == null || !CanUse(player))
            {
                return;
            }

            if (!TryReadHotbarNumber(GetArgs(arg), 0, out var hotbarNumber))
            {
                Reply(player, "Usage: hotbar.delete 1-5");
                return;
            }

            DeleteHotbar(player, hotbarNumber);
        }

        private void HandleHotbarCommand(BasePlayer player, string[] args)
        {
            var action = args[0].ToLowerInvariant();

            switch (action)
            {
                case "list":
                case "menu":
                case "open":
                    DrawMenu(player);
                    break;

                case "close":
                    CuiHelper.DestroyUi(player, UiMenu);
                    break;

                case "save":
                    SavePlayerHotbar(player, true);
                    break;

                case "delete":
                    if (!TryReadHotbarNumber(args, 1, out var deleteNumber))
                    {
                        Reply(player, "Usage: /hotbar delete 1-5");
                        return;
                    }

                    DeleteHotbar(player, deleteNumber);
                    break;

                default:
                    if (!TryReadHotbarNumber(args, 0, out var hotbarNumber))
                    {
                        SendHelp(player);
                        return;
                    }

                    RestoreHotbar(player, hotbarNumber);
                    break;
            }
        }

        #endregion

        #region Hotbar Logic

        private void SavePlayerHotbar(BasePlayer player, bool notify = false)
        {
            if (player == null || !HasPermission(player, PermissionUse))
            {
                return;
            }

            if (!activeHotbar.TryGetValue(player.userID, out var hotbarNumber))
            {
                hotbarNumber = GetFirstAllowedHotbar(player);
                activeHotbar[player.userID] = hotbarNumber;
            }

            SaveHotbar(player, hotbarNumber, notify);
        }

        private void SaveHotbar(BasePlayer player, int hotbarNumber, bool notify, bool refreshUi = true)
        {
            if (!CanAccessHotbar(player, hotbarNumber))
            {
                Reply(player, $"You do not have permission to use hotbar {hotbarNumber}.");
                return;
            }

            var playerData = GetPlayerData(player.userID);
            var hotbarData = new HotbarData();

            for (var slot = 0; slot < player.inventory.containerBelt.capacity; slot++)
            {
                var item = player.inventory.containerBelt.GetSlot(slot);

                if (item == null)
                {
                    continue;
                }

                if (config.OnlyUsableItems && !IsUsableItem(item))
                {
                    continue;
                }

                hotbarData.Items.Add(CreateItemData(item, slot));
            }

            playerData.Hotbars[hotbarNumber] = hotbarData;
            activeHotbar[player.userID] = hotbarNumber;
            SaveData();

            if (notify)
            {
                Reply(player, $"Saved hotbar {hotbarNumber}.");
            }

            if (refreshUi)
            {
                DrawMenu(player);
            }
        }

        private void RestoreHotbar(BasePlayer player, int hotbarNumber, bool quiet = false)
        {
            if (!CanAccessHotbar(player, hotbarNumber))
            {
                Reply(player, $"You do not have permission to use hotbar {hotbarNumber}.");
                return;
            }

            var playerData = GetPlayerData(player.userID);

            if (!playerData.Hotbars.TryGetValue(hotbarNumber, out var hotbarData))
            {
                Reply(player, $"Hotbar {hotbarNumber} has not been saved yet.");
                return;
            }

            if (activeHotbar.TryGetValue(player.userID, out var oldHotbarNumber)
                && oldHotbarNumber != hotbarNumber
                && playerData.Hotbars.ContainsKey(oldHotbarNumber))
            {
                SaveHotbar(player, oldHotbarNumber, false, false);
            }

            RemoveCurrentBeltItems(player);
            var keptItems = new HotbarData();

            foreach (var savedItem in hotbarData.Items)
            {
                var item = FindMatchingItem(player, savedItem);

                if (item == null)
                {
                    continue;
                }

                var targetSlot = Mathf.Clamp(savedItem.Slot, 0, player.inventory.containerBelt.capacity - 1);

                if (!item.MoveToContainer(player.inventory.containerBelt, targetSlot, false))
                {
                    item.MoveToContainer(player.inventory.containerMain);
                    continue;
                }

                keptItems.Items.Add(CreateItemData(item, targetSlot));
            }

            playerData.Hotbars[hotbarNumber] = keptItems;
            activeHotbar[player.userID] = hotbarNumber;
            SaveData();

            if (!quiet)
            {
                Reply(player, $"Switched to hotbar {hotbarNumber}.");
            }
        }

        private void DeleteHotbar(BasePlayer player, int hotbarNumber)
        {
            if (!CanAccessHotbar(player, hotbarNumber))
            {
                Reply(player, $"You do not have permission to delete hotbar {hotbarNumber}.");
                return;
            }

            var playerData = GetPlayerData(player.userID);

            if (!playerData.Hotbars.Remove(hotbarNumber))
            {
                Reply(player, $"Hotbar {hotbarNumber} has not been saved yet.");
                return;
            }

            if (activeHotbar.TryGetValue(player.userID, out var activeNumber) && activeNumber == hotbarNumber)
            {
                activeHotbar.Remove(player.userID);
            }

            SaveData();
            Reply(player, $"Deleted hotbar {hotbarNumber}.");
            DrawMenu(player);
        }

        private void RemoveCurrentBeltItems(BasePlayer player)
        {
            var belt = player.inventory.containerBelt;

            for (var slot = belt.capacity - 1; slot >= 0; slot--)
            {
                var item = belt.GetSlot(slot);

                if (item == null)
                {
                    continue;
                }

                if (!item.MoveToContainer(player.inventory.containerMain))
                {
                    item.Drop(player.transform.position + Vector3.up, Vector3.zero);
                }
            }
        }

        private Item FindMatchingItem(BasePlayer player, SavedItem savedItem)
        {
            var containers = new[]
            {
                player.inventory.containerBelt,
                player.inventory.containerMain,
                player.inventory.containerWear
            };

            foreach (var container in containers)
            {
                if (container == null)
                {
                    continue;
                }

                foreach (var item in container.itemList)
                {
                    if (!MatchesSavedItem(item, savedItem))
                    {
                        continue;
                    }

                    return item;
                }
            }

            return null;
        }

        private static bool MatchesSavedItem(Item item, SavedItem savedItem)
        {
            if (item.info.shortname != savedItem.ShortName || item.skin != savedItem.Skin)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(savedItem.CustomName) && item.name != savedItem.CustomName)
            {
                return false;
            }

            return (item.instanceData?.dataInt ?? 0) == savedItem.InstanceData;
        }

        private SavedItem CreateItemData(Item item, int slot)
        {
            return new SavedItem
            {
                ShortName = item.info.shortname,
                DisplayName = GetItemDisplayName(item),
                ItemId = item.info.itemid,
                Amount = item.amount,
                Skin = item.skin,
                CustomName = item.name,
                InstanceData = item.instanceData?.dataInt ?? 0,
                Slot = slot
            };
        }

        private static string GetItemDisplayName(Item item)
        {
            if (!string.IsNullOrEmpty(item.name))
            {
                return item.name;
            }

            if (!string.IsNullOrEmpty(item.info?.displayName?.english))
            {
                return item.info.displayName.english;
            }

            return item.info?.shortname ?? "Unknown item";
        }

        private bool IsUsableItem(Item item)
        {
            if (item?.info == null)
            {
                return false;
            }

            var info = item.info;

            return info.GetComponent<ItemModDeployable>() != null
                || info.GetComponent<ItemModEntity>() != null
                || info.GetComponent<ItemModProjectile>() != null
                || info.GetComponent<ItemModConsumable>() != null
                || info.GetComponent<ItemModWearable>() != null
                || info.category == ItemCategory.Weapon
                || info.category == ItemCategory.Tool
                || info.category == ItemCategory.Medical
                || info.category == ItemCategory.Food;
        }

        #endregion

        #region UI

        private void DrawMenu(BasePlayer player)
        {
            if (player == null || !HasPermission(player, PermissionUse))
            {
                return;
            }

            CuiHelper.DestroyUi(player, UiMenu);

            var allowedBars = GetAllowedHotbars(player);
            var playerData = GetPlayerData(player.userID);
            var elements = new CuiElementContainer();

            elements.Add(new CuiPanel
            {
                Image =
                {
                    Color = "0.06 0.07 0.08 0.94"
                },
                RectTransform =
                {
                    AnchorMin = config.HotbarContainerBounds.AnchorMin,
                    AnchorMax = config.HotbarContainerBounds.AnchorMax,
                    OffsetMin = config.HotbarContainerBounds.OffsetMin,
                    OffsetMax = config.HotbarContainerBounds.OffsetMax
                },
                CursorEnabled = true
            }, "Overlay", UiMenu);

            elements.Add(new CuiLabel
            {
                Text =
                {
                    Text = "Saved Hotbars",
                    FontSize = 14,
                    Align = TextAnchor.MiddleLeft,
                    Color = "0.95 0.95 0.95 1"
                },
                RectTransform =
                {
                    AnchorMin = "0.05 0.88",
                    AnchorMax = "0.70 0.98",
                    OffsetMin = "0 0",
                    OffsetMax = "0 0"
                }
            }, UiMenu, $"{UiMenu}.Title");

            elements.Add(new CuiLabel
            {
                Text =
                {
                    Text = "Switch between your saved belt loadouts",
                    FontSize = 9,
                    Align = TextAnchor.MiddleLeft,
                    Color = "0.65 0.7 0.76 1"
                },
                RectTransform =
                {
                    AnchorMin = "0.05 0.82",
                    AnchorMax = "0.76 0.91",
                    OffsetMin = "0 0",
                    OffsetMax = "0 0"
                }
            }, UiMenu, $"{UiMenu}.Subtitle");

            elements.Add(new CuiButton
            {
                Button =
                {
                    Command = "hotbar.close",
                    Color = "0.55 0.13 0.13 0.85"
                },
                Text =
                {
                    Text = "X",
                    FontSize = 14,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 1"
                },
                RectTransform =
                {
                    AnchorMin = "0.91 0.86",
                    AnchorMax = "0.98 0.97",
                    OffsetMin = "0 0",
                    OffsetMax = "0 0"
                }
            }, UiMenu, $"{UiMenu}.Close");

            var rowHeight = 0.13f;

            for (var number = 1; number <= MaxHotbars; number++)
            {
                var yMax = 0.78f - ((number - 1) * rowHeight);
                var yMin = yMax - 0.105f;
                var isAllowed = number <= allowedBars;
                var isSaved = playerData.Hotbars.ContainsKey(number);
                var isActive = activeHotbar.TryGetValue(player.userID, out var activeNumber) && activeNumber == number;
                var label = isActive ? $"Hotbar {number} *" : $"Hotbar {number}";
                var rowColor = isAllowed ? "0.16 0.18 0.2 0.88" : "0.12 0.12 0.12 0.55";

                elements.Add(new CuiPanel
                {
                    Image =
                    {
                        Color = rowColor
                    },
                    RectTransform =
                    {
                        AnchorMin = $"0.05 {yMin}",
                        AnchorMax = $"0.95 {yMax}",
                        OffsetMin = "0 0",
                        OffsetMax = "0 0"
                    }
                }, UiMenu, $"{UiMenu}.Row.{number}");

                elements.Add(new CuiLabel
                {
                    Text =
                    {
                        Text = isAllowed ? label : $"Bar {number} locked",
                        FontSize = 10,
                        Align = TextAnchor.MiddleLeft,
                        Color = isAllowed ? "1 1 1 1" : "0.55 0.55 0.55 1"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.04 0",
                        AnchorMax = "0.18 1",
                        OffsetMin = "0 0",
                        OffsetMax = "0 0"
                    }
                }, $"{UiMenu}.Row.{number}", $"{UiMenu}.Row.{number}.Label");

                if (isAllowed && isSaved)
                {
                    AddHotbarItemPreview(elements, $"{UiMenu}.Row.{number}", playerData.Hotbars[number]);
                    AddMenuButton(elements, $"{UiMenu}.Row.{number}", "SWAP", $"hotbar.switch {number}", "0.80 0.27", "0.92 0.73", true);
                    AddMenuButton(elements, $"{UiMenu}.Row.{number}", "X", $"hotbar.delete {number}", "0.94 0.23", "0.985 0.77", true, "0.42 0.12 0.14 0.9");
                }
                else if (isAllowed)
                {
                    AddEmptyHotbarMessage(elements, $"{UiMenu}.Row.{number}");
                    AddMenuButton(elements, $"{UiMenu}.Row.{number}", "SAVE", $"hotbar.save {number}", "0.80 0.27", "0.96 0.73", true);
                }
                else
                {
                    AddLockedHotbarMessage(elements, $"{UiMenu}.Row.{number}");
                }
            }

            CuiHelper.AddUi(player, elements);
        }

        private void AddMenuButton(CuiElementContainer elements, string parent, string text, string command, string anchorMin, string anchorMax, bool enabled, string color = null)
        {
            elements.Add(new CuiButton
            {
                Button =
                {
                    Command = command,
                    Color = enabled ? (color ?? "0.24 0.42 0.64 0.95") : "0.22 0.22 0.22 0.65"
                },
                Text =
                {
                    Text = text,
                    FontSize = 11,
                    Align = TextAnchor.MiddleCenter,
                    Color = enabled ? "1 1 1 1" : "0.55 0.55 0.55 1"
                },
                RectTransform =
                {
                    AnchorMin = anchorMin,
                    AnchorMax = anchorMax,
                    OffsetMin = "0 0",
                    OffsetMax = "0 0"
                }
            }, parent);
        }

        private void AddEmptyHotbarMessage(CuiElementContainer elements, string parent)
        {
            elements.Add(new CuiLabel
            {
                Text =
                {
                    Text = "No items saved\nClick 'Save' to fill this slot",
                    FontSize = 9,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0.78 0.82 0.86 1"
                },
                RectTransform =
                {
                    AnchorMin = "0.22 0.08",
                    AnchorMax = "0.76 0.92",
                    OffsetMin = "0 0",
                    OffsetMax = "0 0"
                }
            }, parent);
        }

        private void AddLockedHotbarMessage(CuiElementContainer elements, string parent)
        {
            elements.Add(new CuiLabel
            {
                Text =
                {
                    Text = "Permission required",
                    FontSize = 9,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0.55 0.55 0.55 1"
                },
                RectTransform =
                {
                    AnchorMin = "0.22 0.08",
                    AnchorMax = "0.96 0.92",
                    OffsetMin = "0 0",
                    OffsetMax = "0 0"
                }
            }, parent);
        }

        private void AddHotbarItemPreview(CuiElementContainer elements, string parent, HotbarData hotbarData)
        {
            for (var slot = 0; slot < 6; slot++)
            {
                var xMin = 0.23f + (slot * 0.065f);
                var xMax = xMin + 0.055f;
                var slotName = $"{parent}.Preview.{slot}";
                var savedItem = GetSavedItemInSlot(hotbarData, slot);

                elements.Add(new CuiPanel
                {
                    Image =
                    {
                        Color = savedItem == null ? "0.08 0.09 0.1 0.75" : "0.11 0.13 0.15 0.95"
                    },
                    RectTransform =
                    {
                        AnchorMin = $"{xMin} 0.12",
                        AnchorMax = $"{xMax} 0.88",
                        OffsetMin = "0 0",
                        OffsetMax = "0 0"
                    }
                }, parent, slotName);

                if (savedItem == null)
                {
                    continue;
                }

                var itemId = savedItem.ItemId;

                if (itemId == 0)
                {
                    itemId = ItemManager.FindItemDefinition(savedItem.ShortName)?.itemid ?? 0;
                }

                if (itemId != 0)
                {
                    elements.Add(new CuiElement
                    {
                        Parent = slotName,
                        Components =
                        {
                            new CuiImageComponent
                            {
                                ItemId = itemId,
                                SkinId = savedItem.Skin,
                                Color = "1 1 1 1"
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.08 0.08",
                                AnchorMax = "0.92 0.92"
                            }
                        }
                    });
                }
                else
                {
                    elements.Add(new CuiLabel
                    {
                        Text =
                        {
                            Text = AbbreviateItemName(savedItem),
                            FontSize = 8,
                            Align = TextAnchor.MiddleCenter,
                            Color = "0.85 0.9 0.95 1"
                        },
                        RectTransform =
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1",
                            OffsetMin = "0 0",
                            OffsetMax = "0 0"
                        }
                    }, slotName);
                }

                if (savedItem.Amount > 1)
                {
                    elements.Add(new CuiLabel
                    {
                        Text =
                        {
                            Text = savedItem.Amount.ToString(),
                            FontSize = 8,
                            Align = TextAnchor.LowerRight,
                            Color = "1 1 1 1"
                        },
                        RectTransform =
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "0.96 0.96",
                            OffsetMin = "0 0",
                            OffsetMax = "0 0"
                        }
                    }, slotName);
                }
            }
        }

        private static SavedItem GetSavedItemInSlot(HotbarData hotbarData, int slot)
        {
            if (hotbarData?.Items == null)
            {
                return null;
            }

            foreach (var item in hotbarData.Items)
            {
                if (item.Slot == slot)
                {
                    return item;
                }
            }

            return null;
        }

        private static string AbbreviateItemName(SavedItem item)
        {
            var name = !string.IsNullOrEmpty(item.DisplayName) ? item.DisplayName : item.ShortName;

            if (string.IsNullOrEmpty(name))
            {
                return "?";
            }

            return name.Length <= 4 ? name : name.Substring(0, 4);
        }

        private void DestroyUi(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UiMenu);
        }

        #endregion

        #region Permissions and Helpers

        private bool CanUse(BasePlayer player)
        {
            if (player == null)
            {
                return false;
            }

            if (HasPermission(player, PermissionUse))
            {
                return true;
            }

            Reply(player, "You do not have permission to use hotbars.");
            return false;
        }

        private bool CanAccessHotbar(BasePlayer player, int hotbarNumber)
        {
            return hotbarNumber >= 1 && hotbarNumber <= GetAllowedHotbars(player);
        }

        private int GetAllowedHotbars(BasePlayer player)
        {
            for (var number = MaxHotbars; number >= 1; number--)
            {
                if (HasPermission(player, $"hotbars.{number}"))
                {
                    return number;
                }
            }

            return 1;
        }

        private int GetFirstAllowedHotbar(BasePlayer player)
        {
            return Mathf.Clamp(activeHotbar.TryGetValue(player.userID, out var number) ? number : 1, 1, GetAllowedHotbars(player));
        }

        private bool HasPermission(BasePlayer player, string permissionName)
        {
            return player.IsAdmin || permission.UserHasPermission(player.UserIDString, permissionName);
        }

        private static string[] GetArgs(ConsoleSystem.Arg arg)
        {
            if (arg?.Args == null || arg.Args.Length == 0)
            {
                return new string[0];
            }

            var args = new string[arg.Args.Length];

            for (var index = 0; index < arg.Args.Length; index++)
            {
                args[index] = arg.Args[index].ToString();
            }

            return args;
        }

        private static bool TryReadHotbarNumber(string[] args, int index, out int hotbarNumber)
        {
            hotbarNumber = 0;

            return args != null
                && args.Length > index
                && int.TryParse(args[index], out hotbarNumber)
                && hotbarNumber >= 1
                && hotbarNumber <= MaxHotbars;
        }

        private void Reply(BasePlayer player, string message)
        {
            SendReply(player, $"<color=#7fb4ff>[Hotbars]</color> {message}");
        }

        private void SendHelp(BasePlayer player)
        {
            Reply(player, "Commands:");
            SendReply(player, "/hotbar list - Open the hotbar menu.");
            SendReply(player, "/hotbar close - Close the hotbar menu.");
            SendReply(player, "/hotbar save - Save the current belt to your active hotbar.");
            SendReply(player, "/hotbar delete 1-5 - Delete a saved hotbar.");
            SendReply(player, "/hotbar 1-5 - Switch to a saved hotbar.");
            SendReply(player, "Bind example: bind f1 \"hotbar.switch 1\"");
        }

        #endregion

        #region Data and Config

        private PlayerData GetPlayerData(ulong userId)
        {
            if (!storedData.Players.TryGetValue(userId, out var playerData))
            {
                playerData = new PlayerData();
                storedData.Players[userId] = playerData;
            }

            return playerData;
        }

        private void LoadData()
        {
            storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);
        }

        protected override void LoadDefaultConfig()
        {
            config = PluginConfig.Default();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<PluginConfig>();

                if (config == null)
                {
                    throw new JsonException("Configuration file is empty.");
                }
            }
            catch (Exception exception)
            {
                PrintWarning($"Configuration error: {exception.Message}");
                LoadDefaultConfig();
            }

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private class StoredData
        {
            public Dictionary<ulong, PlayerData> Players = new Dictionary<ulong, PlayerData>();
        }

        private class PlayerData
        {
            public Dictionary<int, HotbarData> Hotbars = new Dictionary<int, HotbarData>();
        }

        private class HotbarData
        {
            public List<SavedItem> Items = new List<SavedItem>();
        }

        private class SavedItem
        {
            public string ShortName;
            public string DisplayName;
            public int ItemId;
            public int Amount;
            public ulong Skin;
            public string CustomName;
            public int InstanceData;
            public int Slot;
        }

        private class PluginConfig
        {
            public bool OnlyUsableItems = true;
            public bool RestoreHotbarOnDeath = false;
            public UiBounds HotbarContainerBounds = new UiBounds("0.36 0.28", "0.64 0.72", "0 0", "0 0");

            public static PluginConfig Default()
            {
                return new PluginConfig();
            }
        }

        private class UiBounds
        {
            public string AnchorMin;
            public string AnchorMax;
            public string OffsetMin;
            public string OffsetMax;

            public UiBounds()
            {
            }

            public UiBounds(string anchorMin, string anchorMax, string offsetMin, string offsetMax)
            {
                AnchorMin = anchorMin;
                AnchorMax = anchorMax;
                OffsetMin = offsetMin;
                OffsetMax = offsetMax;
            }
        }

        #endregion
    }
}
