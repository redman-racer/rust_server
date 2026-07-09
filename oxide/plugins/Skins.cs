//#define DEBUG

using System;
using System.Collections.Generic;
using System.Linq;
using Facepunch;
using Network;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using ProtoBuf;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Skins", "misticos", "2.2.4")]
    [Description("Change workshop skins of items easily")]
    class Skins : RustPlugin
    {
        #region Variables

        private static Skins _ins;

        private Dictionary<ulong, ContainerController> _controllers = new Dictionary<ulong, ContainerController>();

        private Dictionary<ItemContainerId, ContainerController> _controllersPerContainer =
            new Dictionary<ItemContainerId, ContainerController>();

        private Dictionary<ulong, SkinCraftController> _skinCraftControllers =
            new Dictionary<ulong, SkinCraftController>();

        private Dictionary<ItemContainerId, SkinCraftController> _skinCraftControllersPerContainer =
            new Dictionary<ItemContainerId, SkinCraftController>();

        private HashSet<ItemContainerId> _itemAttachmentContainers = new HashSet<ItemContainerId>();

        [PluginReference]
        private Plugin PlayerDLCAPI;
        [PluginReference]
        private Plugin Clans;

        private const string PermissionUse = "skins.use";
        private const string PermissionAdmin = "skins.admin";
        private const string PermissionSkinSets = "skins.skinsets";
        private const string PermissionAutoSkins = "skins.autoskins";
        private const string PermissionTeamShare = "skins.teamshare";

        private const string CommandDefault = "skins.skin";
        private const string SkinSetCommandDefault = "skins.skinset";
        private const string SkinCraftCommandDefault = "skins.skincraft";
        private const string SkinCraftPanelName = "SkinCraft.Editor";
        private const string SkinAutoCommandDefault = "skins.skinauto";
        private const string SkinTeamCommandDefault = "skins.skinteam";

        private readonly HashSet<Item> _autoSkinIgnoredItems = new HashSet<Item>();

        private enum SkinViewMode
        {
            Free,
            Owned
        }

        #endregion

        #region Configuration

        private Configuration _config;
        private SkinSetData _skinSetData;

        private class Configuration
        {
            [JsonProperty(PropertyName = "Commands")]
            public string[] Commands = { "skinbox", "sb", "skin", "skins" };

            [JsonProperty(PropertyName = "Skin Set Commands")]
            public string[] SkinSetCommands = { "skinset", "ss" };

            [JsonProperty(PropertyName = "Skin Craft Commands")]
            public string[] SkinCraftCommands = { "skincraft", "sc" };

            [JsonProperty(PropertyName = "Auto Skin Commands")]
            public string[] AutoSkinCommands = { "skinauto", "sauto" };

            [JsonProperty(PropertyName = "Team Skin Commands")]
            public string[] TeamSkinCommands = { "skinteam", "st" };

            [JsonProperty(PropertyName = "Skins", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<SkinItem> Skins = new List<SkinItem> { new SkinItem() };

            [JsonIgnore]
            public Dictionary<string, List<SkinItem>> IndexedSkins = new Dictionary<string, List<SkinItem>>();

            [JsonProperty(PropertyName = "Container Panel Name")]
            public string Panel = "generic";

            [JsonProperty(PropertyName = "Container Capacity")]
            public int Capacity = 36;

            [JsonProperty(PropertyName = "UI")]
            public UIConfiguration UI = new UIConfiguration();

            public class SkinItem
            {
                [JsonProperty(PropertyName = "Item Shortname")]
                // ReSharper disable once MemberCanBePrivate.Local
                public string Shortname = "shortname";

                [JsonProperty(PropertyName = "Permission")]
                public string Permission = "";

                [JsonProperty(PropertyName = "Skins", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public List<ulong> Skins = new List<ulong> { 0 };

                [JsonProperty(PropertyName = "Owned Skins", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public List<ulong> OwnedSkins = new List<ulong>();

                public static IEnumerable<SkinItem> Find(IPlayer player, string shortname)
                {
                    List<SkinItem> items;
                    if (!_ins._config.IndexedSkins.TryGetValue(shortname, out items))
                        yield break;

                    foreach (var item in items)
                    {
                        if (!item.CanUse(player))
                            continue;

                        yield return item;
                    }
                }

                public bool CanUse(IPlayer player) => player == null ||
                                                      string.IsNullOrEmpty(Permission) ||
                                                      player.HasPermission(Permission);

                public IEnumerable<ulong> GetFreeSkins()
                {
                    foreach (var skin in Skins)
                        yield return skin;
                }

                public IEnumerable<ulong> GetOwnedSkins(BasePlayer player, ItemDefinition itemDefinition)
                {
                    if (player == null)
                        yield break;

                    var ownedSkins = _ins.GetOwnedItemSkinIds(player, itemDefinition).Distinct().ToList();
                    if (ownedSkins.Count > 0)
                    {
                        foreach (var skin in ownedSkins)
                            yield return skin;

                        yield break;
                    }
                }

                public IEnumerable<ulong> GetAvailableSkins(BasePlayer player, ItemDefinition itemDefinition,
                    SkinViewMode mode)
                {
                    return mode == SkinViewMode.Owned
                        ? GetOwnedSkins(player, itemDefinition)
                        : GetFreeSkins();
                }
            }

            public class UIConfiguration
            {
                [JsonProperty(PropertyName = "Background Color")]
                public string BackgroundColor = "0.18 0.28 0.36";

                [JsonProperty(PropertyName = "Background Anchors")]
                public Anchors BackgroundAnchors = new Anchors
                    { AnchorMinX = "1.0", AnchorMinY = "1.0", AnchorMaxX = "1.0", AnchorMaxY = "1.0" };

                [JsonProperty(PropertyName = "Background Offsets")]
                public Offsets BackgroundOffsets = new Offsets
                    { OffsetMinX = "-300", OffsetMinY = "-100", OffsetMaxX = "0", OffsetMaxY = "0" };

                [JsonProperty(PropertyName = "Left Button Text")]
                public string LeftText = "<size=36><</size>";

                [JsonProperty(PropertyName = "Left Button Color")]
                public string LeftColor = "0.11 0.51 0.83";

                [JsonProperty(PropertyName = "Left Button Anchors")]
                public Anchors LeftAnchors = new Anchors
                    { AnchorMinX = "0.025", AnchorMinY = "0.05", AnchorMaxX = "0.325", AnchorMaxY = "0.95" };

                [JsonProperty(PropertyName = "Center Button Text")]
                public string CenterText = "<size=36>Page: {page}</size>";

                [JsonProperty(PropertyName = "Center Button Color")]
                public string CenterColor = "0.11 0.51 0.83";

                [JsonProperty(PropertyName = "Center Button Anchors")]
                public Anchors CenterAnchors = new Anchors
                    { AnchorMinX = "0.350", AnchorMinY = "0.05", AnchorMaxX = "0.650", AnchorMaxY = "0.95" };

                [JsonProperty(PropertyName = "Right Button Text")]
                public string RightText = "<size=36>></size>";

                [JsonProperty(PropertyName = "Right Button Color")]
                public string RightColor = "0.11 0.51 0.83";

                [JsonProperty(PropertyName = "Right Button Anchors")]
                public Anchors RightAnchors = new Anchors
                    { AnchorMinX = "0.675", AnchorMinY = "0.05", AnchorMaxX = "0.975", AnchorMaxY = "0.95" };

                [JsonIgnore]
                public string ParsedUI;

                [JsonIgnore]
                public int IndexPagePrevious, IndexPageCurrent, IndexPageNext;

                public class Anchors
                {
                    [JsonProperty(PropertyName = "Anchor Min X")]
                    public string AnchorMinX = "0.0";

                    [JsonProperty(PropertyName = "Anchor Min Y")]
                    public string AnchorMinY = "0.0";

                    [JsonProperty(PropertyName = "Anchor Max X")]
                    public string AnchorMaxX = "1.0";

                    [JsonProperty(PropertyName = "Anchor Max Y")]
                    public string AnchorMaxY = "1.0";

                    [JsonIgnore]
                    public string AnchorMin => $"{AnchorMinX} {AnchorMinY}";

                    [JsonIgnore]
                    public string AnchorMax => $"{AnchorMaxX} {AnchorMaxY}";
                }

                public class Offsets
                {
                    [JsonProperty(PropertyName = "Offset Min X")]
                    public string OffsetMinX = "0";

                    [JsonProperty(PropertyName = "Offset Min Y")]
                    public string OffsetMinY = "0";

                    [JsonProperty(PropertyName = "Offset Max X")]
                    public string OffsetMaxX = "100";

                    [JsonProperty(PropertyName = "Offset Max Y")]
                    public string OffsetMaxY = "100";

                    [JsonIgnore]
                    public string OffsetMin => $"{OffsetMinX} {OffsetMinY}";

                    [JsonIgnore]
                    public string OffsetMax => $"{OffsetMaxX} {OffsetMaxY}";
                }
            }

            public void IndexSkins()
            {
                IndexedSkins.Clear();

                foreach (var item in Skins)
                {
                    if (!string.IsNullOrEmpty(item.Permission) && !_ins.permission.PermissionExists(item.Permission))
                        _ins.permission.RegisterPermission(item.Permission, _ins);

                    List<SkinItem> items;
                    if (!IndexedSkins.TryGetValue(item.Shortname, out items))
                        items = IndexedSkins[item.Shortname] = new List<SkinItem>();

                    items.Add(item);
                }
            }
        }

        protected override void LoadConfig()
        {
            _ins = this; // REEE that I have to do this tbh

            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
                SaveConfig();

                GenerateUI();
                _config.IndexSkins();
            }
            catch
            {
                PrintError("Your configuration file contains an error. Using default configuration values.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        protected override void LoadDefaultConfig() => _config = new Configuration();

        #endregion

        #region Hooks

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                { "Not Allowed", "You don't have permission to use this command." },
                { "Cannot Use", "I'm sorry, you cannot use that right now." },
                {
                    "Help", "Command usage:\n" +
                            "skin show - Show skins.\n" +
                            "skin get - Get Skin ID of the item.\n" +
                            "skin purgecache (shortname) - Purge skins cache by shortname (or empty to purge all)"
                },
                {
                    "Admin Help", "Admin command usage:\n" +
                                  "skin remove (Shortname) (Skin ID) [Permission] - Remove a skin.\n" +
                                  "skin add (Shortname) (Skin ID) [Permission] - Add a skin."
                },
                { "Skin Get Format", "{shortname}'s skin: {id}." },
                { "Skin Get No Item", "Please, hold the needed item." },
                { "Incorrect Skin", "You have entered an incorrect skin." },
                { "Skin Already Exists", "This skin already exists on this item." },
                { "Skin Does Not Exist", "This skin does not exist." },
                { "Skin Added", "Skin was successfully added." },
                { "Skin Removed", "Skin was removed." },
                { "Skin Set Help", "Use /ss to view sets, /ss 1-5 to apply one, /ss delete 1-5 to delete one, or /sc save 1-5 to build one." },
                { "Skin Craft Help", "Use /sc save 1-5, /sc apply 1-5, /sc delete 1-5, or /sc list." },
                { "Skin Set Empty", "Skin set slot {0} is empty. Wear skinned items and use /sc save {0}." },
                { "Skin Set Saved", "Saved your worn skins to skin set slot {0}." },
                { "Skin Set No Skins", "You are not wearing any skinned items to save." },
                { "Skin Set Applied", "Applied skin set slot {0}." },
                { "Skin Set Applied Partial", "Applied skin set slot {0}. Some skins were skipped because you don't own them." },
                { "Skin Set Applied None", "Skin set slot {0} did not match any worn items." },
                { "Skin Set Deleted", "Deleted skin set slot {0}." },
                { "Skin Set List", "Skin sets: {0}" },
                { "Skin Set Selected", "Selected skin set slot {0}." },
                { "Skin Craft Opened", "Opened skin set slot {0}. Drop skinned worn items into the row." },
                { "Skin Craft Saved Item", "Saved {0} to skin set slot {1}." },
                { "Skin Craft No Item Skin", "That item has no skin to save." },
                { "Skin Craft Not Worn", "Only worn clothing and armor can be saved in skin sets." },
                { "Skin Craft Not Owned", "That skin was not saved because you don't own it." },
                { "Skin Set Invalid Slot", "Pick a skin set slot from 1 to 5." },
                { "Auto Skin Enabled", "Auto skins enabled using skin set slot {0}." },
                { "Auto Skin Disabled", "Auto skins disabled." },
                { "Auto Skin Status", "Auto skins are {0}. Selected skin set: {1}." },
                { "Team Skin Help", "Use /skinteam 1-5 to apply a set to your team/clan, or /skinteam toggle to opt in/out." },
                { "Team Skin Opt In", "Team skin sharing enabled for you." },
                { "Team Skin Opt Out", "Team skin sharing disabled for you." },
                { "Team Skin Applied", "Applied skin set slot {0} to {1} nearby team/clan members." },
                { "Team Skin No Targets", "No eligible team or clan members were found." }
            }, this);
        }

        private void Init()
        {
            _ins = this;

            permission.RegisterPermission(PermissionUse, this);
            permission.RegisterPermission(PermissionAdmin, this);
            permission.RegisterPermission(PermissionSkinSets, this);
            permission.RegisterPermission(PermissionAutoSkins, this);
            permission.RegisterPermission(PermissionTeamShare, this);

            LoadSkinSetData();
            GenerateUI();
        }

        private void GenerateUI()
        {
            const string pagePrevious = "{pagePrevious}";
            const string pageCurrent = "{page}";
            const string pageNext = "{pageNext}";

            var elements = new CuiElementContainer();

            var background = new CuiElement
            {
                Name = "Skins.Background",
                Parent = "Overlay",
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = _ins._config.UI.BackgroundColor
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = _ins._config.UI.BackgroundAnchors.AnchorMin,
                        AnchorMax = _ins._config.UI.BackgroundAnchors.AnchorMax,
                        OffsetMin = _ins._config.UI.BackgroundOffsets.OffsetMin,
                        OffsetMax = _ins._config.UI.BackgroundOffsets.OffsetMax
                    }
                },
                FadeOut = 0.5f
            };

            var left = new CuiElement
            {
                Name = "Skins.Left",
                Parent = background.Name,
                Components =
                {
                    new CuiButtonComponent
                    {
                        Close = background.Name,
                        Command = $"{CommandDefault} _tech-update {pagePrevious}",
                        Color = _ins._config.UI.LeftColor
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = $"{_ins._config.UI.LeftAnchors.AnchorMinX} 0.05",
                        AnchorMax = $"{_ins._config.UI.LeftAnchors.AnchorMaxX} 0.55"
                    }
                },
                FadeOut = 0.5f
            };

            var leftText = new CuiElement
            {
                Name = "Skins.Left.Text",
                Parent = left.Name,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = _ins._config.UI.LeftText,
                        Align = TextAnchor.MiddleCenter
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1"
                    }
                },
                FadeOut = 0.5f
            };

            var center = new CuiElement
            {
                Name = "Skins.Center",
                Parent = background.Name,
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = _ins._config.UI.CenterColor
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = $"{_ins._config.UI.CenterAnchors.AnchorMinX} 0.05",
                        AnchorMax = $"{_ins._config.UI.CenterAnchors.AnchorMaxX} 0.55"
                    }
                },
                FadeOut = 0.5f
            };

            var centerText = new CuiElement
            {
                Name = "Skins.Center.Text",
                Parent = center.Name,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = _ins._config.UI.CenterText,
                        Align = TextAnchor.MiddleCenter
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1"
                    }
                },
                FadeOut = 0.5f
            };

            var right = new CuiElement
            {
                Name = "Skins.Right",
                Parent = background.Name,
                Components =
                {
                    new CuiButtonComponent
                    {
                        Close = background.Name,
                        Command = $"{CommandDefault} _tech-update {pageNext}",
                        Color = _ins._config.UI.RightColor
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = $"{_ins._config.UI.RightAnchors.AnchorMinX} 0.05",
                        AnchorMax = $"{_ins._config.UI.RightAnchors.AnchorMaxX} 0.55"
                    }
                },
                FadeOut = 0.5f
            };

            var rightText = new CuiElement
            {
                Name = "Skins.Right.Text",
                Parent = right.Name,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = _ins._config.UI.RightText,
                        Align = TextAnchor.MiddleCenter
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1"
                    }
                },
                FadeOut = 0.5f
            };

            elements.Add(background);
            elements.Add(left);
            elements.Add(leftText);
            elements.Add(center);
            elements.Add(centerText);
            elements.Add(right);
            elements.Add(rightText);

            _config.UI.ParsedUI = elements.ToJson();

            _config.UI.IndexPagePrevious = _config.UI.ParsedUI.LastIndexOf(pagePrevious, StringComparison.Ordinal);
            _config.UI.ParsedUI = _config.UI.ParsedUI.Remove(_config.UI.IndexPagePrevious, pagePrevious.Length);

            _config.UI.IndexPageCurrent = _config.UI.ParsedUI.LastIndexOf(pageCurrent, StringComparison.Ordinal);
            _config.UI.ParsedUI = _config.UI.ParsedUI.Remove(_config.UI.IndexPageCurrent, pageCurrent.Length);

            _config.UI.IndexPageNext = _config.UI.ParsedUI.LastIndexOf(pageNext, StringComparison.Ordinal);
            _config.UI.ParsedUI = _config.UI.ParsedUI.Remove(_config.UI.IndexPageNext, pageNext.Length);
        }

        private void OnServerInitialized()
        {
            foreach (var shortname in _config.IndexedSkins.Keys)
            {
                if (ItemManager.FindItemDefinition(shortname) != null)
                    continue;

                PrintWarning(
                    $"Item with shortname \"{shortname}\" does not exist. Please review your Skins configuration.");
            }

            for (var i = 0; i < BasePlayer.activePlayerList.Count; i++)
            {
                OnPlayerConnected(BasePlayer.activePlayerList[i]);
            }

            AddCovalenceCommand(_config.Commands, nameof(CommandSkin));
            AddCovalenceCommand(CommandDefault, nameof(CommandSkin));
            AddCovalenceCommand(_config.SkinSetCommands, nameof(CommandSkinSet));
            AddCovalenceCommand(SkinSetCommandDefault, nameof(CommandSkinSet));
            AddCovalenceCommand(_config.SkinCraftCommands, nameof(CommandSkinCraft));
            AddCovalenceCommand(SkinCraftCommandDefault, nameof(CommandSkinCraft));
            AddCovalenceCommand(_config.AutoSkinCommands, nameof(CommandSkinAuto));
            AddCovalenceCommand(SkinAutoCommandDefault, nameof(CommandSkinAuto));
            AddCovalenceCommand(_config.TeamSkinCommands, nameof(CommandSkinTeam));
            AddCovalenceCommand(SkinTeamCommandDefault, nameof(CommandSkinTeam));
        }

        private void Unload()
        {
            foreach (var controller in _controllers)
                controller.Value.Destroy();

            foreach (var controller in _skinCraftControllers.Values)
                controller.Destroy();

            SaveSkinSetData();
            _ins = null;
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (_controllers.ContainsKey(player.userID))
                return;

            _controllers.Add(player.userID, new ContainerController(player)); // lol
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            SkinCraftController skinCraftController;
            if (_skinCraftControllers.Remove(player.userID, out skinCraftController))
                skinCraftController.Destroy();

            ContainerController container;
            if (!_controllers.Remove(player.userID, out container))
                return;

            container.Destroy();
        }

        #region Working With Containers

        private void OnItemSplit(Item item, int amount)
        {
            if (item.parentItem != null || item.parent == null)
                return;

            ContainerController container;
            if (!_controllersPerContainer.TryGetValue(item.parent.uid, out container))
                return;

#if DEBUG
            Puts($"OnItemSplit: {item.info.shortname} ({item.amount}x, slot {item.position}); {amount}x");
#endif

            var main = container.Container.GetSlot(0);
            if (main == null)
            {
#if DEBUG
                Puts("Main item is null");
#endif
                return;
            }

            NextFrame(() =>
            {
                if (main.uid != item.uid) // Ignore main item because it's amount will be changed
                    main.amount -= amount;

                container.UpdateContent(0);
            });
        }

        private void OnItemAddedToContainer(ItemContainer itemContainer, Item item)
        {
            if (item.parentItem != null)
                return;

            SkinCraftController skinCraftController;
            if (_skinCraftControllersPerContainer.TryGetValue(itemContainer.uid, out skinCraftController))
            {
                skinCraftController.OnItemAdded(item);
                return;
            }

            var player = itemContainer.GetOwnerPlayer();
            if (player != null)
            {
                TryAutoSkinItem(player, item);
                return;
            }

            ContainerController container;
            if (!_controllersPerContainer.TryGetValue(itemContainer.uid, out container))
                return;

#if DEBUG
            Puts($"OnItemAddedToContainer: {item.info.shortname} (slot {item.position})");
#endif

            if (itemContainer.itemList.Count != 1)
            {
                item.position = -1;
                item.parent.itemList.Remove(item);
                item.parent = null;
                container.GiveItemBack();
                container.Clear();
                item.parent = container.Container;
                item.parent.itemList.Add(item);
            }

            item.position = 0;
            container.StoreContent(item);
            container.UpdateContent(0);
        }

        private void OnItemRemovedFromContainer(ItemContainer itemContainer, Item item)
        {
            if (item.parentItem != null)
                return;

            var player = itemContainer.GetOwnerPlayer();
            if (player != null)
                return;

            ContainerController container;
            if (!_controllersPerContainer.TryGetValue(itemContainer.uid, out container))
                return;

#if DEBUG
            Puts($"OnItemRemovedFromContainer: {item.info.shortname} (slot {item.position})");
#endif

            container.OnItemTaken(item);

            Interface.CallHook("OnItemSkinChanged", player, item);

            container.Clear();
        }

        private void OnPlayerLootEnd(PlayerLoot loot)
        {
            var player = loot.baseEntity ?? loot.gameObject.GetComponent<BasePlayer>();

#if DEBUG
            Puts("OnPlayerLootEnd: Closing container");
#endif

            CloseSkinContainer(player);
            CloseSkinCraft(player);
        }

        private void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            CloseSkinContainer(player);
            CloseSkinCraft(player);
        }

        private void CloseSkinContainer(BasePlayer player)
        {
            if (player == null)
                return;

            ContainerController container;
            if (!_controllers.TryGetValue(player.userID, out container) || !container.IsOpened)
                return;

            container.Close();
        }

        private object CanLootPlayer(BasePlayer looter, BasePlayer target)
        {
            if (looter != target)
                return null;

            ContainerController container;
            if (!_controllers.TryGetValue(looter.userID, out container) || !container.IsOpened)
            {
                SkinCraftController skinCraftController;
                if (!_skinCraftControllers.TryGetValue(looter.userID, out skinCraftController) ||
                    !skinCraftController.IsOpen)
                    return null;

                return true;
            }

            return true;
        }

        private object CanMoveItem(Item item, PlayerInventory playerLoot, ItemContainerId targetContainerId, int slot,
            int amount)
        {
            if (_itemAttachmentContainers.Contains(targetContainerId))
            {
#if DEBUG
                Puts("// CanMoveItem: Preventing attachments abuse");
#endif
                return false;
            }

            SkinCraftController skinCraftFrom = null, skinCraftTo = null;
            _skinCraftControllersPerContainer.TryGetValue(targetContainerId, out skinCraftTo);
            if (item.parent != null)
                _skinCraftControllersPerContainer.TryGetValue(item.parent.uid, out skinCraftFrom);

            if (skinCraftFrom != null || skinCraftTo != null)
                return CanMoveSkinCraftItem(skinCraftFrom, skinCraftTo, item, slot);

            ContainerController containerFrom = null, containerTo = null;
            if (!_controllersPerContainer.TryGetValue(targetContainerId, out containerTo) &&
                (item.parent == null || !_controllersPerContainer.TryGetValue(item.parent.uid, out containerFrom)))
                return null;

#if DEBUG
            Puts(
                $"CanMoveItem: {item.info.shortname} ({item.amount}) from {item.parent?.uid.Value ?? 0} to {targetContainerId} in {slot} ({amount})");
#endif

            if (item.parent?.uid == targetContainerId)
            {
#if DEBUG
                Puts("// CanMoveItem: Preventing same containers");
#endif

                return false;
            }

            return CanMoveItemTo(containerFrom, containerTo, item, slot, amount);
        }

        #region Minor helpers

        private object CanMoveSkinCraftItem(SkinCraftController controllerFrom, SkinCraftController controllerTo,
            Item item, int slot)
        {
            if (controllerFrom != null && controllerFrom.IsPreview(item))
                return false;

            if (controllerTo == null)
                return null;

            if (!controllerTo.CanAccept(item, slot))
                return false;

            controllerTo.RememberReturnLocation(item);
            controllerTo.ClearSlot(slot);
            return null;
        }

        private object CanMoveItemTo(ContainerController controllerFrom, ContainerController controllerTo, Item item,
            int slot, int amount)
        {
            if (controllerTo == null && controllerFrom != null && !controllerFrom.CanTakeSkin(item))
                return false;

            var targetItem = controllerTo?.Container?.GetSlot(slot);
            if (targetItem != null)
            {
                // Give target item back
                controllerTo.GiveItemBack(targetItem);
                controllerTo.Clear();
            }

            return null;
        }

        #endregion

        #endregion

        #endregion

        #region Commands

        private void CommandSkin(IPlayer player, string command, string[] args)
        {
            if (!CanUse(player))
            {
#if DEBUG
                Puts("Not allowed");
#endif

                player.Reply(GetMsg("Not Allowed", player.Id));
                return;
            }

            var basePlayer = player.Object as BasePlayer;
            var isPlayer = basePlayer != null;
            var isAdmin = player.IsServer || CanUseAdmin(player);

            if (args.Length == 0)
                args = new[] { isPlayer ? "show" : string.Empty }; // :P strange yeah


#if DEBUG
            Puts($"Arguments: {string.Join(" ", args)}");
#endif

            switch (args[0].ToLower())
            {
                case "_tech-update":
                {
                    if (!isPlayer)
                        break;

                    int page;
                    if (args.Length != 2 || !int.TryParse(args[1], out page))
                        break;

                    ContainerController container;
                    if (!_controllers.TryGetValue(basePlayer.userID, out container))
                        break;

                    container.UpdateContent(page);
                    break;
                }

                case "_tech-mode":
                {
                    if (!isPlayer)
                        break;

                    if (args.Length != 2)
                        break;

                    SkinViewMode mode;
                    if (!Enum.TryParse(args[1], true, out mode))
                        break;

                    ContainerController container;
                    if (!_controllers.TryGetValue(basePlayer.userID, out container))
                        break;

                    container.SetViewMode(mode);
                    break;
                }

                case "purgecache":
                case "pc":
                {
                    if (!isPlayer)
                        break;

                    ContainerController container;
                    if (!_controllers.TryGetValue(basePlayer.userID, out container))
                        break;

                    container.TotalSkinsCache.Clear();
                    break;
                }

                case "show":
                case "s":
                {
                    if (!isPlayer)
                    {
                        player.Reply(GetMsg("Cannot Use", player.Id));
                        break;
                    }

                    ContainerController container;
                    if (!_controllers.TryGetValue(basePlayer.userID, out container) || !container.CanShow())
                    {
                        player.Reply(GetMsg("Cannot Use", player.Id));
                        break;
                    }

                    basePlayer.Invoke(container.Show, 0.5f);
                    break;
                }

                case "remove":
                case "delete":
                case "r":
                case "d":
                {
                    if (args.Length < 3)
                        goto default;

                    if (!isAdmin)
                    {
                        player.Reply(GetMsg("Not Allowed", player.Id));
                        break;
                    }

                    var shortname = args[1];
                    ulong skin;
                    if (!ulong.TryParse(args[2], out skin))
                    {
                        player.Reply(GetMsg("Incorrect Skin", player.Id));
                        break;
                    }

                    string permission = null;
                    if (args.Length == 4)
                        permission = args[3];

                    LoadConfig();

                    var skinData = Configuration.SkinItem.Find(null, shortname)
                        .Where(x => permission == null || x.Permission == permission);

                    if (!skinData.Any())
                    {
                        player.Reply(GetMsg("Skin Does Not Exist", player.Id));
                        break;
                    }

                    foreach (var data in skinData)
                        data.Skins.Remove(skin);

                    player.Reply(GetMsg("Skin Removed", player.Id));

                    SaveConfig();
                    break;
                }

                case "add":
                case "a":
                {
                    if (args.Length < 3)
                        goto default;

                    if (!isAdmin)
                    {
                        player.Reply(GetMsg("Not Allowed", player.Id));
                        break;
                    }

                    var shortname = args[1];
                    ulong skin;
                    if (!ulong.TryParse(args[2], out skin))
                    {
                        player.Reply(GetMsg("Incorrect Skin", player.Id));
                        break;
                    }

                    string permission = null;
                    if (args.Length == 4)
                        permission = args[3];

                    LoadConfig();

                    var skinData = Configuration.SkinItem.Find(null, shortname)
                        .FirstOrDefault(x => permission == null || x.Permission == permission);

                    if (skinData == null)
                    {
                        _config.Skins.Add(new Configuration.SkinItem
                        {
                            Permission = permission ?? string.Empty,
                            Shortname = shortname,
                            Skins = new List<ulong> { skin }
                        });

                        _config.IndexSkins();
                        player.Reply(GetMsg("Skin Added", player.Id));
                    }
                    else
                    {
                        if (skinData.Skins.Contains(skin))
                            player.Reply(GetMsg("Skin Already Exists", player.Id));
                        else
                        {
                            skinData.Skins.Add(skin);
                            player.Reply(GetMsg("Skin Added", player.Id));
                        }
                    }

                    SaveConfig();
                    break;
                }

                case "get":
                case "g":
                {
                    if (!isPlayer)
                    {
                        player.Reply(GetMsg("Cannot Use", player.Id));
                        break;
                    }

                    var item = basePlayer.GetActiveItem();
                    if (item == null || !item.IsValid())
                    {
                        player.Reply(GetMsg("Skin Get No Item", player.Id));
                        break;
                    }

                    player.Reply(GetMsg("Skin Get Format", player.Id).Replace("{shortname}", item.info.shortname)
                        .Replace("{id}", item.skin.ToString()));

                    break;
                }

                case "dlctest":
                {
                    if (!isPlayer)
                    {
                        player.Reply(GetMsg("Cannot Use", player.Id));
                        break;
                    }

                    var item = basePlayer.GetActiveItem();
                    if (item == null || !item.IsValid())
                    {
                        player.Reply(GetMsg("Skin Get No Item", player.Id));
                        break;
                    }

                    var initialized = PlayerDLCAPI?.Call("Initialized");
                    var ownedSkins = GetOwnedItemSkinIds(basePlayer, item.info).Distinct().ToList();
                    player.Reply(
                        $"PlayerDLCAPI: {(PlayerDLCAPI == null ? "missing" : "loaded")}, initialized: {initialized ?? "unknown"}, item: {item.info.shortname}, owned skins found: {ownedSkins.Count}");
                    if (ownedSkins.Count > 0)
                        player.Reply($"First owned skins: {string.Join(", ", ownedSkins.Take(10))}");

                    break;
                }

                default: // "help" and all other args
                {
                    player.Reply(GetMsg("Help", player.Id));
                    if (isAdmin)
                        player.Reply(GetMsg("Admin Help", player.Id));

                    break;
                }
            }
        }

        #endregion

        #region API

        [HookMethod(nameof(SkinsClose))]
        private void SkinsClose(BasePlayer player)
        {
            if (player == null)
                return;

            ContainerController container;
            if (!_controllers.TryGetValue(player.userID, out container))
                return;

            container.Close();
        }

        [HookMethod(nameof(PurgeCache))]
        private void PurgeCache(ulong id, string shortname)
        {
            ContainerController container;
            if (!_controllers.TryGetValue(id, out container))
                return;

            if (string.IsNullOrEmpty(shortname))
            {
                container.TotalSkinsCache.Clear();
            }
            else
            {
                container.TotalSkinsCache.Remove(container.GetCacheKey(shortname, SkinViewMode.Free));
                container.TotalSkinsCache.Remove(container.GetCacheKey(shortname, SkinViewMode.Owned));
            }
        }

        private class SkinSetData
        {
            public Dictionary<ulong, PlayerSkinSetData> Players = new Dictionary<ulong, PlayerSkinSetData>();
        }

        private class PlayerSkinSetData
        {
            public int SelectedSet = 1;
            public bool AutoSkinsEnabled;
            public bool TeamShareBlocked;
            public Dictionary<int, SkinSet> Sets = new Dictionary<int, SkinSet>();
        }

        private class SkinSet
        {
            public List<SkinSetEntry> Wear = new List<SkinSetEntry>();
        }

        private class SkinSetEntry
        {
            public string Shortname;
            public ulong Skin;
            public int Position = -1;
        }

        [HookMethod(nameof(CanUseSkin))]
        private bool CanUseSkin(BasePlayer player, string shortname, ulong skin)
        {
            if (skin == 0)
                return true;

            var definition = ItemManager.FindItemDefinition(shortname);
            if (definition == null)
                return false;

            return IsConfiguredFreeSkin(player, shortname, skin) || PlayerOwnsSkin(player, definition, skin);
        }

        [HookMethod(nameof(IsConfiguredFreeSkin))]
        private bool IsConfiguredFreeSkin(BasePlayer player, string shortname, ulong skin)
        {
            return Configuration.SkinItem.Find(player?.IPlayer, shortname)
                .Any(x => x.Skins.Contains(skin));
        }

        private void CommandSkinSet(IPlayer player, string command, string[] args)
        {
            if (!CanUseSkinSets(player))
            {
                player.Reply(GetMsg("Not Allowed", player.Id));
                return;
            }

            var basePlayer = player.Object as BasePlayer;
            if (basePlayer == null)
            {
                player.Reply(GetMsg("Cannot Use", player.Id));
                return;
            }

            if (args == null || args.Length == 0)
            {
                ReplySkinSetList(player, basePlayer.userID);
                OpenSkinCraftEditorNextFrame(player, basePlayer, GetSkinSetData(basePlayer.userID).SelectedSet);
                return;
            }

            var data = GetSkinSetData(basePlayer.userID);
            var action = args[0].ToLowerInvariant();

            if (action == "list")
            {
                ReplySkinSetList(player, basePlayer.userID);
                OpenSkinCraftEditorNextFrame(player, basePlayer, data.SelectedSet);
                return;
            }

            if (action == "selected" || action == "current")
            {
                player.Reply(string.Format(GetMsg("Skin Set Selected", player.Id), data.SelectedSet));
                return;
            }

            if (args.Length == 1 && TryParseSkinSetSlot(args[0], out var quickSlot))
            {
                SelectSkinSet(player, basePlayer, quickSlot, true);
                return;
            }

            var targetSlot = data.SelectedSet;
            if (args.Length >= 2 && !TryParseSkinSetSlot(args[1], out targetSlot))
            {
                player.Reply(GetMsg("Skin Set Invalid Slot", player.Id));
                return;
            }

            switch (action)
            {
                case "open":
                case "view":
                case "edit":
                    OpenSkinCraftEditorNextFrame(player, basePlayer, targetSlot);
                    return;

                case "delete":
                case "remove":
                case "del":
                case "r":
                    DeleteSkinSet(player, basePlayer.userID, targetSlot);
                    OpenSkinCraftEditorNextFrame(player, basePlayer, targetSlot);
                    return;

                case "save":
                case "s":
                    SaveSkinSet(player, basePlayer, targetSlot);
                    OpenSkinCraftEditorNextFrame(player, basePlayer, targetSlot);
                    return;

                case "load":
                case "use":
                case "apply":
                case "l":
                    SelectSkinSet(player, basePlayer, targetSlot, true);
                    return;
            }

            player.Reply(GetMsg("Skin Set Help", player.Id));
        }

        private void CommandSkinCraft(IPlayer player, string command, string[] args)
        {
            if (!CanUseSkinSets(player))
            {
                player.Reply(GetMsg("Not Allowed", player.Id));
                return;
            }

            var basePlayer = player.Object as BasePlayer;
            if (basePlayer == null)
            {
                player.Reply(GetMsg("Cannot Use", player.Id));
                return;
            }

            if (args != null && args.Length > 0 && args[0].Equals("close", StringComparison.OrdinalIgnoreCase))
            {
                CloseSkinCraft(basePlayer, true);
                return;
            }

            if (args == null || args.Length == 0)
            {
                OpenSkinCraftEditor(player, basePlayer, GetSkinSetData(basePlayer.userID).SelectedSet);
                return;
            }

            if (args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                ReplySkinSetList(player, basePlayer.userID);
                return;
            }

            if (args.Length == 1 && TryParseSkinSetSlot(args[0], out var quickSlot))
            {
                OpenSkinCraftEditor(player, basePlayer, quickSlot);
                return;
            }

            if ((args[0].Equals("open", StringComparison.OrdinalIgnoreCase) ||
                 args[0].Equals("edit", StringComparison.OrdinalIgnoreCase)) &&
                args.Length >= 2 && TryParseSkinSetSlot(args[1], out var editSlot))
            {
                OpenSkinCraftEditor(player, basePlayer, editSlot);
                return;
            }

            if (args.Length < 2 || !TryParseSkinSetSlot(args[1], out var slot))
            {
                player.Reply(GetMsg("Skin Craft Help", player.Id));
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "save":
                case "s":
                    SaveSkinSet(player, basePlayer, slot);
                    break;

                case "load":
                case "use":
                case "apply":
                case "l":
                    SelectSkinSet(player, basePlayer, slot, true);
                    break;

                case "delete":
                case "remove":
                case "del":
                case "r":
                    DeleteSkinSet(player, basePlayer.userID, slot);
                    break;

                default:
                    player.Reply(GetMsg("Skin Craft Help", player.Id));
                    break;
            }
        }

        private void CommandSkinAuto(IPlayer player, string command, string[] args)
        {
            if (!CanUseAutoSkins(player))
            {
                player.Reply(GetMsg("Not Allowed", player.Id));
                return;
            }

            var basePlayer = player.Object as BasePlayer;
            if (basePlayer == null)
            {
                player.Reply(GetMsg("Cannot Use", player.Id));
                return;
            }

            var data = GetSkinSetData(basePlayer.userID);
            if (args == null || args.Length == 0 || args[0].Equals("toggle", StringComparison.OrdinalIgnoreCase))
            {
                data.AutoSkinsEnabled = !data.AutoSkinsEnabled;
                SaveSkinSetData();
                player.Reply(data.AutoSkinsEnabled
                    ? string.Format(GetMsg("Auto Skin Enabled", player.Id), data.SelectedSet)
                    : GetMsg("Auto Skin Disabled", player.Id));
                return;
            }

            if (args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                player.Reply(string.Format(GetMsg("Auto Skin Status", player.Id),
                    data.AutoSkinsEnabled ? "enabled" : "disabled", data.SelectedSet));
                return;
            }

            if (args[0].Equals("off", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("disable", StringComparison.OrdinalIgnoreCase))
            {
                data.AutoSkinsEnabled = false;
                SaveSkinSetData();
                player.Reply(GetMsg("Auto Skin Disabled", player.Id));
                return;
            }

            if (args[0].Equals("on", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("enable", StringComparison.OrdinalIgnoreCase))
            {
                data.AutoSkinsEnabled = true;
                SaveSkinSetData();
                player.Reply(string.Format(GetMsg("Auto Skin Enabled", player.Id), data.SelectedSet));
                return;
            }

            if (TryParseSkinSetSlot(args[0], out var slot))
            {
                data.SelectedSet = slot;
                data.AutoSkinsEnabled = true;
                SaveSkinSetData();
                player.Reply(string.Format(GetMsg("Auto Skin Enabled", player.Id), slot));
                return;
            }

            player.Reply(GetMsg("Skin Set Invalid Slot", player.Id));
        }

        private void CommandSkinTeam(IPlayer player, string command, string[] args)
        {
            if (!CanUseTeamShare(player))
            {
                player.Reply(GetMsg("Not Allowed", player.Id));
                return;
            }

            var basePlayer = player.Object as BasePlayer;
            if (basePlayer == null)
            {
                player.Reply(GetMsg("Cannot Use", player.Id));
                return;
            }

            var data = GetSkinSetData(basePlayer.userID);
            if (args != null && args.Length > 0 && args[0].Equals("toggle", StringComparison.OrdinalIgnoreCase))
            {
                data.TeamShareBlocked = !data.TeamShareBlocked;
                SaveSkinSetData();
                player.Reply(GetMsg(data.TeamShareBlocked ? "Team Skin Opt Out" : "Team Skin Opt In", player.Id));
                return;
            }

            var slot = data.SelectedSet;
            if (args != null && args.Length > 0 && !TryParseSkinSetSlot(args[0], out slot))
            {
                player.Reply(GetMsg("Team Skin Help", player.Id));
                return;
            }

            ApplySkinSetToTeam(player, basePlayer, slot);
        }

        #endregion

        #region Controller

        private class SkinCraftController
        {
            private const int EditorCapacity = 7;

            private readonly Skins _plugin;
            private readonly BasePlayer _owner;
            private readonly ItemContainer _container;
            private readonly HashSet<Item> _previewItems = new HashSet<Item>();
            private readonly Dictionary<Item, ReturnLocation> _returnLocations = new Dictionary<Item, ReturnLocation>();
            private bool _ignoreItemAdded;
            private float _ignoreCloseUntil;
            private int _slot = 1;

            public bool IsOpen;

            private class ReturnLocation
            {
                public ItemContainer Container;
                public int Position;
            }

            public SkinCraftController(Skins plugin, BasePlayer owner)
            {
                _plugin = plugin;
                _owner = owner;
                _container = new ItemContainer
                {
                    entityOwner = owner,
                    capacity = EditorCapacity,
                    isServer = true,
                    allowedContents = ItemContainer.ContentsType.Generic
                };

                _container.GiveUID();
                _plugin._skinCraftControllersPerContainer[_container.uid] = this;
            }

            public void Open(int slot)
            {
                if (!CanOpen())
                    return;

                _ignoreCloseUntil = Time.realtimeSinceStartup + 0.75f;
                IsOpen = true;
                _slot = slot;
                _container.capacity = EditorCapacity;
                _container.MarkDirty();
                ReturnItems();
                Clear();
                PopulatePreviews();

                var loot = _owner.inventory.loot;
                loot.Clear();
                loot.PositionChecks = false;
                loot.entitySource = _owner;
                loot.itemSource = null;
                loot.AddContainer(_container);
                loot.SendImmediate();

                DrawUI();
                _owner.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", _owner), "generic_resizable");
                _plugin.NextFrame(() =>
                {
                    if (_owner == null || !_owner.IsConnected || !IsOpen)
                        return;

                    _container.MarkDirty();
                    _owner.inventory.loot.SendImmediate();
                });
            }

            public void Close(bool force = false)
            {
                if (!force && ShouldIgnoreClose())
                    return;

                DestroyUI();
                ReturnItems();
                Clear();
                IsOpen = false;
            }

            public bool ShouldIgnoreClose()
            {
                return Time.realtimeSinceStartup < _ignoreCloseUntil;
            }

            public void Destroy()
            {
                Close(true);
                _plugin._skinCraftControllersPerContainer.Remove(_container.uid);
                _container.Kill();
            }

            public bool IsPreview(Item item)
            {
                return item != null && _previewItems.Contains(item);
            }

            public bool CanAccept(Item item, int position)
            {
                return item?.info != null && item.skin != 0 && item.info.category == ItemCategory.Attire &&
                       position >= 0 && position < EditorCapacity;
            }

            public void RememberReturnLocation(Item item)
            {
                if (item?.parent == null)
                    return;

                _returnLocations[item] = new ReturnLocation
                {
                    Container = item.parent,
                    Position = item.position
                };
            }

            public void ClearSlot(int position)
            {
                if (position < 0 || position >= EditorCapacity)
                    return;

                var existing = _container.GetSlot(position);
                if (existing != null && _previewItems.Contains(existing))
                    RemovePreview(existing);
            }

            public void OnItemAdded(Item item)
            {
                if (_ignoreItemAdded)
                    return;

                if (item == null || item.parent != _container)
                    return;

                var position = Mathf.Clamp(item.position, 0, EditorCapacity - 1);
                var saved = _plugin.SaveSkinCraftItem(_owner, item, _slot, position);
                _plugin.NextFrame(() =>
                {
                    var shortname = item.info?.shortname;
                    var skin = item.skin;
                    ReturnItem(item);

                    if (saved && !string.IsNullOrEmpty(shortname) && skin != 0)
                        AddPreview(shortname, skin, position);
                });
            }

            private void DrawUI()
            {
                DestroyUI();

                var elements = new CuiElementContainer();
                elements.Add(new CuiPanel
                {
                    Image =
                    {
                        Color = "0 0 0 0.30"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.645 0.455",
                        AnchorMax = "0.935 0.595"
                    },
                    CursorEnabled = false
                }, "Overlay", SkinCraftPanelName);

                AddHeaderButton(elements, "Set Skins", "0.00 0.55", "0.22 1.00", true, "");
                AddHeaderButton(elements, "Options", "0.225 0.55", "0.39 1.00", false, "");
                AddHeaderButton(elements, "Search Skins...", "0.395 0.55", "0.86 1.00", false, "");

                elements.Add(new CuiButton
                {
                    Button =
                    {
                        Color = "0.58 0.11 0.10 0.72",
                        Command = $"{SkinCraftCommandDefault} close"
                    },
                    Text =
                    {
                        Text = "X",
                        Align = TextAnchor.MiddleCenter,
                        FontSize = 16,
                        Color = "1 1 1 0.95"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.865 0.55",
                        AnchorMax = "0.94 1.00"
                    }
                }, SkinCraftPanelName);

                AddSetTab(elements, "Loot", "0.00 0.25", "0.15 0.55", 0);
                for (var set = 1; set <= MaxSkinSetSlots; set++)
                {
                    var minX = 0.15f + ((set - 1) * 0.15f);
                    var maxX = minX + 0.15f;
                    AddSetTab(elements, $"Set {set}", $"{minX} 0.25", $"{maxX} 0.55", set);
                }

                for (var i = 0; i < EditorCapacity; i++)
                {
                    var minX = 0.025f + (i * 0.128f);
                    var maxX = minX + 0.110f;
                    elements.Add(new CuiLabel
                    {
                        Text =
                        {
                            Text = GetSlotLabel(i),
                            Align = TextAnchor.MiddleCenter,
                            FontSize = 11,
                            Color = "1 1 1 0.84"
                        },
                        RectTransform =
                        {
                            AnchorMin = $"{minX} 0.00",
                            AnchorMax = $"{maxX} 0.20"
                        }
                    }, SkinCraftPanelName);
                }

                CuiHelper.AddUi(_owner, elements);
            }

            private void AddHeaderButton(CuiElementContainer elements, string text, string anchorMin, string anchorMax,
                bool active, string command)
            {
                elements.Add(new CuiButton
                {
                    Button =
                    {
                        Color = active ? "0.16 0.48 0.20 0.92" : "0.35 0.35 0.35 0.70",
                        Command = command
                    },
                    Text =
                    {
                        Text = text,
                        Align = TextAnchor.MiddleCenter,
                        FontSize = 14,
                        Color = active ? "0.72 1 0.72 1" : "0.86 0.86 0.86 0.86"
                    },
                    RectTransform =
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax
                    }
                }, SkinCraftPanelName);
            }

            private void AddSetTab(CuiElementContainer elements, string text, string anchorMin, string anchorMax, int set)
            {
                elements.Add(new CuiButton
                {
                    Button =
                    {
                        Color = set == _slot ? "0.36 0.68 0.28 0.92" : "0.42 0.42 0.42 0.72",
                        Command = set > 0 ? $"{SkinCraftCommandDefault} open {set}" : string.Empty
                    },
                    Text =
                    {
                        Text = text,
                        Align = TextAnchor.MiddleCenter,
                        FontSize = 14,
                        Color = set == _slot ? "0.84 1 0.82 1" : "0.92 0.92 0.92 0.80"
                    },
                    RectTransform =
                    {
                        AnchorMin = anchorMin,
                        AnchorMax = anchorMax
                    }
                }, SkinCraftPanelName);
            }

            private void DestroyUI()
            {
                CuiHelper.DestroyUi(_owner, SkinCraftPanelName);
            }

            private void PopulatePreviews()
            {
                var data = _plugin.GetSkinSetData(_owner.userID);
                SkinSet skinSet;
                if (!data.Sets.TryGetValue(_slot, out skinSet) || skinSet?.Wear == null)
                    return;

                for (var i = 0; i < skinSet.Wear.Count; i++)
                {
                    var entry = skinSet.Wear[i];
                    if (entry == null)
                        continue;

                    var position = entry.Position >= 0 && entry.Position < EditorCapacity
                        ? entry.Position
                        : _plugin.GetSkinSetEditorPosition(entry.Shortname, i);
                    AddPreview(entry.Shortname, entry.Skin, position);
                }
            }

            private void AddPreview(string shortname, ulong skin, int position)
            {
                if (string.IsNullOrEmpty(shortname) || skin == 0 || position < 0 || position >= EditorCapacity)
                    return;

                var definition = ItemManager.FindItemDefinition(shortname);
                if (definition == null)
                    return;

                ClearSlot(position);

                var preview = ItemManager.Create(definition, 1, skin);
                if (preview == null)
                    return;

                _previewItems.Add(preview);
                try
                {
                    _ignoreItemAdded = true;
                    preview.MoveToContainer(_container, position);
                }
                finally
                {
                    _ignoreItemAdded = false;
                }
            }

            private bool CanOpen()
            {
                return _owner != null && !_owner.IsDead() && !_owner.IsWounded() && !_owner.IsIncapacitated();
            }

            private void ReturnItems()
            {
                if (_container?.itemList == null)
                    return;

                for (var i = _container.itemList.Count - 1; i >= 0; i--)
                {
                    var item = _container.itemList[i];
                    if (_previewItems.Contains(item))
                        RemovePreview(item);
                    else
                        ReturnItem(item);
                }
            }

            private void ReturnItem(Item item)
            {
                if (item == null || item.parent != _container)
                    return;

                ReturnLocation location;
                if (_returnLocations.TryGetValue(item, out location))
                {
                    _returnLocations.Remove(item);
                    if (location.Container != null && item.MoveToContainer(location.Container, location.Position))
                        return;
                }

                if (!item.MoveToContainer(_owner.inventory.containerMain))
                    item.Drop(_owner.transform.position, Vector3.up);
            }

            private void RemovePreview(Item item)
            {
                if (item == null)
                    return;

                _previewItems.Remove(item);

                if (item.uid.IsValid && Net.sv != null)
                {
                    Net.sv.ReturnUID(item.uid.Value);
                    item.uid = default(ItemId);
                }

                item.RemoveFromWorld();
                item.parent?.itemList?.Remove(item);
                item.parent = null;

                var heldEntity = item.GetHeldEntity();
                if (heldEntity != null && heldEntity.IsValid() && !heldEntity.IsDestroyed)
                    heldEntity.Kill();
            }

            private void Clear()
            {
                _container.itemList.Clear();
                _previewItems.Clear();
                _returnLocations.Clear();
                _container.MarkDirty();
            }

            private string GetSlotLabel(int index)
            {
                switch (index)
                {
                    case 0:
                        return "Head";
                    case 1:
                        return "Chest";
                    case 2:
                        return "Shirt";
                    case 3:
                        return "Pants";
                    case 4:
                        return "Boots";
                    case 5:
                        return "Gloves";
                    default:
                        return "Extra";
                }
            }
        }

        private class ContainerController
        {
            /*
             * Basic tips:
             * Item with slot 0: Player's skin item
             */

            public BasePlayer Owner;
            public ItemContainer Container;
            public bool IsOpened = false;
            public SkinViewMode ViewMode = SkinViewMode.Free;

            public Dictionary<string, List<ulong>> TotalSkinsCache = new Dictionary<string, List<ulong>>();

            private List<Item> _storedContent;
            private Magazine _storedMagazine;

            public ContainerController(BasePlayer player)
            {
                Owner = player;
                _storedContent = new List<Item>();

                Container = new ItemContainer
                {
                    entityOwner = Owner,
                    capacity = _ins._config.Capacity,
                    isServer = true,
                    allowedContents = ItemContainer.ContentsType.Generic
                };

                Container.GiveUID();

                _ins._controllersPerContainer[Container.uid] = this;
            }

            #region UI

            private void DestroyUI()
            {
                CuiHelper.DestroyUi(Owner, "Skins.TabOwned.Text");
                CuiHelper.DestroyUi(Owner, "Skins.TabOwned");
                CuiHelper.DestroyUi(Owner, "Skins.TabFree.Text");
                CuiHelper.DestroyUi(Owner, "Skins.TabFree");
                CuiHelper.DestroyUi(Owner, "Skins.Right.Text");
                CuiHelper.DestroyUi(Owner, "Skins.Right");
                CuiHelper.DestroyUi(Owner, "Skins.Center.Text");
                CuiHelper.DestroyUi(Owner, "Skins.Center");
                CuiHelper.DestroyUi(Owner, "Skins.Left.Text");
                CuiHelper.DestroyUi(Owner, "Skins.Left");
                CuiHelper.DestroyUi(Owner, "Skins.Background");
            }

            private void DrawUI(int page)
            {
#if DEBUG
                _ins.Puts("Drawing UI");
#endif

                CuiHelper.AddUi(Owner, _ins._config.UI.ParsedUI
                    .Insert(_ins._config.UI.IndexPageNext, (page + 1).ToString())
                    .Insert(_ins._config.UI.IndexPageCurrent, page.ToString())
                    .Insert(_ins._config.UI.IndexPagePrevious, (page - 1).ToString()));

                DrawTabs();
            }

            private void DrawTabs()
            {
                var elements = new CuiElementContainer();
                AddTab(elements, "Skins.TabFree", "Free", SkinViewMode.Free, "0.11 0.51 0.83", "0.025 0.58",
                    "0.4875 0.95");
                AddTab(elements, "Skins.TabOwned", "Owned", SkinViewMode.Owned, "0.27 0.60 0.28", "0.5125 0.58",
                    "0.975 0.95");
                CuiHelper.AddUi(Owner, elements);
            }

            private void AddTab(CuiElementContainer elements, string name, string text, SkinViewMode mode,
                string activeColor, string anchorMin, string anchorMax)
            {
                var isActive = ViewMode == mode;
                elements.Add(new CuiElement
                {
                    Name = name,
                    Parent = "Skins.Background",
                    Components =
                    {
                        new CuiButtonComponent
                        {
                            Close = "Skins.Background",
                            Command = $"{CommandDefault} _tech-mode {mode}",
                            Color = isActive ? activeColor : "0.12 0.12 0.12 0.92"
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = anchorMin,
                            AnchorMax = anchorMax
                        }
                    },
                    FadeOut = 0.5f
                });

                elements.Add(new CuiElement
                {
                    Name = $"{name}.Text",
                    Parent = name,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = $"<size=16>{text}</size>",
                            Align = TextAnchor.MiddleCenter
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1"
                        }
                    },
                    FadeOut = 0.5f
                });
            }

            #endregion

            public void Close()
            {
#if DEBUG
                _ins.Puts("Closing container");
#endif

                DestroyUI();
                GiveItemBack();
                Clear();

                IsOpened = false;
            }

            public void Show()
            {
#if DEBUG
                _ins.Puts($"Showing container. UID: {Container.uid}");
#endif

                if (!CanUse())
                    return;

                var loot = Owner.inventory.loot;

                loot.Clear();
                IsOpened = true;
                ViewMode = SkinViewMode.Free;
                UpdateContent(0);

                loot.PositionChecks = false;
                loot.entitySource = Owner;
                loot.itemSource = null;
                loot.AddContainer(Container);
                loot.SendImmediate();

                Owner.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", Owner), _ins._config.Panel);
            }

            #region Can Show

            public bool CanShow()
            {
                return CanShow(Owner);
            }

            private static bool CanShow(BasePlayer player)
            {
                return player != null && !player.IsDead() && !player.IsWounded() && !player.IsIncapacitated();
            }

            #endregion

            private void AddItemContainer(Item item)
            {
                if (item?.contents == null)
                    return;

                if (!item.contents.uid.IsValid)
                    return;

                _ins._itemAttachmentContainers.Add(item.contents.uid);
            }

            public void GiveItemBack(Item itemOverride = null)
            {
                if (!IsValid())
                    return;

#if DEBUG
                _ins.Puts("Trying to give item back..");
#endif

                var item = itemOverride ?? Container.GetSlot(0);
                if (item == null)
                {
#if DEBUG
                    _ins.Puts("Invalid item");
#endif

                    return;
                }

                MoveItem(item, Owner.inventory.containerMain);
                OnItemTaken(item);
            }

            public void OnItemTaken(Item item)
            {
                if (item?.contents != null)
                    _ins._itemAttachmentContainers.Remove(item.contents.uid);

                SetupContent(item);
            }

            public void SetupContent(Item destination)
            {
#if DEBUG
                _ins.Puts("Setting up content for an item");
#endif
                if (destination == null)
                {
#if DEBUG
                    _ins.Puts("Destination is null!");
#endif

                    return;
                }

                if (_storedMagazine != null)
                {
                    (destination.GetHeldEntity() as BaseProjectile)?.primaryMagazine?.Load(_storedMagazine);
                    _storedMagazine = null;
                }

                var contents = destination.contents?.itemList;
                if (contents == null)
                {
#if DEBUG
                    _ins.Puts("// Contents null");
#endif

                    return;
                }

                for (var i = _storedContent.Count - 1; i >= 0; i--)
                {
                    var item = _storedContent[i];
                    item.parent = destination.contents;
                    item.RemoveFromWorld();

                    _storedContent.RemoveAt(i);
                    contents.Add(item);

                    item.MarkDirty();
                    foreach (var itemMod in item.info.itemMods)
                        itemMod.OnParentChanged(item);
                }

                _storedContent.Clear();
            }

            public void StoreContent(Item source)
            {
#if DEBUG
                _ins.Puts("Removing content for an item");
#endif

                var contents = source.contents?.itemList;
                if (contents != null)
                {
                    for (var i = contents.Count - 1; i >= 0; i--)
                    {
                        var item = contents[i];
                        item.parent = null;
                        contents.RemoveAt(i);
                        _storedContent.Add(item);
                    }
                }

                var magazine = (source.GetHeldEntity() as BaseProjectile)?.primaryMagazine;
                _storedMagazine = magazine?.Save();

                if (magazine != null) // Just in case so they won't be able to take out the ammo
                    magazine.contents = 0;
            }

            public void Clear()
            {
#if DEBUG
                _ins.Puts("Clearing container");
#endif

                for (var i = Container.itemList.Count - 1; i >= 0; i--)
                {
                    RemoveItem(Container.itemList[i]);
                }

                Container.itemList.Clear();
                Container.MarkDirty();
            }

            public void Destroy()
            {
                Close();
                Container.Kill();
            }

            public void SetViewMode(SkinViewMode mode)
            {
                if (ViewMode == mode)
                    return;

                ViewMode = mode;
                UpdateContent(0);
            }

            public string GetCacheKey(string shortname, SkinViewMode mode)
            {
                return $"{shortname}:{mode}";
            }

            public void UpdateContent(int page)
            {
                if (!IsValid())
                {
#if DEBUG
                    _ins.Puts("// Invalid container");
#endif

                    return;
                }

                var source = Container.GetSlot(0);
                if (source == null)
                {
#if DEBUG
                    _ins.Puts("// Source item is null");
#endif

                    return;
                }

                if (!source.uid.IsValid || !source.IsValid() || source.amount <= 0)
                {
#if DEBUG
                    _ins.Puts("// Invalid item that was removed. Player may have tried to dupe something");
#endif

                    return;
                }

                var skins = Pool.Get<List<ulong>>();
                try
                {
                    // Cache or get total skins available for user

                    List<ulong> totalSkins;
                    var cacheKey = GetCacheKey(source.info.shortname, ViewMode);
                    if (!TotalSkinsCache.TryGetValue(cacheKey, out totalSkins))
                    {
                        // Fetch custom skins

                        var newSkins = new List<ulong>();

                        if (ViewMode == SkinViewMode.Free)
                            Interface.CallHook("OnSkinsFetch", Owner, source.info, newSkins);

                        TotalSkinsCache[cacheKey] = totalSkins = newSkins.Concat(Configuration.SkinItem
                            .Find(Owner.IPlayer, source.info.shortname)
                            .SelectMany(x => x.GetAvailableSkins(Owner, source.info, ViewMode))).Distinct().ToList();

                        if (ViewMode == SkinViewMode.Free)
                            Interface.CallHook("OnSkinsFetched", Owner, source.info, newSkins);
                    }

                    // Page checks

                    var perPage = Container.capacity - 1;
                    var maxPage = (totalSkins.Count - 1) / perPage;

                    if (page < 0)
                        page = 0;

                    if (page > maxPage)
                        page = maxPage;

                    // Grab skins and skip some offset

                    foreach (var skin in totalSkins.Skip(perPage * page).Take(perPage))
                        skins.Add(skin);

                    Interface.CallHook("OnSkinsPage", Owner, source.info, skins, page);

                    Container.itemList.Remove(source);
                    for (var i = 0; i < source.info.itemMods.Length; i++)
                    {
                        var itemMod = source.info.itemMods[i];
                        itemMod.OnParentChanged(source);
                    }

#if DEBUG
                    _ins.Puts($"Updating content. Page: {page}");
#endif

                    Clear();

                    MoveItem(source, Container);
                    DestroyUI();
                    DrawUI(page);

                    for (var i = 0; i < skins.Count; i++)
                    {
                        var duplicate = GetDuplicateItem(source, skins[i]);
                        MoveItem(duplicate, Container, i + 1);
                    }
                }
                finally
                {
                    Pool.FreeUnmanaged(ref skins);
                }
            }

            private bool IsValid() => Owner == null || Container?.itemList != null;

            private bool CanUse()
            {
                var result = Interface.CallHook("CanUseSkins", Owner.IPlayer.Id);
                if (!(result is bool))
                    return true;

#if DEBUG
                _ins.Puts($"Hook result: {result}");
#endif

                return (bool)result;
            }

            public bool CanTakeSkin(Item item)
            {
                if (item == null || item.position == 0)
                    return true;

                List<ulong> totalSkins;
                if (TotalSkinsCache.TryGetValue(GetCacheKey(item.info.shortname, ViewMode), out totalSkins) &&
                    totalSkins.Contains(item.skin))
                    return true;

                return Configuration.SkinItem.Find(Owner.IPlayer, item.info.shortname)
                    .Any(x => x.GetAvailableSkins(Owner, item.info, SkinViewMode.Free).Contains(item.skin) ||
                              x.GetAvailableSkins(Owner, item.info, SkinViewMode.Owned).Contains(item.skin));
            }

            #region Working with items

            private Item GetDuplicateItem(Item item, ulong skin)
            {
                var duplicate = ItemManager.Create(item.info, item.amount, skin);
                if (item.hasCondition)
                {
                    duplicate._maxCondition = item._maxCondition;
                    duplicate._condition = item._condition;
                }

                if (item.contents != null)
                {
                    duplicate.contents.capacity = item.contents.capacity;
                }

                var projectile = duplicate.GetHeldEntity() as BaseProjectile;
                if (projectile != null)
                    projectile.primaryMagazine.contents = 0;

                return duplicate;
            }

            private void MoveItem(Item item, ItemContainer container, int slot = 0)
            {
                while (container.SlotTaken(item, slot) && container.capacity > slot)
                    slot++;

                if (container.IsFull() || container.SlotTaken(item, slot))
                {
#if DEBUG
                    _ins.Puts("Container is full, dropping item");
#endif

                    item.Drop(Owner.transform.position, Vector3.up);
                    return;
                }

                item.parent?.itemList?.Remove(item);

                item.RemoveFromWorld();

                item.position = slot;
                item.parent = container;

                container.itemList.Add(item);
                item.MarkDirty();

                for (var i = 0; i < item.info.itemMods.Length; i++)
                {
                    item.info.itemMods[i].OnParentChanged(item);
                }

                if (container == Container)
                    AddItemContainer(item);
            }

            private void RemoveItem(Item item)
            {
                if (item.uid.IsValid && Net.sv != null)
                {
                    Net.sv.ReturnUID(item.uid.Value);
                    item.uid = default(ItemId);
                }

                if (item.contents != null)
                {
                    for (var i = item.contents.itemList.Count - 1; i >= 0; i--)
                    {
                        RemoveItem(item.contents.itemList[i]);
                    }

                    item.contents = null;
                }

                item.RemoveFromWorld();

                item.parent = null;

                var heldEntity = item.GetHeldEntity();
                if (heldEntity != null && heldEntity.IsValid() && !heldEntity.IsDestroyed)
                    heldEntity.Kill();
            }

            #endregion
        }

        #endregion

        #region Helpers

        private const int MaxSkinSetSlots = 5;

        private bool CanUse(IPlayer player) => player.HasPermission(PermissionUse);

        private bool CanUseAdmin(IPlayer player) => player.HasPermission(PermissionAdmin);

        private bool CanUseSkinSets(IPlayer player) => CanUse(player) || player.HasPermission(PermissionSkinSets);

        private bool CanUseAutoSkins(IPlayer player) => CanUse(player) || player.HasPermission(PermissionAutoSkins);

        private bool CanUseTeamShare(IPlayer player) => CanUse(player) || player.HasPermission(PermissionTeamShare);

        private string GetMsg(string key, string userId = null) => lang.GetMessage(key, this, userId);

        private void TryAutoSkinItem(BasePlayer player, Item item)
        {
            if (player == null || item?.info == null || item.parentItem != null)
                return;

            if (_autoSkinIgnoredItems.Remove(item))
                return;

            var data = GetSkinSetData(player.userID);
            if (!data.AutoSkinsEnabled)
                return;

            SkinSet skinSet;
            if (!data.Sets.TryGetValue(data.SelectedSet, out skinSet) || skinSet?.Wear == null)
                return;

            var entry = skinSet.Wear.FirstOrDefault(x => x != null && x.Shortname == item.info.shortname);
            if (entry == null || entry.Skin == 0 || item.skin == entry.Skin)
                return;

            if (!CanUseSkin(player, entry.Shortname, entry.Skin))
                return;

            NextFrame(() =>
            {
                if (player == null || !player.IsConnected || item == null || item.parent == null ||
                    item.info == null || item.skin == entry.Skin)
                    return;

                ApplySkinToInventoryItem(player, item, entry.Skin);
            });
        }

        private void LoadSkinSetData()
        {
            try
            {
                _skinSetData = Interface.Oxide.DataFileSystem.ReadObject<SkinSetData>($"{Name}_SkinSets") ??
                               new SkinSetData();
            }
            catch
            {
                _skinSetData = new SkinSetData();
            }
        }

        private void SaveSkinSetData()
        {
            if (_skinSetData != null)
                Interface.Oxide.DataFileSystem.WriteObject($"{Name}_SkinSets", _skinSetData);
        }

        private PlayerSkinSetData GetSkinSetData(ulong userId)
        {
            if (_skinSetData == null)
                _skinSetData = new SkinSetData();

            if (!_skinSetData.Players.TryGetValue(userId, out var data))
                _skinSetData.Players[userId] = data = new PlayerSkinSetData();

            if (data.Sets == null)
                data.Sets = new Dictionary<int, SkinSet>();

            return data;
        }

        private bool TryParseSkinSetSlot(string value, out int slot)
        {
            if (!int.TryParse(value, out slot))
                return false;

            return slot >= 1 && slot <= MaxSkinSetSlots;
        }

        private void SelectSkinSet(IPlayer player, BasePlayer basePlayer, int slot, bool applyNow)
        {
            var data = GetSkinSetData(basePlayer.userID);
            data.SelectedSet = slot;
            SaveSkinSetData();

            if (applyNow)
            {
                ApplySkinSet(player, basePlayer, slot);
                return;
            }

            player.Reply(string.Format(GetMsg("Skin Set Selected", player.Id), slot));
        }

        private void OpenSkinCraftEditor(IPlayer player, BasePlayer basePlayer, int slot)
        {
            var data = GetSkinSetData(basePlayer.userID);
            data.SelectedSet = slot;
            SaveSkinSetData();

            SkinCraftController controller;
            if (!_skinCraftControllers.TryGetValue(basePlayer.userID, out controller))
                _skinCraftControllers[basePlayer.userID] = controller = new SkinCraftController(this, basePlayer);

            controller.Open(slot);
            player.Reply(string.Format(GetMsg("Skin Craft Opened", player.Id), slot));
        }

        private void OpenSkinCraftEditorNextFrame(IPlayer player, BasePlayer basePlayer, int slot)
        {
            NextFrame(() =>
            {
                if (basePlayer == null || !basePlayer.IsConnected)
                    return;

                OpenSkinCraftEditor(player, basePlayer, slot);
            });
        }

        private void CloseSkinCraft(BasePlayer player, bool force = false)
        {
            if (player == null)
                return;

            SkinCraftController controller;
            if (_skinCraftControllers.TryGetValue(player.userID, out controller) && controller.IsOpen &&
                (force || !controller.ShouldIgnoreClose()))
                controller.Close(force);
        }

        private bool SaveSkinCraftItem(BasePlayer player, Item item, int slot, int position)
        {
            if (item?.info == null || item.skin == 0)
            {
                SendReply(player, GetMsg("Skin Craft No Item Skin", player.UserIDString));
                return false;
            }

            if (!IsWornSkinSetItem(item))
            {
                SendReply(player, GetMsg("Skin Craft Not Worn", player.UserIDString));
                return false;
            }

            if (!CanUseSkin(player, item.info.shortname, item.skin))
            {
                SendReply(player, GetMsg("Skin Craft Not Owned", player.UserIDString));
                return false;
            }

            var skinSet = GetOrCreateSkinSet(player.userID, slot);
            var entry = new SkinSetEntry
            {
                Shortname = item.info.shortname,
                Skin = item.skin,
                Position = Mathf.Clamp(position, 0, 6)
            };

            skinSet.Wear.RemoveAll(x => x == null || x.Shortname == entry.Shortname || x.Position == entry.Position);
            skinSet.Wear.Add(entry);

            SaveSkinSetData();
            SendReply(player, string.Format(GetMsg("Skin Craft Saved Item", player.UserIDString),
                item.info.displayName.english, slot));
            return true;
        }

        private SkinSet GetOrCreateSkinSet(ulong userId, int slot)
        {
            var data = GetSkinSetData(userId);
            SkinSet skinSet;
            if (!data.Sets.TryGetValue(slot, out skinSet))
                data.Sets[slot] = skinSet = new SkinSet();

            if (skinSet.Wear == null)
                skinSet.Wear = new List<SkinSetEntry>();

            return skinSet;
        }

        private bool IsWornSkinSetItem(Item item)
        {
            return item?.info != null && item.info.category == ItemCategory.Attire;
        }

        private int GetSkinSetEditorPosition(string shortname, int fallback)
        {
            if (string.IsNullOrEmpty(shortname))
                return Mathf.Clamp(fallback, 0, 6);

            var value = shortname.ToLowerInvariant();

            if (value.Contains("helmet") || value.Contains("mask") || value.Contains("hat") ||
                value.Contains("coffeecan") || value.Contains("facemask") || value.Contains("head"))
                return 0;

            if (value.Contains("chest") || value.Contains("vest") || value.Contains("jacket"))
                return 1;

            if (value.Contains("shirt") || value.Contains("hoodie"))
                return 2;

            if (value.Contains("pants") || value.Contains("kilt") || value.Contains("burlap.trousers"))
                return 3;

            if (value.Contains("boots") || value.Contains("shoes"))
                return 4;

            if (value.Contains("gloves"))
                return 5;

            return 6;
        }

        private void SaveSkinSet(IPlayer player, BasePlayer basePlayer, int slot)
        {
            var skinSet = CaptureWornSkinSet(basePlayer);
            if (skinSet.Wear.Count == 0)
            {
                player.Reply(GetMsg("Skin Set No Skins", player.Id));
                return;
            }

            GetSkinSetData(basePlayer.userID).Sets[slot] = skinSet;
            SaveSkinSetData();
            player.Reply(string.Format(GetMsg("Skin Set Saved", player.Id), slot));
        }

        private void ApplySkinSet(IPlayer player, BasePlayer basePlayer, int slot)
        {
            var data = GetSkinSetData(basePlayer.userID);
            if (!data.Sets.TryGetValue(slot, out var skinSet) || skinSet?.Wear == null || skinSet.Wear.Count == 0)
            {
                player.Reply(string.Format(GetMsg("Skin Set Empty", player.Id), slot));
                return;
            }

            var skipped = 0;
            var applied = 0;
            foreach (var entry in skinSet.Wear)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Shortname) || entry.Skin == 0)
                    continue;

                if (!CanUseSkin(basePlayer, entry.Shortname, entry.Skin))
                {
                    skipped++;
                    continue;
                }

                var item = FindWornItem(basePlayer, entry.Shortname, entry.Position);
                if (item == null)
                    continue;

                if (ApplySkinToWornItem(basePlayer, item, entry.Skin))
                    applied++;
            }

            RefreshWornSkins(basePlayer);

            var messageKey = applied == 0
                ? "Skin Set Applied None"
                : skipped == 0
                    ? "Skin Set Applied"
                    : "Skin Set Applied Partial";

            player.Reply(string.Format(GetMsg(messageKey, player.Id), slot));
        }

        private void ApplySkinSetToTeam(IPlayer player, BasePlayer owner, int slot)
        {
            var data = GetSkinSetData(owner.userID);
            SkinSet skinSet;
            if (!data.Sets.TryGetValue(slot, out skinSet) || skinSet?.Wear == null || skinSet.Wear.Count == 0)
            {
                player.Reply(string.Format(GetMsg("Skin Set Empty", player.Id), slot));
                return;
            }

            var count = 0;
            foreach (var target in GetTeamShareTargets(owner))
            {
                if (target == null || target == owner || !target.IsConnected)
                    continue;

                var targetData = GetSkinSetData(target.userID);
                if (targetData.TeamShareBlocked)
                    continue;

                if (ApplySkinSetToPlayer(target, skinSet) > 0)
                    count++;
            }

            player.Reply(count == 0
                ? GetMsg("Team Skin No Targets", player.Id)
                : string.Format(GetMsg("Team Skin Applied", player.Id), slot, count));
        }

        private int ApplySkinSetToPlayer(BasePlayer target, SkinSet skinSet)
        {
            var applied = 0;
            foreach (var entry in skinSet.Wear)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Shortname) || entry.Skin == 0)
                    continue;

                if (!CanUseSkin(target, entry.Shortname, entry.Skin))
                    continue;

                var item = FindWornItem(target, entry.Shortname, entry.Position);
                if (item == null)
                    continue;

                if (ApplySkinToWornItem(target, item, entry.Skin))
                    applied++;
            }

            if (applied > 0)
                RefreshWornSkins(target);

            return applied;
        }

        private IEnumerable<BasePlayer> GetTeamShareTargets(BasePlayer owner)
        {
            var ids = new HashSet<ulong>();

            var team = owner != null && owner.currentTeam != 0
                ? RelationshipManager.ServerInstance.FindTeam(owner.currentTeam)
                : null;
            if (team?.members != null)
            {
                foreach (var memberId in team.members)
                    ids.Add(memberId);
            }

            foreach (var clanMemberId in GetClanMemberIds(owner))
                ids.Add(clanMemberId);

            foreach (var id in ids)
            {
                if (id == owner.userID)
                    continue;

                var player = BasePlayer.activePlayerList.FirstOrDefault(x => x.userID == id);
                if (player != null)
                    yield return player;
            }
        }

        private IEnumerable<ulong> GetClanMemberIds(BasePlayer owner)
        {
            if (Clans == null || owner == null)
                yield break;

            var clan = Clans.Call("GetClanOf", owner.UserIDString) as string ??
                       Clans.Call("GetClanOf", owner.userID) as string;
            if (string.IsNullOrEmpty(clan))
                yield break;

            var result = Clans.Call("GetClanMembers", clan);
            if (result == null)
                yield break;

            if (result is IEnumerable<string> stringMembers)
            {
                foreach (var member in stringMembers)
                {
                    ulong id;
                    if (ulong.TryParse(member, out id))
                        yield return id;
                }

                yield break;
            }

            if (result is System.Collections.IEnumerable enumerable && !(result is string))
            {
                foreach (var member in enumerable)
                {
                    ulong id;
                    if (member != null && ulong.TryParse(member.ToString(), out id))
                        yield return id;
                }
            }
        }

        private void DeleteSkinSet(IPlayer player, ulong userId, int slot)
        {
            GetSkinSetData(userId).Sets.Remove(slot);
            SaveSkinSetData();
            player.Reply(string.Format(GetMsg("Skin Set Deleted", player.Id), slot));
        }

        private void ReplySkinSetList(IPlayer player, ulong userId)
        {
            var data = GetSkinSetData(userId);
            var slots = new List<string>();
            for (var slot = 1; slot <= MaxSkinSetSlots; slot++)
                slots.Add($"{slot}:{(data.Sets.ContainsKey(slot) ? "saved" : "empty")}");

            player.Reply(string.Format(GetMsg("Skin Set List", player.Id), string.Join(", ", slots)));
        }

        private SkinSet CaptureWornSkinSet(BasePlayer player)
        {
            var skinSet = new SkinSet();
            var container = player?.inventory?.containerWear;
            if (container?.itemList == null)
                return skinSet;

            foreach (var item in container.itemList)
            {
                if (item?.info == null || item.skin == 0)
                    continue;

                var position = GetSkinSetEditorPosition(item.info.shortname, skinSet.Wear.Count);
                skinSet.Wear.Add(new SkinSetEntry
                {
                    Shortname = item.info.shortname,
                    Skin = item.skin,
                    Position = position
                });
            }

            return skinSet;
        }

        private Item FindWornItem(BasePlayer player, string shortname, int position)
        {
            var container = player?.inventory?.containerWear;
            if (container?.itemList == null)
                return null;

            if (position >= 0)
            {
                var slottedItem = container.GetSlot(position);
                if (slottedItem?.info != null && slottedItem.info.shortname == shortname)
                    return slottedItem;
            }

            return container.itemList.FirstOrDefault(item => item?.info != null && item.info.shortname == shortname);
        }

        private bool ApplySkinToWornItem(BasePlayer player, Item item, ulong skin)
        {
            var applied = ApplySkinToInventoryItem(player, item, skin);
            if (applied)
                RefreshWornSkins(player);

            return applied;
        }

        private bool ApplySkinToInventoryItem(BasePlayer player, Item item, ulong skin)
        {
            var container = item.parent;
            var position = item.position;
            if (container == null)
                return false;

            var replacement = ItemManager.Create(item.info, item.amount, skin);
            if (replacement == null)
                return false;

            if (item.hasCondition)
            {
                replacement._maxCondition = item._maxCondition;
                replacement._condition = item._condition;
            }

            replacement.name = item.name;
            _autoSkinIgnoredItems.Add(replacement);

            item.RemoveFromContainer();
            item.Remove();

            if (!replacement.MoveToContainer(container, position))
            {
                replacement.Drop(player.transform.position, Vector3.up);
                return false;
            }

            replacement.skin = skin;
            replacement.MarkDirty();
            RefreshWornItem(replacement);
            Interface.CallHook("OnItemSkinChanged", player, replacement);
            player.inventory.SendSnapshot();
            return true;
        }

        private void RefreshWornItem(Item item)
        {
            if (item?.info?.itemMods == null)
                return;

            for (var i = 0; i < item.info.itemMods.Length; i++)
                item.info.itemMods[i].OnParentChanged(item);

            var heldEntity = item.GetHeldEntity();
            if (heldEntity != null && heldEntity.IsValid() && !heldEntity.IsDestroyed)
                heldEntity.SendNetworkUpdateImmediate();
        }

        private void RefreshWornSkins(BasePlayer player)
        {
            if (player == null)
                return;

            RefreshWornContainerItems(player);
            player.inventory.containerWear?.MarkDirty();
            player.inventory.SendSnapshot();
            player.SendNetworkUpdateImmediate();

            NextFrame(() =>
            {
                if (player == null || !player.IsConnected)
                    return;

                RefreshWornContainerItems(player);
                player.inventory.containerWear?.MarkDirty();
                player.inventory.SendSnapshot();
                player.SendNetworkUpdateImmediate();
            });
        }

        private void RefreshWornContainerItems(BasePlayer player)
        {
            var container = player?.inventory?.containerWear;
            if (container?.itemList == null)
                return;

            for (var i = 0; i < container.itemList.Count; i++)
                RefreshWornItem(container.itemList[i]);
        }

        private IEnumerable<ulong> GetOwnedItemSkinIds(BasePlayer player, ItemDefinition itemDefinition)
        {
            if (player == null || itemDefinition == null)
                yield break;

            var skins = GetAllItemSkinObjects();
            if (skins == null)
                yield break;

            foreach (var skinObject in skins)
            {
                if (!SkinObjectBelongsToItem(skinObject, itemDefinition))
                    continue;

                ulong skinId;
                if (!TryGetSkinId(skinObject, out skinId) || skinId == 0)
                    continue;

                bool ownsSkin;
                if (TryPlayerDLCAPIIsOwnedOrFreeSkin(player, skinId, out ownsSkin) && ownsSkin)
                    yield return skinId;
            }
        }

        private bool PlayerOwnsSkin(BasePlayer player, ItemDefinition itemDefinition, ulong skin)
        {
            if (skin == 0)
                return true;

            bool ownsSkin;
            if (TryPlayerDLCAPIIsOwnedOrFreeSkin(player, skin, out ownsSkin))
                return ownsSkin;

            if (!SkinBelongsToItem(itemDefinition, skin))
                return false;

            var steamInventory = GetSteamInventory(player);
            if (steamInventory == null)
                return false;

            if (TryCallOwnershipMethod(steamInventory, itemDefinition, skin, out ownsSkin))
                return ownsSkin;

            return false;
        }

        private bool TryPlayerDLCAPIFilterOwnedOrFreeSkins(BasePlayer player, List<ulong> workshopIds)
        {
            if (PlayerDLCAPI == null || player == null || workshopIds == null)
                return false;

            var result = PlayerDLCAPI.Call("FilterOwnedOrFreeSkins", player, workshopIds);
            return result is bool && (bool)result;
        }

        private bool TryPlayerDLCAPIIsOwnedOrFreeSkin(BasePlayer player, ulong skin,
            out bool ownsSkin)
        {
            ownsSkin = false;

            if (PlayerDLCAPI == null || player == null)
                return false;

            var result = PlayerDLCAPI.Call("IsOwnedOrFreeSkin", player, skin);
            if (result is bool)
            {
                ownsSkin = (bool)result;
                return true;
            }

            return false;
        }

        private bool SkinBelongsToItem(ItemDefinition itemDefinition, ulong skin)
        {
            var skins = GetAllItemSkinObjects();
            if (skins == null)
                return false;

            foreach (var skinObject in skins)
            {
                ulong skinId;
                if (SkinObjectBelongsToItem(skinObject, itemDefinition) &&
                    TryGetSkinId(skinObject, out skinId) &&
                    skinId == skin)
                    return true;
            }

            return false;
        }

        private System.Collections.IEnumerable GetAllItemSkinObjects()
        {
            var directory = ItemSkinDirectory.Instance;
            return directory == null ? null : GetMemberValue(directory, "skins") as System.Collections.IEnumerable;
        }

        private bool SkinObjectBelongsToItem(object skinObject, ItemDefinition itemDefinition)
        {
            if (skinObject == null || itemDefinition == null)
                return false;

            ulong itemId;
            if (TryGetNumericMember(skinObject, out itemId, "itemid", "itemID", "ItemID") &&
                itemId == (ulong)itemDefinition.itemid)
                return true;

            var invItem = GetMemberValue(skinObject, "invItem");
            var skinItemDefinition = GetMemberValue(invItem, "itemDefinition") as ItemDefinition;
            if (skinItemDefinition != null && skinItemDefinition.itemid == itemDefinition.itemid)
                return true;

            var redirect = GetMemberValue(invItem, "Redirect") as ItemDefinition;
            if (redirect != null && redirect.itemid == itemDefinition.itemid)
                return true;

            var itemName = GetMemberValue(invItem, "itemname") as string;
            return !string.IsNullOrEmpty(itemName) &&
                   (itemName == itemDefinition.shortname || itemName == $"{itemDefinition.shortname}.item");
        }

        private bool PlayerOwnsSkinObject(BasePlayer player, object steamInventory, object skinObject, ulong skinId)
        {
            bool ownsSkin;
            if (TryPlayerDLCAPIIsOwnedOrFreeSkin(player, skinId, out ownsSkin))
                return ownsSkin;

            var invItem = GetMemberValue(skinObject, "invItem") ??
                          GetMemberValue(skinObject, "inventoryItem") ??
                          GetMemberValue(skinObject, "steamItem");

            if (invItem != null && TryCallOwnershipMethod(steamInventory, invItem, skinId, out ownsSkin))
                return ownsSkin;

            ulong inventoryItemId;
            if (TryGetInventoryItemId(skinObject, out inventoryItemId) &&
                TryCallOwnershipMethod(steamInventory, null, inventoryItemId, out ownsSkin))
                return ownsSkin;

            if (invItem != null && TryGetInventoryItemId(invItem, out inventoryItemId) &&
                TryCallOwnershipMethod(steamInventory, null, inventoryItemId, out ownsSkin))
                return ownsSkin;

            if (TryCallOwnershipMethod(steamInventory, null, skinId, out ownsSkin))
                return ownsSkin;

            return false;
        }

        private bool TryCallOwnershipMethod(object steamInventory, object itemDefinition, ulong skin,
            out bool ownsSkin)
        {
            ownsSkin = false;

            var methodNames = new[]
            {
                "HasItem",
                "ContainsItem",
                "Contains",
                "HasSkin",
                "OwnsSkin",
                "OwnsItem"
            };

            var methods = steamInventory.GetType()
                .GetMethods(System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Instance)
                .Where(x => x.ReturnType == typeof(bool))
                .OrderByDescending(x => methodNames.Contains(x.Name));

            foreach (var method in methods)
            {
                if (!methodNames.Contains(method.Name) &&
                    method.Name.IndexOf("skin", StringComparison.OrdinalIgnoreCase) == -1 &&
                    method.Name.IndexOf("item", StringComparison.OrdinalIgnoreCase) == -1)
                    continue;

                object[] args;
                if (!TryBuildOwnershipArgs(method.GetParameters(), itemDefinition, skin, out args))
                    continue;

                try
                {
                    ownsSkin = (bool)method.Invoke(steamInventory, args);
                    return true;
                }
                catch
                {
                    // Try the next known inventory method signature.
                }
            }

            return false;
        }

        private bool TryBuildOwnershipArgs(System.Reflection.ParameterInfo[] parameters, object itemDefinition,
            ulong skin, out object[] args)
        {
            args = null;

            if (parameters.Length == 1)
            {
                object itemArg;
                if (TryBuildOwnershipArg(parameters[0].ParameterType, itemDefinition, false, out itemArg))
                {
                    args = new[] { itemArg };
                    return true;
                }

                object skinArg;
                if (!TryConvertNumber(skin, parameters[0].ParameterType, out skinArg))
                    return false;

                args = new[] { skinArg };
                return true;
            }

            if (parameters.Length != 2)
                return false;

            object firstArg;
            object secondArg;

            if (TryBuildOwnershipArg(parameters[0].ParameterType, itemDefinition, false, out firstArg) &&
                TryConvertNumber(skin, parameters[1].ParameterType, out secondArg))
            {
                args = new[] { firstArg, secondArg };
                return true;
            }

            if (TryConvertNumber(skin, parameters[0].ParameterType, out firstArg) &&
                TryBuildOwnershipArg(parameters[1].ParameterType, itemDefinition, false, out secondArg))
            {
                args = new[] { firstArg, secondArg };
                return true;
            }

            return false;
        }

        private bool TryBuildOwnershipArg(Type type, object itemDefinition, bool allowSkin, out object value)
        {
            value = null;

            if (itemDefinition != null && type.IsInstanceOfType(itemDefinition))
            {
                value = itemDefinition;
                return true;
            }

            ulong itemId;
            if (itemDefinition != null && TryGetInventoryItemId(itemDefinition, out itemId) &&
                TryConvertNumber(itemId, type, out value))
                return true;

            if (allowSkin)
                return TryConvertNumber(0, type, out value);

            return false;
        }

        private bool TryGetSkinId(object skinObject, out ulong skinId)
        {
            skinId = 0;
            var invItem = GetMemberValue(skinObject, "invItem");
            if (TryGetNumericMember(invItem, out skinId,
                    "workshopID", "workshopId", "WorkshopID", "WorkshopId", "workshopid") &&
                skinId != 0)
                return true;

            return TryGetNumericMember(skinObject, out skinId,
                "workshopid", "workshopID", "WorkshopID", "workshopDownload", "WorkshopDownload",
                "workshopId", "WorkshopId", "skinid", "skinID", "SkinID", "id");
        }

        private bool TryGetInventoryItemId(object value, out ulong itemId)
        {
            itemId = 0;
            return TryGetNumericMember(value, out itemId,
                "itemid", "itemID", "ItemID", "itemdefid", "itemDefId", "ItemDefId", "itemDefID",
                "definitionId", "DefinitionId", "id", "ID");
        }

        private bool TryGetNumericMember(object target, out ulong number, params string[] names)
        {
            number = 0;
            if (target == null)
                return false;

            foreach (var name in names)
            {
                var value = GetMemberValue(target, name);
                if (value == null)
                    continue;

                try
                {
                    number = Convert.ToUInt64(value);
                    return true;
                }
                catch
                {
                    // Try the next likely member name.
                }
            }

            return false;
        }

        private object GetSteamInventory(BasePlayer player)
        {
            return GetMemberValue(GetMemberValue(player, "blueprints"), "steamInventory");
        }

        private bool TryConvertNumber(ulong number, Type type, out object value)
        {
            value = null;
            type = Nullable.GetUnderlyingType(type) ?? type;

            try
            {
                if (type == typeof(ulong))
                    value = number;
                else if (type == typeof(long))
                    value = unchecked((long)number);
                else if (type == typeof(uint))
                    value = unchecked((uint)number);
                else if (type == typeof(int))
                    value = unchecked((int)number);
                else if (type == typeof(ushort))
                    value = unchecked((ushort)number);
                else if (type == typeof(short))
                    value = unchecked((short)number);
                else
                    return false;
            }
            catch
            {
                return false;
            }

            return true;
        }

        private object GetMemberValue(object target, string name)
        {
            if (target == null)
                return null;

            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public |
                                                         System.Reflection.BindingFlags.NonPublic |
                                                         System.Reflection.BindingFlags.Instance;

            var type = target.GetType();
            var property = type.GetProperty(name, flags);
            if (property != null)
                return property.GetValue(target, null);

            var field = type.GetField(name, flags);
            return field?.GetValue(target);
        }

        #endregion
    }
}
