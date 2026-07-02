using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Ext.Discord.Builders;
using Oxide.Ext.Discord.Clients;
using Oxide.Ext.Discord.Connections;
using Oxide.Ext.Discord.Constants;
using Oxide.Ext.Discord.Entities;
using Oxide.Ext.Discord.Extensions;
using Oxide.Ext.Discord.Interfaces;
using Oxide.Ext.Discord.Libraries;
using Oxide.Ext.Discord.Logging;
using Random = Oxide.Core.Random;

namespace Oxide.Plugins
{
    [Info("Discord Auth", "OuTSMoKE", "1.4.0")]
    [Description("Allows players to connect their discord account with steam")]
    public class DiscordAuth : CovalencePlugin, IDiscordPlugin, IDiscordLink
    {
        #region Fields
        public DiscordClient Client { get; set; }

        private Configuration _pluginConfig;
        private Data _pluginData;
        
        private readonly BotConnection _settings = new BotConnection
        {
            Intents = GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.DirectMessages
        };

        private readonly DiscordLink _link = GetLibrary<DiscordLink>();

        private DiscordGuild _guild;
        
        private string _groupNames;
        private string _roleNames;

        private readonly List<DiscordRole> _roles = new List<DiscordRole>();
        private readonly StringBuilder _builder = new StringBuilder();
        private readonly Dictionary<string, string> _codes = new Dictionary<string, string>();

        private char[] _codeCharacters;
        
        public enum DeauthReason {Command, IsLeaving, Inactive}
        
        private const string AuthPerm = "discordauth.auth";
        private const string DeauthPerm = "discordauth.deauth";
        #endregion

        #region Config
        protected override void LoadConfig()
        {
            base.LoadConfig();
            _pluginConfig = Config.ReadObject<Configuration>();
            Config.WriteObject(_pluginConfig);
        }

        protected override void LoadDefaultConfig() => _pluginConfig = new Configuration();

        protected override void SaveConfig() => Config.WriteObject(_pluginConfig);
        #endregion
        
        #region Lang
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Chat Format"] = "[#1874CD](Auth)[/#]: {0}",
                ["Code Generation"] = "Here is your code: [#1874CD]{0}[/#]\nJoin our [#EE3B3B]Discord[/#] and PM the code to the Discord Bot",
                ["Code Expired"] = "Your code has [#EE3B3B]Expired![/#]",
                ["Authenticated"] = "Thank you for authenticating your account",
                ["Game-Deauthenticated"] = "Successfully deauthenticated your account",
                ["Discord-Deauthenticated"] = "You have been deauthenticated from {0}",
                ["Discord-Deauthenticated-NonActive"] = "You have been deauthenticated from {0} because you haven't been active on the game server for {1} days",
                ["Already Authenticated"] = "You have already [#1874CD]authenticated[/#] your account, no need to do it again",
                ["Not Authenticated"] = "You are not authenticated",
                ["Group Revoked"] = "Your '{0}' Server Group(s) have been revoked! Reauthenticate to receive it",
                ["Roles Revoked"] = "Your '{0}' Discord Role(s) have been revoked! Reauthenticate to receive it",
                ["Join Granted"] = "Granted '{0}' Group(s) and '{1}' Discord Role(s)",
                ["Rejoin Granted"] = "Granted '{0}' Group(s) and '{1}' Discord Role(s) for joining {2} back",
                ["Unable to find code"] = "Sorry, we couldn't find your code, please try to authenticate again, If you haven't generated a code, please type /auth in-game",
                ["No Permission"] = "You dont have permission to use this command"
            }, this);
        }
        #endregion

        #region Chat Commands
        private void AuthCommand(IPlayer player, string command, string[] args)
        {
            // No Permission
            if (!player.HasPermission(AuthPerm))
            {
                Message(player, "No Permission");
                return;
            }

            // Already authenticated-check
            if (_link.IsLinked(player.Id))
            {
                Message(player, "Already Authenticated");
                return;
            }

            // Sends the code if already exist to prevent duplication
            if (_codes.ContainsKey(player.Id))
            {
                Message(player, "Code Generation", _codes[player.Id]);
                return;
            }

            // Adds a random code and send it to the player if doesn't already exist
            string code = GenerateCode();
            _codes.Add(player.Id, code);
            Message(player, "Code Generation", code);

            // Code Expiration Function
            timer.In(_pluginConfig.Code.CodeLifetime * 60, () =>
            {
                if (_codes.ContainsKey(player.Id))
                {
                    _codes.Remove(player.Id);
                    Message(player, "Code Expired");
                }
            });
        }

        private void DeauthCommand(IPlayer player, string command, string[] args)
        {
            // No Permission
            if (!player.HasPermission(DeauthPerm))
            {
                Message(player, "No Permission");
                return;
            }

            Snowflake userId = _link.GetDiscordId(player.Id);
            if (!userId.IsValid())
            {
                Message(player, "Not Authenticated");
                return;
            }

            DiscordUser user = GetDiscordUser(userId);

            Deauthenticate(player, user, DeauthReason.Command);
            Message(player, "Game-Deauthenticated");
        }
        #endregion

        #region Oxide Hooks
        private void Init()
        {
            _pluginData = Interface.Oxide.DataFileSystem.ReadObject<Data>(Name);
            _codeCharacters = _pluginConfig.Code.CodeChars.ToCharArray();
            
            _link.AddLinkPlugin(this);
            
            AddCovalenceCommand(_pluginConfig.Info.AuthCommands, nameof(AuthCommand));
            AddCovalenceCommand(_pluginConfig.Info.DeauthCommands, nameof(DeauthCommand));
            foreach (string group in _pluginConfig.Info.Groups)
            {
                permission.CreateGroup(group, group, 0);
            }
            
            permission.RegisterPermission(AuthPerm, this);
            permission.RegisterPermission(DeauthPerm, this);

            if (!_pluginConfig.Info.AutomaticallyReauthenticate)
            {
                Unsubscribe(nameof(OnDiscordGuildMemberAdded));
            }
        }

        private void OnServerInitialized()
        {
            _groupNames = string.Join(", ", _pluginConfig.Info.Groups);
            
            foreach (IPlayer player in players.Connected)
            {
                OnUserConnected(player);
            }
        }
        
        private void OnUserConnected(IPlayer player)
        {
            if (player.IsLinked())
            {
                _pluginData.LastJoinedDate[player.Id] = DateTime.UtcNow;
            }
        }

        private void OnServerSave() => SaveData();
        
        private void Unload()
        {
            SaveData();
        }
        #endregion

        #region Discord Hooks
        [HookMethod(DiscordExtHooks.OnDiscordClientCreated)]
        private void OnDiscordClientCreated()
        {
            if (string.IsNullOrEmpty(_pluginConfig.Info.BotToken))
            {
                PrintWarning("Please set the Discord Bot Token and reload the plugin");
                return;
            }

            _settings.ApiToken = _pluginConfig.Info.BotToken;
            _settings.LogLevel = _pluginConfig.Info.ExtensionDebugging;
            Client.Connect(_settings);
        }
        
        // Called when the client is created, and the plugin can use it
        [HookMethod(DiscordExtHooks.OnDiscordGatewayReady)]
        private void OnDiscordGatewayReady(GatewayReadyEvent ready)
        {
            if (ready.Guilds.Count == 0)
            {
                PrintError("Your bot was not found in any discord servers. Please invite it to a server and reload the plugin.");
                return;
            }

            DiscordGuild guild = null;
            if (ready.Guilds.Count == 1 && !_pluginConfig.Info.GuildId.IsValid())
            {
                guild = ready.Guilds.Values.FirstOrDefault();
            }

            if (guild == null)
            {
                guild = ready.Guilds[_pluginConfig.Info.GuildId];
                if (guild == null)
                {
                    PrintError("Failed to find a matching guild for the Discord Server Id. " +
                               "Please make sure your guild Id is correct and the bot is in the discord server.");
                    return;
                }
            }

            if (!Client.Bot.Application.HasApplicationFlag(ApplicationFlags.GatewayGuildMembersLimited) && !Client.Bot.Application.HasApplicationFlag(ApplicationFlags.GatewayGuildMembers))
            {
                PrintError($"You need to enable \"Server Members Intent\" for {Client.Bot.BotUser.Username} @ https://discord.com/developers/applications\n" +
                           $"{Name} will not function correctly until that is fixed. Once updated please reload {Name}.");
                return;
            }
            
            _guild = guild;
            Puts($"Connected to bot: {Client.Bot.BotUser.Username}");
        }

        [HookMethod(DiscordExtHooks.OnDiscordGuildMembersLoaded)]
        private void OnDiscordGuildMembersLoaded(DiscordGuild guild)
        {
            if (_guild?.Id != guild.Id)
            {
                return;
            }

            foreach (string name in _pluginConfig.Info.Roles)
            {
                DiscordRole role = _guild.GetRole(name);
                if (role != null)
                {
                    _roles.Add(role);
                    continue;
                }

                Snowflake roleId;
                if (Snowflake.TryParse(name, out roleId) && _guild.Roles.ContainsKey(roleId))
                {
                    _roles.Add(_guild.Roles[roleId]);
                    continue;
                }

                PrintWarning($"Failed to find role {name} in guild {_guild.Name}");
            }

            _roleNames = string.Join(", ", _roles.Select(r => r.Name));

            List<KeyValuePair<string, Snowflake>> leftLinks = new List<KeyValuePair<string, Snowflake>>();
            foreach (KeyValuePair<string, Snowflake> link in _pluginData.Players.ToList())
            {
                if (!_guild.Members.ContainsKey(link.Value) || players.FindPlayerById(link.Key) == null)
                {
                    leftLinks.Add(link);
                }
            }
            
            ProcessNextLeft(leftLinks);
            CheckInactivePlayers();
            timer.Every(24 * 60 * 60, CheckInactivePlayers);
        }

        // Called when a member leaves the Discord server
        [HookMethod(DiscordExtHooks.OnDiscordGuildMemberRemoved)]
        private void OnDiscordGuildMemberRemoved(GuildMemberRemovedEvent removed, DiscordGuild guild)
        {
            if (_guild?.Id != guild.Id)
            {
                return;
            }
            
            PlayerId steamId = _link.GetPlayerId(removed.User);

            // No user found
            if (!steamId.IsValid)
            {
                return;
            }

            IPlayer player = steamId.Player;
            if (player == null)
            {
                return;
            }
            
            Deauthenticate(player, removed.User, DeauthReason.IsLeaving);
            Message(player, "Game-Deauthenticated");
        }

        // Called when a user joins the discord server
        [HookMethod(DiscordExtHooks.OnDiscordGuildMemberAdded)]
        private void OnDiscordGuildMemberAdded(GuildMember member, DiscordGuild guild)
        {
            if (!_pluginConfig.Info.AutomaticallyReauthenticate)
            {
                return;
            }
            
            string playerId = _pluginData.Backup[member.Id];
            if (string.IsNullOrEmpty(playerId))
            {
                return;
            }

            IPlayer player = players.FindPlayerById(playerId);
            if (player == null)
            {
                return;
            }
            
            Authenticate(player, member.User);
            Message(player, "Authenticated");
            member.User.SendDirectMessage(Client, new List<DiscordEmbed>
            {
                GetEmbed(Formatter.ToPlaintext(Lang("Authenticated", player)), 11523722),
                GetEmbed(Formatter.ToPlaintext(Lang("Rejoin Granted", player, _groupNames, _roleNames, _guild?.Name)), 11523722)
            });
        }

        // Called when a private message is received
        [HookMethod(DiscordExtHooks.OnDiscordDirectMessageCreated)]
        private void OnDiscordDirectMessageCreated(DiscordMessage message)
        {
            // Bot-check
            if (message.Author.Bot == true)
                return;

            //Don't process guild channel messages
            if (message.GuildId.HasValue)
                return;
            
            // Length-check
            if (string.IsNullOrEmpty(message.Content) || message.Content.Length != _pluginConfig.Code.CodeLength)
                return;

            // No code found
            StringComparison comparison = _pluginConfig.Code.CaseInsensitiveMatch ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            string playerId = null;
            foreach (KeyValuePair<string, string> code in _codes)
            {
                if (code.Value.Equals(message.Content, comparison))
                {
                    playerId = code.Key;
                    break;
                }
            }

            if (string.IsNullOrEmpty(playerId))
            { 
                message.Reply(Client, GetEmbed(Formatter.ToPlaintext(Lang("Unable to find code")), 16098851));
                return;
            }

            // Already authenticated-check
            if(_link.IsLinked(message.Author.Id))
            {
                message.Reply(Client, GetEmbed(Formatter.ToPlaintext(Lang("Already Authenticated", message.Author.Player)), 4886754));
                return;
            }

            _codes.Remove(playerId);
                
            IPlayer player = covalence.Players.FindPlayerById(playerId);
            if (player == null)
            {
                return;
            }
                
            Message(player, "Authenticated");
            message.Reply(Client, new List<DiscordEmbed>
            {
                GetEmbed(Formatter.ToPlaintext(Lang("Authenticated", player)),11523722),
                GetEmbed(Formatter.ToPlaintext(Lang("Join Granted", player, _groupNames, _roleNames)),11523722),
            });
            Authenticate(player, message.Author);
        }
        #endregion

        #region Core
        public void Authenticate(IPlayer player, DiscordUser user)
        {
            _pluginData.Players[player.Id] = user.Id;
            _pluginData.Backup.Remove(user.Id);
            _link.OnLinked(this, player, user);
            foreach (string group in _pluginConfig.Info.Groups)
            {
                permission.AddUserGroup(player.Id, group);
            }
            
            foreach (DiscordRole role in _roles)
            {
                _guild?.AddMemberRole(Client, user, role);
            }
            OnUserConnected(player);
            SaveData();
        }

        public void Deauthenticate(IPlayer player, DiscordUser user, DeauthReason reason)
        {
            List<DiscordEmbed> embeds = new List<DiscordEmbed>();
            if (reason == DeauthReason.IsLeaving)
            {
                if (user.Id.IsValid())
                {
                    _pluginData.Backup[user.Id] = player.Id;
                }
            }
            else if (reason == DeauthReason.Inactive)
            {
                embeds.Add( GetEmbed(Formatter.ToPlaintext(Lang("Discord-Deauthenticated-NonActive", player, _guild?.Name, _pluginConfig.Info.NonActiveDuration)), 9905970));
            }
            else
            {
                embeds.Add(GetEmbed(Formatter.ToPlaintext(Lang("Discord-Deauthenticated", player, _guild?.Name)), 9905970));
            }
            
            _pluginData.LastJoinedDate.Remove(player.Id);
            _link.OnUnlinked(this, player, user);
            _pluginData.Players.Remove(player.Id);

            if (_pluginConfig.Info.RemoveFromGroups)
            {
                foreach (string group in _pluginConfig.Info.Groups)
                {
                    permission.RemoveUserGroup(player.Id, group);
                }

                embeds.Add(GetEmbed(Formatter.ToPlaintext(Lang("Group Revoked", player, _groupNames)), 16098851));
            }

            if (_pluginConfig.Info.RemoveFromRoles && reason != DeauthReason.IsLeaving)
            {
                Snowflake userId = user.Id;
                if (userId.IsValid())
                {
                    foreach (DiscordRole role in _roles)
                    {
                        _guild?.RemoveMemberRole(Client, userId, role.Id);
                    }
                    
                    embeds.Add(GetEmbed(Formatter.ToPlaintext(Lang("Roles Revoked", player, _roleNames)), 16098851));
                }
            }

            if (reason != DeauthReason.IsLeaving)
            {
                user.SendDirectMessage(Client, embeds);
            }

            SaveData();
        }

        public void ProcessNextLeft(List<KeyValuePair<string, Snowflake>> leftLinks)
        {
            if (leftLinks.Count == 0)
            {
                return;
            }

            KeyValuePair<string, Snowflake> link = leftLinks[0];
            leftLinks.RemoveAt(0);
            
            IPlayer player = players.FindPlayerById(link.Key);
            if (player == null)
            {
                if (link.Value.IsValid())
                {
                    _pluginData.Backup[link.Value] = link.Key;
                }
              
                _pluginData.Players.Remove(link.Key);
                timer.In(2f, () => ProcessNextLeft(leftLinks));
                return;
            }
                
            try
            {
                DiscordUser user = _link.GetDiscordUser(link.Key);
                if (user.Id == default(Snowflake))
                {
                    user.Id = link.Value;
                }
                Deauthenticate(player, user, DeauthReason.IsLeaving);
            }
            catch (Exception ex)
            {
                PrintWarning($"Failed to Deauthenticate Left Player {player.Name}({link.Key}) User ID: {link.Value}\n{ex}");
            }
            
            timer.In(2f, () => ProcessNextLeft(leftLinks));
        }
        
        public void CheckInactivePlayers()
        {
            foreach (KeyValuePair<string, Snowflake> link in _pluginData.Players.ToList())
            {
                IPlayer player = _link.GetPlayer(link.Value);
                if (!_pluginData.LastJoinedDate.ContainsKey(link.Key))
                {
                    _pluginData.LastJoinedDate[link.Key] = DateTime.UtcNow;
                    continue;
                }

                if (!_pluginConfig.Info.DeauthNonActive)
                {
                    continue;
                }

                if (!player.IsConnected && _pluginData.LastJoinedDate[link.Key] + TimeSpan.FromDays(_pluginConfig.Info.NonActiveDuration) < DateTime.UtcNow)
                {
                    Snowflake userId = _link.GetDiscordId(link.Key);
                    if (userId.IsValid())
                    {
                        DiscordUser user = GetDiscordUser(userId);
                        Deauthenticate(player, user, DeauthReason.Inactive);
                    }
                }
            }
        }
        #endregion

        #region Helpers
        public DiscordUser GetDiscordUser(Snowflake userId)
        {
            DiscordUser user = _guild.Members[userId]?.User ?? new DiscordUser
            {
                Id = userId,
                Bot = false
            };
            return user;
        }
        
        public void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _pluginData);

        public string Lang(string key, IPlayer player = null) => lang.GetMessage(key, this, player?.Id);

        public string Lang(string key, IPlayer player = null, params object[] args)
        {
            try
            {
                return string.Format(Lang(key, player), args);
            }
            catch(Exception ex)
            {
                PrintError($"Lang Key '{key}' threw exception\n:{ex}");
                throw;
            }
        }

        public void Message(IPlayer player, string key, params object[] args)
        {
            if (player.IsConnected)
            {
                player.Reply(Lang("Chat Format", player, Lang(key, player, args)));
            }
        }
        
        public void Message(IPlayer player, string key)
        {
            if (player.IsConnected)
            {
                player.Reply(Lang("Chat Format", player, Lang(key, player)));
            }
        }

        public string GenerateCode()
        {
            _builder.Clear();
            
            for (int i = 0; i < _pluginConfig.Code.CodeLength; i++)
            {
                _builder.Append(_codeCharacters[Random.Range(0, _codeCharacters.Length)]);
            }

            return _builder.ToString();
        }

        public DiscordEmbed GetEmbed(string text, uint color)
        {
            return new DiscordEmbedBuilder()
                   .AddDescription(text)
                   .AddColor(color)
                   .Build();
        }
        #endregion

        #region Discord Link
        public IDictionary<PlayerId, Snowflake> GetPlayerIdToDiscordIds()
        {
            Puts($"Loaded {_pluginData.Players.Count} Players");
            return _pluginData.Players.ToDictionary(key => new PlayerId(key.Key), value => value.Value);
        }
        #endregion
        
        #region Classes
        class Configuration
        {
            [JsonProperty(PropertyName = "Settings")]
            public Settings Info = new Settings();

            [JsonProperty(PropertyName = "Authentication Code")]
            public AuthCode Code = new AuthCode();

            public class Settings
            {
                [JsonProperty(PropertyName = "Bot Token")]
                public string BotToken = string.Empty;

                [JsonProperty(PropertyName = "Discord Server ID (Optional if bot only in 1 guild)")]
                public Snowflake GuildId;

                [JsonProperty(PropertyName = "Auth Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public string[] AuthCommands = { "auth", "authenticate" };

                [JsonProperty(PropertyName = "Deauth Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public string[] DeauthCommands = { "deauth", "deauthenticate" };

                [JsonProperty(PropertyName = "Oxide Groups to Assign", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public List<string> Groups = new List<string>
                {
                    "authenticated"
                };
                
                [JsonProperty(PropertyName = "Discord Roles to Assign", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public List<string> Roles = new List<string>
                {
                    "Authenticated"
                };

                [JsonProperty(PropertyName = "Revoke Oxide Groups on Deauthenticate")]
                public bool RemoveFromGroups = true;
                
                [JsonProperty(PropertyName = "Revoke Discord Roles on Deauthenticate")]
                public bool RemoveFromRoles = true;
                
                [JsonProperty(PropertyName = "Automatically Reauthenticate on Leaving and Rejoining the Discord Server")]
                public bool AutomaticallyReauthenticate = true;
                
                [JsonProperty(PropertyName = "Automatically Deauthenticate Non Active Players")]
                public bool DeauthNonActive = false;
                
                [JsonProperty(PropertyName = "Player Considered Non Active After (Days)")]
                public float NonActiveDuration = 30f;

                [JsonConverter(typeof(StringEnumConverter))]
                [DefaultValue(DiscordLogLevel.Info)]
                [JsonProperty(PropertyName = "Discord Extension Log Level (Verbose, Debug, Info, Warning, Error, Exception, Off)")]
                public DiscordLogLevel ExtensionDebugging = DiscordLogLevel.Info;
            }

            public class AuthCode
            {
                [JsonProperty(PropertyName = "Code Lifetime (minutes)")]
                public int CodeLifetime = 60;

                [JsonProperty(PropertyName = "Code Length")]
                public int CodeLength = 5;

                [JsonProperty(PropertyName = "Code Case Insensitive Match")]
                public bool CaseInsensitiveMatch = true;

                [JsonProperty(PropertyName = "Code Characters")]
                public string CodeChars = "ABCDEFGHJKMNPQRSTUVWXYZ";
            }
        }
        
        private class Data
        {
            public Hash<string, Snowflake> Players = new Hash<string, Snowflake>();
            public Hash<Snowflake, string> Backup = new Hash<Snowflake, string>();
            public Hash<string, DateTime> LastJoinedDate = new Hash<string, DateTime>();
        }
        #endregion
    }
}
