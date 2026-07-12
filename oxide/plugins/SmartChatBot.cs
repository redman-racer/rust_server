using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ConVar;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;
using Random = System.Random;
using Time = Oxide.Core.Libraries.Time;

namespace Oxide.Plugins
{
    [Info("Smart Chat Bot", "Iv Misticos", "2.0.13")]
    [Description("I send chat messages based on some triggers or time.")]
    class SmartChatBot : RustPlugin
    {
        #region Variables

        [PluginReference]
        // ReSharper disable once InconsistentNaming
        private Plugin PlaceholderAPI = null;

        private static readonly Random Random = new Random();
        private readonly Dictionary<BasePlayer, uint> _lastSent = new Dictionary<BasePlayer, uint>();
        private uint _lastSentGlobal;

        private static readonly Time Time = GetLibrary<Time>();

        private const string CountryRequest = "http://ip-api.com/json/{ip}?fields=country,countryCode,status";
        private const string AdminPermission = "smartchatbot.admin";
        private const string AdminUiName = "SmartChatBot.Admin";
        private readonly Dictionary<ulong, AdminUiState> _adminUiStates = new Dictionary<ulong, AdminUiState>();
        
        #endregion
        
        #region Configuration

        private Configuration _config = new Configuration();

        private class Configuration
        {
            [JsonProperty(PropertyName = "Chat Prefix")]
            public string Prefix = "<color=#787FFF>Bot </color>";

            [JsonProperty(PropertyName = "Show Chat Prefix")]
            public bool ShowPrefix = true;

            [JsonProperty(PropertyName = "Bot Icon (SteamID)")]
            public ulong ChatSteamId = 0;

            [JsonProperty(PropertyName = "Cooldown Between Auto Responses For User")]
            public string Cooldown = "10s";

            [JsonProperty(PropertyName = "Global Cooldown Between Auto Responses")]
            public string CooldownGlobal = "2s";

            [JsonProperty(PropertyName = "Use Default Chat (0), Chat Plus (1), Better Chat (2)")]
            public ushort ChatSystem = 0;

            [JsonIgnore] public uint ParsedCooldown;
            [JsonIgnore] public uint ParsedCooldownGlobal;

            [JsonProperty(PropertyName = "Debug")]
            public bool Debug = false;

            [JsonProperty(PropertyName = "Allow Multiple Auto Responses")]
            public bool MultipleAutoResponses = false;

            [JsonProperty(PropertyName = "Minimal Time Between Message And Answer")]
            public float MinTime = 1.0f;

            [JsonProperty(PropertyName = "Maximal Time Between Message And Answer")]
            public float MaxTime = 3.0f;

            [JsonProperty(PropertyName = "Welcome Message", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> WelcomeMessage = new List<string> { "Welcome, {name}!", "Hello, dear {name}!", "Hello, {name}! Your IP: { ip }" };

            [JsonProperty(PropertyName = "Welcome Message Enabled")]
            public bool WelcomeMessageEnabled = false;

            [JsonProperty(PropertyName = "Joining Message", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> JoiningMessage = new List<string> { "Welcome, {name} ({id}, { ip })!", "Hello, dear {name} ({id}, { ip })!", "{name} came from {country} ({countrycode})" };

            [JsonProperty(PropertyName = "Leaving Message", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> LeavingMessage = new List<string> { "Bye, {name} ({id}, { ip })!\nReason: {reason}", "{name} ({id}, { ip }) left the game. Reason: {reason}", "{name} from {country} ({countrycode}) just left the game!" };

            [JsonProperty(PropertyName = "Joining Message Enabled")]
            public bool JoiningMessageEnabled = false;

            [JsonProperty(PropertyName = "Leaving Message Enabled")]
            public bool LeavingMessageEnabled = false;

            [JsonProperty(PropertyName = "Show Joining Message To Player That Joined")]
            public bool JoiningMessageSelfEnabled = false;

            [JsonProperty(PropertyName = "Show Leaving Message To Player That Left")]
            public bool LeavingMessageSelfEnabled = false;
            
            [JsonProperty(PropertyName = "Auto Messages", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<AutoMessageGroup> AutoMessages = new List<AutoMessageGroup> { new AutoMessageGroup() };
            
            [JsonProperty(PropertyName = "Auto Responses", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<AutoResponseGroup> AutoResponses = new List<AutoResponseGroup> { new AutoResponseGroup() };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
            }
            catch
            {
                PrintError("Your configuration file contains an error. Using default configuration values.");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void SaveConfig() => Config.WriteObject(_config);

        private class AutoMessageGroup
        {
            [JsonProperty(PropertyName = "Permission")]
            public string Permission = "smartchatbot.messages";
            
            [JsonProperty(PropertyName = "Message Frequency")]
            public string Frequency = "5m";

            [JsonIgnore] public uint ParsedFrequency;
            [JsonIgnore] public short ActiveMessage;

            [JsonProperty(PropertyName = "Auto Messages", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<AutoMessage> AutoMessages = new List<AutoMessage> { new AutoMessage() };
        }

        private class AutoMessage
        {
            [JsonProperty(PropertyName = "Is Enabled")]
            public bool Enabled = true;

            [JsonProperty(PropertyName = "Message")]
            public string Message = "Do not mind, I am just a stupid bot.";
        }

        private class AutoResponseGroup
        {
            [JsonProperty(PropertyName = "Permission")]
            public string Permission = "smartchatbot.response";
            
            [JsonProperty(PropertyName = "Auto Responses", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<AutoResponse> AutoResponses = new List<AutoResponse> { new AutoResponse() };
        }

        private class AutoResponse
        {
            [JsonProperty(PropertyName = "Is Enabled")]
            public bool Enabled = true;
            
            [JsonProperty(PropertyName = "Remove Message From Sender")]
            public bool RemoveMessage = false;
            
            [JsonProperty(PropertyName = "Send Response For Everyone (true) or Only For Sender (false)")]
            public bool SendPublic = true;
            
            [JsonProperty(PropertyName = "Triggers", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<AutoResponseTrigger> Triggers = new List<AutoResponseTrigger> { new AutoResponseTrigger() };

            [JsonProperty(PropertyName = "Answers", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Answers = new List<string> { "This bot really works.", "IT WORKS OMG!" };

            public bool IsValid() => Triggers.Count > 0 && Answers.Count > 0;
        }

        private class AutoResponseTrigger
        {
            [JsonProperty(PropertyName = "Percentage Of Contained Words")]
            public float ContainedWordsPercentage = 0.75f;

            [JsonProperty(PropertyName = "Regex Enabled")]
            public bool Regex = false;
            
            [JsonProperty(PropertyName = "Words", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Words = new List<string> { "How", "this", "bot", "works" };
        }

        private class AdminUiState
        {
            public string Tab = "messages";
        }
        
        #endregion
        
        #region Hooks

        private void OnServerInitialized()
        {
            LoadConfig();
            InitializeLoadedConfig(true);
        }

        private void InitializeLoadedConfig(bool scheduleTimers)
        {
            permission.RegisterPermission(AdminPermission, this);
            Unsubscribe(nameof(OnChatPlusMessage));
            Unsubscribe(nameof(OnBetterChat));
            Unsubscribe(nameof(OnPlayerChat));

            if (_config.ChatSystem == 0)
                Subscribe(nameof(OnPlayerChat));
            else if (_config.ChatSystem == 1)
                Subscribe(nameof(OnChatPlusMessage));
            else if (_config.ChatSystem == 2)
                Subscribe(nameof(OnBetterChat));

            var pl = plugins.GetAll();
            var plCount = pl.Length;
            for (var i = 0; i < plCount; i++)
            {
                var p = pl[i];
                // ReSharper disable once ConvertIfStatementToSwitchStatement
                if (p.Name == "BetterChat" || p.Name == "Better Chat")
                    PrintWarning("Detected Better Chat. Make sure you enabled it in your configuration.");
                else if (p.Name == "ChatPlus" || p.Name == "Chat Plus")
                    PrintWarning("Detected Chat Plus. Make sure you enabled it in your configuration.");
            }

            if (!ConvertToSeconds(_config.Cooldown, out _config.ParsedCooldown))
            {
                PrintError($"Unable to convert \"{_config.Cooldown}\" to seconds!");
                _config.ParsedCooldown = 0;
            }

            if (!ConvertToSeconds(_config.CooldownGlobal, out _config.ParsedCooldownGlobal))
            {
                PrintError($"Unable to convert \"{_config.CooldownGlobal}\" to seconds!");
                _config.ParsedCooldownGlobal = 0;
            }

            var messageGroupsCount = _config.AutoMessages.Count;
            for (var i = 0; i < messageGroupsCount; i++)
            {
                var messageGroup = _config.AutoMessages[i];
                PrintDebug($"Handling Message Group (ID: {i})");
                
                // Time Handling
                if (!ConvertToSeconds(messageGroup.Frequency, out messageGroup.ParsedFrequency))
                {
                    PrintError($"Unable to convert \"{messageGroup.Frequency}\" to seconds!");
                    messageGroup.ParsedFrequency = 60;
                }
                
                // Permissions
                if (!string.IsNullOrEmpty(messageGroup.Permission))
                    permission.RegisterPermission(messageGroup.Permission, this);
                
                // Timers
                if (scheduleTimers)
                    timer.Every(messageGroup.ParsedFrequency, () => HandleBroadcast(messageGroup));
            }

            var responseGroupsCount = _config.AutoResponses.Count;
            for (var i = 0; i < responseGroupsCount; i++)
            {
                PrintDebug($"Handling Response Group (ID: {i})");
                var responseGroup = _config.AutoResponses[i];
                
                // Permissions
                if (!string.IsNullOrEmpty(responseGroup.Permission))
                    permission.RegisterPermission(responseGroup.Permission, this);
            }
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
                DestroyAdminUi(player);
        }

        [ChatCommand("smartchatbot")]
        private void CmdSmartChatBot(BasePlayer player, string command, string[] args)
        {
            HandleAdminChatCommand(player, args);
        }

        [ChatCommand("scb")]
        private void CmdSmartChatBotShort(BasePlayer player, string command, string[] args)
        {
            HandleAdminChatCommand(player, args);
        }

        [ConsoleCommand("smartchatbot.adminui")]
        private void CmdSmartChatBotAdminUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !CanAdmin(player))
                return;

            var args = arg.Args ?? new string[0];
            if (args.Length == 0)
            {
                ShowAdminUi(player);
                return;
            }

            var state = GetAdminUiState(player);
            switch (args[0].ToLowerInvariant())
            {
                case "close":
                    DestroyAdminUi(player);
                    return;

                case "tab":
                    if (args.Length > 1)
                        state.Tab = args[1].ToLowerInvariant();
                    break;

                case "reload":
                    LoadConfig();
                    InitializeLoadedConfig(false);
                    SendReply(player, "SmartChatBot config reloaded. Run oxide.reload SmartChatBot to reschedule timed-message frequency changes.");
                    break;

                case "save":
                    SaveConfig();
                    SendReply(player, "SmartChatBot config saved.");
                    break;

                case "togglemsg":
                    if (TryGetInt(args, 1, out var messageGroupIndex) && TryGetInt(args, 2, out var messageIndex))
                        ToggleMessage(player, messageGroupIndex, messageIndex);
                    break;

                case "toggleresponse":
                    if (TryGetInt(args, 1, out var responseGroupIndex) && TryGetInt(args, 2, out var responseIndex))
                        ToggleResponse(player, responseGroupIndex, responseIndex);
                    break;

                case "broadcast":
                    if (TryGetInt(args, 1, out messageGroupIndex) && TryGetInt(args, 2, out messageIndex))
                        BroadcastMessage(player, messageGroupIndex, messageIndex);
                    break;

                case "testresponse":
                    if (TryGetInt(args, 1, out responseGroupIndex) && TryGetInt(args, 2, out responseIndex))
                        TestResponse(player, responseGroupIndex, responseIndex);
                    break;
            }

            ShowAdminUi(player);
        }

        [ConsoleCommand("smartchatbot.adminset")]
        private void CmdSmartChatBotAdminSet(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !CanAdmin(player))
                return;

            var args = arg.Args ?? new string[0];
            if (args.Length < 2)
                return;

            var value = JoinArgs(args, 1).Trim();
            switch (args[0].ToLowerInvariant())
            {
                case "usercooldown":
                    _config.Cooldown = value;
                    ConvertToSeconds(_config.Cooldown, out _config.ParsedCooldown);
                    break;

                case "globalcooldown":
                    _config.CooldownGlobal = value;
                    ConvertToSeconds(_config.CooldownGlobal, out _config.ParsedCooldownGlobal);
                    break;

                case "frequency":
                    if (args.Length >= 3 && int.TryParse(args[1], out var groupIndex))
                    {
                        value = JoinArgs(args, 2).Trim();
                        if (TryGetMessageGroup(groupIndex, out var group))
                        {
                            group.Frequency = value;
                            ConvertToSeconds(group.Frequency, out group.ParsedFrequency);
                        }
                    }
                    break;

                case "message":
                    if (args.Length >= 4 && int.TryParse(args[1], out groupIndex) && int.TryParse(args[2], out var messageIndex))
                    {
                        value = JoinArgs(args, 3).Trim();
                        if (TryGetMessage(groupIndex, messageIndex, out var message))
                            message.Message = value;
                    }
                    break;

                case "answer":
                    if (args.Length >= 5 && int.TryParse(args[1], out groupIndex) && int.TryParse(args[2], out var responseIndex) && int.TryParse(args[3], out var answerIndex))
                    {
                        value = JoinArgs(args, 4).Trim();
                        if (TryGetResponse(groupIndex, responseIndex, out var response) && answerIndex >= 0 && answerIndex < response.Answers.Count)
                            response.Answers[answerIndex] = value;
                    }
                    break;
            }

            SaveConfig();
            ShowAdminUi(player);
        }
 
        // ReSharper disable once SuggestBaseTypeForParameter
        private object OnChatPlusMessage(Dictionary<string, object> data)
        {
            PrintDebug("Called OnChatPlusMessage");
            object playerObj, messageObj;
            if (!data.TryGetValue("Player", out playerObj) || !data.TryGetValue("Message", out messageObj))
                return null;
            
            var player = BasePlayer.Find((playerObj as IPlayer)?.Id);
            var message = messageObj?.ToString();
            if (player == null || !player.IsConnected || string.IsNullOrEmpty(message))
                return null;
            
            return HandleChatMessage(player, message);
        }

        private object OnChatPlusMessage(BasePlayer player, string message)
        {
            PrintDebug("Called OnChatPlusMessage");
            return HandleChatMessage(player, message);
        }

        // ReSharper disable once SuggestBaseTypeForParameter
        private object OnBetterChat(Dictionary<string, object> data)
        {
            PrintDebug("Called OnBetterChat");
            object playerObj, messageObj, chatChannel;
            if (!data.TryGetValue("Player", out playerObj) || !data.TryGetValue("Message", out messageObj) ||
                !data.TryGetValue("ChatChannel", out chatChannel))
            {
                return null;
            }

            if ((Chat.ChatChannel) chatChannel != Chat.ChatChannel.Global)
            {
                return null;
            }
            
            var player = BasePlayer.Find((playerObj as IPlayer)?.Id);
            var message = messageObj?.ToString();
            if (player == null || !player.IsConnected || string.IsNullOrEmpty(message))
                return null;

            if (HandleChatMessage(player, message) == null)
                return null;
            
            data["CancelOption"] = 2;
            return null;
        }

        private object OnPlayerChat(BasePlayer player, string message, Chat.ChatChannel channel)
        {
            PrintDebug("Called OnPlayerChat");
            if (player == null || string.IsNullOrEmpty(message) || channel != Chat.ChatChannel.Global)
                return null;

            return HandleChatMessage(player, message);
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (_config.JoiningMessage.Count > 0 && _config.JoiningMessageEnabled)
            {
                var message =
                    new StringBuilder(_config.JoiningMessage[Random.Next(0, _config.JoiningMessage.Count)]);
                var ip = player.net.connection.ipaddress.Substring(0, player.net.connection.ipaddress.LastIndexOf(':'));
                message = message.Replace("{name}", player.displayName).Replace("{id}", player.UserIDString)
                    .Replace("{ip}", ip);

                var usePlayer = _config.JoiningMessageSelfEnabled ? null : player;
                if (message.ToString().IndexOf("{country}", StringComparison.CurrentCultureIgnoreCase) != -1 || message.ToString().IndexOf("{countrycode}", StringComparison.CurrentCultureIgnoreCase) != -1)
                    HandleCountryMessage(message, ip, usePlayer);
                else Publish(message.ToString(), player2: usePlayer);
            }

            if (_config.WelcomeMessage.Count <= 0 || !_config.WelcomeMessageEnabled) return;
            
            {
                var message = new StringBuilder(_config.WelcomeMessage[Random.Next(0, _config.WelcomeMessage.Count)]);
                var ip = player.net.connection.ipaddress.Substring(0, player.net.connection.ipaddress.LastIndexOf(':'));
                message = message.Replace("{name}", player.displayName).Replace("{id}", player.UserIDString)
                    .Replace("{ip}", ip);
                
                if (message.ToString().IndexOf("{country}", StringComparison.CurrentCultureIgnoreCase) != -1 || message.ToString().IndexOf("{countrycode}", StringComparison.CurrentCultureIgnoreCase) != -1)
                    HandleCountryMessage(message, ip, player, false);
                else Publish(message.ToString(), player2: player, exclude: false);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (_config.LeavingMessage.Count <= 0 || !_config.LeavingMessageEnabled) return;
            
            var message =
                new StringBuilder(_config.LeavingMessage[Random.Next(0, _config.LeavingMessage.Count)]);
            var ip = player.net.connection.ipaddress.Substring(0, player.net.connection.ipaddress.LastIndexOf(':'));
            message = message.Replace("{name}", player.displayName).Replace("{id}", player.UserIDString)
                .Replace("{ip}", ip).Replace("{reason}", reason);

            var usePlayer = _config.LeavingMessageSelfEnabled ? null : player;
            if (message.ToString().IndexOf("{country}", StringComparison.CurrentCultureIgnoreCase) != -1 ||
                message.ToString().IndexOf("{countrycode}", StringComparison.CurrentCultureIgnoreCase) != -1)
                HandleCountryMessage(message, ip, usePlayer);
            else Publish(message.ToString(), string.Empty, usePlayer);
        }

        #endregion
        
        #region Helpers

        private void HandleCountryMessage(StringBuilder message, string ip, BasePlayer player, bool exclude = true)
        {
            webrequest.Enqueue(CountryRequest.Replace("{ip}", ip), string.Empty, (status, result) =>
            {
                PrintDebug("Requested a country!");
                try
                {
                    // ReSharper disable once InvertIf
                    if (status == 200)
                    {
                        var info = JsonConvert.DeserializeObject<Dictionary<string, object>>(result);
                        if ((string) info["status"] == "success")
                        {
                            message = message.Replace("{country}", (string) info["country"])
                                .Replace("{countrycode}", (string) info["countryCode"]);
                        }
                    }
                }
                catch (Exception e)
                {
                    PrintError(e.ToString());
                }
                finally
                {
                    Publish(message.ToString(), string.Empty, player, exclude);
                }
            }, this);
        }

        private void PrintDebug(string message)
        {
            if (!_config.Debug) return;
            Puts($"DEBUG: {message}");
        }

        // ReSharper disable once SuggestBaseTypeForParameter
        private void Publish(string message, string perm = "", BasePlayer player2 = null, bool exclude = true)
        {
            PrintDebug($"Called Publish (Message: {message}; Permission: {perm})");
            var notRequirePermission = string.IsNullOrEmpty(perm);
            message = FormatMessage(message);

            if (exclude)
            {
                var players = BasePlayer.activePlayerList;
                var playersCount = players.Count;
                for (var i = 0; i < playersCount; i++)
                {
                    var player = players[i];
                    if (player != player2 &&
                        (notRequirePermission || permission.UserHasPermission(player.UserIDString, perm)))
                        SendMessage(player, RunPlaceholders(player.IPlayer, message));
                }
            }
            else
                SendMessage(player2, RunPlaceholders(player2?.IPlayer, message));
        }

        private void Publish(BasePlayer player, string message) => SendMessage(player, RunPlaceholders(player.IPlayer, FormatMessage(message)));

        private void SendMessage(BasePlayer player, string message)
        {
            PrintDebug($"SendMessage: {message}");
            player.SendConsoleCommand("chat.add", 2, _config.ChatSteamId, message);
        }

        private string FormatMessage(string message) => _config.ShowPrefix ? _config.Prefix + message : message;

        private string RunPlaceholders(IPlayer player, string message)
        {
            if (PlaceholderAPI == null || !PlaceholderAPI.IsLoaded)
                return message;
            
            var builder = new StringBuilder(message);
            PlaceholderAPI.CallHook("ProcessPlaceholders", player, builder);
            return builder.ToString();
        }

        private void HandleAdminChatCommand(BasePlayer player, string[] args)
        {
            if (!CanAdmin(player))
            {
                SendReply(player, "You do not have permission to use SmartChatBot admin.");
                return;
            }

            if (args != null && args.Length > 0)
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "reload":
                        LoadConfig();
                        InitializeLoadedConfig(false);
                        SendReply(player, "SmartChatBot config reloaded. Run oxide.reload SmartChatBot to reschedule timed-message frequency changes.");
                        return;

                    case "save":
                        SaveConfig();
                        SendReply(player, "SmartChatBot config saved.");
                        return;

                    case "close":
                        DestroyAdminUi(player);
                        return;

                    case "help":
                        SendReply(player, "SmartChatBot admin: /smartchatbot or /scb opens the UI. Console UI actions use smartchatbot.adminui and smartchatbot.adminset.");
                        return;
                }
            }

            ShowAdminUi(player);
        }

        private bool CanAdmin(BasePlayer player)
        {
            return player != null && (player.IsAdmin || permission.UserHasPermission(player.UserIDString, AdminPermission));
        }

        private AdminUiState GetAdminUiState(BasePlayer player)
        {
            if (!_adminUiStates.TryGetValue(player.userID, out var state))
            {
                state = new AdminUiState();
                _adminUiStates[player.userID] = state;
            }

            return state;
        }

        private void DestroyAdminUi(BasePlayer player)
        {
            if (player == null)
                return;

            CuiHelper.DestroyUi(player, AdminUiName);
        }

        private bool TryGetMessageGroup(int groupIndex, out AutoMessageGroup group)
        {
            group = null;
            if (groupIndex < 0 || groupIndex >= _config.AutoMessages.Count)
                return false;

            group = _config.AutoMessages[groupIndex];
            return group != null;
        }

        private bool TryGetMessage(int groupIndex, int messageIndex, out AutoMessage message)
        {
            message = null;
            if (!TryGetMessageGroup(groupIndex, out var group) || messageIndex < 0 || messageIndex >= group.AutoMessages.Count)
                return false;

            message = group.AutoMessages[messageIndex];
            return message != null;
        }

        private bool TryGetResponse(int groupIndex, int responseIndex, out AutoResponse response)
        {
            response = null;
            if (groupIndex < 0 || groupIndex >= _config.AutoResponses.Count)
                return false;

            var group = _config.AutoResponses[groupIndex];
            if (group == null || responseIndex < 0 || responseIndex >= group.AutoResponses.Count)
                return false;

            response = group.AutoResponses[responseIndex];
            return response != null;
        }

        private static bool TryGetInt(string[] args, int index, out int value)
        {
            value = 0;
            return args != null && index >= 0 && index < args.Length && int.TryParse(args[index], out value);
        }

        private static string JoinArgs(string[] args, int startIndex)
        {
            if (args == null || startIndex >= args.Length)
                return string.Empty;

            return string.Join(" ", args.Skip(startIndex).ToArray());
        }

        private void ToggleMessage(BasePlayer player, int groupIndex, int messageIndex)
        {
            if (!TryGetMessage(groupIndex, messageIndex, out var message))
                return;

            message.Enabled = !message.Enabled;
            SaveConfig();
            SendReply(player, $"SmartChatBot timed message {messageIndex + 1} is now {(message.Enabled ? "enabled" : "disabled")}.");
        }

        private void ToggleResponse(BasePlayer player, int groupIndex, int responseIndex)
        {
            if (!TryGetResponse(groupIndex, responseIndex, out var response))
                return;

            response.Enabled = !response.Enabled;
            SaveConfig();
            SendReply(player, $"SmartChatBot response {responseIndex + 1} is now {(response.Enabled ? "enabled" : "disabled")}.");
        }

        private void BroadcastMessage(BasePlayer player, int groupIndex, int messageIndex)
        {
            if (!TryGetMessage(groupIndex, messageIndex, out var message))
                return;

            Publish(message.Message, string.Empty);
            SendReply(player, "SmartChatBot broadcast sent.");
        }

        private void TestResponse(BasePlayer player, int groupIndex, int responseIndex)
        {
            if (!TryGetResponse(groupIndex, responseIndex, out var response) || response.Answers.Count == 0)
                return;

            Publish(player, response.Answers[0]);
        }

        private static string Shorten(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value ?? string.Empty;

            return value.Substring(0, Math.Max(0, max - 3)) + "...";
        }

        private void ShowAdminUi(BasePlayer player)
        {
            DestroyAdminUi(player);

            var state = GetAdminUiState(player);
            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel
            {
                Image = { Color = "0.035 0.040 0.048 0.96", Material = "assets/content/ui/uibackgroundblur.mat" },
                RectTransform = { AnchorMin = "0.08 0.08", AnchorMax = "0.92 0.92" },
                CursorEnabled = true
            }, "Overlay", AdminUiName);

            AddUiLabel(container, root, "SmartChatBot Admin", 20, TextAnchor.MiddleLeft, "0.035 0.920", "0.44 0.975", "1 0.86 0.58 1");
            AddUiLabel(container, root, $"Timed {_config.AutoMessages.Sum(g => g.AutoMessages.Count)} | Responses {_config.AutoResponses.Sum(g => g.AutoResponses.Count)} | User {_config.Cooldown} | Global {_config.CooldownGlobal}", 10, TextAnchor.MiddleLeft, "0.035 0.885", "0.78 0.925", "0.66 0.74 0.80 1");
            AddUiButton(container, root, "Reload", "smartchatbot.adminui reload", "0.770 0.920", "0.845 0.968", "0.16 0.30 0.42 0.95", 10);
            AddUiButton(container, root, "Save", "smartchatbot.adminui save", "0.855 0.920", "0.915 0.968", "0.16 0.30 0.20 0.95", 10);
            AddUiButton(container, root, "X", "smartchatbot.adminui close", "0.925 0.920", "0.965 0.968", "0.55 0.12 0.10 0.95", 14);

            AddAdminTab(container, root, "Messages", "messages", state.Tab, 0.035f, 0.155f);
            AddAdminTab(container, root, "Responses", "responses", state.Tab, 0.165f, 0.285f);
            AddAdminTab(container, root, "Settings", "settings", state.Tab, 0.295f, 0.415f);
            AddAdminTab(container, root, "Commands", "commands", state.Tab, 0.425f, 0.545f);

            var body = container.Add(new CuiPanel
            {
                Image = { Color = "0.075 0.088 0.105 0.92" },
                RectTransform = { AnchorMin = "0.035 0.055", AnchorMax = "0.965 0.830" }
            }, root);

            switch (state.Tab)
            {
                case "responses":
                    RenderResponsesTab(container, body);
                    break;
                case "settings":
                    RenderSettingsTab(container, body);
                    break;
                case "commands":
                    RenderCommandsTab(container, body);
                    break;
                default:
                    state.Tab = "messages";
                    RenderMessagesTab(container, body);
                    break;
            }

            CuiHelper.AddUi(player, container);
        }

        private void RenderMessagesTab(CuiElementContainer container, string body)
        {
            AddUiLabel(container, body, "Timed Auto Messages", 16, TextAnchor.MiddleLeft, "0.035 0.900", "0.45 0.965", "1 0.86 0.58 1");
            AddUiLabel(container, body, "Messages rotate on the group frequency. Edit text, toggle rows, or send a one-off test broadcast.", 10, TextAnchor.MiddleLeft, "0.035 0.850", "0.90 0.900", "0.66 0.74 0.80 1");

            var y = 0.765f;
            for (var groupIndex = 0; groupIndex < _config.AutoMessages.Count; groupIndex++)
            {
                var group = _config.AutoMessages[groupIndex];
                AddUiLabel(container, body, $"Group {groupIndex + 1} frequency", 9, TextAnchor.MiddleLeft, "0.045 " + FormatUiFloat(y + 0.050f), "0.190 " + FormatUiFloat(y + 0.090f), "0.70 0.78 0.84 1");
                AddUiInput(container, body, group.Frequency, "smartchatbot.adminset frequency " + groupIndex, "0.195 " + FormatUiFloat(y + 0.050f), "0.310 " + FormatUiFloat(y + 0.092f), 9);

                for (var messageIndex = 0; messageIndex < group.AutoMessages.Count && messageIndex < 10; messageIndex++)
                {
                    var message = group.AutoMessages[messageIndex];
                    var rowY = y - (messageIndex * 0.067f);
                    AddUiPanel(container, body, "0.045 " + FormatUiFloat(rowY), "0.955 " + FormatUiFloat(rowY + 0.055f), message.Enabled ? "0.100 0.125 0.150 0.95" : "0.075 0.080 0.090 0.90");
                    AddUiLabel(container, body, $"{messageIndex + 1}.", 9, TextAnchor.MiddleCenter, "0.052 " + FormatUiFloat(rowY + 0.008f), "0.082 " + FormatUiFloat(rowY + 0.047f), "1 0.86 0.58 1");
                    AddUiInput(container, body, Shorten(message.Message, 120), "smartchatbot.adminset message " + groupIndex + " " + messageIndex, "0.090 " + FormatUiFloat(rowY + 0.008f), "0.670 " + FormatUiFloat(rowY + 0.047f), 8);
                    AddUiButton(container, body, message.Enabled ? "On" : "Off", "smartchatbot.adminui togglemsg " + groupIndex + " " + messageIndex, "0.690 " + FormatUiFloat(rowY + 0.008f), "0.755 " + FormatUiFloat(rowY + 0.047f), message.Enabled ? "0.15 0.32 0.18 0.95" : "0.42 0.14 0.10 0.95", 8);
                    AddUiButton(container, body, "Send", "smartchatbot.adminui broadcast " + groupIndex + " " + messageIndex, "0.770 " + FormatUiFloat(rowY + 0.008f), "0.835 " + FormatUiFloat(rowY + 0.047f), "0.42 0.18 0.12 0.95", 8);
                }
            }
        }

        private void RenderResponsesTab(CuiElementContainer container, string body)
        {
            AddUiLabel(container, body, "Keyword Responses", 16, TextAnchor.MiddleLeft, "0.035 0.900", "0.45 0.965", "1 0.86 0.58 1");
            AddUiLabel(container, body, "Rows show trigger words and the first answer. Toggle or test a response privately.", 10, TextAnchor.MiddleLeft, "0.035 0.850", "0.90 0.900", "0.66 0.74 0.80 1");

            var y = 0.770f;
            var rendered = 0;
            for (var groupIndex = 0; groupIndex < _config.AutoResponses.Count; groupIndex++)
            {
                var group = _config.AutoResponses[groupIndex];
                for (var responseIndex = 0; responseIndex < group.AutoResponses.Count && rendered < 10; responseIndex++, rendered++)
                {
                    var response = group.AutoResponses[responseIndex];
                    var rowY = y - (rendered * 0.070f);
                    var triggerText = response.Triggers.Count > 0 ? string.Join(", ", response.Triggers[0].Words.ToArray()) : "no triggers";
                    var answer = response.Answers.Count > 0 ? response.Answers[0] : "";
                    AddUiPanel(container, body, "0.045 " + FormatUiFloat(rowY), "0.955 " + FormatUiFloat(rowY + 0.058f), response.Enabled ? "0.100 0.125 0.150 0.95" : "0.075 0.080 0.090 0.90");
                    AddUiLabel(container, body, Shorten(triggerText, 48), 9, TextAnchor.MiddleLeft, "0.060 " + FormatUiFloat(rowY + 0.030f), "0.350 " + FormatUiFloat(rowY + 0.055f), "1 0.86 0.58 1");
                    AddUiInput(container, body, Shorten(answer, 100), "smartchatbot.adminset answer " + groupIndex + " " + responseIndex + " 0", "0.365 " + FormatUiFloat(rowY + 0.008f), "0.690 " + FormatUiFloat(rowY + 0.048f), 8);
                    AddUiButton(container, body, response.Enabled ? "On" : "Off", "smartchatbot.adminui toggleresponse " + groupIndex + " " + responseIndex, "0.710 " + FormatUiFloat(rowY + 0.010f), "0.775 " + FormatUiFloat(rowY + 0.048f), response.Enabled ? "0.15 0.32 0.18 0.95" : "0.42 0.14 0.10 0.95", 8);
                    AddUiButton(container, body, "Test", "smartchatbot.adminui testresponse " + groupIndex + " " + responseIndex, "0.790 " + FormatUiFloat(rowY + 0.010f), "0.855 " + FormatUiFloat(rowY + 0.048f), "0.42 0.18 0.12 0.95", 8);
                    AddUiLabel(container, body, response.SendPublic ? "Public" : "Private", 8, TextAnchor.MiddleCenter, "0.870 " + FormatUiFloat(rowY + 0.010f), "0.935 " + FormatUiFloat(rowY + 0.048f), "0.66 0.74 0.80 1");
                }
            }
        }

        private void RenderSettingsTab(CuiElementContainer container, string body)
        {
            AddUiLabel(container, body, "Settings", 16, TextAnchor.MiddleLeft, "0.035 0.900", "0.45 0.965", "1 0.86 0.58 1");
            AddUiLabel(container, body, "Cooldowns accept values like 15s, 5m, or 1h. Save writes oxide/config/SmartChatBot.json.", 10, TextAnchor.MiddleLeft, "0.035 0.850", "0.90 0.900", "0.66 0.74 0.80 1");

            AddUiLabel(container, body, "Per-player response cooldown", 11, TextAnchor.MiddleLeft, "0.060 0.720", "0.350 0.770", "0.70 0.78 0.84 1");
            AddUiInput(container, body, _config.Cooldown, "smartchatbot.adminset usercooldown", "0.365 0.720", "0.520 0.770", 10);
            AddUiLabel(container, body, "Global response cooldown", 11, TextAnchor.MiddleLeft, "0.060 0.640", "0.350 0.690", "0.70 0.78 0.84 1");
            AddUiInput(container, body, _config.CooldownGlobal, "smartchatbot.adminset globalcooldown", "0.365 0.640", "0.520 0.690", 10);
            AddUiLabel(container, body, "Response delay", 11, TextAnchor.MiddleLeft, "0.060 0.560", "0.350 0.610", "0.70 0.78 0.84 1");
            AddUiLabel(container, body, _config.MinTime + "s - " + _config.MaxTime + "s", 11, TextAnchor.MiddleLeft, "0.365 0.560", "0.620 0.610", "1 1 1 1");
            AddUiLabel(container, body, "Chat system", 11, TextAnchor.MiddleLeft, "0.060 0.480", "0.350 0.530", "0.70 0.78 0.84 1");
            AddUiLabel(container, body, _config.ChatSystem == 2 ? "Better Chat" : _config.ChatSystem == 1 ? "Chat Plus" : "Default Rust chat", 11, TextAnchor.MiddleLeft, "0.365 0.480", "0.620 0.530", "1 1 1 1");
            AddUiLabel(container, body, "Admin permission", 11, TextAnchor.MiddleLeft, "0.060 0.400", "0.350 0.450", "0.70 0.78 0.84 1");
            AddUiLabel(container, body, AdminPermission, 11, TextAnchor.MiddleLeft, "0.365 0.400", "0.620 0.450", "1 1 1 1");
        }

        private void RenderCommandsTab(CuiElementContainer container, string body)
        {
            AddUiLabel(container, body, "Commands", 16, TextAnchor.MiddleLeft, "0.035 0.900", "0.45 0.965", "1 0.86 0.58 1");
            var lines = new[]
            {
                "/smartchatbot or /scb - open this admin UI",
                "/smartchatbot reload - reload oxide/config/SmartChatBot.json",
                "/smartchatbot save - save the current in-memory config",
                "smartchatbot.adminui reload|save|close - console UI actions",
                "smartchatbot.adminui togglemsg <group> <message> - enable/disable a timed message",
                "smartchatbot.adminui toggleresponse <group> <response> - enable/disable a keyword response",
                "smartchatbot.adminui broadcast <group> <message> - send one timed message now",
                "smartchatbot.adminui testresponse <group> <response> - send first answer privately",
                "smartchatbot.adminset message <group> <message> <text> - edit a timed message",
                "smartchatbot.adminset answer <group> <response> <answer> <text> - edit an answer",
                "smartchatbot.adminset usercooldown <time> / globalcooldown <time> - edit cooldowns"
            };

            for (var i = 0; i < lines.Length; i++)
            {
                var y = 0.800f - (i * 0.060f);
                AddUiPanel(container, body, "0.055 " + FormatUiFloat(y), "0.945 " + FormatUiFloat(y + 0.045f), "0.100 0.125 0.150 0.88");
                AddUiLabel(container, body, lines[i], 9, TextAnchor.MiddleLeft, "0.075 " + FormatUiFloat(y), "0.925 " + FormatUiFloat(y + 0.045f), "0.78 0.84 0.88 1");
            }
        }

        private void AddAdminTab(CuiElementContainer container, string parent, string label, string key, string selected, float xMin, float xMax)
        {
            AddUiButton(container, parent, label, "smartchatbot.adminui tab " + key, FormatUiFloat(xMin) + " 0.842", FormatUiFloat(xMax) + " 0.882", selected == key ? "0.42 0.18 0.12 0.95" : "0.13 0.18 0.24 0.95", 9);
        }

        private void AddUiPanel(CuiElementContainer container, string parent, string anchorMin, string anchorMax, string color)
        {
            container.Add(new CuiPanel
            {
                Image = { Color = color },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);
        }

        private void AddUiLabel(CuiElementContainer container, string parent, string text, int size, TextAnchor align, string anchorMin, string anchorMax, string color)
        {
            container.Add(new CuiLabel
            {
                Text = { Text = text ?? string.Empty, FontSize = size, Align = align, Color = color },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);
        }

        private void AddUiButton(CuiElementContainer container, string parent, string text, string command, string anchorMin, string anchorMax, string color, int size)
        {
            container.Add(new CuiButton
            {
                Button = { Command = command ?? string.Empty, Color = color },
                Text = { Text = text ?? string.Empty, FontSize = size, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);
        }

        private void AddUiInput(CuiElementContainer container, string parent, string text, string command, string anchorMin, string anchorMax, int size)
        {
            container.Add(new CuiElement
            {
                Name = CuiHelper.GetGuid(),
                Parent = parent,
                Components =
                {
                    new CuiImageComponent { Color = "0.015 0.018 0.024 0.92" },
                    new CuiInputFieldComponent
                    {
                        Text = text ?? string.Empty,
                        Command = string.IsNullOrEmpty(command) ? string.Empty : command + " ",
                        FontSize = size,
                        Align = TextAnchor.MiddleLeft,
                        Color = "1 1 1 1",
                        CharsLimit = 180
                    },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax }
                }
            });
        }

        private static string FormatUiFloat(float value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }
        
        #endregion
        
        #region Parsers
        
        private static readonly Regex RegexStringTime = new Regex(@"(\d+)([dhms])", RegexOptions.Compiled);
        private static bool ConvertToSeconds(string time, out uint seconds)
        {
            seconds = 0;
            if (time == "0" || string.IsNullOrEmpty(time)) return true;
            var matches = RegexStringTime.Matches(time);
            if (matches.Count == 0) return false;
            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                // ReSharper disable once SwitchStatementMissingSomeCases
                switch (match.Groups[2].Value)
                {
                    case "d":
                    {
                        seconds += uint.Parse(match.Groups[1].Value) * 24 * 60 * 60;
                        break;
                    }
                    case "h":
                    {
                        seconds += uint.Parse(match.Groups[1].Value) * 60 * 60;
                        break;
                    }
                    case "m":
                    {
                        seconds += uint.Parse(match.Groups[1].Value) * 60;
                        break;
                    }
                    case "s":
                    {
                        seconds += uint.Parse(match.Groups[1].Value);
                        break;
                    }
                }
            }
            return true;
        }
        
        #endregion

        #region Automated Messages

        private object HandleChatMessage(BasePlayer player, string msg)
        {
            PrintDebug("Called HandleChatMessage");

            var response = Interface.GetMod().CallHook("CanSmartHandleMessage", player, msg);
            if (response != null)
                return null;
            
            var autoResponseGroups = _config.AutoResponses;
            var autoResponseGroupsCount = autoResponseGroups.Count;

            var tNow = Time.GetUnixTimestamp();
            uint lastSent;
            _lastSent.TryGetValue(player, out lastSent);

            PrintDebug($"tNow: {tNow}");
            PrintDebug($"lastSent: {lastSent}");
            if (_config.ParsedCooldown != 0 && lastSent + _config.ParsedCooldown > tNow ||
                _config.ParsedCooldownGlobal != 0 && _lastSentGlobal + _config.ParsedCooldownGlobal > tNow)
                return null;

            var matched = false;
            var removeMessage = false;
            // Auto Response Groups
            for (var i1 = 0; i1 < autoResponseGroupsCount; i1++)
            {
                var autoResponseGroup = autoResponseGroups[i1];
                var perm = autoResponseGroup.Permission;
                if (!string.IsNullOrEmpty(perm) &&
                    !permission.UserHasPermission(player.UserIDString, autoResponseGroup.Permission))
                    continue;

                var autoResponses = autoResponseGroup.AutoResponses;
                var autoResponsesCount = autoResponses.Count;
                // Auto Responses
                for (var i2 = 0; i2 < autoResponsesCount; i2++)
                {
                    var autoResponse = autoResponses[i2];
                    if (!autoResponse.IsValid() || !autoResponse.Enabled)
                        continue;

                    var answersCount = autoResponse.Answers.Count;
                    var autoResponseTriggers = autoResponse.Triggers;
                    var autoResponseTriggersCount = autoResponseTriggers.Count;
                    // Triggers
                    for (var i3 = 0; i3 < autoResponseTriggersCount; i3++)
                    {
                        var trigger = autoResponseTriggers[i3];

                        float wordsCount = trigger.Words.Count;
                        ushort wordsMatches = 0;
                        var regex = trigger.Regex;
                        
                        // Each Word In Triggers
                        for (var i4 = 0; i4 < wordsCount; i4++)
                        {
                            if (regex && Regex.IsMatch(msg, trigger.Words[i4]) ||
                                msg.IndexOf(trigger.Words[i4], StringComparison.CurrentCultureIgnoreCase) != -1)
                                wordsMatches++;
                        }

                        var match = wordsMatches / wordsCount;
                        PrintDebug($"Matched: {match}");

                        if (wordsMatches / wordsCount < trigger.ContainedWordsPercentage) continue;

                        matched = true;
                        var answer = autoResponse.Answers[Random.Next(0, answersCount)];
                        PrintDebug("Matched message");

                        response = Interface.GetMod().CallHook("CanSmartAnswerMessage", player, msg, answer);
                        if (response != null)
                            return null;

                        if (_config.MinTime <= 0 && _config.MaxTime <= 0)
                            NextTick(() => TrySend(player, autoResponse.SendPublic, answer)); // Next Tick to be sure it's after player's message.
                        else
                            timer.Once(Mathf.Lerp(_config.MinTime, _config.MaxTime, (float) Random.NextDouble()), () => TrySend(player, autoResponse.SendPublic, answer));

                        if (autoResponse.RemoveMessage)
                            removeMessage = true;

                        break;
                    }

                    if (matched && !_config.MultipleAutoResponses)
                        break;
                }
            }

            if (matched)
            {
                _lastSent[player] = tNow;
                _lastSentGlobal = tNow;
                PrintDebug("Matched. Changing cooldown info.");
            }

            if (removeMessage)
                return false;
            
            return null;
        }

        private void TrySend(BasePlayer player, bool isPublic, string answer)
        {
            if (isPublic)
                Publish(answer, string.Empty);
            else
                Publish(player, answer);
        }

        private void HandleBroadcast(AutoMessageGroup group)
        {
            PrintDebug("Called HandleBroadcast");
            if (group.ActiveMessage > group.AutoMessages.Count - 1)
                group.ActiveMessage = 0;
            PrintDebug($"Active Message: {group.ActiveMessage}");

            var message = group.AutoMessages[group.ActiveMessage++];
            if (message.Enabled)
                Publish(message.Message, group.Permission);
        }

        #endregion
    }
}
