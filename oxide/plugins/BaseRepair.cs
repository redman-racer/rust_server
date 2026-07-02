using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using ProtoBuf;
using UnityEngine;

namespace Oxide.Plugins;

[Info("Base Repair", "MJSU", "1.0.28")]
[Description("Allows player to repair their entire base")]
internal class BaseRepair : RustPlugin
{
    #region Class Fields

    [PluginReference] private Plugin NoEscape, RaidBlock;

    private StoredData _storedData; //Plugin Data
    private PluginConfig _pluginConfig; //Plugin Config

    private const string UsePermission = "baserepair.use";
    private const string NoCostPermission = "baserepair.nocost";
    private const string NoAuthPermission = "baserepair.noauth";
    private const string AccentColor = "#de8732";

    private readonly List<ulong> _repairingPlayers = new();
    private readonly ItemAmountPool _itemAmountPool = new();
    private readonly StringBuilder _sb = new();

    private GameObject _go;
    private RepairBehavior _rb;

    private readonly object _true = true;
    #endregion

    #region Setup & Loading
    private void Init()
    {
        _storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
        permission.RegisterPermission(UsePermission, this);
        permission.RegisterPermission(NoCostPermission, this);
        permission.RegisterPermission(NoAuthPermission, this);
        foreach (string command in _pluginConfig.ChatCommands)
        {
            cmd.AddChatCommand(command, this, BaseRepairChatCommand);
        }

        UnsubscribeAll();
    }

    protected override void LoadDefaultMessages()
    {
        lang.RegisterMessages(new Dictionary<string, string>
        {
            [LangKeys.Chat] = $"<color=#bebebe>[<color={AccentColor}>{Title}</color>] {{0}}</color>",
            [LangKeys.NoPermission] = "You do not have permission to use this command",
            [LangKeys.RepairInProcess] = "You have a current repair in progress. Please wait for that to finish before repairing again",
            [LangKeys.RecentlyDamaged] = "We failed to repair {0} because they were recently damaged",
            [LangKeys.AmountRepaired] = "We have repaired {0} damaged items in this base. ",
            [LangKeys.Enabled] = "You enabled enabled building repair. Hit the building you wish to repair with the hammer and we will do the rest for you.",
            [LangKeys.Disabled] = "You have disabled building repair.",
            [LangKeys.RaidBlockPluginBlocked] = "You are currently raid blocked and cannot base repair this building"
        }, this);
    }

    protected override void LoadDefaultConfig()
    {
        PrintWarning("Loading Default Config");
    }

    protected override void LoadConfig()
    {
        base.LoadConfig();
        Config.Settings.DefaultValueHandling = DefaultValueHandling.Populate;
        _pluginConfig = AdditionalConfig(Config.ReadObject<PluginConfig>());
        Config.WriteObject(_pluginConfig);
    }

    private PluginConfig AdditionalConfig(PluginConfig config)
    {
        config.ChatCommands ??= new List<string>
        {
            "br"
        };
        return config;
    }

    private void OnServerInitialized()
    {
        _go = new GameObject(Name);
        _rb = _go.AddComponent<RepairBehavior>();
            
        SubscribeAll();
    }

    private void Unload()
    {
        if (_rb)
        {
            _rb.StopAllCoroutines();
            _rb.DoDestroy();
        }
           
        GameObject.Destroy(_go);
    }
    #endregion

    #region Chat Command
    private void BaseRepairChatCommand(BasePlayer player, string cmd, string[] args)
    {
        if (!player.IsAdmin && !HasPermission(player, UsePermission))
        {
            Chat(player, Lang(LangKeys.NoPermission, player));
            return;
        }

        bool enabled = !_storedData.RepairEnabled[player.userID];
        _storedData.RepairEnabled[player.userID] = enabled;

        Chat(player, enabled ? Lang(LangKeys.Enabled, player) : Lang(LangKeys.Disabled, player));
        SaveData();
    }
    #endregion

    #region Oxide Hooks
    private object OnHammerHit(BasePlayer player, HitInfo info)
    {
        BaseCombatEntity entity = info?.HitEntity as BaseCombatEntity;
        if (entity == null || entity.IsDestroyed)
        {
            return null;
        }
            
        if (entity is BaseVehicle or ConstructableEntity)
        {
            return null;
        }

        if (!CanRepair(player))
        {
            return null;
        }

        if (IsRepairBlocked(player))
        {
            return null;
        }

        if (_repairingPlayers.Contains(player.userID))
        {
            Chat(player, Lang(LangKeys.RepairInProcess, player));
            return _true;
        }

        bool hasNoAuth = HasPermission(player, NoAuthPermission);
            
        BuildingPrivlidge priv = player.GetBuildingPrivilege();
        if (priv && !hasNoAuth && !priv.IsAuthed(player))
        {
            return null;
        }
            
        BuildingManager.Building building = null;
        if (entity is DecayEntity decayEntity)
        {
            building = decayEntity.GetBuilding();
        }
            
        if (building == null)
        {
            if (!priv)
            {
                return null;
            }
                
            building = priv.GetBuilding();
            if (building == null)
            {
                return null;
            }
        }
            
        priv = building.GetDominatingBuildingPrivilege();
        if (!priv && !_pluginConfig.AllowNoTcRepair)
        {
            return null;
        }
            
        if (priv && !hasNoAuth && !priv.IsAuthed(player))
        {
            return null;
        }

        PlayerRepairStats stats = new();

        if (Interface.CallHook("OnBaseRepair", building, player) != null)
        {
            return null;
        }
            
        _rb.StartCoroutine(DoBuildingRepair(player, building, stats));
        return _true;
    }
    
    public bool CanRepair(BasePlayer player)
    {
        if (!HasPermission(player, UsePermission))
        {
            return false;
        }

        if (_pluginConfig.EnableHammerSkin && player.GetActiveItem()?.skin == _pluginConfig.HammerSkinId)
        {
            return true;
        }

        if (_storedData.RepairEnabled.TryGetValue(player.userID, out bool enabled))
        {
            return enabled;
        }

        return _pluginConfig.DefaultEnabled;
    }
    
    public bool IsRepairBlocked(BasePlayer player)
    {
        if (Interface.Call("CanBaseRepair", player) is bool canRepair)
        {
            return canRepair;
        }
        
        if (IsPluginLoaded(NoEscape) && NoEscape.Call("CanDo", "repair", player) is string result && !string.IsNullOrEmpty(result))
        {
            Chat(player, result);
            return true;
        }

        if (IsPluginLoaded(RaidBlock) && RaidBlock.Call("IsBlocked", player) is true)
        {
            Chat(player, Lang(LangKeys.RaidBlockPluginBlocked));
            return true;
        }

        return false;
    }

    #endregion

    #region Repair Handler

    private IEnumerator DoBuildingRepair(BasePlayer player, BuildingManager.Building building, PlayerRepairStats stats)
    {
        _repairingPlayers.Add(player.userID);
        bool noCostPerm = HasPermission(player, NoCostPermission);
            
        for (int index = 0; index < building.decayEntities.Count; index++)
        {
            DecayEntity entity = building.decayEntities[index];
            DoRepair(player, entity, stats, noCostPerm);

            for (int i = 0; i < entity.children.Count; i++)
            {
                BaseEntity childEntity = entity.children[i];
                if (childEntity is BaseLadder ladder)
                {
                    DoRepair(player, ladder, stats, noCostPerm);
                }
            }

            if (index % _pluginConfig.RepairsPerFrame == 0)
            {
                yield return null;
            }
        }

        _sb.Clear();
        _sb.AppendLine(Lang(LangKeys.AmountRepaired, player, stats.TotalSuccess));

        if (stats.RecentlyDamaged > 0)
        {
            _sb.AppendLine(Lang(LangKeys.RecentlyDamaged, player, stats.RecentlyDamaged));
        }

        Chat(player, _sb.ToString());

        if (stats.TotalCantAfford > 0)
        {
            List<ItemAmount> missingAmounts = Pool.Get<List<ItemAmount>>();
            foreach (KeyValuePair<int, ItemAmount> missing in stats.MissingAmounts)
            {
                float amountMissing = missing.Value.amount - player.inventory.GetAmount(missing.Key);
                if (amountMissing <= 0)
                {
                    ItemAmount amount = missing.Value;
                    _itemAmountPool.Free(ref amount);
                    continue;
                }

                missingAmounts.Add(missing.Value);
            }
                
            SendMissingItemAmounts(player, missingAmounts);
            FreeItemAmounts(missingAmounts);
        }

        foreach (KeyValuePair<int, int> taken in stats.AmountTaken)
        {
            player.Command("note.inv", taken.Key, -taken.Value);
        }

        _repairingPlayers.Remove(player.userID);
    }

    private void DoRepair(BasePlayer player, BaseCombatEntity entity, PlayerRepairStats stats, bool noCost)
    {
        if (!entity.IsValid() || entity.IsDestroyed)
        {
            return;
        }

        if (!entity.repair.enabled || entity.health == entity.MaxHealth())
        {
            return;
        }

        if (Interface.CallHook("OnStructureRepair", entity, player) != null)
        {
            return;
        }

        if (entity.SecondsSinceAttacked <= _pluginConfig.EntityRepairDelay)
        {
            entity.OnRepairFailed(null, string.Empty);
            stats.RecentlyDamaged++;
            return;
        }

        float missingHealth = entity.MaxHealth() - entity.health;
        float healthPercentage = missingHealth / entity.MaxHealth();
        if (missingHealth <= 0f || healthPercentage <= 0f)
        {
            entity.OnRepairFailed(null, string.Empty);
            return;
        }

        if (!noCost)
        {
            List<ItemAmount> itemAmounts = Pool.Get<List<ItemAmount>>();
            GetEntityRepairCost(entity, itemAmounts, healthPercentage);
            if (!HasRepairCost(itemAmounts))
            {
                entity.health += missingHealth;
                entity.SendNetworkUpdate();
                entity.OnRepairFinished(player);
                FreeItemAmounts(itemAmounts);
                return;
            }

            if (Math.Abs(_pluginConfig.RepairCostMultiplier - 1f) > 0.001f)
            {
                foreach (ItemAmount amount in itemAmounts)
                {
                    amount.amount *= _pluginConfig.RepairCostMultiplier;
                }
            }

            if (!CanAffordRepair(player, itemAmounts))
            {
                entity.OnRepairFailed(null, string.Empty);
                    
                foreach (ItemAmount amount in itemAmounts)
                {
                    ItemAmount missing = stats.MissingAmounts[amount.itemid];
                    if (missing == null)
                    {
                        missing = _itemAmountPool.Get();
                        missing.itemDef = amount.itemDef;
                        missing.amount = amount.amount;
                        stats.MissingAmounts[amount.itemid] = missing;
                        continue;
                    }

                    missing.amount += amount.amount;
                }

                stats.TotalCantAfford++;
                FreeItemAmounts(itemAmounts);
                return;
            }

            List<Item> items = Pool.Get<List<Item>>();
            foreach (ItemAmount amount in itemAmounts)
            {
                player.inventory.Take(items, amount.itemid, (int) amount.amount);
                stats.AmountTaken[amount.itemid] += (int) amount.amount;
            }

            for (int index = 0; index < items.Count; index++)
            {
                Item item = items[index];
                item.Remove();
            }

            Pool.FreeUnmanaged(ref items);
            FreeItemAmounts(itemAmounts);
        }

        entity.health += missingHealth;
        entity.SendNetworkUpdate();

        if (entity.health < entity.MaxHealth())
        {
            entity.OnRepair();
        }
        else
        {
            entity.OnRepairFinished(player);
        }

        stats.TotalSuccess++;
    }

    public bool CanAffordRepair(BasePlayer player, List<ItemAmount> amounts)
    {
        for (int index = 0; index < amounts.Count; index++)
        {
            ItemAmount amount = amounts[index];
            if (player.inventory.GetAmount(amount.itemid) < amount.amount)
            {
                return false;
            }
        }

        return true;
    }

    public void GetEntityRepairCost(BaseCombatEntity entity, List<ItemAmount> repairAmounts, float missingHealthFraction)
    {
        List<ItemAmount> entityAmount = entity.BuildCost();
        if (entityAmount == null)
        {
            return;
        }
            
        float repairCostFraction = entity.RepairCostFraction();
        for (int index = 0; index < entityAmount.Count; index++)
        {
            ItemAmount itemAmount = entityAmount[index];
                
            if (entity.repair.ignoreForRepair && itemAmount.itemDef.itemid == entity.repair.ignoreForRepair.itemid)
            {
                continue;
            }
                
            int amount = Mathf.RoundToInt(itemAmount.amount * repairCostFraction * missingHealthFraction);
            if (amount > 0)
            {
                ItemAmount repairAmount = _itemAmountPool.Get();
                repairAmount.itemDef = itemAmount.itemDef;
                repairAmount.amount = amount;
                repairAmounts.Add(repairAmount);
            }
        }
    }

    public bool HasRepairCost(List<ItemAmount> amounts)
    {
        for (int index = 0; index < amounts.Count; index++)
        {
            ItemAmount amount = amounts[index];
            if (amount.amount >= 1)
            {
                return true;
            }
        }

        return false;
    }

    public void FreeItemAmounts(List<ItemAmount> amounts)
    {
        for (int index = 0; index < amounts.Count; index++)
        {
            ItemAmount amount = amounts[index];
            _itemAmountPool.Free(ref amount);
        }
            
        Pool.FreeUnmanaged(ref amounts);
    }
    #endregion

    #region Helper Methods
    public void SendMissingItemAmounts(BasePlayer player, List<ItemAmount> itemAmounts)
    {
        using ItemAmountList itemAmountList = ItemAmount.SerialiseList(itemAmounts);
        player.ClientRPC(RpcTarget.Player("Client_OnRepairFailedResources", player), itemAmountList);
    }
        
    public void SubscribeAll()
    {
        Subscribe(nameof(OnHammerHit));
    }
        
    public void UnsubscribeAll()
    {
        Unsubscribe(nameof(OnHammerHit));
    }
    
    public bool IsPluginLoaded(Plugin plugin) => plugin is { IsLoaded: true };

    private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _storedData);

    private void Chat(BasePlayer player, string format) => PrintToChat(player, Lang(LangKeys.Chat, player, format));

    private bool HasPermission(BasePlayer player, string perm) => permission.UserHasPermission(player.UserIDString, perm);

    private string Lang(string key, BasePlayer player = null)
    {
        return lang.GetMessage(key, this, player?.UserIDString);
    }
        
    private string Lang(string key, BasePlayer player = null, params object[] args)
    {
        try
        {
            return string.Format(lang.GetMessage(key, this, player?.UserIDString), args);
        }
        catch (Exception ex)
        {
            PrintError($"Lang Key '{key}' threw exception\n:{ex}");
            throw;
        }
    }

    #endregion

    #region Behavior
    private class RepairBehavior : FacepunchBehaviour
    {
        private void Awake()
        {
            enabled = false;
        }

        public void DoDestroy()
        {
            Destroy(this);
        }
    }
    #endregion
        
    #region Classes

    private class PluginConfig
    {
        [DefaultValue(10)]
        [JsonProperty(PropertyName = "Number of entities to repair per server frame")]
        public int RepairsPerFrame { get; set; }

        [DefaultValue(false)]
        [JsonProperty(PropertyName = "Default Enabled")]
        public bool DefaultEnabled { get; set; }
            
        [DefaultValue(false)]
        [JsonProperty(PropertyName = "Allow Repairing Bases Without A Tool Cupboard")]
        public bool AllowNoTcRepair { get; set; }

        [DefaultValue(1f)]
        [JsonProperty(PropertyName = "Repair Cost Multiplier")]
        public float RepairCostMultiplier { get; set; }
            
        [DefaultValue(30f)]
        [JsonProperty(PropertyName = "How long after an entity is damaged before it can be repaired (Seconds)")]
        public float EntityRepairDelay { get; set; }

        [JsonProperty(PropertyName = "Chat Commands")]
        public List<string> ChatCommands { get; set; }
            
        [JsonProperty(PropertyName = "Enable Repairs Using A Skinned Hammer")]
        public bool EnableHammerSkin { get; set; }
            
        [DefaultValue(2902701361)]
        [JsonProperty(PropertyName = "Repair Hammer Skin ID")]
        public ulong HammerSkinId { get; set; }
    }

    private class StoredData
    {
        public Hash<ulong, bool> RepairEnabled = new();
    }

    private class PlayerRepairStats
    {
        public int TotalSuccess { get; set; }
        public int TotalCantAfford { get; set; }
        public int RecentlyDamaged { get; set; }
        public Hash<int, ItemAmount> MissingAmounts { get; } = new();
        public Hash<int, int> AmountTaken { get; } = new();
    }

    private class LangKeys
    {
        public const string Chat = "Chat";
        public const string NoPermission = "NoPermission";
        public const string RepairInProcess = "RepairInProcess";
        public const string RecentlyDamaged = "RecentlyDamaged";
        public const string AmountRepaired = "AmountRepaired";
        public const string Enabled = "Enabled";
        public const string Disabled = "Disabled";
        public const string RaidBlockPluginBlocked = "RaidBlockPlugin.Blocked";
    }

    #endregion

    #region Pool
    private class BasePool<T> where T : class
    {
        protected readonly List<T> Pool = new();
        protected readonly Func<T> Init;

        public BasePool(Func<T> init)
        {
            Init = init;
        }
            
        public virtual T Get()
        {
            if (Pool.Count == 0)
            {
                return Init.Invoke();
            }

            int index = Pool.Count - 1; //Removing the last element prevents an array copy.
            T entity = Pool[index];
            Pool.RemoveAt(index);
                
            return entity;
        }

        public virtual void Free(ref T entity)
        {
            Pool.Add(entity);
            entity = null;
        }
    }
        
    private class ItemAmountPool : BasePool<ItemAmount>
    {
        public ItemAmountPool() : base(() => new ItemAmount())
        {
        }
            
        public override void Free(ref ItemAmount ia)
        {
            ia.itemDef = null;
            ia.amount = 0;
            ia.startAmount = 0;
            base.Free(ref ia);
        }
    }
    #endregion
}