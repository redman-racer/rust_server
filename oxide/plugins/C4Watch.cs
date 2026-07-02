using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("C4Watch", "Goose22", "1.1.2")]
    [Description("Monitors excessive C4 Placement")]
    internal class C4Watch : CovalencePlugin
    {
        #region Configuration

        private Configuration config;

        public class Configuration
        {
            [JsonProperty("C4 Limit (0 to disable)")]
            public int maxC4 = 0;

            [JsonProperty("Limit RF C4")]
            public bool applytoRF = false;

            [JsonProperty("Limit Regular C4")]
            public bool applytoC4 = false;

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void LoadConfig()

        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new JsonException();
                }

                if (!config.ToDictionary().Keys.SequenceEqual(Config.ToDictionary(x => x.Key, x => x.Value).Keys))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch
            {
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            LogWarning($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        #endregion Configuration

        #region initialization

        private void Init()
        {
            c4prefabId = StringPool.Get(c4prefab);
            permission.RegisterPermission(bypassPermission, this);
        }

        #endregion initialization

        #region Lang
        //Method inspired from Friends by MrBlue
        private void RespondToUser(BasePlayer player, string langMessage, params object[] args)
        {
            if (!player.IsConnected) return;
            string response = GetLang(langMessage, player.UserIDString, args);
            if (response == langMessage) response = GetLang(langMessage, null, args);
            player.SendConsoleCommand("gametip.showtoast_translated", GameTip.Styles.Blue_Normal, _Message, response);
        }
        
        private string GetLang(string langKey, string playerId = null, params object[] args)
        {
            return string.Format(lang.GetMessage(langKey, this, playerId), args);
        }
        
        //Registers the lang for this file
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["MaxReached"] = "<color=red>Max number of C4 Reached ({0})</color>"
            }, this);
        }

        #endregion

        #region Stored Data

        //C4 Prefab which will be used as a reference
        private readonly string c4prefab = "assets/prefabs/tools/c4/explosive.timed.deployed.prefab";
        
        private readonly string c4Shortname = "explosive.timed";
        
        private readonly string bypassPermission = "c4watch.bypass";

        private readonly string _Message = "timed.explosive.limit";
        //A variable which will ultimately hold the uint of the C4 prefab for use 
        private uint c4prefabId;
        //A collection of player ids mapped to individual's "throw" count, or active C4 entities
        // <string SteamID, int throwcount>
        private Dictionary<string, int> playerData = new Dictionary<string, int>();
        //A temporary collection of currently active C4 entities and their owners
        // <ulong netID, string SteamID>
        public Dictionary<ulong, string> entityDatabase = new Dictionary<ulong, string>();


        #endregion Stored Data

        #region helpers

        //Checks to see if a player's ID is present in the playerData Dictionary
        void playerCheck(string id)
        {
            if(!playerData.ContainsKey(id))
            {
                playerData.Add(id, 0);
            }
        }

        //checks if a player has the bypass permission
        bool hasPerm(IPlayer player)
        {
            return player.HasPermission(bypassPermission);
        }

        //A method that updates the entitydatabase with respect to the active C4 entities on the server
        void updateData(ulong entity, string id, bool add)
        {   
            if(add)
            {
                entityDatabase.Add(entity, id);
            }
            else
            {
                entityDatabase.Remove(entity);
            }
        }

        //Replaces C4 when a player throws one after being over the limit
        void sendNewC4(BasePlayer player, RFTimedExplosive entity, bool rf)
        {
            if(rf)
            {
                Item explo = ItemManager.CreateByName(c4Shortname);
                explo.SetFlag(Item.Flag.IsOn, true);
                explo.instanceData.dataInt = entity.GetFrequency();
                explo.MarkDirty();
                player.inventory.GiveItem(explo);
            }
            else
            {
                player.inventory.GiveItem(ItemManager.CreateByName(c4Shortname));
            }
        }

        //Decides if the player is under or past the set C4 limit
        void limit(BasePlayer player, RFTimedExplosive entity, bool rf)
        {
            playerCheck(player.UserIDString);

            if(playerData[player.UserIDString] >= config.maxC4)
            {
                entity.Kill();  
                RespondToUser(player, "MaxReached", config.maxC4);
                sendNewC4(player, entity, rf);
            }
            else
            {
                updateData(entity.net.ID.Value, player.UserIDString, true);
                playerData[player.UserIDString]++;
            }
        }

        #endregion helpers

        #region hooks
        //When an explosive is thrown, depending on the thrown entity type, maxC4 in config, and the player's permissions 
        //the thrown entity may be allowed to live and added to the entitydatabase, or it will be killed. The player's
        //active entity count will be increased

        void OnExplosiveThrown(BasePlayer player, RFTimedExplosive entity, ThrownWeapon item)
        {
            if (player == null || entity == null || entity.prefabID != c4prefabId || config.maxC4 <= 0 || hasPerm(player.IPlayer)) return;
            
            bool isRf = entity.GetFrequency() > 0; 
            
            if((config.applytoRF && isRf) || (config.applytoC4 && !isRf))
            { 
                limit(player, entity, isRf); 
            }
        }

        //Once an entity dies, if it is a C4 then the entity id will be traced to it's owner
        //and their "active entity" count will be reduced
        void OnEntityKill(TimedExplosive entity)
        {
            if (entity == null || entity.prefabID != c4prefabId || entity.net == null) return;
            
            ulong entityName = entity.net.ID.Value;
            
            if (!entityDatabase.ContainsKey(entityName)) return;
            
            string id = entityDatabase[entityName];
            updateData(entityName, id, false);
            playerData[id]--;
        }

        //when a player disconnects, their id will be removed from the collection.
        //no data is stored beyond a players playtime
        void OnUserDisconnected(IPlayer player)
        {
            if(playerData.ContainsKey(player.Id))
            {
                playerData.Remove(player.Id);
            }
        }

        // When a player connects, they are added to the playerData dictionary
        // this ensures they are consistently and properly tracked
        void OnUserConnected(IPlayer player)
        {
            // Bug fix on 06/09/2024
            // Need to keep track of all players upon joining
            playerCheck(player.Id);
        }

        // GC assistance
        void Unload()
        {
            playerData = null;
            entityDatabase = null;
        }

        #endregion hooks

    }

} 