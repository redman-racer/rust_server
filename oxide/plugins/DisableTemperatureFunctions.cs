using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Disable Temperature Functions", "The Friendly Chap", "1.1.0")]
    [Description("Prevents cold/heat damage/overlay for players")]
    public class DisableTemperatureFunctions : RustPlugin
    {
        #region ConfigFileStuff
        private ConfigData configData;
        class ConfigData
        {
            [JsonProperty(PropertyName = "Debug Mode")]
            public bool debug = false;
            [JsonProperty(PropertyName = "Set Temprature to (°C)")]
            public float usertemp = 30.0f;
            [JsonProperty(PropertyName = "Use permission : ")]
            public bool usePerm = false;
        }

        private bool LoadConfigVariables()
        {
            try
            {
                configData = Config.ReadObject<ConfigData>();
            }
            catch
            {
                return false;
            }
            SaveConfig(configData);
            return true;
        }

        void Init()
        {
            if (!LoadConfigVariables())
            {
                Puts("Config file issue detected. Please delete file, or check syntax and fix.");
                return;
            }
            permission.RegisterPermission(permDisable, this);
        }
            

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            configData = new ConfigData();
            SaveConfig(configData);
        }
        void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }
        #endregion

        private const string permDisable = "disabletemperaturefunctions.use";
		
        private void OnServerInitialized()
        {
            foreach (var player in BasePlayer.activePlayerList.ToList())
            {
                OnPlayerSleepEnded(player);
            }
            
            foreach (var player in BasePlayer.sleepingPlayerList.ToList())
            {
                OnPlayerSleep(player);
            }
        }
		
		private object OnRunPlayerMetabolism(PlayerMetabolism metabolism, BasePlayer player)
		{
			if (player == null) return null;
			if (configData.usePerm && !permission.UserHasPermission(player.UserIDString, permDisable))
				return null;
			metabolism.temperature.min = configData.usertemp;
			metabolism.temperature.max = configData.usertemp;
			metabolism.temperature.value = configData.usertemp;
			player.SendNetworkUpdate();
			return null;
		}
        
        private void OnPlayerSleep(BasePlayer player)
        {
            Check(player);
        }
        
        private void OnPlayerSleepEnded(BasePlayer player)
        {
            Check(player);
        }
		
		void OnPlayerRespawn(BasePlayer player) => Check(player);
		void OnPlayerRespawned(BasePlayer player) => Check(player);
		void OnPlayerInit(BasePlayer player) => Check(player);

        private void FixTemp(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;
			if (configData.debug) Puts($"Initial Values : {player.metabolism.temperature.max}, {player.metabolism.temperature.min}, {player.metabolism.temperature.value}");
            if (configData.debug) Puts($" Adjusting Tolerance for {player.displayName}");
            player.metabolism.temperature.max = configData.usertemp;
            player.metabolism.temperature.min = configData.usertemp;
            player.metabolism.temperature.value = configData.usertemp;
            player.SendNetworkUpdate();
            if (configData.debug) Puts($"Changed Values : {player.metabolism.temperature.max}, {player.metabolism.temperature.min}, {player.metabolism.temperature.value}");
            return;
        }

        private void Check(BasePlayer player)
        {
            if (permission.UserHasPermission(player.UserIDString, permDisable))
            {
                FixTemp(player);
                return;
            }
            else if (!configData.usePerm)
            {
                FixTemp(player);
                return;
            }
        }
    }
}