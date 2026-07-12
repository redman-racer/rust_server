using System;
using System.Reflection;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("No Heli Fire", "Tryhard", "1.3.0")]
    [Description("Optionally removes explosion effects, gibs, and fire from mini, scrap, and attack helicopters.")]
    public class NoHeliFire : RustPlugin
    {
        private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private ConfigData configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Disable minicopter gibs")]
            public bool MinicopterGibs = true;

            [JsonProperty(PropertyName = "Disable minicopter fire")]
            public bool MinicopterFire = true;

            [JsonProperty(PropertyName = "Disable minicopter explosion sound")]
            public bool MinicopterExplosion = true;

            [JsonProperty(PropertyName = "Disable scraphelicopter gibs")]
            public bool ScrapHelicopterGibs = true;

            [JsonProperty(PropertyName = "Disable scraphelicopter explosion sound")]
            public bool ScrapHelicopterExplosion = true;

            // Keep the original key (including its trailing space) for config compatibility.
            [JsonProperty(PropertyName = "Disable scraphelicopter fire ")]
            public bool ScrapHelicopterFire = true;

            [JsonProperty(PropertyName = "Disable attackhelicopter gibs")]
            public bool AttackHelicopterGibs = true;

            [JsonProperty(PropertyName = "Disable attackhelicopter explosion sound")]
            public bool AttackHelicopterExplosion = true;

            // Keep the original key (including its trailing space) for config compatibility.
            [JsonProperty(PropertyName = "Disable attackhelicopter fire ")]
            public bool AttackHelicopterFire = true;
        }

        protected override void LoadDefaultConfig()
        {
            configData = new ConfigData();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                configData = Config.ReadObject<ConfigData>();
                if (configData == null)
                {
                    throw new JsonException("Configuration deserialized as null.");
                }
            }
            catch (Exception exception)
            {
                PrintWarning($"Invalid configuration; defaults were loaded. Error: {exception.Message}");
                LoadDefaultConfig();
            }

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(configData, true);
        }

        private void OnServerInitialized()
        {
            foreach (BaseNetworkable entity in BaseNetworkable.serverEntities)
            {
                ApplySettings(entity);
            }
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            ApplySettings(entity);
        }

        private void ApplySettings(BaseNetworkable entity)
        {
            if (entity == null || entity.IsDestroyed || configData == null)
            {
                return;
            }

            bool disableExplosion;
            bool disableFire;
            bool disableGibs;

            if (!TryGetSettings(entity, out disableExplosion, out disableFire, out disableGibs))
            {
                return;
            }

            if (disableExplosion)
            {
                DisableEffectMember(entity, "explosionEffect");
            }

            if (disableFire)
            {
                DisableEffectMember(entity, "fireBall", "fireball");
            }

            if (disableGibs)
            {
                DisableEffectMember(entity, "serverGibs");
            }
        }

        private bool TryGetSettings(BaseNetworkable entity, out bool explosion, out bool fire, out bool gibs)
        {
            explosion = false;
            fire = false;
            gibs = false;

            string prefabName = entity.PrefabName ?? string.Empty;
            string typeName = entity.GetType().Name;

            if (Contains(prefabName, "minicopter.entity.prefab") || EqualsIgnoreCase(typeName, "Minicopter"))
            {
                explosion = configData.MinicopterExplosion;
                fire = configData.MinicopterFire;
                gibs = configData.MinicopterGibs;
                return true;
            }

            if (Contains(prefabName, "scraptransporthelicopter.prefab") || EqualsIgnoreCase(typeName, "ScrapTransportHelicopter"))
            {
                explosion = configData.ScrapHelicopterExplosion;
                fire = configData.ScrapHelicopterFire;
                gibs = configData.ScrapHelicopterGibs;
                return true;
            }

            if (Contains(prefabName, "attackhelicopter.entity.prefab") || EqualsIgnoreCase(typeName, "AttackHelicopter"))
            {
                explosion = configData.AttackHelicopterExplosion;
                fire = configData.AttackHelicopterFire;
                gibs = configData.AttackHelicopterGibs;
                return true;
            }

            return false;
        }

        private static bool Contains(string value, string expected)
        {
            return value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool EqualsIgnoreCase(string value, string expected)
        {
            return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }

        private void DisableEffectMember(object target, params string[] memberNames)
        {
            Type targetType = target.GetType();

            foreach (string memberName in memberNames)
            {
                FieldInfo field = FindField(targetType, memberName);
                if (field != null)
                {
                    object value = field.GetValue(target);
                    object disabledValue = DisableReferenceValue(value, field.FieldType);
                    field.SetValue(target, disabledValue);
                    return;
                }

                PropertyInfo property = FindProperty(targetType, memberName);
                if (property != null && property.CanRead && property.CanWrite)
                {
                    object value = property.GetValue(target, null);
                    object disabledValue = DisableReferenceValue(value, property.PropertyType);
                    property.SetValue(target, disabledValue, null);
                    return;
                }
            }
        }

        private static object DisableReferenceValue(object value, Type valueType)
        {
            if (value != null)
            {
                Type runtimeType = value.GetType();
                FieldInfo guidField = FindField(runtimeType, "guid");
                if (guidField != null)
                {
                    guidField.SetValue(value, null);
                    return value;
                }

                PropertyInfo guidProperty = FindProperty(runtimeType, "guid");
                if (guidProperty != null && guidProperty.CanWrite)
                {
                    guidProperty.SetValue(value, null, null);
                    return value;
                }
            }

            return valueType.IsValueType ? Activator.CreateInstance(valueType) : null;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(name, MemberFlags);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            return null;
        }

        private static PropertyInfo FindProperty(Type type, string name)
        {
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(name, MemberFlags);
                if (property != null)
                {
                    return property;
                }

                type = type.BaseType;
            }

            return null;
        }
    }
}