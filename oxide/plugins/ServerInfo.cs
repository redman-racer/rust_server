using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Server Info", "FastBurst/Raidlands", "0.6.3")]
    [Description("UI customizable server info with multiple tabs")]
    public sealed class ServerInfo : RustPlugin
    {
        private static Settings _settings;
        private static readonly Dictionary<ulong, PlayerInfoState> PlayerActiveTabs = new Dictionary<ulong, PlayerInfoState>();
        private static readonly Permission Permission = Interface.GetMod().GetLibrary<Permission>();
        private const string CommandsTabButtonText = "/commands";
        private const int CommandRowsPerPage = 15;

        protected override void LoadDefaultConfig()
        {
            Config.Set("settings", Settings.CreateDefault());
        }

        private void OnServerInitialized()
        {
            LoadConfig();
            var configFileName = Manager.ConfigPath + "/server_info_text.json";

            _settings = null;
            try
            {
                var settingsDict = Config.Get("settings") as Dictionary<string, object>;
                _settings = JsonConvert.DeserializeObject<Settings>(JsonConvert.SerializeObject(settingsDict));
            }
            catch (Exception)
            {
                Puts("ServerInfo: Failed to load config");
                return;
            }

            _settings = _settings ?? Settings.CreateDefault();

            if (!_settings.UpgradedConfig && Config.Exists(configFileName))
            {
                try
                {
                    Puts("ServerInfo: Upgrading settings from server_info_text.json");
                    _settings = Config.ReadObject<Settings>(configFileName);
                    _settings.UpgradedConfig = true;
                    Puts("ServerInfo: Successfully upgraded config");
                }
                catch (Exception)
                {
                    Puts("ServerInfo: Failed to upgrade config. Manual editing is required.");
                    Puts("ServerInfo: Copy your settings by parts to new config in ServerInfo.json");
                }
            }

            _settings = _settings ?? Settings.CreateDefault();
            _settings.StaffGroups = DistinctConfigValues(_settings.StaffGroups);
            _settings.AdminGroups = DistinctConfigValues(_settings.AdminGroups);

            foreach (var player in BasePlayer.activePlayerList)
                AddHelpButton(player);

            Config.Set("settings", _settings);
            SaveConfig();
        }

        void Unload()
        {
            foreach (var playerActiveTab in PlayerActiveTabs)
            {
                var player = BasePlayer.activePlayerList.FirstOrDefault(f => f.userID == playerActiveTab.Key);
                if (player == null)
                    continue;

                CuiHelper.DestroyUi(player, playerActiveTab.Value.MainPanelName);
                CuiHelper.DestroyUi(player, playerActiveTab.Value.ChatHelpButtonName);
            }

            PlayerActiveTabs.Clear();
        }

        private static List<string> DistinctConfigValues(IEnumerable<string> values)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var value in values ?? Enumerable.Empty<string>())
            {
                var normalized = (value ?? string.Empty).Trim();

                if (normalized.Length > 0 && seen.Add(normalized))
                {
                    result.Add(normalized);
                }
            }

            return result;
        }

        [ConsoleCommand("changetab")]
        private void ChangeTab(ConsoleSystem.Arg arg)
        {
            if (arg.Connection == null || arg.Connection.player == null || !arg.HasArgs(4) || _settings == null)
                return;

            var player = arg.Connection.player as BasePlayer;
            if (player == null)
                return;

            if (!PlayerActiveTabs.ContainsKey(player.userID.Get()))
                return;

            var state = PlayerActiveTabs[player.userID.Get()];
            var previousTabIndex = state.ActiveTabIndex;
            var tabToChangeTo = arg.GetInt(0, 65535);

            if (previousTabIndex == tabToChangeTo)
                return;

            var tabToSelectIndex = arg.GetInt(0);
            var activeButtonName = arg.GetString(1);
            var tabToSelectButtonName = arg.GetString(2);
            var mainPanelName = arg.GetString(3);

            var allowedTabs = GetAllowedTabs(player);
            if (tabToSelectIndex < 0 || tabToSelectIndex >= allowedTabs.Count)
                return;

            CuiHelper.DestroyUi(player, PlayerActiveTabs[player.userID.Get()].ActiveTabContentPanelName);
            CuiHelper.DestroyUi(player, activeButtonName);
            CuiHelper.DestroyUi(player, tabToSelectButtonName);

            var tabToSelect = allowedTabs[tabToSelectIndex];
            state.ActiveTabIndex = tabToSelectIndex;
            state.ActiveSubTabIndex = 0;
            state.PageIndex = 0;

            var container = new CuiElementContainer();
            var tabContentPanelName = CreateTabContent(tabToSelect, container, mainPanelName, 0, state.ActiveSubTabIndex);
            var newActiveButtonName = AddActiveButton(tabToSelectIndex, tabToSelect, container, mainPanelName);
            if (previousTabIndex >= 0 && previousTabIndex < allowedTabs.Count)
                AddNonActiveButton(previousTabIndex, container, allowedTabs[previousTabIndex], mainPanelName, newActiveButtonName);

            PlayerActiveTabs[player.userID.Get()].ActiveTabContentPanelName = tabContentPanelName;

            SendUI(player, container);
        }

        [ConsoleCommand("changesubtab")]
        private void ChangeSubTab(ConsoleSystem.Arg arg)
        {
            if (arg.Connection == null || arg.Connection.player == null || !arg.HasArgs(2) || _settings == null)
                return;

            var player = arg.Connection.player as BasePlayer;
            if (player == null)
                return;

            PlayerInfoState playerInfoState;
            if (!PlayerActiveTabs.TryGetValue(player.userID.Get(), out playerInfoState) || playerInfoState == null)
                return;

            var allowedTabs = GetAllowedTabs(player);
            if (playerInfoState.ActiveTabIndex < 0 || playerInfoState.ActiveTabIndex >= allowedTabs.Count)
                return;

            var currentTab = allowedTabs[playerInfoState.ActiveTabIndex];
            if (!HasSubTabs(currentTab))
                return;

            var subTabToSelectIndex = arg.GetInt(0, 65535);
            if (subTabToSelectIndex < 0 || subTabToSelectIndex >= currentTab.SubTabs.Count)
                return;

            var currentTabContentPanelName = playerInfoState.ActiveTabContentPanelName;
            var mainPanelName = arg.GetString(1);

            CuiHelper.DestroyUi(player, currentTabContentPanelName);

            playerInfoState.ActiveSubTabIndex = subTabToSelectIndex;
            playerInfoState.PageIndex = 0;

            var container = new CuiElementContainer();
            var tabContentPanelName = CreateTabContent(currentTab, container, mainPanelName, 0, subTabToSelectIndex);
            playerInfoState.ActiveTabContentPanelName = tabContentPanelName;

            SendUI(player, container);
        }

        [ConsoleCommand("changepage")]
        private void ChangePage(ConsoleSystem.Arg arg)
        {
            if (arg.Connection == null || arg.Connection.player == null || !arg.HasArgs(2) || _settings == null)
                return;

            var player = arg.Connection.player as BasePlayer;
            if (player == null)
                return;

            if (!PlayerActiveTabs.ContainsKey(player.userID.Get()))
                return;

            var playerInfoState = PlayerActiveTabs[player.userID.Get()];
            var allowedTabs = GetAllowedTabs(player);
            if (playerInfoState.ActiveTabIndex < 0 || playerInfoState.ActiveTabIndex >= allowedTabs.Count)
                return;

            var currentTab = allowedTabs[playerInfoState.ActiveTabIndex];
            var currentContentTab = GetActiveContentTab(currentTab, playerInfoState.ActiveSubTabIndex);
            var currentPageIndex = playerInfoState.PageIndex;

            var pageToChangeTo = arg.GetInt(0, 65535);
            var currentTabContentPanelName = playerInfoState.ActiveTabContentPanelName;
            var mainPanelName = arg.GetString(1);

            if (pageToChangeTo == currentPageIndex)
                return;
            if (pageToChangeTo < 0 || pageToChangeTo >= currentContentTab.Pages.Count)
                return;

            CuiHelper.DestroyUi(player, currentTabContentPanelName);

            playerInfoState.PageIndex = pageToChangeTo;

            var container = new CuiElementContainer();
            var tabContentPanelName = CreateTabContent(currentTab, container, mainPanelName, pageToChangeTo, playerInfoState.ActiveSubTabIndex);
            PlayerActiveTabs[player.userID.Get()].ActiveTabContentPanelName = tabContentPanelName;

            SendUI(player, container);
        }

        [ConsoleCommand("infoclose")]
        private void CloseInfo(ConsoleSystem.Arg arg)
        {
            if (arg.Connection == null || arg.Connection.player == null || !arg.HasArgs() || _settings == null)
                return;

            var player = arg.Connection.player as BasePlayer;
            if (player == null)
                return;

            if (!PlayerActiveTabs.ContainsKey(player.userID.Get()))
                return;

            const string defaultName = "defaultString";
            var mainPanelName = arg.GetString(0, defaultName);

            if (mainPanelName.Equals(defaultName, StringComparison.OrdinalIgnoreCase))
                return;

            PlayerInfoState state;
            PlayerActiveTabs.TryGetValue(player.userID.Get(), out state);
            if (state == null)
                return;

            CuiHelper.DestroyUi(player, mainPanelName);

            state.ActiveTabIndex = _settings.TabToOpenByDefault;
            state.MainPanelName = string.Empty;
            state.ActiveSubTabIndex = 0;
            state.PageIndex = 0;
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || _settings == null)
                return;

            if (player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
            {
                timer.Once(2, () => OnPlayerConnected(player));
                return;
            }

            PlayerInfoState state;
            PlayerActiveTabs.TryGetValue(player.userID.Get(), out state);

            if (state == null)
            {
                state = new PlayerInfoState(_settings);
                PlayerActiveTabs.Add(player.userID.Get(), state);
            }

            AddHelpButton(player);

            if (!state.InfoShownOnLogin)
                return;
            if (state.InfoShownOnLoginOnce && checkedPlayers.Contains(player.userID.Get()))
                return;
            ShowInfo(player, string.Empty, new string[0]);
            if (state.InfoShownOnLoginOnce)
                checkedPlayers.Add(player.userID.Get());
        }

        List<ulong> checkedPlayers = new List<ulong>();

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null || _settings == null)
                return;

            PlayerInfoState state;
            PlayerActiveTabs.TryGetValue(player.userID.Get(), out state);

            if (state == null)
                return;

            CuiHelper.DestroyUi(player, state.MainPanelName);

            state.ActiveTabIndex = _settings.TabToOpenByDefault;
            state.ActiveSubTabIndex = 0;
            state.MainPanelName = string.Empty;
            state.PageIndex = 0;
        }

        private static void AddHelpButton(BasePlayer player)
        {
            if (!_settings.HelpButton.IsEnabled || _settings == null)
                return;

            var container = new CuiElementContainer();
            var helpChatButton = CreateHelpButton();
            var helpButtonName = container.Add(helpChatButton);
            if (!PlayerActiveTabs.ContainsKey(player.userID.Get()))
                PlayerActiveTabs[player.userID.Get()] = new PlayerInfoState(_settings);

            PlayerActiveTabs[player.userID.Get()].ChatHelpButtonName = helpButtonName;
            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("info")]
        private void ShowConsoleInfo(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Connection == null || arg.Connection.player == null || _settings == null)
                return;

            var player = arg.Connection.player as BasePlayer;
            if (player == null)
                return;
            OpenInfo(player);
        }

        [ChatCommand("info")]
        private void ShowInfo(BasePlayer player, string command, string[] args)
        {
            OpenInfo(player, GetRequestedTabIndex(args));
        }

        [ChatCommand("help")]
        private void ShowHelp(BasePlayer player, string command, string[] args)
        {
            OpenInfo(player);
        }

        [ChatCommand("commands")]
        private void ShowCommands(BasePlayer player, string command, string[] args)
        {
            OpenInfo(player, null, CommandsTabButtonText);
        }

        private void OpenInfo(BasePlayer player, int? requestedTabIndex = null, string requestedTabButtonText = null)
        {
            if (player == null || _settings == null)
                return;

            PlayerInfoState state;
            if (!PlayerActiveTabs.TryGetValue(player.userID.Get(), out state) || state == null)
            {
                state = new PlayerInfoState(_settings);
                PlayerActiveTabs[player.userID.Get()] = state;
            }

            if (!string.IsNullOrEmpty(state.MainPanelName))
            {
                CuiHelper.DestroyUi(player, state.MainPanelName);
                state.MainPanelName = string.Empty;
                state.ActiveTabContentPanelName = string.Empty;
            }

            var allowedTabs = GetAllowedTabs(player);
            if (allowedTabs.Count <= 0)
            {
                SendReply(player, "[GUI Help] You don't have permissions to see info.");
                return;
            }

            var tabToSelectIndex = requestedTabIndex.HasValue ? requestedTabIndex.Value : _settings.TabToOpenByDefault;
            if (!string.IsNullOrEmpty(requestedTabButtonText))
            {
                var namedTabIndex = allowedTabs.FindIndex(tab =>
                    string.Equals(tab.ButtonText, requestedTabButtonText, StringComparison.OrdinalIgnoreCase));
                if (namedTabIndex >= 0)
                    tabToSelectIndex = namedTabIndex;
            }

            if (tabToSelectIndex < 0 || tabToSelectIndex >= allowedTabs.Count)
                tabToSelectIndex = 0;

            var container = new CuiElementContainer();
            var mainPanelName = AddMainPanel(container);
            state.MainPanelName = mainPanelName;
            state.ActiveTabIndex = tabToSelectIndex;
            state.ActiveSubTabIndex = 0;
            state.PageIndex = 0;

            var activeAllowedTab = allowedTabs[tabToSelectIndex];
            var tabContentPanelName = CreateTabContent(activeAllowedTab, container, mainPanelName, 0, state.ActiveSubTabIndex);
            var activeTabButtonName = AddActiveButton(tabToSelectIndex, activeAllowedTab, container, mainPanelName);


            for (int tabIndex = 0; tabIndex < allowedTabs.Count; tabIndex++)
            {
                if (tabIndex == tabToSelectIndex)
                    continue;

                AddNonActiveButton(tabIndex, container, allowedTabs[tabIndex], mainPanelName, activeTabButtonName);
            }
            state.ActiveTabContentPanelName = tabContentPanelName;
            SendUI(player, container);
        }

        private static int? GetRequestedTabIndex(string[] args)
        {
            if (args == null || args.Length != 1)
                return null;

            int tabIndex;
            return int.TryParse(args[0], out tabIndex) ? (int?)tabIndex : null;
        }

        private List<HelpTab> GetAllowedTabs(BasePlayer player)
        {
            var allowedTabs = new List<HelpTab>();
            if (_settings == null || _settings.Tabs == null)
                return allowedTabs;

            var disabledCommandKeys = LoadDisabledCommandKeys();

            foreach (var tab in _settings.Tabs)
            {
                var renderedTab = BuildRenderableTab(player, tab, disabledCommandKeys);
                if (renderedTab != null && HasVisibleContent(renderedTab))
                    allowedTabs.Add(renderedTab);
            }

            return allowedTabs;
        }

        private HelpTab BuildRenderableTab(BasePlayer player, HelpTab source, HashSet<string> disabledCommandKeys)
        {
            if (source == null || !IsTabAllowed(player, source))
                return null;

            var tab = CloneTabShell(source);
            if (IsCommandsTab(source) && _settings.CommandCatalog != null && _settings.CommandCatalog.Count > 0)
            {
                tab.Pages = new List<HelpTabPage>();
                tab.SubTabs = BuildCommandSubTabs(player, source, disabledCommandKeys);
                return tab;
            }

            tab.Pages = ClonePages(source.Pages);
            tab.SubTabs = new List<HelpTab>();

            if (source.SubTabs != null)
            {
                foreach (var subTab in source.SubTabs)
                {
                    var renderedSubTab = BuildRenderableTab(player, subTab, disabledCommandKeys);
                    if (renderedSubTab != null && HasVisibleContent(renderedSubTab))
                        tab.SubTabs.Add(renderedSubTab);
                }
            }

            return tab;
        }

        private List<HelpTab> BuildCommandSubTabs(BasePlayer player, HelpTab commandTab, HashSet<string> disabledCommandKeys)
        {
            var subTabs = new List<HelpTab>();
            foreach (var category in _settings.CommandCatalog)
            {
                if (category == null || category.Commands == null || category.Commands.Count <= 0)
                    continue;

                var visibleCommands = category.Commands
                    .Where(command => IsCommandVisible(player, command, disabledCommandKeys))
                    .ToList();

                if (visibleCommands.Count <= 0)
                    continue;

                var template = FindSubTabTemplate(commandTab, category.ButtonText);
                var subTab = CloneTabShell(template ?? commandTab);
                subTab.ButtonText = FirstNonEmpty(category.ButtonText, subTab.ButtonText);
                subTab.HeaderText = FirstNonEmpty(category.HeaderText, subTab.HeaderText);
                subTab.TabButtonFontSize = template != null ? template.TabButtonFontSize : 13;
                subTab.Pages = BuildCommandPages(subTab, visibleCommands);
                subTab.SubTabs = new List<HelpTab>();
                subTabs.Add(subTab);
            }

            return subTabs;
        }

        private static HelpTab FindSubTabTemplate(HelpTab commandTab, string buttonText)
        {
            if (commandTab == null || commandTab.SubTabs == null || string.IsNullOrEmpty(buttonText))
                return null;

            return commandTab.SubTabs.FirstOrDefault(subTab =>
                string.Equals(subTab.ButtonText, buttonText, StringComparison.OrdinalIgnoreCase));
        }

        private static List<HelpTabPage> BuildCommandPages(HelpTab tab, List<CommandEntry> commands)
        {
            var pages = new List<HelpTabPage>();
            if (commands == null || commands.Count <= 0)
                return pages;

            for (var index = 0; index < commands.Count; index += CommandRowsPerPage)
            {
                var page = new HelpTabPage();
                page.TextLines.Add(string.Format("<color=#ff3b3b><b>{0}</b></color>", tab.HeaderText));
                page.TextLines.Add("");

                foreach (var command in commands.Skip(index).Take(CommandRowsPerPage))
                    page.TextLines.Add(CreateCommandTextLine(command));

                pages.Add(page);
            }

            return pages;
        }

        private static string CreateCommandTextLine(CommandEntry command)
        {
            if (!string.IsNullOrEmpty(command.Text))
                return command.Text;

            var aliases = command.Aliases != null && command.Aliases.Count > 0
                ? command.Aliases
                : new List<string> { command.Command };

            var commandText = string.Join(", ", aliases
                .Where(alias => !string.IsNullOrEmpty(alias))
                .Select(alias => string.Format("<color=#ffd166>/{0}</color>", NormalizeDisplayCommand(alias)))
                .ToArray());

            if (string.IsNullOrEmpty(commandText))
                commandText = "<color=#ffd166>/command</color>";

            return string.IsNullOrEmpty(command.Description)
                ? commandText
                : string.Format("{0} - {1}", commandText, command.Description);
        }

        private bool IsCommandVisible(BasePlayer player, CommandEntry command, HashSet<string> disabledCommandKeys)
        {
            if (player == null || command == null || !command.Enabled)
                return false;

            if (ShouldCheckPluginLoaded(command) && !IsPluginLoaded(command.Plugin))
                return false;

            var isAdmin = IsAdmin(player);
            var isStaff = isAdmin || IsStaff(player);

            if (command.RequireAdmin && !isAdmin)
                return false;

            if (command.RequireStaff && !isStaff)
                return false;

            if (command.MinimumAuthLevel > 0 && GetAuthLevel(player) < command.MinimumAuthLevel)
                return false;

            if (!HasRequiredGroups(player, command.RequiredGroups))
                return false;

            if (!HasAnyGroup(player, command.AnyGroup))
                return false;

            if (ShouldCheckPermissions(command))
            {
                var bypassPermissions = isAdmin &&
                    (_settings.CommandCatalogAdminsBypassPermissionChecks || command.AdminBypassesPermissions);

                if (!HasRequiredPermissions(player, command.RequiredPermissions, bypassPermissions))
                    return false;

                if (!HasAnyPermission(player, command.AnyPermission, bypassPermissions))
                    return false;
            }

            if (IsCommandDisabled(command, disabledCommandKeys))
                return false;

            return true;
        }

        private bool ShouldCheckPluginLoaded(CommandEntry command)
        {
            if (command != null && command.CheckPluginLoaded.HasValue)
                return command.CheckPluginLoaded.Value;

            return _settings != null && _settings.CommandCatalogUsesPluginLoadedChecks;
        }

        private bool ShouldCheckPermissions(CommandEntry command)
        {
            if (command != null && command.CheckPermissions.HasValue)
                return command.CheckPermissions.Value;

            return _settings != null && _settings.CommandCatalogUsesPermissionChecks;
        }

        private bool IsPluginLoaded(string pluginName)
        {
            if (string.IsNullOrEmpty(pluginName))
                return true;

            if (string.Equals(pluginName, Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pluginName, GetType().Name, StringComparison.OrdinalIgnoreCase))
                return true;

            var plugin = plugins.Find(pluginName);
            return plugin != null && plugin.IsLoaded;
        }

        private static bool HasRequiredPermissions(BasePlayer player, List<string> permissions, bool bypass)
        {
            if (permissions == null || permissions.Count <= 0 || bypass)
                return true;

            return permissions
                .Where(permissionName => !string.IsNullOrEmpty(permissionName))
                .All(permissionName => Permission.UserHasPermission(player.UserIDString, permissionName));
        }

        private static bool HasAnyPermission(BasePlayer player, List<string> permissions, bool bypass)
        {
            if (permissions == null || permissions.Count <= 0 || bypass)
                return true;

            var normalized = permissions.Where(permissionName => !string.IsNullOrEmpty(permissionName)).ToList();
            return normalized.Count <= 0 || normalized.Any(permissionName =>
                Permission.UserHasPermission(player.UserIDString, permissionName));
        }

        private static bool HasRequiredGroups(BasePlayer player, List<string> groups)
        {
            if (groups == null || groups.Count <= 0)
                return true;

            return groups
                .Where(group => !string.IsNullOrEmpty(group))
                .All(group => Permission.UserHasGroup(player.UserIDString, group));
        }

        private static bool HasAnyGroup(BasePlayer player, List<string> groups)
        {
            if (groups == null || groups.Count <= 0)
                return true;

            var normalized = groups.Where(group => !string.IsNullOrEmpty(group)).ToList();
            return normalized.Count <= 0 || normalized.Any(group => Permission.UserHasGroup(player.UserIDString, group));
        }

        private bool IsStaff(BasePlayer player)
        {
            return GetAuthLevel(player) > 0 || player.IsAdmin || HasAnyConfiguredGroup(player, _settings.StaffGroups) ||
                HasAnyConfiguredGroup(player, _settings.AdminGroups);
        }

        private bool IsAdmin(BasePlayer player)
        {
            return GetAuthLevel(player) >= 2 || player.IsAdmin || HasAnyConfiguredGroup(player, _settings.AdminGroups);
        }

        private static int GetAuthLevel(BasePlayer player)
        {
            return player != null && player.net != null && player.net.connection != null
                ? (int)player.net.connection.authLevel
                : 0;
        }

        private static bool HasAnyConfiguredGroup(BasePlayer player, List<string> groups)
        {
            if (player == null || groups == null)
                return false;

            return groups.Any(group => !string.IsNullOrEmpty(group) &&
                Permission.UserHasGroup(player.UserIDString, group));
        }

        private static bool IsCommandDisabled(CommandEntry command, HashSet<string> disabledCommandKeys)
        {
            if (disabledCommandKeys == null || disabledCommandKeys.Count <= 0)
                return false;

            var keys = command.DisabledCommandKeys != null && command.DisabledCommandKeys.Count > 0
                ? command.DisabledCommandKeys
                : command.Aliases;

            if (keys == null || keys.Count <= 0)
                return false;

            return keys.Any(key => disabledCommandKeys.Contains(NormalizeCommandKey(key)));
        }

        private HashSet<string> LoadDisabledCommandKeys()
        {
            var disabledCommandKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var file = Interface.Oxide.DataFileSystem.GetFile("NTeleportationDisabledCommands");
                var data = file.ReadObject<DisabledCommandData>();
                if (data == null || data.DisabledCommands == null)
                    return disabledCommandKeys;

                foreach (var command in data.DisabledCommands)
                {
                    var key = NormalizeCommandKey(command);
                    if (!string.IsNullOrEmpty(key))
                        disabledCommandKeys.Add(key);
                }
            }
            catch
            {
                return disabledCommandKeys;
            }

            return disabledCommandKeys;
        }

        private static bool IsTabAllowed(BasePlayer player, HelpTab tab)
        {
            if (tab == null || string.IsNullOrEmpty(tab.OxideGroup))
                return true;

            return tab.OxideGroup.Split(',')
                .Select(group => group.Trim())
                .Where(group => !string.IsNullOrEmpty(group))
                .Any(group => Permission.UserHasGroup(player.userID.ToString(), group));
        }

        private static bool IsCommandsTab(HelpTab tab)
        {
            return tab != null && string.Equals(tab.ButtonText, CommandsTabButtonText, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasVisibleContent(HelpTab tab)
        {
            return tab != null && ((tab.Pages != null && tab.Pages.Count > 0) ||
                (tab.SubTabs != null && tab.SubTabs.Count > 0));
        }

        private static HelpTab CloneTabShell(HelpTab source)
        {
            return new HelpTab
            {
                ButtonText = source.ButtonText,
                HeaderText = source.HeaderText,
                TabButtonAnchor = source.TabButtonAnchor,
                TabButtonFontSize = source.TabButtonFontSize,
                HeaderAnchor = source.HeaderAnchor,
                HeaderFontSize = source.HeaderFontSize,
                TextFontSize = source.TextFontSize,
                TextAnchor = source.TextAnchor,
                OxideGroup = source.OxideGroup,
                Pages = new List<HelpTabPage>(),
                SubTabs = new List<HelpTab>()
            };
        }

        private static List<HelpTabPage> ClonePages(List<HelpTabPage> pages)
        {
            if (pages == null)
                return new List<HelpTabPage>();

            return pages.Select(page => new HelpTabPage
            {
                TextLines = page.TextLines != null ? new List<string>(page.TextLines) : new List<string>(),
                ImageSettings = page.ImageSettings != null ? new List<ImageSettings>(page.ImageSettings) : new List<ImageSettings>()
            }).ToList();
        }

        private static string NormalizeDisplayCommand(string command)
        {
            return string.IsNullOrEmpty(command) ? string.Empty : command.Trim().TrimStart('/');
        }

        private static string NormalizeCommandKey(string command)
        {
            return NormalizeDisplayCommand(command).ToLowerInvariant();
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return string.Empty;

            foreach (var value in values)
            {
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            return string.Empty;
        }

        private static void SendUI(BasePlayer player, CuiElementContainer container)
        {
            var json = JsonConvert.SerializeObject(container, Formatting.None, new JsonSerializerSettings
            {
                StringEscapeHandling = StringEscapeHandling.Default,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                Formatting = Formatting.Indented
            });
            json = json.Replace(@"\t", "\t");
            json = json.Replace(@"\n", "\n");

            CuiHelper.AddUi(player, container);
        }

        private static string AddMainPanel(CuiElementContainer container)
        {
            Color backgroundColor;
            ColorExtensions.TryParseHexString(_settings.BackgroundColor, out backgroundColor);
            var mainPanel = new CuiPanel
            {
                Image =
                {
                    Color = ColorExtensions.ToRustFormatString(backgroundColor)
                },
                CursorEnabled = true,
                RectTransform =
                {
                    AnchorMin = _settings.Position.GetRectTransformAnchorMin(),
                    AnchorMax = _settings.Position.GetRectTransformAnchorMax()
                }
            };

            var tabContentPanelName = container.Add(mainPanel);
            if (!_settings.BackgroundImage.Enabled)
                return tabContentPanelName;

            var backgroundImage = CreateImage(tabContentPanelName, _settings.BackgroundImage);

            container.Add(backgroundImage);

            return tabContentPanelName;
        }

        private static CuiElement CreateImage(string panelName, ImageSettings settings)
        {
            var element = new CuiElement();
            var image = new CuiRawImageComponent
            {
                Url = settings.Url,
                Color = string.Format("1 1 1 {0:F1}", (settings.TransparencyInPercent / 100.0f))
            };

            var position = settings.Position;
            var rectTransform = new CuiRectTransformComponent
            {
                AnchorMin = position.GetRectTransformAnchorMin(),
                AnchorMax = position.GetRectTransformAnchorMax()
            };
            element.Components.Add(image);
            element.Components.Add(rectTransform);
            element.Name = CuiHelper.GetGuid();
            element.Parent = panelName;

            return element;
        }

        private string CreateTabContent(HelpTab helpTab, CuiElementContainer container, string mainPanelName, int pageIndex = 0, int subTabIndex = 0)
        {
            var tabPanelName = CreateTab(helpTab, container, mainPanelName, pageIndex, subTabIndex);
            var closeButton = CreateCloseButton(mainPanelName, _settings.CloseButtonColor);
            container.Add(closeButton, tabPanelName);
            return tabPanelName;
        }

        private static string CreateTab(HelpTab helpTab, CuiElementContainer container, string mainPanelName, int pageIndex, int subTabIndex)
        {
            var tabPanelName = CreateTabPanel(container, mainPanelName, "#00000000");
            var activeContentTab = GetActiveContentTab(helpTab, subTabIndex);
            var hasSubTabs = HasSubTabs(helpTab);

            var currentPage = activeContentTab.Pages.ElementAtOrDefault(pageIndex);
            if (currentPage == null)
                return tabPanelName;

            foreach (var imageSettings in currentPage.ImageSettings)
            {
                var imageObject = CreateImage(tabPanelName, imageSettings);
                container.Add(imageObject);
            }

            var cuiLabel = CreateHeaderLabel(helpTab);
            container.Add(cuiLabel, tabPanelName);

            if (hasSubTabs)
                AddSubTabButtons(helpTab, container, tabPanelName, mainPanelName, subTabIndex);

            var firstLineMargin = hasSubTabs ? 0.75f : 0.91f;
            const float textLineHeight = 0.04f;

            for (var textRow = 0; textRow < currentPage.TextLines.Count; textRow++)
            {
                var textLine = currentPage.TextLines[textRow];
                var textLineLabel = CreateTextLineLabel(activeContentTab, firstLineMargin, textLineHeight, textRow, textLine);
                container.Add(textLineLabel, tabPanelName);
            }

            if (pageIndex > 0)
            {
                var prevPageButton = CreatePrevPageButton(mainPanelName, pageIndex, _settings.PrevPageButtonColor);
                container.Add(prevPageButton, tabPanelName);
            }

            if (activeContentTab.Pages.Count - 1 == pageIndex)
                return tabPanelName;

            var nextPageButton = CreateNextPageButton(mainPanelName, pageIndex, _settings.NextPageButtonColor);
            container.Add(nextPageButton, tabPanelName);

            return tabPanelName;
        }

        private static bool HasSubTabs(HelpTab helpTab)
        {
            return helpTab != null && helpTab.SubTabs != null && helpTab.SubTabs.Count > 0;
        }

        private static HelpTab GetActiveContentTab(HelpTab helpTab, int subTabIndex)
        {
            if (!HasSubTabs(helpTab))
                return helpTab;

            if (subTabIndex < 0 || subTabIndex >= helpTab.SubTabs.Count)
                subTabIndex = 0;

            return helpTab.SubTabs[subTabIndex];
        }

        private static void AddSubTabButtons(HelpTab helpTab, CuiElementContainer container, string tabPanelName, string mainPanelName, int activeSubTabIndex)
        {
            var subTabs = helpTab.SubTabs;
            if (subTabs == null || subTabs.Count <= 0)
                return;

            const float startX = 0.01f;
            const float endX = 0.84f;
            const float spacing = 0.006f;
            const float minY = 0.765f;
            const float maxY = 0.83f;

            var buttonWidth = (endX - startX - spacing * (subTabs.Count - 1)) / subTabs.Count;

            for (var subTabIndex = 0; subTabIndex < subTabs.Count; subTabIndex++)
            {
                var subTab = subTabs[subTabIndex];
                var color = subTabIndex == activeSubTabIndex ? _settings.ActiveButtonColor : _settings.InactiveButtonColor;
                var button = CreateSubTabButton(subTab, subTabIndex, mainPanelName, startX + subTabIndex * (buttonWidth + spacing), buttonWidth, minY, maxY, color);
                container.Add(button, tabPanelName);
            }
        }

        private static CuiButton CreateSubTabButton(HelpTab helpTab, int subTabIndex, string mainPanelName, float minX, float width, float minY, float maxY, string hexColor)
        {
            Color color;
            ColorExtensions.TryParseHexString(hexColor, out color);

            return new CuiButton
            {
                Button =
                {
                    Command = string.Format("changesubtab {0} {1}", subTabIndex, mainPanelName),
                    Color = ColorExtensions.ToRustFormatString(color)
                },
                RectTransform =
                {
                    AnchorMin = string.Format("{0:F3} {1:F3}", minX, minY),
                    AnchorMax = string.Format("{0:F3} {1:F3}", minX + width, maxY)
                },
                Text =
                {
                    Text = helpTab.ButtonText,
                    FontSize = Math.Min(13, helpTab.TabButtonFontSize),
                    Align = TextAnchor.MiddleCenter
                }
            };
        }

        private static string CreateTabPanel(CuiElementContainer container, string mainPanelName, string hexColor)
        {
            Color backgroundColor;
            ColorExtensions.TryParseHexString(hexColor, out backgroundColor);

            return container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image =
                {
                    Color = ColorExtensions.ToRustFormatString(backgroundColor),
                },
                RectTransform =
                {
                    AnchorMin = "0.22 0.01",
                    AnchorMax = "0.99 0.98"
                }
            }, mainPanelName);
        }

        private static string CreateTabContentPanel(CuiElementContainer container, string mainPanelName, string hexColor)
        {
            Color backgroundColor;
            ColorExtensions.TryParseHexString(hexColor, out backgroundColor);

            return container.Add(new CuiPanel
            {
                CursorEnabled = false,
                Image =
                {
                    Color = ColorExtensions.ToRustFormatString(backgroundColor),
                },
                RectTransform =
                {
                    AnchorMin = "0 0",
                    AnchorMax = "0.86 1"
                }
            }, mainPanelName);
        }

        private static CuiLabel CreateHeaderLabel(HelpTab helpTab)
        {
            return new CuiLabel
            {
                RectTransform =
                {
                    AnchorMin = "0.01 0.85",
                    AnchorMax = "1.0 0.98"
                },
                Text =
                {
                    Align = helpTab.HeaderAnchor,
                    FontSize = helpTab.HeaderFontSize,
                    Text = helpTab.HeaderText
                }
            };
        }

        private static CuiButton CreateCloseButton(string mainPanelName, string hexColor)
        {
            Color color;
            ColorExtensions.TryParseHexString(hexColor, out color);
            return new CuiButton
            {
                Button =
                {
                    Command = string.Format("infoclose {0}", mainPanelName),
                    Close = mainPanelName,
                    Color = ColorExtensions.ToRustFormatString(color)
                },
                RectTransform =
                {
                    AnchorMin = "0.86 0.93",
                    AnchorMax = "0.97 0.99"
                },
                Text =
                {
                    Text = _settings.CloseButtonText,
                    FontSize = 18,
                    Align = TextAnchor.MiddleCenter
                }
            };
        }

        private static CuiButton CreateHelpButton()
        {
            Color color;
            ColorExtensions.TryParseHexString(_settings.HelpButton.Color, out color);
            return new CuiButton
            {
                Button =
                {
                    Command = "info",
                    Color = ColorExtensions.ToRustFormatString(color)
                },
                RectTransform =
                {
                    AnchorMin = _settings.HelpButton.Position.GetRectTransformAnchorMin(),
                    AnchorMax = _settings.HelpButton.Position.GetRectTransformAnchorMax()
                },
                Text =
                {
                    Text = _settings.HelpButton.Text,
                    FontSize = _settings.HelpButton.FontSize,
                    Align = TextAnchor.MiddleCenter
                }
            };
        }

        private static CuiLabel CreateTextLineLabel(HelpTab helpTab, float firstLineMargin, float textLineHeight, int textRow,
            string textLine)
        {
            var textLineLabel = new CuiLabel
            {
                RectTransform =
                {
                    AnchorMin = "0.01 " + (firstLineMargin - textLineHeight * (textRow + 1)),
                    AnchorMax = "0.85 " + (firstLineMargin - textLineHeight * textRow)
                },
                Text =
                {
                    Align = helpTab.TextAnchor,
                    FontSize = helpTab.TextFontSize,
                    Text = textLine
                }
            };
            return textLineLabel;
        }

        private static CuiButton CreatePrevPageButton(string mainPanelName, int pageIndex, string hexColor)
        {
            Color color;
            ColorExtensions.TryParseHexString(hexColor, out color);
            return new CuiButton
            {
                Button =
                {
                    Command = string.Format("changepage {0} {1}", pageIndex - 1, mainPanelName),
                    Color = ColorExtensions.ToRustFormatString(color)
                },
                RectTransform =
                {
                    AnchorMin = "0.86 0.01",
                    AnchorMax = "0.97 0.07"
                },
                Text =
                {
                    Text = _settings.PrevPageText,
                    FontSize = 18,
                    Align = TextAnchor.MiddleCenter
                }
            };
        }

        private static CuiButton CreateNextPageButton(string mainPanelName, int pageIndex, string hexColor)
        {
            Color color;
            ColorExtensions.TryParseHexString(hexColor, out color);
            return new CuiButton
            {
                Button =
                {
                    Command = string.Format("changepage {0} {1}", pageIndex + 1, mainPanelName),
                    Color = ColorExtensions.ToRustFormatString(color)
                },
                RectTransform =
                {
                    AnchorMin = "0.86 0.08",
                    AnchorMax = "0.97 0.15"
                },
                Text =
                {
                    Text = _settings.NextPageText,
                    FontSize = 18,
                    Align = TextAnchor.MiddleCenter
                }
            };
        }

        private static void AddNonActiveButton(
             int tabIndex,
             CuiElementContainer container,
             HelpTab helpTab,
             string mainPanelName,
             string activeTabButtonName)
        {
            Color nonActiveButtonColor;
            ColorExtensions.TryParseHexString(_settings.InactiveButtonColor, out nonActiveButtonColor);

            CuiButton helpTabButton = CreateTabButton(tabIndex, helpTab, nonActiveButtonColor);
            string helpTabButtonName = container.Add(helpTabButton, mainPanelName);
            string command = string.Format("changeTab {0} {1} {2} {3}", tabIndex, activeTabButtonName, helpTabButtonName, mainPanelName);
            helpTabButton.Button.Command = command;
        }

        private static string AddActiveButton(
            int activeTabIndex,
            HelpTab activeTab,
            CuiElementContainer container,
            string mainPanelName)
        {
            Color activeButtonColor;
            ColorExtensions.TryParseHexString(_settings.ActiveButtonColor, out activeButtonColor);

            var activeHelpTabButton = CreateTabButton(activeTabIndex, activeTab, activeButtonColor);
            var activeTabButtonName = container.Add(activeHelpTabButton, mainPanelName);
            var command = string.Format("changeTab {0}", activeTabIndex);

            activeHelpTabButton.Button.Command = command;
            return activeTabButtonName;
        }

        private static CuiButton CreateTabButton(int tabIndex, HelpTab helpTab, Color color)
        {
            const float verticalMargin = 0.03f;
            const float buttonHeight = 0.06f;

            return new CuiButton
            {
                Button =
                {
                    Color = ColorExtensions.ToRustFormatString(color)
                },
                RectTransform =
                {
                    AnchorMin = string.Format("0.01 {0}", 1 - ((verticalMargin + buttonHeight) * (tabIndex + 1))),
                    AnchorMax = string.Format("0.20 {0}", 1 - ((verticalMargin * (tabIndex + 1)) + (tabIndex * buttonHeight)))
                },
                Text =
                {
                    Text = helpTab.ButtonText,
                    FontSize = helpTab.TabButtonFontSize,
                    Align = helpTab.TabButtonAnchor
                }
            };
        }

        [JsonObject]
        public sealed class Settings
        {
            public Settings()
            {
                Tabs = new List<HelpTab>();
                CommandCatalog = new List<CommandCategory>();
                StaffGroups = new List<string> { "admin", "moderator", "staff", "owner" };
                AdminGroups = new List<string> { "admin", "owner" };
                CommandCatalogUsesPluginLoadedChecks = false;
                CommandCatalogUsesPermissionChecks = false;
                CommandCatalogAdminsBypassPermissionChecks = true;
                ShowInfoOnPlayerInit = true;
                ShowInfoOnlyOncePerRuntime = true;
                TabToOpenByDefault = 0;
                Position = new Position();

                //ActiveButtonColor = "#" + Color.cyan.ToHexStringRGBA();
                //InactiveButtonColor = "#" + Color.gray.ToHexStringRGBA();
                //CloseButtonColor = "#" + Color.gray.ToHexStringRGBA();
                //PrevPageButtonColor = "#" + Color.gray.ToHexStringRGBA();
                //NextPageButtonColor = "#" + Color.gray.ToHexStringRGBA();
                //BackgroundColor = "#" + new Color(0f, 0f, 0f, 1.0f).ToHexStringRGBA();

                ActiveButtonColor = "#" + ColorExtensions.ToHexStringRGBA(Color.cyan);
                InactiveButtonColor = "#" + ColorExtensions.ToHexStringRGBA(Color.gray);
                CloseButtonColor = "#" + ColorExtensions.ToHexStringRGBA(Color.gray);
                PrevPageButtonColor = "#" + ColorExtensions.ToHexStringRGBA(Color.gray);
                NextPageButtonColor = "#" + ColorExtensions.ToHexStringRGBA(Color.gray);
                BackgroundColor = "#" + ColorExtensions.ToHexStringRGBA(new Color(0f, 0f, 0f, 1.0f));
                NextPageText = "Next Page";
                PrevPageText = "Prev Page";
                CloseButtonText = "Close";

                HelpButton = new HelpButtonSettings();

                BackgroundImage = new BackgroundImageSettings();
            }

            public List<HelpTab> Tabs { get; set; }
            public List<CommandCategory> CommandCatalog { get; set; }
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> StaffGroups { get; set; }
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> AdminGroups { get; set; }
            public bool CommandCatalogUsesPluginLoadedChecks { get; set; }
            public bool CommandCatalogUsesPermissionChecks { get; set; }
            public bool CommandCatalogAdminsBypassPermissionChecks { get; set; }
            public bool ShowInfoOnPlayerInit { get; set; }
            public bool ShowInfoOnlyOncePerRuntime { get; set; }

            public int TabToOpenByDefault { get; set; }

            public Position Position { get; set; }
            public BackgroundImageSettings BackgroundImage { get; set; }

            public string ActiveButtonColor { get; set; }
            public string InactiveButtonColor { get; set; }
            public string CloseButtonColor { get; set; }
            public string CloseButtonText { get; set; }
            public string NextPageButtonColor { get; set; }
            public string NextPageText { get; set; }
            public string PrevPageButtonColor { get; set; }
            public string PrevPageText { get; set; }
            public string BackgroundColor { get; set; }

            public HelpButtonSettings HelpButton { get; set; }

            public bool UpgradedConfig { get; set; }

            public static Settings CreateDefault()
            {
                var settings = new Settings();
                var firstTab = new HelpTab
                {
                    ButtonText = "First Tab",
                    HeaderText = "First Tab",
                    Pages =
                    {
                        new HelpTabPage
                        {
                            TextLines =
                            {
                                "This is first tab, first page.",
                                "Add some text here by adding more lines.",
                                "You should replace all default text lines with whatever you feel up to",
                                "type <color=red> /info </color> to open this window",
                                "Press next page to check second page.",
                                "You may add more pages in config file."
                            },
                            ImageSettings =
                            {
                                new ImageSettings
                                {
                                    Position = new Position
                                    {
                                        MinX = 0,
                                        MaxX = 0.5f,
                                        MinY = 0,
                                        MaxY = 0.5f
                                    },
                                    Url = "http://th04.deviantart.net/fs70/PRE/f/2012/223/4/4/rust_logo_by_furrypigdog-d5aqi3r.png"
                                },
                                new ImageSettings
                                {
                                    Position = new Position
                                    {
                                        MinX = 0.5f,
                                        MaxX = 1f,
                                        MinY = 0,
                                        MaxY = 0.5f
                                    },
                                    Url = "http://files.enjin.com/176331/IMGS/LOGO_RUST1.fw.png"
                                },
                                new ImageSettings
                                {
                                    Position = new Position
                                    {
                                        MinX = 0,
                                        MaxX = 0.5f,
                                        MinY = 0.5f,
                                        MaxY = 1f
                                    },
                                    Url = "http://files.enjin.com/176331/IMGS/LOGO_RUST1.fw.png"
                                },
                                new ImageSettings
                                {
                                    Position = new Position
                                    {
                                        MinX = 0.5f,
                                        MaxX = 1f,
                                        MinY = 0.5f,
                                        MaxY = 1f
                                    },
                                    Url = "http://th04.deviantart.net/fs70/PRE/f/2012/223/4/4/rust_logo_by_furrypigdog-d5aqi3r.png"
                                },
                            }
                        },
                        new HelpTabPage
                        {
                            TextLines =
                            {
                                "This is first tab, second page",
                                "Add some text here by adding more lines.",
                                "You should replace all default text lines with whatever you feel up to",
                                "type <color=red> /info </color> to open this window",
                                "Press next page to check third page.",
                                "Press prev page to go back to first page.",
                                "You may add more pages in config file."
                            }
                        }
                        ,
                        new HelpTabPage
                        {
                            TextLines =
                            {
                                "This is first tab, third page",
                                "Add some text here by adding more lines.",
                                "You should replace all default text lines with whatever you feel up to",
                                "type <color=red> /info </color> to open this window",
                                "Press prev page to go back to second page.",
                            }
                        }
                    }
                };
                var secondTab = new HelpTab
                {
                    ButtonText = "Second Tab",
                    HeaderText = "Second Tab",
                    Pages =
                    {
                        new HelpTabPage
                        {
                            TextLines =
                            {
                                "This is second tab, first page.",
                                "Add some text here by adding more lines.",
                                "You should replace all default text lines with whatever you feel up to",
                                "type <color=red> /info </color> to open this window",
                                "You may add more pages in config file."
                            }
                        }
                    }
                };
                var thirdTab = new HelpTab
                {
                    ButtonText = "Third Tab",
                    HeaderText = "Third Tab",
                    Pages =
                    {
                        new HelpTabPage
                        {
                            TextLines =
                            {
                                "This is third tab, first page.",
                                "Add some text here by adding more lines.",
                                "You should replace all default text lines with whatever you feel up to",
                                "type <color=red> /info </color> to open this window",
                                "You may add more pages in config file."
                            }
                        }
                    }
                };

                settings.Tabs.Add(firstTab);
                settings.Tabs.Add(secondTab);
                settings.Tabs.Add(thirdTab);

                return settings;
            }
        }

        public sealed class Position
        {
            public Position()
            {
                MinX = 0.15f;
                MaxX = 0.9f;
                MinY = 0.2f;
                MaxY = 0.9f;
            }

            public float MinX { get; set; }
            public float MaxX { get; set; }
            public float MinY { get; set; }
            public float MaxY { get; set; }

            public string GetRectTransformAnchorMin()
            {
                return string.Format("{0} {1}", MinX, MinY);
            }

            public string GetRectTransformAnchorMax()
            {
                return string.Format("{0} {1}", MaxX, MaxY);
            }
        }

        public sealed class HelpTab
        {
            private string _headerText;
            private string _buttonText;

            public HelpTab()
            {
                ButtonText = "Default ServerInfo Help Tab";
                HeaderText = "Default ServerInfo Help";
                Pages = new List<HelpTabPage>();
                SubTabs = new List<HelpTab>();
                TextFontSize = 16;
                HeaderFontSize = 32;
                TabButtonFontSize = 16;
                TextAnchor = TextAnchor.MiddleLeft;
                HeaderAnchor = TextAnchor.UpperLeft;
                TabButtonAnchor = TextAnchor.MiddleCenter;
                OxideGroup = string.Empty;
            }

            public string ButtonText
            {
                get { return string.IsNullOrEmpty(_buttonText) ? _headerText : _buttonText; }
                set { _buttonText = value; }
            }

            public string HeaderText
            {
                get
                {
                    return string.IsNullOrEmpty(_headerText) ? _buttonText : _headerText;
                }
                set { _headerText = value; }
            }

            public List<HelpTabPage> Pages { get; set; }
            public List<HelpTab> SubTabs { get; set; }

            public TextAnchor TabButtonAnchor { get; set; }
            public int TabButtonFontSize { get; set; }

            public TextAnchor HeaderAnchor { get; set; }
            public int HeaderFontSize { get; set; }

            public int TextFontSize { get; set; }
            public TextAnchor TextAnchor { get; set; }

            public string OxideGroup { get; set; }
        }

        public sealed class CommandCategory
        {
            public CommandCategory()
            {
                ButtonText = "Commands";
                HeaderText = "COMMANDS";
                Commands = new List<CommandEntry>();
            }

            public string ButtonText { get; set; }
            public string HeaderText { get; set; }
            public List<CommandEntry> Commands { get; set; }
        }

        public sealed class CommandEntry
        {
            public CommandEntry()
            {
                Enabled = true;
                Text = string.Empty;
                Command = string.Empty;
                Description = string.Empty;
                Plugin = string.Empty;
                Aliases = new List<string>();
                RequiredPermissions = new List<string>();
                AnyPermission = new List<string>();
                RequiredGroups = new List<string>();
                AnyGroup = new List<string>();
                DisabledCommandKeys = new List<string>();
                RequireStaff = false;
                RequireAdmin = false;
                MinimumAuthLevel = 0;
                AdminBypassesPermissions = false;
                CheckPluginLoaded = null;
                CheckPermissions = null;
            }

            public bool Enabled { get; set; }
            public string Text { get; set; }
            public string Command { get; set; }
            public string Description { get; set; }
            public string Plugin { get; set; }
            public List<string> Aliases { get; set; }
            public List<string> RequiredPermissions { get; set; }
            public List<string> AnyPermission { get; set; }
            public List<string> RequiredGroups { get; set; }
            public List<string> AnyGroup { get; set; }
            public List<string> DisabledCommandKeys { get; set; }
            public bool RequireStaff { get; set; }
            public bool RequireAdmin { get; set; }
            public int MinimumAuthLevel { get; set; }
            public bool AdminBypassesPermissions { get; set; }
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public bool? CheckPluginLoaded { get; set; }

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public bool? CheckPermissions { get; set; }
        }

        public sealed class DisabledCommandData
        {
            public DisabledCommandData()
            {
                DisabledCommands = new List<string>();
            }

            [JsonProperty("List of disabled commands")]
            public List<string> DisabledCommands { get; set; }
        }

        public sealed class HelpTabPage
        {
            public List<string> TextLines { get; set; }
            public List<ImageSettings> ImageSettings { get; set; }

            public HelpTabPage()
            {
                TextLines = new List<string>();
                ImageSettings = new List<ImageSettings>();
            }
        }

        public class ImageSettings
        {
            public Position Position { get; set; }
            public string Url { get; set; }
            public int TransparencyInPercent { get; set; }

            public ImageSettings()
            {
                Position = new Position
                {
                    MaxX = 1.0f,
                    MaxY = 1.0f,
                    MinY = 0.0f,
                    MinX = 0.0f
                };
                Url = "http://7-themes.com/data_images/out/35/6889756-black-backgrounds.jpg";
                TransparencyInPercent = 100;
            }
        }

        public sealed class BackgroundImageSettings : ImageSettings
        {
            public bool Enabled { get; set; }

            public BackgroundImageSettings()
            {
                Enabled = false;
            }
        }

        public sealed class HelpButtonSettings
        {
            public bool IsEnabled { get; set; }
            public string Text { get; set; }
            public Position Position { get; set; }
            public string Color { get; set; }
            public int FontSize { get; set; }

            public HelpButtonSettings()
            {
                IsEnabled = false;
                Text = "Help";
                Color = "#" + ColorExtensions.ToHexStringRGBA(UnityEngine.Color.gray);

                FontSize = 18;

                Position = new Position { MinX = 0.00f, MaxX = 0.05f, MinY = 0.10f, MaxY = 0.14f };
            }
        }

        public sealed class PlayerInfoState
        {
            public PlayerInfoState(Settings settings)
            {
                if (settings == null) throw new ArgumentNullException("settings");

                ActiveTabIndex = settings.TabToOpenByDefault;
                ActiveSubTabIndex = 0;
                PageIndex = 0;
                InfoShownOnLogin = settings.ShowInfoOnPlayerInit;
                InfoShownOnLoginOnce = settings.ShowInfoOnlyOncePerRuntime;
                ActiveTabContentPanelName = string.Empty;
                ChatHelpButtonName = string.Empty;
                MainPanelName = string.Empty;
            }

            public int ActiveTabIndex { get; set; }
            public int ActiveSubTabIndex { get; set; }
            public int PageIndex { get; set; }
            public bool InfoShownOnLogin { get; set; }
            public bool InfoShownOnLoginOnce { get; set; }
            public string ActiveTabContentPanelName { get; set; }
            public string ChatHelpButtonName { get; set; }
            public string MainPanelName { get; set; }
        }

        public static class ColorExtensions
        {
            public static string ToRustFormatString(Color color)
            {
                return string.Format("{0:F2} {1:F2} {2:F2} {3:F2}", color.r, color.g, color.b, color.a);
            }

            //
            // UnityEngine 5.1 Color extensions which were removed in 5.2
            //

            public static string ToHexStringRGB(Color col)
            {
                Color32 color = col;
                return string.Format("{0}{1}{2}", color.r, color.g, color.b);
            }

            public static string ToHexStringRGBA(Color col)
            {
                Color32 color = col;
                return string.Format("{0}{1}{2}{3}", color.r, color.g, color.b, color.a);
            }

            public static bool TryParseHexString(string hexString, out Color color)
            {
                try
                {
                    color = FromHexString(hexString);
                    return true;
                }
                catch
                {
                    color = Color.white;
                    return false;
                }
            }

            private static Color FromHexString(string hexString)
            {
                if (string.IsNullOrEmpty(hexString))
                {
                    throw new InvalidOperationException("Cannot convert an empty/null string.");
                }
                var trimChars = new[] { '#' };
                var str = hexString.Trim(trimChars);
                switch (str.Length)
                {
                    case 3:
                        {
                            var chArray2 = new[] { str[0], str[0], str[1], str[1], str[2], str[2], 'F', 'F' };
                            str = new string(chArray2);
                            break;
                        }
                    case 4:
                        {
                            var chArray3 = new[] { str[0], str[0], str[1], str[1], str[2], str[2], str[3], str[3] };
                            str = new string(chArray3);
                            break;
                        }
                    default:
                        if (str.Length < 6)
                        {
                            str = str.PadRight(6, '0');
                        }
                        if (str.Length < 8)
                        {
                            str = str.PadRight(8, 'F');
                        }
                        break;
                }
                var r = byte.Parse(str.Substring(0, 2), NumberStyles.HexNumber);
                var g = byte.Parse(str.Substring(2, 2), NumberStyles.HexNumber);
                var b = byte.Parse(str.Substring(4, 2), NumberStyles.HexNumber);
                var a = byte.Parse(str.Substring(6, 2), NumberStyles.HexNumber);

                return new Color32(r, g, b, a);
            }
        }
    }
}
