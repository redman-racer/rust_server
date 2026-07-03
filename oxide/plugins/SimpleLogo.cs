using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins
{
    /// <summary>
    /// Displays a custom logo on player screens with rotation support
    /// </summary>
    [Info("SimpleLogo", "Sami37", "1.2.14")]
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
        private const string ImageNamePrefix = "simplelogo";
        private const string MenuLogoUrl = "https://raidlands.net/assets/media/raidlands-logo.png";
        private const string MistakenNavLogoUrl = "https://raidlands.net/assets/media/nav-logo.png";
        private const string LegacyFooterLogoUrl = "https://raidlands.net/assets/media/in-game/raidlands-footer-logo.png";
        private const string DefaultAnchorMin = "0.795 0.015";
        private const string DefaultAnchorMax = "0.84 0.095";
        private const double DefaultImageAspectRatio = 1.0;
        private const double DefaultScreenAspectRatio = 1.77778;

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

        private bool HasImage(string shortname, ulong skin = 0)
        {
            if (string.IsNullOrEmpty(shortname) || ImageLibrary == null)
                return false;

            var result = ImageLibrary.Call("HasImage", shortname, skin);
            return result is bool && (bool)result;
        }

        private string GetImageName(int index)
        {
            return $"{ImageNamePrefix}{index}";
        }

        private bool AddImageToLibrary(string url, string shortname, ulong skin = 0, Action callback = null)
        {
            if (ImageLibrary == null)
                return false;

            var result = ImageLibrary.Call("AddImage", url, shortname, skin, callback);
            return result is bool && (bool)result;
        }

        private static bool TryParseAnchor(string value, out double x, out double y)
        {
            x = 0;
            y = 0;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                return false;

            return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                   double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        }

        private static string FormatAnchor(double x, double y)
        {
            return $"{x.ToString("0.###", CultureInfo.InvariantCulture)} {y.ToString("0.###", CultureInfo.InvariantCulture)}";
        }

        private static string AnchorMinWithAutoWidth(string anchorMin, string anchorMax, double imageAspectRatio, double screenAspectRatio)
        {
            double minX;
            double minY;
            double maxX;
            double maxY;

            if (!TryParseAnchor(anchorMin, out minX, out minY) ||
                !TryParseAnchor(anchorMax, out maxX, out maxY) ||
                imageAspectRatio <= 0 ||
                screenAspectRatio <= 0)
                return anchorMin;

            var height = Math.Max(0.001, maxY - minY);
            var width = height * imageAspectRatio / screenAspectRatio;
            var adjustedMinX = Math.Max(0, maxX - width);

            return FormatAnchor(adjustedMinX, minY);
        }

        private static bool SameConfigValue(string current, string expected)
        {
            return string.Equals((current ?? "").Trim(), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLegacyLogoUrl(string url)
        {
            var normalized = (url ?? "").Trim().Replace('\\', '/');
            return SameConfigValue(normalized, LegacyFooterLogoUrl) ||
                   SameConfigValue(normalized, MistakenNavLogoUrl) ||
                   normalized.EndsWith("/assets/media/in-game/raidlands-footer-logo.png", StringComparison.OrdinalIgnoreCase) ||
                   normalized.EndsWith("/assets/media/nav-logo.png", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLegacyAnchorSet(string anchorMin, string anchorMax)
        {
            return (SameConfigValue(anchorMin, "0.675 0.015") && SameConfigValue(anchorMax, "0.84 0.11")) ||
                   (SameConfigValue(anchorMin, "0.01 0.02") && SameConfigValue(anchorMax, "0.15 0.1"));
        }

        private bool ApplyRaidlandsLogoDefaults()
        {
            bool changed = false;
            var urls = Config["UI", "BackgroundMainURL"] as List<object>;

            if (urls == null || urls.Count == 0 || urls.Any(url => IsLegacyLogoUrl(url?.ToString())))
            {
                Config["UI", "BackgroundMainURL"] = new List<object> { MenuLogoUrl };
                changed = true;
            }

            var anchorMin = Config["UI", "GUIAnchorMin"]?.ToString();
            var anchorMax = Config["UI", "GUIAnchorMax"]?.ToString();

            if (changed || IsLegacyAnchorSet(anchorMin, anchorMax))
            {
                Config["UI", "GUIAnchorMin"] = DefaultAnchorMin;
                Config["UI", "GUIAnchorMax"] = DefaultAnchorMax;
                Config["UI", "AutoWidthFromImage"] = true;
                Config["UI", "ImageAspectRatio"] = DefaultImageAspectRatio;
                Config["UI", "ScreenAspectRatio"] = DefaultScreenAspectRatio;
                changed = true;
            }

            return changed;
        }

        void LoadConfig()
        {
            List<object> listUrl = new List<object> { MenuLogoUrl };
            SetConfig("UI", "GUIAnchorMin", DefaultAnchorMin);
            SetConfig("UI", "GUIAnchorMax", DefaultAnchorMax);
            SetConfig("UI", "AutoWidthFromImage", true);
            SetConfig("UI", "ImageAspectRatio", DefaultImageAspectRatio);
            SetConfig("UI", "ScreenAspectRatio", DefaultScreenAspectRatio);
            SetConfig("UI", "BackgroundMainColor", "0 0 0 0");
            SetConfig("UI", "BackgroundMainURL", listUrl);
            SetConfig("UI", "IntervalBetweenImage", 30);

            if (ApplyRaidlandsLogoDefaults())
                Puts("Updated SimpleLogo config to use the Raidlands website menu logo and shorter HUD sizing.");

            SaveConfig();

            _anchorMin = Config["UI", "GUIAnchorMin"].ToString();
            _anchorMax = Config["UI", "GUIAnchorMax"].ToString();
            bool autoWidthFromImage = GetConfig(true, "UI", "AutoWidthFromImage");
            double imageAspectRatio = GetConfig(DefaultImageAspectRatio, "UI", "ImageAspectRatio");
            double screenAspectRatio = GetConfig(DefaultScreenAspectRatio, "UI", "ScreenAspectRatio");

            if (autoWidthFromImage)
                _anchorMin = AnchorMinWithAutoWidth(_anchorMin, _anchorMax, imageAspectRatio, screenAspectRatio);

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
                var imageName = GetImageName(i);
                AddImageToLibrary(url.ToString(), imageName, 0, () => NextTick(RefreshUi));
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
            var imageName = GetImageName(_currentlySelected);

            if (!HasImage(imageName))
            {
                PrintWarning($"Image {imageName} not found in ImageLibrary; skipping SimpleLogo UI until the image is cached.");
                return null;
            }

            var url = GetImage(imageName);

            if (string.IsNullOrEmpty(url))
            {
                PrintWarning($"Image {imageName} returned an empty ImageLibrary id; skipping SimpleLogo UI.");
                return null;
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
                if (backgroundImageWin == null)
                    return;

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
