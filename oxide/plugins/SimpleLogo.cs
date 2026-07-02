using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins
{
    /// <summary>
    /// Displays a custom logo on player screens with rotation support
    /// </summary>
    [Info("SimpleLogo", "Sami37", "1.2.9")]
    [Description("Place your own logo to your player screen.")]
    public class SimpleLogo : RustPlugin
    {
        #region config
        [PluginReference]
        private readonly Plugin ImageLibrary;
        private static SimpleLogo _instance;
        private Timer _refreshTimer;

        private const string Perm = "simplelogo.display";
        private const string NoDisplay = "simplelogo.nodisplay";
        private const string UiName = "containerSimpleUI";

        private string _anchorMin, _anchorMax, _backgroundColor;

        List<object> _urlList = new List<object>();
        private int _currentlySelected, _intervals;
        private Dictionary<ulong, bool> playerHide = new Dictionary<ulong, bool>();

        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            LoadConfig();
        }

        string ListToString<T>(List<T> list, int first = 0, string seperator = ", ") => string.Join(seperator, (from val in list select val.ToString()).Skip(first).ToArray());
        void SetConfig(params object[] args) { List<string> stringArgs = (from arg in args select arg.ToString()).ToList(); stringArgs.RemoveAt(args.Length - 1); if (Config.Get(stringArgs.ToArray()) == null) Config.Set(args); }
        T GetConfig<T>(T defaultVal, params object[] args) { List<string> stringArgs = (from arg in args select arg.ToString()).ToList(); if (Config.Get(stringArgs.ToArray()) == null) { PrintError($"The plugin failed to read something from the config: {ListToString(stringArgs, 0, "/")}{Environment.NewLine}Please reload the plugin and see if this message is still showing. If so, please post this into the support thread of this plugin."); return defaultVal; } return (T)Convert.ChangeType(Config.Get(stringArgs.ToArray()), typeof(T)); }

        private string GetImage(string shortname, ulong skin = 0, bool returnUrl = false)
        {
            return string.IsNullOrEmpty(shortname) ? null : (string) _instance.ImageLibrary?.Call("GetImage", shortname, skin, returnUrl);
        }

        private static bool AddImageToLibrary(string url, string shortname, ulong skin = 0)
        {
            return (bool)_instance.ImageLibrary.Call("AddImage", url, shortname.ToLower(), skin);
        }

        void LoadConfig()
        {
            List<object> listUrl = new List<object> { "http://i.imgur.com/KVmbhyB.png" };
            SetConfig("UI", "GUIAnchorMin", "0.01 0.02");
            SetConfig("UI", "GUIAnchorMax", "0.15 0.1");
            SetConfig("UI", "BackgroundMainColor", "0 0 0 0");
            SetConfig("UI", "BackgroundMainURL", listUrl);
            SetConfig("UI", "IntervalBetweenImage", 30);

            SaveConfig();

            _anchorMin = Config["UI", "GUIAnchorMin"].ToString();
            _anchorMax = Config["UI", "GUIAnchorMax"].ToString();
            _backgroundColor = Config["UI", "BackgroundMainColor"].ToString();
            _intervals = GetConfig(30, "UI", "IntervalBetweenImage");
            _urlList = (List<object>)Config["UI", "BackgroundMainURL"];

            if (_urlList == null || _urlList.Count == 0)
            {
                PrintWarning("No url registered !");
                return;
            }

            int i = 0;
            foreach (var url in _urlList)
            {
                if (string.IsNullOrEmpty(url?.ToString()))
                {
                    PrintWarning($"Empty URL at index {i}");
                    continue;
                }
                AddImageToLibrary(url.ToString(), "SimpleLogo" + i);
                i++;
            }
        }

        #endregion

        #region data_init

        void Unload()
        {
            _refreshTimer?.Destroy();

            foreach (var player in BasePlayer.activePlayerList)
            {
                GUIDestroy(player);
            }

            if (playerHide != null)
            {
                try
                {
                    Interface.Oxide.DataFileSystem.WriteObject(Name, playerHide);
                    Puts("Player preferences saved successfully.");
                }
                catch (Exception ex)
                {
                    PrintError($"Failed to save player preferences: {ex.Message}");
                }
            }
        }
        #endregion

        private CuiElement CreateImage(string panelName)
        {
            var url = GetImage($"SimpleLogo{_currentlySelected}");

            if (string.IsNullOrEmpty(url))
            {
                PrintWarning($"Image SimpleLogo{_currentlySelected} not found in ImageLibrary!");
            }

            return new CuiElement
            {
                Name = CuiHelper.GetGuid(),
                Parent = panelName,
                Components =
                {
                    new CuiRawImageComponent { Png = url },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1"
                    }
                }
            };
        }
        void GUIDestroy(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UiName);
        }

        void CreateUi(BasePlayer player)
        {
            if (permission.UserHasPermission(player.UserIDString, Perm) && !permission.UserHasPermission(player.UserIDString, NoDisplay))
            {
                var panel = new CuiElementContainer
                {
                    {
                        new CuiPanel
                        {
                            Image =
                            {
                                Color = _backgroundColor
                            },
                            RectTransform =
                            {
                                AnchorMin = _anchorMin,
                                AnchorMax = _anchorMax
                            },
                            CursorEnabled = false
                        },
                        "Hud", UiName
                    }
                };
                var backgroundImageWin = CreateImage(UiName);
                panel.Add(backgroundImageWin);
                CuiHelper.AddUi(player, panel);
            }
        }

        void RefreshUi()
        {
            _refreshTimer?.Destroy();

            if (_urlList == null || _urlList.Count == 0)
                return;

            foreach (var player in BasePlayer.activePlayerList)
            {
                GUIDestroy(player);

                bool isHidden = playerHide != null &&
                                playerHide.TryGetValue(player.userID, out bool hidden) &&
                                hidden;
                if (!isHidden)
                    CreateUi(player);
            }

            if (_urlList.Count > 1)
            {
                _refreshTimer = timer.In(_intervals, () =>
                {
                    _currentlySelected = (_currentlySelected + 1) % _urlList.Count;
                    RefreshUi();
                });
            }
        }

        void OnPlayerConnected(BasePlayer player)
        {
            if (player.IsNpc) return;

            timer.Once(1f, () =>
            {
                if (player != null && player.IsConnected)
                {
                    bool isHidden = playerHide != null &&
                                    playerHide.TryGetValue(player.userID, out bool hidden) &&
                                    hidden;

                    if (!isHidden)
                        CreateUi(player);
                }
            });
        }

        void OnServerInitialized()
        {
            _instance = this;

            if (ImageLibrary == null)
            {
                PrintError("ImageLibrary isn't loaded !");
                return;
            }

            playerHide = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, bool>>(Name);
            if (playerHide == null)
                playerHide = new Dictionary<ulong, bool>();

            permission.RegisterPermission(Perm, this);
            permission.RegisterPermission(NoDisplay, this);

            LoadConfig();
            NextTick(RefreshUi);
        }

        [ChatCommand("SL")]
        void chatCmd(BasePlayer player, string command, string[] args)
        {
            if (playerHide == null)
                playerHide = new Dictionary<ulong, bool>();

            // Simplification avec TryGetValue
            playerHide.TryGetValue(player.userID, out bool currentState);
            playerHide[player.userID] = !currentState;

            GUIDestroy(player);
            if (!playerHide[player.userID])
                CreateUi(player);

            player.ChatMessage($"Logo {(playerHide[player.userID] ? "hidden" : "displayed")}");
        }
    }
}