using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Dropped Item Despawn", "Raidlands", "1.0.0")]
    [Description("Applies category-based despawn times to individually dropped items.")]
    public class DroppedItemDespawn : RustPlugin
    {
        private PluginConfig _config;

        private static readonly HashSet<string> ExplosiveShortNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explosive.timed",
            "explosive.satchel",
            "explosives",
            "surveycharge",
            "grenade.beancan",
            "grenade.f1"
        };

        private void OnItemDropped(Item item, BaseEntity entity)
        {
            if (item == null || item.info == null || entity == null || entity.IsDestroyed || !(entity is DroppedItem))
                return;

            var lifetime = GetLifetime(item.info);
            NextTick(() => ApplyLifetime(entity, lifetime));
        }

        private void ApplyLifetime(BaseEntity entity, float lifetime)
        {
            if (entity == null || entity.IsDestroyed)
                return;

            entity.CancelInvoke(entity.KillMessage);
            entity.Invoke(entity.KillMessage, Math.Max(1f, lifetime));
        }

        private float GetLifetime(ItemDefinition definition)
        {
            if (IsExplosive(definition.shortname))
                return _config.AmmunitionAndExplosivesSeconds;

            switch (definition.category)
            {
                case ItemCategory.Resources:
                case ItemCategory.Component:
                    return _config.ResourcesAndComponentsSeconds;

                case ItemCategory.Ammunition:
                    return _config.AmmunitionAndExplosivesSeconds;

                case ItemCategory.Weapon:
                case ItemCategory.Attire:
                case ItemCategory.Tool:
                    return _config.WeaponsArmourAndToolsSeconds;
            }

            return _config.DefaultSeconds;
        }

        private static bool IsExplosive(string shortName)
        {
            if (string.IsNullOrEmpty(shortName))
                return false;

            return ExplosiveShortNames.Contains(shortName)
                || shortName.StartsWith("ammo.rocket.", StringComparison.OrdinalIgnoreCase)
                || shortName.StartsWith("ammo.grenadelauncher.", StringComparison.OrdinalIgnoreCase);
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
                _config = Config.ReadObject<PluginConfig>() ?? new PluginConfig();
            }
            catch
            {
                PrintWarning("Invalid configuration; using default despawn times.");
                _config = new PluginConfig();
            }

            _config.Validate();
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private class PluginConfig
        {
            [JsonProperty("Resources and components (seconds)")]
            public float ResourcesAndComponentsSeconds = 60f;

            [JsonProperty("Ammunition and explosives (seconds)")]
            public float AmmunitionAndExplosivesSeconds = 180f;

            [JsonProperty("Weapons, armour and tools (seconds)")]
            public float WeaponsArmourAndToolsSeconds = 300f;

            [JsonProperty("Everything else (seconds)")]
            public float DefaultSeconds = 180f;

            public void Validate()
            {
                ResourcesAndComponentsSeconds = Math.Max(1f, ResourcesAndComponentsSeconds);
                AmmunitionAndExplosivesSeconds = Math.Max(1f, AmmunitionAndExplosivesSeconds);
                WeaponsArmourAndToolsSeconds = Math.Max(1f, WeaponsArmourAndToolsSeconds);
                DefaultSeconds = Math.Max(1f, DefaultSeconds);
            }
        }
    }
}
