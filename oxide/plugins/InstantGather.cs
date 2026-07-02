using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Instant Gather", "supreme", "1.0.9")]
    [Description("Enhances the tools used for mining in order to gather instantly")]
    public class InstantGather : RustPlugin
    {
        #region Class Fields

        private Configuration _pluginConfig;
        private const string UsePermission = "instantgather.use";
        private const string SalvagedAxeShortname = "axe.salvaged";
        private const string ChainsawShortname = "chainsaw";
        private const string JackhammerShortname = "jackhammer";

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(UsePermission, this);
        }
        
        private void OnMeleeAttack(BasePlayer player, HitInfo hitInfo)
        {
            if (!permission.UserHasPermission(player.UserIDString, UsePermission) || !hitInfo.HitEntity)
            {
                return;
            }

            Item activeItem = player.GetActiveItem();
            if (activeItem == null)
            {
                return;
            }

            if (!_pluginConfig.Tools.Contains(activeItem.info.shortname))
            {
                return;
            }

            BaseEntity entity = hitInfo.HitEntity;
            if (!entity)
            {
                return;
            }
            
            OreResourceEntity ore = entity as OreResourceEntity;
            if (ore)
            {
                MineOre(ore, player, hitInfo, activeItem);
                return;
            }
    
            TreeEntity tree = entity as TreeEntity;
            if (tree)
            {
                ChopTree(tree, player, hitInfo, activeItem);
            }
        }

        #endregion

        #region Core Methods

        private void ChopTree(TreeEntity tree, BasePlayer player, HitInfo hitInfo, Item activeItem)
        {
            BaseMelee baseMelee = player.GetHeldEntity() as BaseMelee;
            if (!baseMelee)
            {
                return;
            }

            hitInfo.gatherScale = 100f;
            if (activeItem.info.shortname != SalvagedAxeShortname && activeItem.info.shortname != ChainsawShortname)
            {
                tree.resourceDispenser.finishBonus[0].amount += 3;
                tree.resourceDispenser.AssignFinishBonus(player, 1f - baseMelee.GetGatherInfoFromIndex(tree.resourceDispenser.gatherType).destroyFraction, hitInfo.Weapon);
            }

            NextFrame(() =>
            {
                if (!tree)
                {
                    return;
                }
                
                tree.Kill(BaseNetworkable.DestroyMode.Gib);
            });
        }

        private void MineOre(OreResourceEntity ore, BasePlayer player, HitInfo hitInfo, Item activeItem)
        {
            BaseMelee baseMelee = player.GetHeldEntity() as BaseMelee;
            if (!baseMelee)
            {
                return;
            }
            
            hitInfo.gatherScale = 100f;
            if (activeItem.info.shortname == JackhammerShortname)
            {
                for (int i = 0; i < 37; i++)
                {
                    ore.OnAttacked(hitInfo);
                }
            }

            NextFrame(() =>
            {
                if (!ore)
                {
                    return;
                }
                
                ore.resourceDispenser.finishBonus[0].amount += 6;
                ore.resourceDispenser.AssignFinishBonus(player, 1f - baseMelee.GetGatherInfoFromIndex(ore.resourceDispenser.gatherType).destroyFraction, hitInfo.Weapon);
                ore.Kill(BaseNetworkable.DestroyMode.Gib);
            });
        }

        #endregion

        #region Configuration

        private class Configuration
        {
            [JsonProperty(PropertyName = "Tools")]
            public List<string> Tools { get; set; }
        }
        
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _pluginConfig = Config.ReadObject<Configuration>();
                if (_pluginConfig == null)
                {
                    throw new Exception();
                }
                
                SaveConfig();
            }
            catch
            {
                PrintError("Your configuration file contains an error. Using default configuration values.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_pluginConfig);

        protected override void LoadDefaultConfig()
        {
            _pluginConfig = new Configuration
            {
                Tools = new List<string>
                {
                    "pickaxe",
                    "jackhammer",
                    "rock",
                    "hammer.salvaged",
                    "icepick.salvaged",
                    "stone.pickaxe",
                    "axe.salvaged",
                    "boneclub",
                    "hatchet",
                    "stonehatchet",
                    "chainsaw"
                }
            };
        }

        #endregion
    }
}