using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Facepunch.Extend;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Entity Limit", "Orange/The Friendly Chap", "2.1.5")]
    [Description("Limit entities per player or building")]
    public class EntityLimit : RustPlugin
    {
        #region Vars
        
        private Coroutine lookup;

        #endregion
        
        #region Oxide Hooks

        private void Init()
        {
            foreach (var value in config.permissions)
            {
                if (string.IsNullOrEmpty(value.permission) == false && permission.PermissionExists(value.permission) == false)
                {
                    permission.RegisterPermission(value.permission, this);
                }
            }

            foreach (var command in config.commands)
            {
                cmd.AddChatCommand(command, this, nameof(cmdControlChat));
            }

            cmd.AddConsoleCommand("elimit.debug", this, nameof(cmdControlConsole));
            timer.Every(Core.Random.Range(config.cacheTime, config.cacheTime + 60), () => { cachePermission.Clear(); });
        }
        
        private void OnServerInitialized()
        {
            timer.Once(3f, () =>
            {
                PrintWarning("Organizing data for plugin, it can take a while, expect small lag");
                CheckAllEntities();
            });
        }

        private void Unload()
        {
            if (lookup != null)
            {
                ServerMgr.Instance.StopCoroutine(lookup);
            }
        }

        private object CanBuild(Planner planner, Construction entity, Construction.Target target)
        {
            return CheckBuild(planner.GetOwnerPlayer(), entity.fullName);
        }

        private void OnEntitySpawned(BaseEntity entity)
        {
            CheckLifeState(entity, false);
        }

        private void OnEntityKill(BaseEntity entity)
        {
            CheckLifeState(entity, true);
        }

        #endregion

        #region Commands

        private void cmdControlConsole(ConsoleSystem.Arg arg)
        {
            if (arg.IsAdmin)
            {
                Interface.Oxide.DataFileSystem.WriteObject("entitylimit_debug", Data);
                SendReply(arg, "Done, check data");
            }
        }

        [ChatCommand("testmessage")]
        private void TestMessage(BasePlayer player)
        {
            SendMessage(player, MessageType.LimitsList, "{global}" + "{building}");
        }

        private void cmdControlChat(BasePlayer player)
        {
            var perm = GetPermissionFromPlayerID(player.UserIDString, config.permissions);
            if (perm == null)
            {
                return;
            }

            var limitsBuilding = perm.limitsBuilding.Clone();
            var limitsGlobal = perm.limitsGlobal.Clone();
            
            var buildingEntities = player.GetBuildingPrivilege()?.GetBuilding()?.decayEntities;
            if (buildingEntities != null)
            {
                foreach (var entity in buildingEntities)
                {
                    if (entity.IsValid() == false)
                    {
                        continue;
                    }
                    
                    if (limitsBuilding.ContainsKey(entity.ShortPrefabName) == true)
                    {
                        limitsBuilding[entity.ShortPrefabName]--;
                    }
                }
            }

            var globalEntities = Data.Get(player, false)?.entities?.SelectMany(x => x.list);
            if (globalEntities != null)
            {
                foreach (var entity in globalEntities)
                {
                    if (entity.IsValid() == false)
                    {
                        continue;
                    }
                
                    if (limitsGlobal.ContainsKey(entity.ShortPrefabName) == true)
                    {
                        limitsGlobal[entity.ShortPrefabName]--;
                    }
                }
            }

            var text1 = string.Empty;
            var text2 = string.Empty;

            foreach (var pair in limitsGlobal)
            {
                text1 += pair.Key + " x" + pair.Value + "\n";
            }
            
            foreach (var pair in limitsBuilding)
            {
                text2 += pair.Key + " x" + pair.Value + "\n";
            }
            
            SendMessage(player, MessageType.LimitsList, "{global}", text1, "{building}", text2);
        }

        #endregion

        #region Core

        private object CheckBuild(BasePlayer player, string fullName)
        {
            var perm = GetPermissionFromPlayerID(player.UserIDString, config.permissions);
            if (perm == null)
            {
                return null;
            }

            var shortname = GetShortname(fullName);
            var cupboard = player.GetBuildingPrivilege();
            var limit = 0;
            var exists = 0;
            var left = 0;
            
            if (cupboard != null) // Check for building limits
            {
                var pair = perm.limitsBuilding.FirstOrDefault(x => SameName(x.Key, shortname, fullName));
                limit = pair.Value;
                if (limit > 0)
                {
                    var entities = player.GetBuildingPrivilege()?.GetBuilding()?.decayEntities;
                    if (entities != null)
                    {
                        exists = pair.Key == "*" ? entities.Count : entities.Count(x => x.IsValid() && x.PrefabName == fullName);
                       
                        left = limit - exists;
                        if (left < 1)
                        {
                            SendMessage(player, MessageType.LimitBuilding, 
                                "{name}", shortname, 
                                "{used}", exists, 
                                "{left}", left, 
                                "{limit}", limit);
                            return true;
                        }
                        else
                        {
                            if (exists % config.warnCount == 0 || left < config.warnCount)
                            {
                                SendMessage(player, MessageType.LimitBuildingWarning, 
                                    "{name}", shortname, 
                                    "{used}", exists + 1, 
                                    "{left}", left - 1, 
                                    "{limit}", limit);
                            }
                        }
                    }
                }
            }
            
            // Check for global limits
            var pairGlobal = perm.limitsGlobal.FirstOrDefault(x => SameName(x.Key, shortname, fullName));
            limit = pairGlobal.Value;
            if (limit > 0)
            {
                var data = Data.Get(player.UserIDString, false);
                if (data == null)
                {
                    return null;
                }

                if (pairGlobal.Key == "*")
                {
                    exists = data.entities.Sum(x => x.count);
                }
                else
                {
                    var entitiesGlobal = data.entities.FirstOrDefault(x => x.prefab == fullName);
                    if (entitiesGlobal == null)
                    {
                        return null;
                    }

                    exists = entitiesGlobal.count;
                }
                
                left = limit - exists;
                if (left < 1)
                {
                    SendMessage(player, MessageType.LimitGlobal, 
                        "{name}", shortname, 
                        "{used}", exists, 
                        "{left}", left, 
                        "{limit}", limit);
                    return true;
                }
                else
                {
                    if (exists % config.warnCount == 0 || left < config.warnCount)
                    {
                        SendMessage(player, MessageType.LimitGlobalWarning, 
                            "{name}", shortname, 
                            "{used}", exists + 1, 
                            "{left}", left - 1, 
                            "{limit}", limit);
                    }
                }
            }
            
            return null;
        }

        private void CheckLifeState(BaseEntity entity, bool dying)
        {
            if (entity.OwnerID.IsSteamId() == false)
            {
                return;
            }

            var owner = entity.OwnerID;
            var prefab = entity.PrefabName;
            
            NextTick(() =>
            {
                if (dying == false && entity.IsValid() == false)
                {
                    return;
                }

                var data = Data.Get(owner, dying == false);
                if (data == null)
                {
                    if (dying == true)
                    {
                        return;
                    }
                        
                    data = new DataEntry();
                    Data.Set(owner, data);
                }

                var eData = data.entities.FirstOrDefault(x => x.prefab == prefab);
                if (eData == null)
                {
                    if (dying == true)
                    {
                        return;
                    }
                        
                    eData = new EntityData();
                    eData.prefab = prefab;
                    data.entities.Add(eData);
                }
                
                if (dying == true)
                {
                    eData.list.Remove(entity);
                }
                else
                {
                    eData.list.Add(entity);
                }
            });
        }

        private static string GetShortname(string original)
        {
            var index = original.LastIndexOf("/", StringComparison.Ordinal) + 1;
            var name = original.Substring(index);
            return name.Replace(".prefab", string.Empty);
        }

        private static bool SameName(string original, string name1, string name2)
        {
            return 
                original == "*" ||
                string.Equals(original, name1, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(original, name2, StringComparison.OrdinalIgnoreCase);
        }

        private void CheckAllEntities()
        {
            if (lookup == null)
            {
                lookup = ServerMgr.Instance.StartCoroutine(LookupEntities());
            }
        }

        private IEnumerator LookupEntities()
        {
            yield return new WaitForEndOfFrame();
            var entities = UnityEngine.Object.FindObjectsOfType<BaseEntity>();
            yield return new WaitForSecondsRealtime(1);
            var total = entities.Length;
            
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (entity.IsValid() == false || entity.OwnerID.IsSteamId() == false)
                {
                    continue;
                }

                var pData = Data.Get(entity.OwnerID, true);
                var eData = pData.entities.FirstOrDefault(x => x.prefab == entity.PrefabName);
                if (eData == null)
                {
                    eData = new EntityData();
                    eData.prefab = entity.PrefabName;
                    pData.entities.Add(eData);
                }

                eData.list.Add(entity);

                if (i > 0 && i % 1000 == 0)
                {
                    Puts($"Organizing data: {i}/{total}");
                    yield return new WaitForEndOfFrame();
                }
            }

            yield return new WaitForSecondsRealtime(1);
            Puts("Entity data was organized!");
        }

        #endregion
        
        #region Permissions Support

        private Dictionary<string, PermissionEntry> cachePermission = new Dictionary<string, PermissionEntry>();
        
        private class PermissionEntry
        {
            public PermissionEntry GetClone()
            {
                return (PermissionEntry) this.MemberwiseClone();
            }
            
            [JsonProperty(PropertyName = "Permission")]
            public string permission;

            [JsonProperty(PropertyName = "Priority")]
            public int priority;

            [JsonProperty(PropertyName = "Limits Global")]
            public Dictionary<string, int> limitsGlobal = new Dictionary<string, int>();

            [JsonProperty(PropertyName = "Limits Building")]
            public Dictionary<string, int> limitsBuilding = new Dictionary<string, int>();
        }
        
        private PermissionEntry GetPermissionFromPlayerID(string playerID, PermissionEntry[] permissions)
        {
            var lastPermission = (PermissionEntry) null;
            if (cachePermission.TryGetValue(playerID, out lastPermission) == true)
            {
                return lastPermission;
            }

            var lastPriority = -1;

            foreach (var value in permissions)
            {
                if (value.priority > lastPriority && permission.UserHasPermission(playerID, value.permission))
                {
                    lastPriority = value.priority;
                    lastPermission = value;
                }
            }

            if (lastPermission != null)
            {
                cachePermission.Add(playerID, lastPermission);
            }
            
            return lastPermission;
        }

        #endregion

        #region Configuration | 2.0.1

        private static ConfigData config = new ConfigData();

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Commands")]
            public string[] commands =
            {
                "buildinglimits", "limits", "limit", "blimit"
            };
            
            [JsonProperty(PropertyName = "Permission cache time (seconds)")]
            public int cacheTime = 700;

            [JsonProperty(PropertyName = "Warn about limits every X entities")]
            public int warnCount = 50;

            [JsonProperty(PropertyName = "Permissions")]
            public PermissionEntry[] permissions =
            {
                new PermissionEntry
                {
                    permission = nameof(EntityLimit) + ".default",
                    priority = 0,
                    limitsGlobal = new Dictionary<string, int>
                    {
                        {"foundation", 50},
                        {"assets/prefabs/building core/roof/roof.prefab", 50},
                        {"assets/prefabs/deployable/furnace/furnace.prefab", 5},
                        {"*", 1000},
                    },
                    limitsBuilding = new Dictionary<string, int>
                    {
                        {"foundation", 25},
                        {"foundation.triangle", 25},
                        {"assets/prefabs/npc/autoturret/autoturret_deployed.prefab", 10}
                    }
                }, 
                new PermissionEntry
                {
                    permission = nameof(EntityLimit) + ".vip",
                    priority = 1,
                    limitsGlobal = new Dictionary<string, int>
                    {
                        {"foundation", 200},
                        {"assets/prefabs/building core/roof/roof.prefab", 200},
                        {"*", 2000},
                    },
                    limitsBuilding = new Dictionary<string, int>
                    {
                        {"foundation", 200},
                        {"foundation.triangle", 200},
                        {"assets/prefabs/npc/autoturret/autoturret_deployed.prefab", 200}
                    }
                }, 
                new PermissionEntry
                {
                    permission = nameof(EntityLimit) + ".nolimit",
                    priority = 999,
                    limitsGlobal = new Dictionary<string, int>
                    {
                        
                    },
                    limitsBuilding = new Dictionary<string, int>
                    {
                        
                    }
                }, 
                new PermissionEntry
                {
                    permission = nameof(EntityLimit) + ".debug",
                    priority = 9999,
                    limitsGlobal = new Dictionary<string, int>
                    {
                        {"foundation", 3},
                        {"*", 15},
                    },
                    limitsBuilding = new Dictionary<string, int>
                    {
                        {"foundation", 2},
                        {"*", 5},
                    }
                }, 
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                for (var i = 0; i < 3; i++)
                {
                    PrintError("Configuration file is corrupt! Check your config file at https://jsonlint.com/");
                }
                
                LoadDefaultConfig();
                return;
            }

            ValidateConfig();
            SaveConfig();
        }

        private static void ValidateConfig()
        {
            
        }

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        #endregion
        
        #region Language | 2.0.1
        
        private Dictionary<object, string> langMessages = new Dictionary<object, string>
        {
            {MessageType.LimitBuilding, "You reached limit at that building! Used: {used}, Limit: {limit}"},
            {MessageType.LimitGlobal, "You reached global limit! Used: {used}, Limit: {limit}"},
            {MessageType.LimitBuildingWarning, "You used {used} that type of building parts in that building, {left} available..."},
            {MessageType.LimitGlobalWarning, "You used {used} that type of building parts in whole world, {left} available..."},
            {MessageType.LimitsList, "Your global limits:\n{global}\nYour building limits:\n{building}"},
        };
        
        private enum MessageType
        {
            LimitsList,
            LimitBuilding,
            LimitBuildingWarning,
            LimitGlobal,
            LimitGlobalWarning,
        }
        
        protected override void LoadDefaultMessages()
        {
            var dictionary = new Dictionary<string, string>();
            foreach (var pair in langMessages)
            {
                var key = pair.Key.ToString();
                var value = pair.Value;
                dictionary.TryAdd(key, value);
            }
            lang.RegisterMessages(dictionary, this, "en");
        }

        private string GetMessage(MessageType key, string playerID = null, params object[] args)
        {
            var message = lang.GetMessage(key.ToString(), this, playerID);
            var organized = OrganizeArgs(args);
            message = ReplaceArgs(message, organized);
            return message;
        }
        
        private static Dictionary<string, object> OrganizeArgs(object[] args)
        {
            var dic = new Dictionary<string, object>();
            for (var i = 0; i < args.Length; i += 2)
            {
                var value = args[i].ToString();
                var nextValue = i + 1 < args.Length ? args[i + 1] : null;
                dic.Add(value, nextValue);
            }

            return dic;
        }

		private static string ReplaceArgs(string message, Dictionary<string, object> args)
		{
			if (args == null || args.Count < 1){return message;}
			
			foreach (var pair in args)
			{
				var s0 = "{" + pair.Key + "}";
				var s1 = pair.Key;
				var s2 = pair.Value != null ? pair.Value.ToString() : "null";
				
				// Case-insensitive replacement
				message = message.Replace(s0, s2, StringComparison.OrdinalIgnoreCase);
				message = message.Replace(s1, s2, StringComparison.OrdinalIgnoreCase);
			}
			return message;
		}


        private void SendMessage(object receiver, MessageType key, params object[] args)
        {
            var userID = (receiver as BasePlayer)?.UserIDString;
            var message = GetMessage(key, userID, args);
            SendMessage(receiver, message);
        }
        
        private void SendMessage(object receiver, string message)
        {
            if (receiver == null)
            {
                Puts(message);
                return;
            }
            
            var console = receiver as ConsoleSystem.Arg;
            if (console != null)
            {
                SendReply(console, message);
                return;
            }
            
            var player = receiver as BasePlayer;
            if (player != null)
            {
                player.ChatMessage(message);
                return;
            }
        }

        #endregion
        
        #region Data | 2.2.0
        
        private static PluginData Data = new PluginData();

        private class DataEntry
        {
            public HashSet<EntityData> entities = new HashSet<EntityData>();
        }
        
        private class EntityData
        {
            public string prefab;
            public int count => list.Count;
            [JsonIgnore] public HashSet<BaseEntity> list = new HashSet<BaseEntity>();
        }
        
        private class PluginData
        {
            /* ### Values ### */
            // ReSharper disable once MemberCanBePrivate.Local
            [JsonProperty] private Dictionary<string, DataEntry> values = new Dictionary<string, DataEntry>();
            [JsonIgnore] private Dictionary<string, DataEntry> cache = new Dictionary<string, DataEntry>();
            
            public DataEntry Get(object param, bool createNewOnMissing)
            {
                var key = GetKeyFrom(param);
                if (string.IsNullOrEmpty(key) == true)
                {
                    return null;
                }
                
                var value = (DataEntry) null;
                if (cache.TryGetValue(key, out value) == true)
                {
                    return value;
                }

                if (values.TryGetValue(key, out value) == false && createNewOnMissing == true)
                {
                    value = new DataEntry();
                    values.Add(key, value);
                }

                if (value != null)
                {
                    cache.TryAdd(key, value);
                }
                
                return value;
            }
            
            public void Set(object param, DataEntry value)
            {
                var key = GetKeyFrom(param);
                if (string.IsNullOrEmpty(key) == true)
                {
                    return;
                }

                if (value == null)
                {
                    if (values.ContainsKey(key) == true)
                    {
                        values.Remove(key);
                    }
                    
                    if (cache.ContainsKey(key) == true)
                    {
                        cache.Remove(key);
                    }
                }
                else
                {
                    if (values.TryAdd(key, value) == false)
                    {
                        values[key] = value;
                    
                        if (cache.ContainsKey(key) == true)
                        {
                            cache[key] = value;
                        }
                    }
                }
            }

            private static string GetKeyFrom(object obj)
            {
                if (obj == null)
                {
                    return null;
                }

                if (obj is string)
                {
                    return obj as string;
                }

                if (obj is BasePlayer)
                {
                    return (obj as BasePlayer).UserIDString;
                }

                if (obj is BaseNetworkable)
                {
                    return (obj as BaseNetworkable).net?.ID.ToString();
                }

                return obj.ToString();
            }
        }

        #endregion
    }
}