using System;
using System.Collections.Generic;
using Network;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Skin Sets", "Codex", "1.0.0")]
    [Description("Lets players save and apply up to five personal skin sets.")]
    internal class SkinSets : RustPlugin
    {
        [PluginReference]
        private Plugin Skins;

        private const string PermissionUse = "skinsets.use";
        private const int MaxSlots = 5;
        private const int SlotsPerSet = 7;
        private const int EditorCapacity = MaxSlots * SlotsPerSet;
        private const string PanelName = "generic";
        private const string EditorUiName = "SkinSets.EditorLabels";

        private StoredData _storedData;
        private readonly Dictionary<ulong, SkinSetController> _controllers = new Dictionary<ulong, SkinSetController>();
        private readonly Dictionary<ItemContainerId, SkinSetController> _controllersByContainer =
            new Dictionary<ItemContainerId, SkinSetController>();

        private sealed class StoredData
        {
            public Dictionary<ulong, PlayerData> Players = new Dictionary<ulong, PlayerData>();
        }

        private sealed class PlayerData
        {
            public Dictionary<int, SkinSet> Sets = new Dictionary<int, SkinSet>();
        }

        private sealed class SkinSet
        {
            public List<SkinEntry> Wear = new List<SkinEntry>();
            public List<SkinEntry> Belt = new List<SkinEntry>();
            public List<SkinEntry> Items = new List<SkinEntry>();
        }

        private sealed class SkinEntry
        {
            public string Shortname;
            public ulong Skin;
            public int EditorPosition = -1;
        }

        private void Init()
        {
            permission.RegisterPermission(PermissionUse, this);
            LoadData();
        }

        private void Unload()
        {
            foreach (var controller in _controllers.Values)
                controller.Destroy();

            SaveData();
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player != null && !_controllers.ContainsKey(player.userID))
                _controllers[player.userID] = new SkinSetController(this, player);
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null)
                return;

            if (_controllers.TryGetValue(player.userID, out var controller))
            {
                controller.Destroy();
                _controllers.Remove(player.userID);
            }
        }

        private void OnServerInitialized()
        {
            foreach (var player in BasePlayer.activePlayerList)
                OnPlayerConnected(player);
        }

        private void LoadData()
        {
            _storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, _storedData);
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["No Permission"] = "You don't have permission to use skin sets.",
                ["Syntax"] = "Use /ss 1-5, /ss save 1-5, /ss load 1-5, /ss delete 1-5, or /ss list.",
                ["Invalid Slot"] = "Pick a slot from 1 to 5.",
                ["Saved"] = "Saved your current skin set to slot {0}.",
                ["Loaded"] = "Applied skin set slot {0}.",
                ["Deleted"] = "Deleted skin set slot {0}.",
                ["Empty"] = "Slot {0} is empty. Use /ss save {0} to save your current skins.",
                ["No Skins"] = "You don't have any skinned worn or belt items to save.",
                ["List Header"] = "Skin sets: {0}",
                ["List Slot"] = "{0}:{1}",
                ["Loaded Partial"] = "Applied skin set slot {0}. Some skins were skipped because you don't own them.",
                ["Saved Item"] = "Saved {0}'s skin to skin set slot {1}.",
                ["No Item Skin"] = "That item has no skin to save.",
                ["Not Worn Item"] = "Only worn item skins can be saved in skin sets.",
                ["Opened"] = "Drop worn-item skins into the editor. Each row saves one skin set.",
            }, this);
        }

        [ChatCommand("ss")]
        private void CommandSkinSet(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;

            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                Reply(player, "No Permission");
                return;
            }

            if (args == null || args.Length == 0)
            {
                OpenEditor(player);
                return;
            }

            if (args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                ListSlots(player);
                return;
            }

            if (args[0].Equals("open", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("edit", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("editor", StringComparison.OrdinalIgnoreCase))
            {
                OpenEditor(player);
                return;
            }

            if (args.Length == 1 && TryParseSlot(args[0], out var quickSlot))
            {
                var playerData = GetPlayerData(player.userID);
                if (playerData.Sets.ContainsKey(quickSlot))
                    LoadSet(player, quickSlot);
                else
                    SaveSet(player, quickSlot);
                return;
            }

            if (args.Length < 2 || !TryParseSlot(args[1], out var slot))
            {
                Reply(player, "Syntax");
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "save":
                case "s":
                    SaveSet(player, slot);
                    break;

                case "load":
                case "l":
                case "use":
                    LoadSet(player, slot);
                    break;

                case "delete":
                case "del":
                case "remove":
                case "r":
                    DeleteSet(player, slot);
                    break;

                default:
                    Reply(player, "Syntax");
                    break;
            }
        }

        private void SaveSet(BasePlayer player, int slot)
        {
            var skinSet = CaptureSet(player);
            if (skinSet.Items.Count == 0)
            {
                Reply(player, "No Skins");
                return;
            }

            AssignEditorPositions(skinSet, slot);
            GetPlayerData(player.userID).Sets[slot] = skinSet;
            SaveData();
            Reply(player, "Saved", slot);
        }

        private void LoadSet(BasePlayer player, int slot)
        {
            var playerData = GetPlayerData(player.userID);
            if (!playerData.Sets.TryGetValue(slot, out var skinSet))
            {
                Reply(player, "Empty", slot);
                return;
            }

            NormalizeSkinSet(skinSet);
            var skipped = 0;
            ApplyEntries(player, skinSet.Items, ref skipped);

            Reply(player, skipped == 0 ? "Loaded" : "Loaded Partial", slot);
        }

        private void DeleteSet(BasePlayer player, int slot)
        {
            var playerData = GetPlayerData(player.userID);
            playerData.Sets.Remove(slot);
            SaveData();
            Reply(player, "Deleted", slot);
        }

        private void OpenEditor(BasePlayer player)
        {
            if (!_controllers.TryGetValue(player.userID, out var controller))
                _controllers[player.userID] = controller = new SkinSetController(this, player);

            controller.Open();
            Reply(player, "Opened");
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (item?.parentItem != null)
                return;

            if (!_controllersByContainer.TryGetValue(container.uid, out var controller))
                return;

            controller.OnItemAdded(item);
        }

        private void OnPlayerLootEnd(PlayerLoot loot)
        {
            var player = loot.baseEntity ?? loot.gameObject.GetComponent<BasePlayer>();
            CloseEditor(player);
        }

        private void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            CloseEditor(player);
        }

        private object CanLootPlayer(BasePlayer looter, BasePlayer target)
        {
            if (looter != target)
                return null;

            if (_controllers.TryGetValue(looter.userID, out var controller) && controller.IsOpen)
                return true;

            return null;
        }

        private void CloseEditor(BasePlayer player)
        {
            if (player == null)
                return;

            if (_controllers.TryGetValue(player.userID, out var controller) && controller.IsOpen)
                controller.Close();
        }

        private SkinSet CaptureSet(BasePlayer player)
        {
            var wear = CaptureContainer(player.inventory.containerWear);
            var items = new List<SkinEntry>();
            MergeEntries(items, wear);

            return new SkinSet
            {
                Wear = wear,
                Belt = new List<SkinEntry>(),
                Items = items
            };
        }

        private List<SkinEntry> CaptureContainer(ItemContainer container)
        {
            var entries = new List<SkinEntry>();
            if (container?.itemList == null)
                return entries;

            foreach (var item in container.itemList)
            {
                if (item?.info == null || item.skin == 0)
                    continue;

                entries.Add(new SkinEntry
                {
                    Shortname = item.info.shortname,
                    Skin = item.skin
                });
            }

            return entries;
        }

        private void ApplyEntries(BasePlayer player, List<SkinEntry> entries, ref int skipped)
        {
            ApplyEntries(player, player.inventory.containerWear, entries, ref skipped);
        }

        private void ApplyEntries(BasePlayer player, ItemContainer container, List<SkinEntry> entries, ref int skipped)
        {
            if (container?.itemList == null || entries == null)
                return;

            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Shortname) || entry.Skin == 0)
                    continue;

                if (!CanUseSkin(player, entry.Shortname, entry.Skin))
                {
                    skipped++;
                    continue;
                }

                var item = FindItem(container, entry.Shortname);
                if (item == null)
                    continue;

                SetItemSkin(item, entry.Skin);
            }
        }

        private void SaveItemToSlot(BasePlayer player, Item item, int slot, int editorPosition)
        {
            if (item?.info == null || item.skin == 0)
            {
                Reply(player, "No Item Skin");
                return;
            }

            if (!IsWornItem(item))
            {
                Reply(player, "Not Worn Item");
                return;
            }

            if (!CanUseSkin(player, item.info.shortname, item.skin))
            {
                Reply(player, "Loaded Partial", slot);
                return;
            }

            var skinSet = GetOrCreateSet(player.userID, slot);
            var entry = new SkinEntry
            {
                Shortname = item.info.shortname,
                Skin = item.skin,
                EditorPosition = editorPosition
            };

            UpsertEntry(skinSet.Items, entry);
            SaveData();
            Reply(player, "Saved Item", item.info.displayName.english, slot);
        }

        private bool IsWornItem(Item item)
        {
            return item?.info != null && item.info.category == ItemCategory.Attire;
        }

        private void NormalizeSkinSet(SkinSet skinSet)
        {
            if (skinSet == null)
                return;

            if (skinSet.Items == null)
                skinSet.Items = new List<SkinEntry>();
            if (skinSet.Wear == null)
                skinSet.Wear = new List<SkinEntry>();
            if (skinSet.Belt == null)
                skinSet.Belt = new List<SkinEntry>();

            if (skinSet.Items.Count != 0)
                return;

            MergeEntries(skinSet.Items, skinSet.Wear);
            MergeEntries(skinSet.Items, skinSet.Belt);
        }

        private void AssignEditorPositions(SkinSet skinSet, int slot)
        {
            if (skinSet?.Items == null)
                return;

            var start = (slot - 1) * SlotsPerSet;
            for (var i = 0; i < skinSet.Items.Count && i < SlotsPerSet; i++)
                skinSet.Items[i].EditorPosition = start + i;
        }

        private void MergeEntries(List<SkinEntry> target, List<SkinEntry> source)
        {
            if (target == null || source == null)
                return;

            foreach (var entry in source)
                UpsertEntry(target, entry);
        }

        private void UpsertEntry(List<SkinEntry> entries, SkinEntry entry)
        {
            if (entries == null || entry == null || string.IsNullOrEmpty(entry.Shortname))
                return;

            var existing = entries.Find(x => x.Shortname == entry.Shortname);
            if (existing == null)
                entries.Add(entry);
            else
                existing.Skin = entry.Skin;
        }

        private Item FindItem(ItemContainer container, string shortname)
        {
            foreach (var item in container.itemList)
            {
                if (item?.info != null && item.info.shortname == shortname)
                    return item;
            }

            return null;
        }

        private void SetItemSkin(Item item, ulong skin)
        {
            if (item == null || item.skin == skin)
                return;

            var container = item.parent;
            var position = item.position;
            var replacement = ItemManager.Create(item.info, item.amount, skin);
            if (replacement == null)
                return;

            if (item.hasCondition)
            {
                replacement._maxCondition = item._maxCondition;
                replacement._condition = item._condition;
            }

            RemoveItem(item);
            replacement.MoveToContainer(container, position);
        }

        private void RemoveItem(Item item)
        {
            if (item == null)
                return;

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

        private bool CanUseSkin(BasePlayer player, string shortname, ulong skin)
        {
            if (skin == 0 || Skins == null)
                return true;

            var result = Skins.Call("CanUseSkin", player, shortname, skin);
            return !(result is bool) || (bool)result;
        }

        private void ListSlots(BasePlayer player)
        {
            var playerData = GetPlayerData(player.userID);
            var slots = new List<string>();
            for (var slot = 1; slot <= MaxSlots; slot++)
            {
                slots.Add(string.Format(GetMsg("List Slot", player.UserIDString), slot,
                    playerData.Sets.ContainsKey(slot) ? "saved" : "empty"));
            }

            Reply(player, "List Header", string.Join(", ", slots));
        }

        private PlayerData GetPlayerData(ulong userId)
        {
            if (!_storedData.Players.TryGetValue(userId, out var playerData))
                _storedData.Players[userId] = playerData = new PlayerData();

            return playerData;
        }

        private SkinSet GetOrCreateSet(ulong userId, int slot)
        {
            var playerData = GetPlayerData(userId);
            if (!playerData.Sets.TryGetValue(slot, out var skinSet))
                playerData.Sets[slot] = skinSet = new SkinSet();

            if (skinSet.Items == null)
                skinSet.Items = new List<SkinEntry>();
            if (skinSet.Wear == null)
                skinSet.Wear = new List<SkinEntry>();
            if (skinSet.Belt == null)
                skinSet.Belt = new List<SkinEntry>();

            return skinSet;
        }

        private bool TryParseSlot(string value, out int slot)
        {
            return int.TryParse(value, out slot) && slot >= 1 && slot <= MaxSlots;
        }

        private void Reply(BasePlayer player, string key, params object[] args)
        {
            var message = GetMsg(key, player.UserIDString);
            if (args != null && args.Length > 0)
                message = string.Format(message, args);

            SendReply(player, message);
        }

        private string GetMsg(string key, string playerId = null)
        {
            return lang.GetMessage(key, this, playerId);
        }

        private sealed class SkinSetController
        {
            private readonly SkinSets _plugin;
            private readonly BasePlayer _owner;
            private readonly ItemContainer _container;
            private readonly HashSet<Item> _previewItems = new HashSet<Item>();
            private bool _ignoreItemAdded;

            public bool IsOpen;

            public SkinSetController(SkinSets plugin, BasePlayer owner)
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
                _plugin._controllersByContainer[_container.uid] = this;
            }

            public void Open()
            {
                if (!CanOpen())
                    return;

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

                IsOpen = true;
                DrawLabels();
                _owner.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", _owner), PanelName);
            }

            public void Close()
            {
                DestroyLabels();
                ReturnItems();
                Clear();
                IsOpen = false;
            }

            public void Destroy()
            {
                Close();
                _plugin._controllersByContainer.Remove(_container.uid);
                _container.Kill();
            }

            private void DrawLabels()
            {
                DestroyLabels();

                var elements = new CuiElementContainer();
                elements.Add(new CuiPanel
                {
                    Image =
                    {
                        Color = "0 0 0 0.35"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.18 0.79",
                        AnchorMax = "0.82 0.93"
                    },
                    CursorEnabled = false
                }, "Overlay", EditorUiName);

                for (var i = 0; i < MaxSlots; i++)
                {
                    var minX = i / (float)MaxSlots;
                    var maxX = (i + 1) / (float)MaxSlots;
                    elements.Add(new CuiLabel
                    {
                        Text =
                        {
                            Text = $"Set {i + 1}\nSlots {i * SlotsPerSet + 1}-{(i + 1) * SlotsPerSet}",
                            Align = TextAnchor.MiddleCenter,
                            FontSize = 13,
                            Color = "1 1 1 0.92"
                        },
                        RectTransform =
                        {
                            AnchorMin = $"{minX} 0",
                            AnchorMax = $"{maxX} 1"
                        }
                    }, EditorUiName);
                }

                CuiHelper.AddUi(_owner, elements);
            }

            private void DestroyLabels()
            {
                CuiHelper.DestroyUi(_owner, EditorUiName);
            }

            public void OnItemAdded(Item item)
            {
                if (_ignoreItemAdded)
                    return;

                if (item == null || item.parent != _container)
                    return;

                var position = item.position;
                var slot = GetSetSlot(item.position);

                _plugin.SaveItemToSlot(_owner, item, slot, position);
                _plugin.NextFrame(() =>
                {
                    ReturnItem(item);
                    AddPreview(item.info.shortname, item.skin, position);
                });
            }

            private void PopulatePreviews()
            {
                var playerData = _plugin.GetPlayerData(_owner.userID);
                for (var slot = 1; slot <= MaxSlots; slot++)
                {
                    if (!playerData.Sets.TryGetValue(slot, out var skinSet))
                        continue;

                    _plugin.NormalizeSkinSet(skinSet);
                    foreach (var entry in skinSet.Items)
                    {
                        var position = entry.EditorPosition >= 0
                            ? entry.EditorPosition
                            : ((slot - 1) * SlotsPerSet) + GetNextFreeOffset(slot);

                        AddPreview(entry.Shortname, entry.Skin, position);
                    }
                }
            }

            private int GetNextFreeOffset(int slot)
            {
                var start = (slot - 1) * SlotsPerSet;
                for (var offset = 0; offset < SlotsPerSet; offset++)
                {
                    if (_container.GetSlot(start + offset) == null)
                        return offset;
                }

                return 0;
            }

            private void AddPreview(string shortname, ulong skin, int position)
            {
                if (string.IsNullOrEmpty(shortname) || skin == 0 || position < 0 || position >= EditorCapacity)
                    return;

                var definition = ItemManager.FindItemDefinition(shortname);
                if (definition == null)
                    return;

                var existing = _container.GetSlot(position);
                if (existing != null)
                    RemovePreview(existing);

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

            private int GetSetSlot(int containerPosition)
            {
                if (containerPosition < 0)
                    return 1;

                var slot = (containerPosition / SlotsPerSet) + 1;
                if (slot < 1)
                    return 1;

                return slot > MaxSlots ? MaxSlots : slot;
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
                _container.MarkDirty();
            }
        }
    }
}
