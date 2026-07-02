using System;
using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Ext.Discord.Clients;
using Oxide.Ext.Discord.Connections;
using Oxide.Ext.Discord.Constants;
using Oxide.Ext.Discord.Entities;
using Oxide.Ext.Discord.Interfaces;
using Oxide.Ext.Discord.Libraries;
using Oxide.Ext.Discord.Logging;

namespace Oxide.Plugins
{
    [Info("Discord Presence", "MJSU", "3.0.1")]
    [Description("Updates the Discord bot status message")]
    internal class DiscordPresence : CovalencePlugin, IDiscordPlugin
    {
        #region Class Fields
        public DiscordClient Client { get; set; }

        private PluginConfig _pluginConfig;

        private int _index;

        private readonly DiscordPlaceholders _placeholders = GetLibrary<DiscordPlaceholders>();

        private readonly UpdatePresenceCommand _command = new()
        {
            Afk = false,
            Since = 0,
            Status = UserStatusType.Online
        };

        private readonly DiscordActivity _activity = new();
        private Action _updatePresence;

        private bool _serverInit;
        private bool _gatewayReady;
        private DateTime _nextApiSend;
        #endregion

        #region Setup & Loading
        private void Init()
        {
            _command.Activities = new List<DiscordActivity> {_activity};
            _updatePresence = UpdatePresence;
            if (_pluginConfig.UpdateRate < 1f)
            {
                _pluginConfig.UpdateRate = 1f;
            }

            Unsubscribe(nameof(OnUserConnected));
            Unsubscribe(nameof(OnUserDisconnected));
        }

        [HookMethod(DiscordExtHooks.OnDiscordClientCreated)]
        private void OnDiscordClientCreated()
        {
            if (string.IsNullOrEmpty(_pluginConfig.Token))
            {
                PrintWarning("Please enter your bot token in the config and reload the plugin.");
                return;
            }
            
            Client.Connect(new BotConnection
            {
                ApiToken = _pluginConfig.Token,
                LogLevel = _pluginConfig.ExtensionDebugging,
                Intents = GatewayIntents.None
            });
        }

        [HookMethod(DiscordExtHooks.OnDiscordGatewayReady)]
        private void OnDiscordGatewayReady(GatewayReadyEvent ready)
        {
            _gatewayReady = true;
            Puts($"{Title} Ready");
            if (_pluginConfig.EnableLoadingMessage && !_serverInit)
            {
                SendLoadingMessage();
            }
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

        public PluginConfig AdditionalConfig(PluginConfig config)
        {
            config.LoadingMessage = new MessageSettings(config.LoadingMessage ?? new MessageSettings("Server is booting", ActivityType.Game));
            config.StatusMessages ??= new List<MessageSettings>
            {
                new($"{DefaultKeys.Server.Name}", ActivityType.Custom),
                new($"{DefaultKeys.Server.Players}/{DefaultKeys.Server.MaxPlayers} Players", ActivityType.Custom),
                new("{server.players.sleepers} Sleepers", ActivityType.Custom),
                new("{server.players.stored} Total Players", ActivityType.Custom),
                new("Server FPS {server.fps}", ActivityType.Custom),
                new("{server.entities} Entities", ActivityType.Custom),
                new("{server.players.total} Lifetime Players", ActivityType.Custom),
#if RUST
                new MessageSettings("{server.players.queued} Queued", ActivityType.Custom),
                new MessageSettings("{server.players.loading} Joining", ActivityType.Custom),
                new MessageSettings("Wiped: {server.map.wipe.last!local}", ActivityType.Custom),
                new MessageSettings("Size: {world.size} Seed: {world.seed}", ActivityType.Custom)
#endif
            };

            for (int index = 0; index < config.StatusMessages.Count; index++)
            {
                config.StatusMessages[index] = new MessageSettings(config.StatusMessages[index]);
            }

            return config;
        }
        
        private void OnServerInitialized()
        {
            _serverInit = true;
            if (Client.IsConnected())
            {
                timer.In(2.5f, UpdatePresence);
            }

            if (_pluginConfig.EnableUpdateInterval)
            {
                timer.Every(_pluginConfig.UpdateRate, UpdatePresence);
            }
            
            if (_pluginConfig.UpdateOnPlayerStateChange)
            {
                Subscribe(nameof(OnUserConnected));
                Subscribe(nameof(OnUserDisconnected));
            }
        }
        #endregion
        
        #region Hooks
        private void OnUserConnected(IPlayer player) => UpdatePresence();

        private void OnUserDisconnected(IPlayer player) => NextTick(_updatePresence);
        
        private void OnDiscordGatewayResumed() => UpdatePresence();
        private void OnDiscordGatewayReconnected() => UpdatePresence();
        #endregion

        #region Message Handling
        public void SendLoadingMessage()
        {
            SendUpdate(_pluginConfig.LoadingMessage);
        }
        
        public void UpdatePresence()
        {
            if (!_serverInit || !_gatewayReady)
            {
                return;
            }
            
            if (_pluginConfig.StatusMessages.Count == 0)
            {
                PrintError("Presence Text formats contains no values. Please add some to your config");
                return;
            }
            
            SendUpdate(_pluginConfig.StatusMessages[_index]);
            _index = ++_index % _pluginConfig.StatusMessages.Count;
        }

        public void SendUpdate(MessageSettings settings) => SendUpdate(settings.Message, settings.Type);

        public void SendUpdate(string message, ActivityType type)
        {
            _activity.Name = null;
            _activity.State = null;
            string text = _placeholders.ProcessPlaceholders(message, GetDefault());
            if (type == ActivityType.Custom)
            {
                _activity.State = text;
            }

            _activity.Name = text;
            _activity.Type = type;
            Client?.UpdateStatus(_command);
        }

        public PlaceholderData GetDefault() => _placeholders.CreateData(this);
        #endregion

        #region API
        private void API_SendUpdateMessage(string message, int activity = (int)ActivityType.Custom)
        {
            if (_nextApiSend <= DateTime.UtcNow)
            {
                _nextApiSend = DateTime.UtcNow + TimeSpan.FromSeconds(1);
                SendUpdate(message, (ActivityType)activity);
            }
        }
        #endregion

        #region Classes
        public class PluginConfig
        {
            [DefaultValue("")]
            [JsonProperty(PropertyName = "Discord Application Bot Token")]
            public string Token { get; set; }
            
            [DefaultValue(true)]
            [JsonProperty(PropertyName = "Enable Sending Message Per Update Rate")]
            public bool EnableUpdateInterval { get; set; }
            
            [DefaultValue(true)]
            [JsonProperty(PropertyName = "Enable Sending Message On Player Leave/Join")]
            public bool UpdateOnPlayerStateChange { get; set; }
            
            [DefaultValue(true)]
            [JsonProperty(PropertyName = "Enable Sending Server Loading Message")]
            public bool EnableLoadingMessage { get; set; }
            
            [DefaultValue(15f)]
            [JsonProperty(PropertyName = "Update Rate (Seconds)")]
            public float UpdateRate { get; set; }

            [JsonProperty(PropertyName = "Status Messages")]
            public List<MessageSettings> StatusMessages { get; set; }
            
            [JsonProperty(PropertyName = "Server Loading Message")]
            public MessageSettings LoadingMessage { get; set; }
            
            [JsonConverter(typeof(StringEnumConverter))]
            [DefaultValue(DiscordLogLevel.Info)]
            [JsonProperty(PropertyName = "Discord Extension Log Level (Verbose, Debug, Info, Warning, Error, Exception, Off)")]
            public DiscordLogLevel ExtensionDebugging { get; set; }
        }

        public class MessageSettings
        {
            public string Message { get; set; }
            
            [JsonConverter(typeof(StringEnumConverter))]
            [DefaultValue(ActivityType.Custom)]
            public ActivityType Type { get; set; }

            [JsonConstructor]
            public MessageSettings() { }
            
            public MessageSettings(string message, ActivityType type)
            {
                Message = message;
                Type = type;
            }
            
            public MessageSettings(MessageSettings settings)
            {
                Message = settings?.Message ?? string.Empty;
                Type = settings?.Type ?? ActivityType.Game;
            }
        }
        #endregion
    }
}