using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Beds Cooldowns", "Orange / Updated by OpenAI", "1.2.0")]
    [Description("Changes respawn cooldowns and initial unlock times for sleeping bags and beds by permission.")]
    public class BedsCooldowns : RustPlugin
    {
        private PluginConfig _config;

        #region Oxide Hooks

        private void Init()
        {
            RegisterPermissions();
        }

        private void OnServerInitialized()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                RefreshPlayerBags(player.UserIDString);
            }
        }

        private void OnEntitySpawned(SleepingBag sleepingBag)
        {
            if (sleepingBag == null || sleepingBag.IsDestroyed)
            {
                return;
            }

            // Owner information and the vanilla unlock time may be assigned later in the
            // spawn process, so apply our values on the next server tick.
            NextTick(() =>
            {
                if (sleepingBag == null || sleepingBag.IsDestroyed || sleepingBag.OwnerID == 0)
                {
                    return;
                }

                ApplySettings(sleepingBag, GetSettings(sleepingBag.OwnerID.ToString()));
            });
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player != null)
            {
                RefreshPlayerBags(player.UserIDString);
            }
        }

        private void OnUserPermissionGranted(string userId, string permissionName)
        {
            if (IsConfiguredPermission(permissionName))
            {
                RefreshPlayerBags(userId);
            }
        }

        private void OnUserPermissionRevoked(string userId, string permissionName)
        {
            if (IsConfiguredPermission(permissionName))
            {
                RefreshPlayerBags(userId);
            }
        }

        private void OnUserGroupAdded(string userId, string groupName)
        {
            RefreshPlayerBags(userId);
        }

        private void OnUserGroupRemoved(string userId, string groupName)
        {
            RefreshPlayerBags(userId);
        }

        #endregion

        #region Core

        private void RegisterPermissions()
        {
            foreach (SettingsEntry entry in _config.List)
            {
                if (!string.IsNullOrWhiteSpace(entry.Permission))
                {
                    permission.RegisterPermission(entry.Permission, this);
                }
            }
        }

        private bool IsConfiguredPermission(string permissionName)
        {
            return !string.IsNullOrEmpty(permissionName) &&
                   _config.List.Any(entry => string.Equals(entry.Permission, permissionName, StringComparison.OrdinalIgnoreCase));
        }

        private void RefreshPlayerBags(string userId)
        {
            if (!ulong.TryParse(userId, out ulong playerId))
            {
                return;
            }

            SettingsEntry settings = GetSettings(userId);
            if (settings == null)
            {
                return;
            }

            // Copy the collection because entities can be removed while it is enumerated.
            SleepingBag[] bags = SleepingBag.sleepingBags.ToArray();
            foreach (SleepingBag sleepingBag in bags)
            {
                if (sleepingBag != null && !sleepingBag.IsDestroyed && sleepingBag.OwnerID == playerId)
                {
                    ApplySettings(sleepingBag, settings);
                }
            }
        }

        private void ApplySettings(SleepingBag sleepingBag, SettingsEntry settings)
        {
            if (sleepingBag == null || settings == null)
            {
                return;
            }

            bool isBed = sleepingBag.ShortPrefabName.IndexOf("bed", StringComparison.OrdinalIgnoreCase) >= 0;
            float cooldown = Mathf.Max(0f, isBed ? settings.BedCooldown : settings.SleepingBagCooldown);
            float unlockDelay = Mathf.Max(0f, isBed ? settings.BedUnlockTime : settings.SleepingBagUnlockTime);

            sleepingBag.secondsBetweenReuses = cooldown;
            sleepingBag.unlockTime = Time.realtimeSinceStartup + unlockDelay;
            sleepingBag.SendNetworkUpdate();
        }

        private SettingsEntry GetSettings(string userId)
        {
            SettingsEntry selected = null;
            int selectedPriority = int.MinValue;

            foreach (SettingsEntry entry in _config.List)
            {
                if (string.IsNullOrWhiteSpace(entry.Permission) ||
                    !permission.UserHasPermission(userId, entry.Permission) ||
                    entry.Priority <= selectedPriority)
                {
                    continue;
                }

                selected = entry;
                selectedPriority = entry.Priority;
            }

            return selected;
        }

        #endregion

        #region Configuration

        private sealed class PluginConfig
        {
            [JsonProperty(PropertyName = "List")]
            public List<SettingsEntry> List = new List<SettingsEntry>();
        }

        private sealed class SettingsEntry
        {
            [JsonProperty(PropertyName = "Permission")]
            public string Permission = string.Empty;

            [JsonProperty(PropertyName = "Priority")]
            public int Priority;

            [JsonProperty(PropertyName = "Sleeping bag cooldown")]
            public float SleepingBagCooldown;

            [JsonProperty(PropertyName = "Bed cooldown")]
            public float BedCooldown;

            [JsonProperty(PropertyName = "Sleeping bag unlock time")]
            public float SleepingBagUnlockTime;

            [JsonProperty(PropertyName = "Bed unlock time")]
            public float BedUnlockTime;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig
            {
                List = new List<SettingsEntry>
                {
                    new SettingsEntry
                    {
                        Permission = "bedscooldowns.vip1",
                        Priority = 1,
                        SleepingBagCooldown = 100f,
                        BedCooldown = 100f,
                        SleepingBagUnlockTime = 50f,
                        BedUnlockTime = 50f
                    },
                    new SettingsEntry
                    {
                        Permission = "bedscooldowns.vip2",
                        Priority = 2,
                        SleepingBagCooldown = 75f,
                        BedCooldown = 75f,
                        SleepingBagUnlockTime = 50f,
                        BedUnlockTime = 50f
                    },
                    new SettingsEntry
                    {
                        Permission = "bedscooldowns.vip3",
                        Priority = 3,
                        SleepingBagCooldown = 0f,
                        BedCooldown = 0f,
                        SleepingBagUnlockTime = 50f,
                        BedUnlockTime = 50f
                    }
                }
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null || _config.List == null)
                {
                    throw new JsonException("The configuration is empty or missing the List property.");
                }
            }
            catch (Exception exception)
            {
                PrintWarning($"Could not read the configuration; a new default configuration will be created.\n{exception.Message}");
                LoadDefaultConfig();
            }

            ValidateConfig();
            SaveConfig();
        }

        private void ValidateConfig()
        {
            _config.List.RemoveAll(entry => entry == null || string.IsNullOrWhiteSpace(entry.Permission));

            foreach (SettingsEntry entry in _config.List)
            {
                entry.Permission = entry.Permission.Trim().ToLowerInvariant();
                entry.SleepingBagCooldown = Mathf.Max(0f, entry.SleepingBagCooldown);
                entry.BedCooldown = Mathf.Max(0f, entry.BedCooldown);
                entry.SleepingBagUnlockTime = Mathf.Max(0f, entry.SleepingBagUnlockTime);
                entry.BedUnlockTime = Mathf.Max(0f, entry.BedUnlockTime);
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        #endregion
    }
}
