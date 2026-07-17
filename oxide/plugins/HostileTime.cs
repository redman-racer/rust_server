using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Hostile Time", "Orange", "1.0.2")]
    [Description("Changes value of hostile duration")]
    public class HostileTime : RustPlugin
    {
        #region Oxide Hooks

        private void OnEntityMarkHostile(BasePlayer player, float duration)
        {
            if (player.userID.IsSteamId() == false)
            {
                return;
            }
            
            NextTick(() =>
            {
                if (player.IsValid() == false || player.IsHostile() == false)
                {
                    return;
                }
                
                var diff = duration - config.newDuration;
                if (diff > 0)
                {
                    Unsubscribe(nameof(OnEntityMarkHostile));
                    player.State.unHostileTimestamp -= diff;
                    player.MarkHostileFor(0f);
                    Subscribe(nameof(OnEntityMarkHostile));
                }
            });
        }

        #endregion
        
        #region Configuration | 24.05.2020

        private static ConfigData config = new ConfigData();

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Hostile duration (seconds)")]
            public float newDuration = 300;
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
            if (ConVar.Server.hostname.Contains("[DEBUG]") == true)
            {
                config = new ConfigData();
            }
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
    }
}