using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Rust;
using UnityEngine;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("Auto Pickup Barrel", "l3rady", "1.3")]
    [Description("Allows players to pick up dropped loot from barrels and road signs on destroy automatically.")]

    public class AutoPickupBarrel : RustPlugin
    {
        private readonly string[] BarrelContainerShortPrefabNames = { "loot_barrel_1", "loot_barrel_2", "loot-barrel-1", "loot-barrel-2", "oil_barrel" };
        private readonly string[] RoadSignContainerShortPrefabNames = { "roadsign1", "roadsign2", "roadsign3", "roadsign4", "roadsign5", "roadsign6", "roadsign7", "roadsign8", "roadsign9" };

        #region Configuration

        private Configuration Settings;

        public class Configuration
        {
            [JsonProperty("Auto pickup distance")]
            public float AutoPickupDistance = 3f;

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }

        protected override void LoadDefaultConfig() => Settings = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                Settings = Config.ReadObject<Configuration>();
                if (Settings == null)
                {
                    throw new JsonException();
                }

                if (!Settings.ToDictionary().Keys.SequenceEqual(Config.ToDictionary(x => x.Key, x => x.Value).Keys))
                {
                    PrintWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch
            {
                PrintWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            PrintWarning($"Configuration changes saved to {Name}.json");
            Config.WriteObject(Settings, true);
        }

        #endregion Configuration

        private void Init()
        {
            permission.RegisterPermission("AutoPickupBarrel.Barrel.On", this);
            permission.RegisterPermission("AutoPickupBarrel.Barrel.NoGibs", this);
            permission.RegisterPermission("AutoPickupBarrel.Barrel.InstaKill", this);
            permission.RegisterPermission("AutoPickupBarrel.RoadSign.On", this);
            permission.RegisterPermission("AutoPickupBarrel.RoadSign.NoGibs", this);
            permission.RegisterPermission("AutoPickupBarrel.RoadSign.InstaKill", this);
        }

        private object OnEntityTakeDamage(LootContainer LootEntContainer, HitInfo HitEntInfo)
        {
            // Check we have something to work with
            if (LootEntContainer == null
                || HitEntInfo == null
            ){
                return null;
            }

            // Check we are targetting barrels/roadsigns
            var LootEntContainerName = LootEntContainer.ShortPrefabName;
            if (LootEntContainerName == null){
                return null;
            }

            var AutoPickupPrefab = "";
            if(IsBarrel(LootEntContainerName)) {
                AutoPickupPrefab = "Barrel";
            } else if(IsRoadSign(LootEntContainerName)) {
                AutoPickupPrefab = "RoadSign";
            } else {
                return null;
            }

            return AutoPickup(LootEntContainer, HitEntInfo, AutoPickupPrefab);
        }

        private bool IsBarrel(string LootEntContainerName) {
            return BarrelContainerShortPrefabNames.Contains(LootEntContainerName);
        }

        private bool IsRoadSign(string LootEntContainerName) {
            return RoadSignContainerShortPrefabNames.Contains(LootEntContainerName);
        }

        private object AutoPickup(LootContainer LootEntContainer, HitInfo HitEntInfo, string AutoPickupPrefab) {
            // Check player has permission
            var player = LootEntContainer.lastAttacker as BasePlayer ?? HitEntInfo.InitiatorPlayer;
            if (player == null
                || !permission.UserHasPermission(player.UserIDString, $"AutoPickupBarrel.{AutoPickupPrefab}.On"))
            {
                return null;
            }

            // Check there is loot in the container
            var lootContainerInventory = LootEntContainer?.inventory;
            if (lootContainerInventory == null)
            {
                return null;
            }

            // Check barrel/roadsign is in range unless configured range is 0
            var lootContainerDistance = Vector2.Distance(player.transform.position, LootEntContainer.transform.position);
            if (Settings.AutoPickupDistance > 0 && lootContainerDistance > Settings.AutoPickupDistance)
            {
                return null;
            }


            // Check if InstaKill allowed or enough damage has been done to kill
            var lootContainerRemainingHealth = LootEntContainer.Health() - HitEntInfo.damageTypes.Total();
            if (!permission.UserHasPermission(player.UserIDString, $"AutoPickupBarrel.{AutoPickupPrefab}.InstaKill")
                && lootContainerRemainingHealth > 0)
            {
                return null;
            }

            // Give player the loot from the barrel/roadsign
            for (int i = lootContainerInventory.itemList.Count - 1; i >= 0; i--)
            {

                // If item is scrap, apply the tea bonus drop logic used by the game to award extra scrap.
                if(lootContainerInventory.itemList[i].info.shortname == "scrap")
                {
                    float num = (player.modifiers != null) ? (1f + player.modifiers.GetValue(global::Modifier.ModifierType.Scrap_Yield, 0f)) : 0f;
                    if (num > 1f)
                    {
                        float num2 = player.modifiers.GetVariableValue(global::Modifier.ModifierType.Scrap_Yield, 0f);
                        float num3 = Mathf.Max((float)lootContainerInventory.itemList[i].amount * num - (float)lootContainerInventory.itemList[i].amount, 0f);

                        num2 += num3;
                        int num4 = 0;
                        if (num2 >= 1f)
                        {
                            num4 = (int)num2;
                            num2 -= (float)num4;
                        }
                        player.modifiers.SetVariableValue(global::Modifier.ModifierType.Scrap_Yield, num2);
                        if (num4 > 0)
                        {
                            lootContainerInventory.itemList[i].amount += num4;
                        }
                    }
                }

                // Place item in player inventory
                player.GiveItem(lootContainerInventory.itemList[i], BaseEntity.GiveItemReason.PickedUp);
            }

            // Check the barrel/roadsign is empty
            if (lootContainerInventory.itemList == null || lootContainerInventory.itemList.Count <= 0)
            {
                NextTick(() =>
                {
                    // Call the OnEntityDeath call back as some plugins hook this to award bonus for breaking barrels.
                    Interface.CallHook("OnEntityDeath", LootEntContainer, HitEntInfo);

                    // Kill the barrel/roadsign with or without gibs depending on permission
                    if (permission.UserHasPermission(player.UserIDString, $"AutoPickupBarrel.{AutoPickupPrefab}.NoGibs"))
                    {
                        LootEntContainer?.Kill();

                    } else {
                        LootEntContainer?.Kill(BaseNetworkable.DestroyMode.Gib);

                    }
                });
            }

            return false;
        }
    }
}
