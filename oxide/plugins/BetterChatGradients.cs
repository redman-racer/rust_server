using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Better Chat Gradients", "Raidlands", "1.2.1")]
    [Description("Adds Better Chat gradients and admin-only chat rank previews.")]
    public class BetterChatGradients : RustPlugin
    {
        [PluginReference]
        private Plugin BetterChat;

        private const string PreviewPermission = "betterchatgradients.preview";
        private const string AdminGroup = "admin";

        private const string PreviewUsername = "RaidlandsPlayer";
        private const string PreviewMessage = "Raidlands chat preview.";

        private readonly Dictionary<string, GradientColor[]> _paletteCache =
            new Dictionary<string, GradientColor[]>(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex BetterChatTitlePattern = new Regex(
            @"^\[#(?<palette>[^\]]*,[^\]]*)\]\[\+(?<size>\d+)\](?<text>.*?)\[/\+\]\[/#\]$",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex UniversalFormattingPattern = new Regex(
            @"\[#(?:[^\]]+)\]|\[/#\]|\[\+\d+\]|\[/\+\]|<[^>]+>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Dictionary<string, GradientColor> NamedColors =
            new Dictionary<string, GradientColor>(StringComparer.OrdinalIgnoreCase)
            {
                { "black",   new GradientColor(0, 0, 0) },
                { "blue",    new GradientColor(0, 102, 255) },
                { "brown",   new GradientColor(153, 102, 51) },
                { "cyan",    new GradientColor(0, 255, 255) },
                { "gray",    new GradientColor(128, 128, 128) },
                { "grey",    new GradientColor(128, 128, 128) },
                { "green",   new GradientColor(0, 204, 102) },
                { "magenta", new GradientColor(255, 0, 255) },
                { "orange",  new GradientColor(255, 140, 0) },
                { "purple",  new GradientColor(153, 51, 255) },
                { "red",     new GradientColor(255, 64, 64) },
                { "white",   new GradientColor(255, 255, 255) },
                { "yellow",  new GradientColor(255, 215, 0) }
            };

        private void Init()
        {
            permission.RegisterPermission(PreviewPermission, this);
        }

        private void OnServerInitialized()
        {
            if (BetterChat == null)
            {
                PrintWarning("Better Chat is not loaded. Gradients and previews require BetterChat.");
                return;
            }

            Puts("Better Chat detected. /chatpreview is registered (v1.2.1).");
        }

        [ChatCommand("chatpreview")]
        private void ChatPreviewCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null)
            {
                return;
            }

            if (!CanUsePreview(player))
            {
                SendReply(player, "<color=#E35A43>You must be in the admin group to use /chatpreview.</color>");
                return;
            }

            RunPreview(player, args);
        }


        private bool CanUsePreview(BasePlayer player)
        {
            if (player == null)
            {
                return false;
            }

            if (player.net != null &&
                player.net.connection != null &&
                player.net.connection.authLevel >= 1)
            {
                return true;
            }

            string userId = player.UserIDString;

            return permission.UserHasGroup(userId, AdminGroup) ||
                   permission.UserHasPermission(userId, PreviewPermission);
        }

        private void RunPreview(BasePlayer player, string[] args)
        {
            if (BetterChat == null)
            {
                SendReply(player, "<color=#E35A43>Better Chat is not loaded.</color>");
                return;
            }

            List<PreviewGroup> groups = GetBetterChatGroups();

            if (groups.Count == 0)
            {
                SendReply(
                    player,
                    "<color=#E35A43>No Better Chat groups could be loaded. Check the server console.</color>");
                return;
            }

            string[] suppliedArgs = args ?? new string[0];

            if (suppliedArgs.Length == 0 ||
                suppliedArgs[0].Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                SendSimpleRankStack(player, groups);
                return;
            }

            if (suppliedArgs[0].Equals("combos", StringComparison.OrdinalIgnoreCase) ||
                suppliedArgs[0].Equals("stacks", StringComparison.OrdinalIgnoreCase))
            {
                SendPreviewHeader(player, "Combined rank examples");
                SendStackExamples(player, groups);
                SendPreviewFooter(player);
                return;
            }

            List<PreviewGroup> selectedGroups = new List<PreviewGroup>();

            foreach (string requestedName in suppliedArgs)
            {
                PreviewGroup selected = FindGroupByAlias(groups, requestedName);

                if (selected == null)
                {
                    SendReply(
                        player,
                        "<color=#E35A43>Unknown Better Chat group:</color> " +
                        EscapeRichText(requestedName));
                    return;
                }

                if (!selectedGroups.Contains(selected))
                {
                    selectedGroups.Add(selected);
                }
            }

            string heading = selectedGroups.Count == 1
                ? GetDisplayName(selectedGroups[0])
                : "Custom group combination";

            SendPreviewHeader(player, heading);
            SendReply(player, BuildPreviewMessage(selectedGroups));
            SendPreviewFooter(player);
        }

        private void SendSimpleRankStack(
            BasePlayer player,
            List<PreviewGroup> groups)
        {
            SendPreviewHeader(player, "Raidlands chat rank preview");

            foreach (PreviewGroup group in groups
                .OrderBy(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.GroupName))
            {
                if (group.GroupName.Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string preview = BuildPreviewMessage(
                    new List<PreviewGroup> { group });

                if (!string.IsNullOrEmpty(preview))
                {
                    SendReply(player, preview);
                }
            }

            PreviewGroup defaultGroup = groups.FirstOrDefault(
                group => group.GroupName.Equals(
                    "default",
                    StringComparison.OrdinalIgnoreCase));

            if (defaultGroup != null)
            {
                string preview = BuildPreviewMessage(
                    new List<PreviewGroup> { defaultGroup });

                if (!string.IsNullOrEmpty(preview))
                {
                    SendReply(player, preview);
                }
            }

            SendPreviewFooter(player);
        }

        private static PreviewGroup FindGroupByAlias(
            List<PreviewGroup> groups,
            string requestedName)
        {
            if (groups == null || string.IsNullOrWhiteSpace(requestedName))
            {
                return null;
            }

            string normalizedRequest = NormalizeGroupAlias(requestedName);

            return groups.FirstOrDefault(group =>
                NormalizeGroupAlias(group.GroupName)
                    .Equals(normalizedRequest, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeGroupAlias(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value
                .Trim()
                .ToLowerInvariant()
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .Replace("_", string.Empty);

            if (normalized.StartsWith("rank", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(4);
            }

            if (normalized.EndsWith("vip", StringComparison.Ordinal) &&
                normalized != "vip" &&
                normalized != "vipplus")
            {
                normalized = normalized.Substring(
                    0,
                    normalized.Length - 3);
            }

            if (normalized == "gold")
            {
                normalized = "golden";
            }

            if (normalized == "supporterbadge")
            {
                normalized = "perksupporterbadge";
            }

            if (normalized == "supporter")
            {
                normalized = "perksupporterbadge";
            }

            return normalized;
        }

        private static string GetDisplayName(PreviewGroup group)
        {
            if (group == null)
            {
                return "Rank preview";
            }

            string title = StripFormatting(group.TitleText);

            return string.IsNullOrWhiteSpace(title)
                ? group.GroupName
                : title.Trim('[', ']');
        }

        private List<PreviewGroup> GetBetterChatGroups()
        {
            JArray rawGroups = null;

            try
            {
                object apiResult = BetterChat.Call("API_GetAllGroups");

                if (apiResult != null)
                {
                    JToken token = apiResult as JToken ?? JToken.FromObject(apiResult);
                    rawGroups = token as JArray;
                }
            }
            catch (Exception exception)
            {
                PrintWarning("Better Chat API group read failed: " + exception.Message);
            }

            if (rawGroups == null)
            {
                try
                {
                    rawGroups = Interface.Oxide.DataFileSystem.ReadObject<JArray>("BetterChat");
                }
                catch (Exception exception)
                {
                    PrintError("Could not read oxide/data/BetterChat.json: " + exception.Message);
                    return new List<PreviewGroup>();
                }
            }

            List<PreviewGroup> groups = new List<PreviewGroup>();

            foreach (JToken token in rawGroups)
            {
                JObject rawGroup = token as JObject;
                PreviewGroup parsed;

                if (TryParseGroup(rawGroup, out parsed))
                {
                    groups.Add(parsed);
                }
            }

            return groups;
        }

        private static bool TryParseGroup(JObject source, out PreviewGroup group)
        {
            group = null;

            if (source == null)
            {
                return false;
            }

            string groupName = ReadString(source, "GroupName", string.Empty);

            if (string.IsNullOrWhiteSpace(groupName))
            {
                return false;
            }

            JObject title = source["Title"] as JObject;
            JObject username = source["Username"] as JObject;
            JObject message = source["Message"] as JObject;
            JObject format = source["Format"] as JObject;

            group = new PreviewGroup
            {
                GroupName = groupName,
                Priority = ReadInt(source, "Priority", 0),

                TitleText = ReadString(title, "Text", string.Empty),
                TitleColor = ReadString(title, "Color", "#FFFFFF"),
                TitleSize = ReadInt(title, "Size", 15),
                TitleHidden = ReadBool(title, "Hidden", false),
                TitleHiddenIfNotPrimary = ReadBool(title, "HiddenIfNotPrimary", false),

                UsernameColor = ReadString(username, "Color", "#FFFFFF"),
                UsernameSize = ReadInt(username, "Size", 15),

                MessageColor = ReadString(message, "Color", "#FFFFFF"),
                MessageSize = ReadInt(message, "Size", 15),

                ChatFormat = ReadString(
                    format,
                    "Chat",
                    "{Title} {Username}: {Message}")
            };

            return true;
        }

        private void SendStackExamples(BasePlayer player, List<PreviewGroup> allGroups)
        {
            SendNamedStack(player, allGroups, "Titan + Admin",
                "rank_titan_vip", "admin");

            SendNamedStack(player, allGroups, "Titan + Supporter",
                "rank_titan_vip", "perk_supporter_badge");

            SendNamedStack(player, allGroups, "Titan + Admin + Supporter",
                "rank_titan_vip", "admin", "perk_supporter_badge");

            SendNamedStack(player, allGroups, "Ultimate + VIP+ + Supporter",
                "rank_ultimate_vip", "rank_vip_plus", "perk_supporter_badge");

            SendNamedStack(player, allGroups, "Diamond + Golden + Admin",
                "rank_diamond_vip", "rank_golden_vip", "admin");
        }

        private void SendNamedStack(
            BasePlayer player,
            List<PreviewGroup> allGroups,
            string label,
            params string[] groupNames)
        {
            List<PreviewGroup> selected = new List<PreviewGroup>();

            foreach (string groupName in groupNames)
            {
                PreviewGroup group = allGroups.FirstOrDefault(
                    candidate => candidate.GroupName.Equals(
                        groupName,
                        StringComparison.OrdinalIgnoreCase));

                if (group != null)
                {
                    selected.Add(group);
                }
            }

            if (selected.Count > 0)
            {
                SendPreviewLine(player, label, selected);
            }
        }

        private void SendPreviewHeader(BasePlayer player, string heading)
        {
            SendReply(
                player,
                "\n<size=16><color=#FFB04A><b>" +
                EscapeRichText(heading) +
                "</b></color></size>");

            SendReply(
                player,
                "<size=11><color=#9D9188>" +
                "Private preview — only you can see these lines." +
                "</color></size>");
        }

        private void SendPreviewFooter(BasePlayer player)
        {
            SendReply(
                player,
                "<size=11><color=#9D9188>" +
                "/chatpreview | /chatpreview diamond | /chatpreview titan admin supporter | /chatpreview combos" +
                "</color></size>\n");
        }

        private void SendPreviewLine(
            BasePlayer player,
            string label,
            List<PreviewGroup> memberships)
        {
            string preview = BuildPreviewMessage(memberships);

            if (string.IsNullOrEmpty(preview))
            {
                return;
            }

            SendReply(
                player,
                "<size=11><color=#887D75>" +
                EscapeRichText(label) +
                "</color></size>");

            SendReply(player, preview);
        }

        private string BuildPreviewMessage(List<PreviewGroup> memberships)
        {
            if (memberships == null || memberships.Count == 0)
            {
                return string.Empty;
            }

            List<PreviewGroup> sorted = memberships
                .Where(group => group != null)
                .OrderBy(group => group.Priority)
                .ThenBy(group => group.GroupName)
                .ToList();

            if (sorted.Count == 0)
            {
                return string.Empty;
            }

            PreviewGroup primary = sorted[0];
            List<string> renderedTitles = new List<string>();

            foreach (PreviewGroup group in sorted)
            {
                if (group.TitleHidden || string.IsNullOrWhiteSpace(group.TitleText))
                {
                    continue;
                }

                bool isPrimary = group == primary;

                if (group.TitleHiddenIfNotPrimary && !isPrimary)
                {
                    continue;
                }

                renderedTitles.Add(
                    BuildStyledText(
                        StripFormatting(group.TitleText),
                        group.TitleColor,
                        group.TitleSize));
            }

            string titleText = string.Join(" ", renderedTitles.ToArray());
            string usernameText = BuildStyledText(
                PreviewUsername,
                primary.UsernameColor,
                primary.UsernameSize);

            string messageText = BuildStyledText(
                PreviewMessage,
                primary.MessageColor,
                primary.MessageSize);

            string result = primary.ChatFormat ?? "{Title} {Username}: {Message}";

            result = result.Replace("{Title}", titleText);
            result = result.Replace("{Username}", usernameText);
            result = result.Replace("{Message}", messageText);
            result = result.Replace("{ID}", "76561190000000000");
            result = result.Replace("{Group}", EscapeRichText(primary.GroupName));
            result = result.Replace("{Date}", DateTime.Now.ToString("yyyy-MM-dd"));
            result = result.Replace("{Time}", DateTime.Now.ToString("HH:mm"));

            result = ConvertBetterChatFormatting(result);

            return NormalizePreviewSpacing(result);
        }

        private static string ConvertBetterChatFormatting(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string converted = Regex.Replace(
                text,
                @"\[#(?<color>[0-9A-Fa-f]{6,8})\](?<content>.*?)\[/#\]",
                match =>
                    "<color=#" +
                    match.Groups["color"].Value.Substring(0, 6) +
                    ">" +
                    match.Groups["content"].Value +
                    "</color>",
                RegexOptions.Singleline);

            converted = Regex.Replace(
                converted,
                @"\[\+(?<size>\d+)\](?<content>.*?)\[/\+\]",
                match =>
                    "<size=" +
                    match.Groups["size"].Value +
                    ">" +
                    match.Groups["content"].Value +
                    "</size>",
                RegexOptions.Singleline);

            return converted;
        }

        private string BuildStyledText(string text, string rawColor, int size)
        {
            string safeText = EscapeRichText(StripFormatting(text));
            int safeSize = Math.Max(1, size);

            GradientColor[] palette;

            if (TryGetPalette(rawColor, out palette))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "<size={0}>{1}</size>",
                    safeSize,
                    BuildGradient(safeText, palette));
            }

            GradientColor singleColor;

            if (TryParseColor(rawColor, out singleColor))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "<size={0}><color=#{1}>{2}</color></size>",
                    safeSize,
                    singleColor.ToHex(),
                    safeText);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "<size={0}>{1}</size>",
                safeSize,
                safeText);
        }

        private object OnBetterChat(Dictionary<string, object> data)
        {
            if (data == null)
            {
                return null;
            }

            ApplyTitleGradients(data);
            ApplyTextGradient(data, "Username", "UsernameSettings");
            ApplyTextGradient(data, "Message", "MessageSettings");

            return data;
        }

        private void ApplyTitleGradients(Dictionary<string, object> data)
        {
            object titlesObject;

            if (!data.TryGetValue("Titles", out titlesObject))
            {
                return;
            }

            List<string> titles = titlesObject as List<string>;

            if (titles == null)
            {
                return;
            }

            for (int index = 0; index < titles.Count; index++)
            {
                string title = titles[index];

                if (string.IsNullOrEmpty(title))
                {
                    continue;
                }

                Match match = BetterChatTitlePattern.Match(title);

                if (!match.Success)
                {
                    continue;
                }

                GradientColor[] palette;

                if (!TryGetPalette(match.Groups["palette"].Value, out palette))
                {
                    continue;
                }

                int size;

                if (!int.TryParse(
                    match.Groups["size"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out size))
                {
                    size = 15;
                }

                string plainText = StripFormatting(match.Groups["text"].Value);
                string gradientText = BuildGradient(plainText, palette);

                titles[index] = string.Format(
                    CultureInfo.InvariantCulture,
                    "<size={0}>{1}</size>",
                    Math.Max(1, size),
                    gradientText);
            }
        }

        private void ApplyTextGradient(
            Dictionary<string, object> data,
            string textKey,
            string settingsKey)
        {
            object textObject;
            object settingsObject;

            if (!data.TryGetValue(textKey, out textObject) ||
                !data.TryGetValue(settingsKey, out settingsObject))
            {
                return;
            }

            string text = textObject as string;
            Dictionary<string, object> settings =
                settingsObject as Dictionary<string, object>;

            if (string.IsNullOrEmpty(text) || settings == null)
            {
                return;
            }

            object colorObject;

            if (!settings.TryGetValue("Color", out colorObject))
            {
                return;
            }

            string rawPalette = colorObject as string;
            GradientColor[] palette;

            if (!TryGetPalette(rawPalette, out palette))
            {
                return;
            }

            settings["Color"] = "#" + palette[0].ToHex();
            data[textKey] = BuildGradient(StripFormatting(text), palette);
        }

        private bool TryGetPalette(string rawPalette, out GradientColor[] palette)
        {
            palette = null;

            if (string.IsNullOrWhiteSpace(rawPalette) ||
                rawPalette.IndexOf(',') < 0)
            {
                return false;
            }

            GradientColor[] cached;

            if (_paletteCache.TryGetValue(rawPalette, out cached))
            {
                palette = cached;
                return true;
            }

            string[] parts = rawPalette.Split(',');
            List<GradientColor> colors = new List<GradientColor>();

            for (int index = 0; index < parts.Length; index++)
            {
                GradientColor color;

                if (!TryParseColor(parts[index], out color))
                {
                    return false;
                }

                colors.Add(color);
            }

            if (colors.Count < 2)
            {
                return false;
            }

            palette = colors.ToArray();
            _paletteCache[rawPalette] = palette;
            return true;
        }

        private static bool TryParseColor(string rawColor, out GradientColor color)
        {
            color = new GradientColor();

            if (string.IsNullOrWhiteSpace(rawColor))
            {
                return false;
            }

            string value = rawColor.Trim();

            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                value = value.Substring(1);
            }

            GradientColor namedColor;

            if (NamedColors.TryGetValue(value, out namedColor))
            {
                color = namedColor;
                return true;
            }

            if (value.Length == 3)
            {
                value = string.Concat(
                    value[0], value[0],
                    value[1], value[1],
                    value[2], value[2]);
            }

            if (value.Length == 8)
            {
                value = value.Substring(0, 6);
            }

            if (value.Length != 6)
            {
                return false;
            }

            byte red;
            byte green;
            byte blue;

            if (!byte.TryParse(
                    value.Substring(0, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out red) ||
                !byte.TryParse(
                    value.Substring(2, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out green) ||
                !byte.TryParse(
                    value.Substring(4, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out blue))
            {
                return false;
            }

            color = new GradientColor(red, green, blue);
            return true;
        }

        private static string BuildGradient(
            string text,
            GradientColor[] palette)
        {
            if (string.IsNullOrEmpty(text) ||
                palette == null ||
                palette.Length < 2)
            {
                return text;
            }

            int[] elementIndexes = StringInfo.ParseCombiningCharacters(text);

            if (elementIndexes.Length == 0)
            {
                return text;
            }

            int visibleElementCount = 0;

            for (int index = 0; index < elementIndexes.Length; index++)
            {
                string element = GetTextElement(text, elementIndexes, index);

                if (!string.IsNullOrWhiteSpace(element))
                {
                    visibleElementCount++;
                }
            }

            if (visibleElementCount == 0)
            {
                return text;
            }

            StringBuilder builder =
                new StringBuilder(text.Length + visibleElementCount * 24);

            string activeColor = null;
            int visibleIndex = 0;

            for (int index = 0; index < elementIndexes.Length; index++)
            {
                string element = GetTextElement(text, elementIndexes, index);

                if (string.IsNullOrWhiteSpace(element))
                {
                    builder.Append(element);
                    continue;
                }

                float position = visibleElementCount <= 1
                    ? 0f
                    : visibleIndex / (float)(visibleElementCount - 1);

                string nextColor =
                    EvaluatePalette(palette, position).ToHex();

                if (!string.Equals(
                    activeColor,
                    nextColor,
                    StringComparison.Ordinal))
                {
                    if (activeColor != null)
                    {
                        builder.Append("</color>");
                    }

                    builder.Append("<color=#");
                    builder.Append(nextColor);
                    builder.Append(">");

                    activeColor = nextColor;
                }

                builder.Append(element);
                visibleIndex++;
            }

            if (activeColor != null)
            {
                builder.Append("</color>");
            }

            return builder.ToString();
        }

        private static string GetTextElement(
            string text,
            int[] indexes,
            int elementIndex)
        {
            int start = indexes[elementIndex];
            int end = elementIndex + 1 < indexes.Length
                ? indexes[elementIndex + 1]
                : text.Length;

            return text.Substring(start, end - start);
        }

        private static GradientColor EvaluatePalette(
            GradientColor[] palette,
            float position)
        {
            position = Math.Max(0f, Math.Min(1f, position));

            float scaled = position * (palette.Length - 1);
            int segment = (int)Math.Floor(scaled);

            if (segment >= palette.Length - 1)
            {
                return palette[palette.Length - 1];
            }

            float localPosition = scaled - segment;

            return GradientColor.LerpGammaCorrect(
                palette[segment],
                palette[segment + 1],
                localPosition);
        }

        private static string NormalizePreviewSpacing(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string result = text;

            while (result.Contains("  "))
            {
                result = result.Replace("  ", " ");
            }

            result = result.Replace(" \n", "\n").Trim();

            if (result.StartsWith(":", StringComparison.Ordinal))
            {
                result = result.Substring(1).TrimStart();
            }

            return result;
        }

        private static string EscapeRichText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        private static string StripFormatting(string text)
        {
            return string.IsNullOrEmpty(text)
                ? text
                : UniversalFormattingPattern.Replace(text, string.Empty);
        }

        private static string ReadString(
            JObject source,
            string key,
            string fallback)
        {
            if (source == null)
            {
                return fallback;
            }

            JToken token = source[key];

            return token == null || token.Type == JTokenType.Null
                ? fallback
                : token.ToString();
        }

        private static int ReadInt(
            JObject source,
            string key,
            int fallback)
        {
            if (source == null)
            {
                return fallback;
            }

            JToken token = source[key];
            int value;

            return token != null &&
                   int.TryParse(
                       token.ToString(),
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out value)
                ? value
                : fallback;
        }

        private static bool ReadBool(
            JObject source,
            string key,
            bool fallback)
        {
            if (source == null)
            {
                return fallback;
            }

            JToken token = source[key];
            bool value;

            return token != null &&
                   bool.TryParse(token.ToString(), out value)
                ? value
                : fallback;
        }

        private sealed class PreviewGroup
        {
            public string GroupName;
            public int Priority;

            public string TitleText;
            public string TitleColor;
            public int TitleSize;
            public bool TitleHidden;
            public bool TitleHiddenIfNotPrimary;

            public string UsernameColor;
            public int UsernameSize;

            public string MessageColor;
            public int MessageSize;

            public string ChatFormat;
        }

        private struct GradientColor
        {
            public byte Red;
            public byte Green;
            public byte Blue;

            public GradientColor(byte red, byte green, byte blue)
            {
                Red = red;
                Green = green;
                Blue = blue;
            }

            public string ToHex()
            {
                return Red.ToString("X2", CultureInfo.InvariantCulture) +
                       Green.ToString("X2", CultureInfo.InvariantCulture) +
                       Blue.ToString("X2", CultureInfo.InvariantCulture);
            }

            public static GradientColor LerpGammaCorrect(
                GradientColor from,
                GradientColor to,
                float amount)
            {
                amount = Math.Max(0f, Math.Min(1f, amount));

                double redLinear =
                    ToLinear(from.Red) +
                    (ToLinear(to.Red) - ToLinear(from.Red)) * amount;

                double greenLinear =
                    ToLinear(from.Green) +
                    (ToLinear(to.Green) - ToLinear(from.Green)) * amount;

                double blueLinear =
                    ToLinear(from.Blue) +
                    (ToLinear(to.Blue) - ToLinear(from.Blue)) * amount;

                return new GradientColor(
                    ToSrgb(redLinear),
                    ToSrgb(greenLinear),
                    ToSrgb(blueLinear));
            }

            private static double ToLinear(byte channel)
            {
                double value = channel / 255d;

                return value <= 0.04045d
                    ? value / 12.92d
                    : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
            }

            private static byte ToSrgb(double value)
            {
                value = Math.Max(0d, Math.Min(1d, value));

                double srgb = value <= 0.0031308d
                    ? value * 12.92d
                    : 1.055d * Math.Pow(value, 1d / 2.4d) - 0.055d;

                return (byte)Math.Round(srgb * 255d);
            }
        }
    }
}
