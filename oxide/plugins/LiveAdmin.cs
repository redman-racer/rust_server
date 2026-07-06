using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
namespace Oxide.Plugins
{
[Info("LiveAdmin", "Codex", "0.4.5")]
[Description("A live in-game staff admin panel for Rust/uMod servers.")]
public class LiveAdmin : RustPlugin
{
private const string UiRoot = "LiveAdmin.Root";
private const string PermUse = "liveadmin.use";
private const string PermOwner = "liveadmin.owner";
private const string PermPlayersView = "liveadmin.players.view";
private const string PermPlayersModerate = "liveadmin.players.moderate";
private const string PermPlayersTeleport = "liveadmin.players.teleport";
private const string PermReportsView = "liveadmin.reports.view";
private const string PermReportsManage = "liveadmin.reports.manage";
private const string PermPluginsView = "liveadmin.plugins.view";
private const string PermPluginsManage = "liveadmin.plugins.manage";
private const string PermGroupsView = "liveadmin.groups.view";
private const string PermGroupsManage = "liveadmin.groups.manage";
private const string PermPermissionsView = "liveadmin.permissions.view";
private const string PermPermissionsManage = "liveadmin.permissions.manage";
private const string PermCommandsView = "liveadmin.commands.view";
private const string PermServerActions = "liveadmin.server.actions";
private const int CommandRowsPerPage = 10;
private const float CommandCatalogCacheSeconds = 15f;
private StoredData storedData;
private PluginConfig config;
private readonly Dictionary<ulong, PanelState> states = new Dictionary<ulong, PanelState>();
private readonly HashSet<string> registeredPermissions = new HashSet<string>();
private readonly string[] tabs = { "dashboard", "players", "reports", "manage", "commands", "logs" };
private List<CommandCatalogEntry> commandCatalogCache;
private float commandCatalogCachedAt = -100f;
private static readonly Regex InfoAttributeRegex = new Regex(@"\[Info\(\s*""([^""]+)""", RegexOptions.Compiled);
private static readonly Regex ChatCommandAttributeRegex = new Regex(@"\[ChatCommand\(\s*""([^""]+)""", RegexOptions.Compiled);
private static readonly Regex ConsoleCommandAttributeRegex = new Regex(@"\[ConsoleCommand\(\s*""([^""]+)""", RegexOptions.Compiled);
private static readonly Regex CovalenceCommandAttributeRegex = new Regex(@"\[Command\(([^\)]*)\)", RegexOptions.Compiled);
private static readonly Regex AddChatCommandRegex = new Regex(@"\bAddChatCommand\s*\(\s*""([^""]+)""", RegexOptions.Compiled);
private static readonly Regex AddConsoleCommandRegex = new Regex(@"\bAddConsoleCommand\s*\(\s*""([^""]+)""", RegexOptions.Compiled);
private static readonly Regex AddCovalenceCommandRegex = new Regex(@"\bAddCovalenceCommand\s*\(([^\)]*)\)", RegexOptions.Compiled);
private static readonly Regex StringLiteralRegex = new Regex(@"""([^""]+)""", RegexOptions.Compiled);
private class PanelState
{
public string Tab = "dashboard";
public string SubTab = "plugins";
public string CommandSubTab = "slash";
public int PlayerPage;
public int PluginPage;
public int GroupPage;
public int PermissionGroupPage;
public int PermissionPluginPage;
public int PermissionPage;
public int ConfigPage;
public int SlashCommandPage;
public int ConsoleCommandPage;
public int GroupDropdownPage;
public string SelectedPlayerId;
public string SelectedGroup;
public string SelectedPlugin;
public string SelectedConfigPlugin;
public string PlayerFilter = string.Empty;
public string PluginFilter = string.Empty;
public string GroupFilter = string.Empty;
public string PermissionFilter = string.Empty;
public string CommandFilter = string.Empty;
public string GroupDropdownFilter = string.Empty;
public bool GroupDropdownOpen;
public string PendingAction;
public string PendingTarget;
public string PendingValue;
public string PendingMessage;
}
private class PluginConfig
{
public string Title = "LiveAdmin";
public string AccentColor = "0.12 0.58 0.55 1";
public string WarningColor = "0.96 0.62 0.18 1";
public string DangerColor = "0.82 0.20 0.18 1";
public string SuccessColor = "0.24 0.68 0.36 1";
public int PlayersPerPage = 10;
public int LogsPerPage = 12;
public List<string> ManagedPlugins = new List<string> { "Kits", "BetterChat", "ZoneManager" };
public List<string> CommonGroups = new List<string> { "admin", "moderator", "vip", "default" };
public List<string> CommonPermissions = new List<string>
{
"liveadmin.use",
"liveadmin.players.view",
"liveadmin.players.moderate",
"liveadmin.players.teleport",
"liveadmin.reports.view",
"liveadmin.reports.manage"
};
public List<string> ProtectedGroups = new List<string> { "admin", "owner" };
public List<QuickCommand> QuickCommands = new List<QuickCommand>
{
new QuickCommand { Label = "Save Server", Command = "server.save", Permission = PermServerActions, Confirm = false },
new QuickCommand { Label = "Reload Kits", Command = "oxide.reload Kits", Permission = PermPluginsManage, Confirm = true }
};
}
private class QuickCommand
{
public string Label;
public string Command;
public string Permission;
public bool Confirm;
}
private class StoredData
{
public List<ReportEntry> Reports = new List<ReportEntry>();
public List<ActionLog> Logs = new List<ActionLog>();
public Dictionary<string, List<StaffNote>> Notes = new Dictionary<string, List<StaffNote>>();
public int NextReportId = 1;
}
private class ReportEntry
{
public int Id;
public string ReporterId;
public string ReporterName;
public string TargetId;
public string TargetName;
public string Reason;
public string Status = "Open";
public string ClaimedBy;
public string CreatedAt;
public string UpdatedAt;
}
private class ActionLog
{
public string Time;
public string ActorId;
public string ActorName;
public string Action;
public string Target;
public string Details;
}
private class StaffNote
{
public string Time;
public string StaffId;
public string StaffName;
public string Text;
}
private class ConfigEntry
{
public int Index;
public string Key;
public string Value;
public string Kind;
public JTokenType Type;
public bool Editable;
}
private class CommandCatalogEntry
{
public string Kind;
public string Command;
public string Plugin;
public string Source;
}
protected override void LoadDefaultConfig()
{
config = new PluginConfig();
SaveConfig();
}
protected override void LoadConfig()
{
base.LoadConfig();
config = Config.ReadObject<PluginConfig>();
if (config == null)
{
LoadDefaultConfig();
}
EnsureConfigDefaults();
}
protected override void SaveConfig()
{
Config.WriteObject(config, true);
}
private void EnsureConfigDefaults()
{
if (config.ManagedPlugins == null || config.ManagedPlugins.Count == 0)
{
config.ManagedPlugins = new List<string> { "Kits", "BetterChat", "ZoneManager" };
}
if (config.CommonGroups == null || config.CommonGroups.Count == 0)
{
config.CommonGroups = new List<string> { "admin", "moderator", "vip", "default" };
}
if (config.CommonPermissions == null || config.CommonPermissions.Count == 0)
{
config.CommonPermissions = new List<string>
{
"liveadmin.use",
"liveadmin.players.view",
"liveadmin.players.moderate",
"liveadmin.players.teleport",
"liveadmin.reports.view",
"liveadmin.reports.manage",
"liveadmin.commands.view"
};
}
if (config.ProtectedGroups == null)
{
config.ProtectedGroups = new List<string> { "admin", "owner" };
}
if (config.QuickCommands == null)
{
config.QuickCommands = new List<QuickCommand>();
}
}
private void Init()
{
RegisterPermissions();
storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
}
private void Loaded()
{
RegisterPermissions();
}
private void OnServerInitialized()
{
RegisterPermissions();
}
private void Unload()
{
foreach (var player in BasePlayer.activePlayerList)
{
CuiHelper.DestroyUi(player, UiRoot);
}
SaveData();
}
private void RegisterPermissions()
{
var perms = new[]
{
PermUse, PermOwner, PermPlayersView, PermPlayersModerate, PermPlayersTeleport,
PermReportsView, PermReportsManage, PermPluginsView, PermPluginsManage,
PermGroupsView, PermGroupsManage, PermPermissionsView, PermPermissionsManage,
PermCommandsView, PermServerActions
};
foreach (var perm in perms)
{
if (registeredPermissions.Contains(perm) || permission.PermissionExists(perm, this))
{
registeredPermissions.Add(perm);
continue;
}
permission.RegisterPermission(perm, this);
registeredPermissions.Add(perm);
}
Puts($"Registered {registeredPermissions.Count} LiveAdmin permissions.");
}
private void SaveData()
{
Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);
}
[ChatCommand("admin")]
private void AdminCommand(BasePlayer player, string command, string[] args)
{
if (!Can(player, PermUse))
{
Reply(player, "You do not have access to LiveAdmin.");
return;
}
if (args.Length > 0 && args[0].Equals("close", StringComparison.OrdinalIgnoreCase))
{
Close(player);
return;
}
GetState(player).Tab = args.Length > 0 ? CleanTab(args[0]) : "dashboard";
Draw(player);
}
[ChatCommand("staff")]
private void StaffCommand(BasePlayer player, string command, string[] args) => AdminCommand(player, command, args);
[ChatCommand("ap")]
private void ApCommand(BasePlayer player, string command, string[] args) => AdminCommand(player, command, args);
[ChatCommand("report")]
private void ReportCommand(BasePlayer player, string command, string[] args)
{
if (args.Length < 2)
{
Reply(player, "Use /report <player> <reason>");
return;
}
var targetName = args[0];
var target = FindPlayer(targetName);
var reason = string.Join(" ", args.Skip(1).ToArray());
var entry = new ReportEntry
{
Id = storedData.NextReportId++,
ReporterId = player.UserIDString,
ReporterName = player.displayName,
TargetId = target?.UserIDString ?? targetName,
TargetName = target?.displayName ?? targetName,
Reason = reason,
CreatedAt = Now(),
UpdatedAt = Now()
};
storedData.Reports.Add(entry);
Log(player, "ReportCreated", entry.TargetName, reason);
SaveData();
Reply(player, $"Report #{entry.Id} submitted. Staff have been notified.");
foreach (var staff in BasePlayer.activePlayerList.Where(p => Can(p, PermReportsView)))
{
Reply(staff, $"New report #{entry.Id}: {entry.ReporterName} reported {entry.TargetName} - {entry.Reason}");
}
}
[ChatCommand("lanote")]
private void NoteCommand(BasePlayer player, string command, string[] args)
{
if (!Can(player, PermPlayersModerate))
{
Reply(player, "You do not have permission to add staff notes.");
return;
}
if (args.Length < 2)
{
Reply(player, "Use /lanote <player> <note>");
return;
}
var target = FindPlayer(args[0]);
if (target == null)
{
Reply(player, "Could not find that online player.");
return;
}
var text = string.Join(" ", args.Skip(1).ToArray());
AddNote(player, target.UserIDString, text);
Reply(player, $"Added a note to {target.displayName}.");
}
[ChatCommand("laaddgroup")]
private void AddGroupCommand(BasePlayer player, string command, string[] args)
{
if (!Can(player, PermGroupsManage))
{
Reply(player, "You do not have permission to manage groups.");
return;
}
if (args.Length < 2)
{
Reply(player, "Use /laaddgroup <player> <group>");
return;
}
var target = FindPlayer(args[0]);
if (target == null)
{
Reply(player, "Could not find that online player.");
return;
}
permission.AddUserGroup(target.UserIDString, args[1]);
Log(player, "GroupAdd", target.UserIDString, args[1]);
SaveData();
Reply(player, $"Added {target.displayName} to {args[1]}.");
}
[ChatCommand("laremovegroup")]
private void RemoveGroupCommand(BasePlayer player, string command, string[] args)
{
if (!Can(player, PermGroupsManage))
{
Reply(player, "You do not have permission to manage groups.");
return;
}
if (args.Length < 2)
{
Reply(player, "Use /laremovegroup <player> <group>");
return;
}
var target = FindPlayer(args[0]);
if (target == null)
{
Reply(player, "Could not find that online player.");
return;
}
permission.RemoveUserGroup(target.UserIDString, args[1]);
Log(player, "GroupRemove", target.UserIDString, args[1]);
SaveData();
Reply(player, $"Removed {target.displayName} from {args[1]}.");
}
[ChatCommand("lagrantgroup")]
private void GrantGroupCommand(BasePlayer player, string command, string[] args)
{
if (!Can(player, PermPermissionsManage))
{
Reply(player, "You do not have permission to manage permissions.");
return;
}
if (args.Length < 2)
{
Reply(player, "Use /lagrantgroup <group> <permission>");
return;
}
if (IsDangerousPermission(args[1]) && !Can(player, PermOwner))
{
Reply(player, "Only owners can grant wildcard or high-risk permissions.");
return;
}
permission.GrantGroupPermission(args[0], args[1], this);
Log(player, "GrantGroupPermission", args[0], args[1]);
SaveData();
Reply(player, $"Granted {args[1]} to {args[0]}.");
}
[ChatCommand("larevokegroup")]
private void RevokeGroupPermissionCommand(BasePlayer player, string command, string[] args)
{
if (!Can(player, PermPermissionsManage))
{
Reply(player, "You do not have permission to manage permissions.");
return;
}
if (args.Length < 2)
{
Reply(player, "Use /larevokegroup <group> <permission>");
return;
}
permission.RevokeGroupPermission(args[0], args[1]);
Log(player, "RevokeGroupPermission", args[0], args[1]);
SaveData();
Reply(player, $"Revoked {args[1]} from {args[0]}.");
}
[ChatCommand("lafilter")]
private void FilterCommand(BasePlayer player, string command, string[] args)
{
if (!Can(player, PermUse))
{
Reply(player, "You do not have access to LiveAdmin.");
return;
}
if (args.Length < 2)
{
Reply(player, "Use /lafilter <players|plugins|groups|permissions|commands> <text>");
return;
}
var target = args[0].ToLowerInvariant();
var text = string.Join(" ", args.Skip(1).ToArray()).Trim();
var state = GetState(player);
if (SetFilter(state, target, text))
{
Reply(player, $"Filter set for {target}: {text}");
Draw(player);
}
else
{
Reply(player, "Filter target must be players, plugins, groups, permissions, or commands.");
}
}
[ChatCommand("laclearfilter")]
private void ClearFilterCommand(BasePlayer player, string command, string[] args)
{
if (!Can(player, PermUse))
{
Reply(player, "You do not have access to LiveAdmin.");
return;
}
var target = args.Length > 0 ? args[0].ToLowerInvariant() : "current";
ClearFilter(GetState(player), target);
Reply(player, target == "all" ? "All filters cleared." : "Filter cleared.");
Draw(player);
}
[ConsoleCommand("liveadmin.ui")]
private void UiCommand(ConsoleSystem.Arg arg)
{
var player = arg.Player();
if (player == null || !Can(player, PermUse)) return;
var args = arg.Args == null ? new string[0] : arg.Args.Select(a => a.ToString()).ToArray();
if (args.Length == 0) return;
var state = GetState(player);
var action = args[0].ToLowerInvariant();
switch (action)
{
case "tab":
state.Tab = CleanTab(Get(args, 1, "dashboard"));
break;
case "subtab":
state.SubTab = Get(args, 1, "plugins").ToLowerInvariant();
state.SelectedConfigPlugin = null;
break;
case "commandsubtab":
state.CommandSubTab = Get(args, 1, "slash").ToLowerInvariant() == "console" ? "console" : "slash";
break;
case "page":
ChangePage(state, Get(args, 1, "current"), ToInt(Get(args, 2, "0")));
break;
case "select":
state.SelectedPlayerId = Get(args, 1, null);
state.Tab = "players";
break;
case "selectgroup":
state.SelectedGroup = Get(args, 1, null);
state.Tab = "manage";
state.SubTab = "permissions";
state.GroupDropdownOpen = false;
break;
case "selectplugin":
state.SelectedPlugin = Get(args, 1, "all");
state.Tab = "manage";
state.SubTab = "permissions";
state.PermissionPage = 0;
break;
case "cyclegroup":
CycleSelectedGroup(state, ToInt(Get(args, 1, "0")));
break;
case "togglegroupdropdown":
state.GroupDropdownOpen = !state.GroupDropdownOpen;
break;
case "groupdropdownpage":
state.GroupDropdownPage = Math.Max(0, state.GroupDropdownPage + ToInt(Get(args, 1, "0")));
break;
case "config":
state.SelectedConfigPlugin = Get(args, 1, null);
state.Tab = "manage";
state.SubTab = "plugins";
state.ConfigPage = 0;
break;
case "configback":
state.SelectedConfigPlugin = null;
break;
case "configtoggle":
ToggleConfigValue(player, ToInt(Get(args, 1, "-1")));
break;
case "close":
Close(player);
return;
case "confirm":
ExecutePending(player);
break;
case "cancel":
ClearPending(state);
break;
case "clearfilter":
ClearFilter(state, Get(args, 1, "current"));
break;
case "act":
HandleAction(player, args);
break;
}
Draw(player);
}
[ConsoleCommand("liveadmin.configset")]
private void ConfigSetCommand(ConsoleSystem.Arg arg)
{
var player = arg.Player();
if (player == null || !Can(player, PermPluginsManage)) return;
var args = arg.Args == null ? new string[0] : arg.Args.Select(a => a.ToString()).ToArray();
if (args.Length < 2) return;
var index = ToInt(args[0]);
var value = string.Join(" ", args.Skip(1).ToArray());
SetConfigValue(player, index, value);
Draw(player);
}
[ConsoleCommand("liveadmin.filter")]
private void FilterInputCommand(ConsoleSystem.Arg arg)
{
var player = arg.Player();
if (player == null || !Can(player, PermUse)) return;
var args = arg.Args == null ? new string[0] : arg.Args.Select(a => a.ToString()).ToArray();
if (args.Length == 0) return;
var target = args[0].ToLowerInvariant();
var text = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()).Trim() : string.Empty;
if (target == "groupdropdown")
{
var state = GetState(player);
state.GroupDropdownFilter = text;
state.GroupDropdownPage = 0;
state.GroupDropdownOpen = true;
Draw(player);
return;
}
SetFilter(GetState(player), target, text);
Draw(player);
}
[ConsoleCommand("liveadmin.registerperms")]
private void RegisterPermsCommand(ConsoleSystem.Arg arg)
{
if (arg.Player() != null && !arg.Player().IsAdmin) return;
RegisterPermissions();
Puts("LiveAdmin permissions were registered manually.");
}
private void HandleAction(BasePlayer player, string[] args)
{
var type = Get(args, 1, string.Empty);
var target = Get(args, 2, string.Empty);
var value = Get(args, 3, string.Empty);
var state = GetState(player);
switch (type)
{
case "kick":
RequireConfirm(player, "kick", target, "Staff action", $"Kick {NameOf(target)}?");
break;
case "ban":
RequireConfirm(player, "ban", target, "Banned by staff", $"Ban {NameOf(target)}?");
break;
case "tpto":
if (!Can(player, PermPlayersTeleport)) return;
var targetPlayer = BasePlayer.FindByID(ToUlong(target));
if (targetPlayer != null)
{
player.Teleport(targetPlayer.transform.position);
Log(player, "TeleportToPlayer", targetPlayer.UserIDString, targetPlayer.displayName);
}
break;
case "bring":
if (!Can(player, PermPlayersTeleport)) return;
var bringPlayer = BasePlayer.FindByID(ToUlong(target));
if (bringPlayer != null)
{
bringPlayer.Teleport(player.transform.position);
Log(player, "BringPlayer", bringPlayer.UserIDString, bringPlayer.displayName);
}
break;
case "claimreport":
if (!Can(player, PermReportsManage)) return;
UpdateReport(ToInt(target), "Claimed", player.displayName);
Log(player, "ReportClaimed", target, string.Empty);
break;
case "resolvereport":
if (!Can(player, PermReportsManage)) return;
UpdateReport(ToInt(target), "Resolved", player.displayName);
Log(player, "ReportResolved", target, string.Empty);
break;
case "plugin":
if (!Can(player, PermPluginsManage)) return;
RequireConfirm(player, $"plugin:{value}", target, string.Empty, $"{value} plugin {target}?");
break;
case "groupadd":
if (!Can(player, PermGroupsManage)) return;
RequireConfirm(player, "groupadd", target, value, $"Add {NameOf(target)} to group {value}?");
break;
case "groupremove":
if (!Can(player, PermGroupsManage)) return;
RequireConfirm(player, "groupremove", target, value, $"Remove {NameOf(target)} from group {value}?");
break;
case "grantgroup":
if (!Can(player, PermPermissionsManage)) return;
RequireConfirm(player, "grantgroup", target, value, $"Grant {value} to group {target}?");
break;
case "revokegroup":
if (!Can(player, PermPermissionsManage)) return;
RequireConfirm(player, "revokegroup", target, value, $"Revoke {value} from group {target}?");
break;
case "note":
if (!Can(player, PermPlayersModerate)) return;
var noteTarget = BasePlayer.FindByID(ToUlong(target));
if (noteTarget != null)
{
AddNote(player, noteTarget.UserIDString, "Quick staff note added from panel.");
}
break;
case "quick":
var quick = config.QuickCommands.FirstOrDefault(q => q.Label.Equals(target, StringComparison.OrdinalIgnoreCase));
if (quick == null || !Can(player, quick.Permission)) return;
if (quick.Confirm) RequireConfirm(player, "quick", quick.Label, string.Empty, $"Run {quick.Label}?");
else RunServerCommand(player, quick.Command, quick.Label);
break;
}
SaveData();
}
private void ExecutePending(BasePlayer player)
{
var state = GetState(player);
if (string.IsNullOrEmpty(state.PendingAction)) return;
var action = state.PendingAction;
var target = state.PendingTarget;
var value = state.PendingValue;
ClearPending(state);
if (action == "kick" && Can(player, PermPlayersModerate))
{
var targetPlayer = BasePlayer.FindByID(ToUlong(target));
targetPlayer?.Kick(value);
Log(player, "Kick", target, value);
}
else if (action == "ban" && Can(player, PermPlayersModerate))
{
var targetPlayer = BasePlayer.FindByID(ToUlong(target));
if (targetPlayer != null)
{
ServerUsers.Set(targetPlayer.userID, ServerUsers.UserGroup.Banned, targetPlayer.displayName, value);
targetPlayer.Kick(value);
Log(player, "Ban", targetPlayer.UserIDString, value);
}
}
else if (action.StartsWith("plugin:") && Can(player, PermPluginsManage))
{
var verb = action.Substring("plugin:".Length);
RunServerCommand(player, $"oxide.{verb} {target}", $"{verb} plugin {target}");
}
else if (action == "groupadd" && Can(player, PermGroupsManage))
{
permission.AddUserGroup(target, value);
Log(player, "GroupAdd", target, value);
}
else if (action == "groupremove" && Can(player, PermGroupsManage))
{
permission.RemoveUserGroup(target, value);
Log(player, "GroupRemove", target, value);
}
else if (action == "grantgroup" && Can(player, PermPermissionsManage))
{
if (IsDangerousPermission(value) && !Can(player, PermOwner))
{
Reply(player, "Only owners can grant wildcard or high-risk permissions.");
return;
}
permission.GrantGroupPermission(target, value, this);
Log(player, "GrantGroupPermission", target, value);
}
else if (action == "revokegroup" && Can(player, PermPermissionsManage))
{
permission.RevokeGroupPermission(target, value);
Log(player, "RevokeGroupPermission", target, value);
}
else if (action == "quick")
{
var quick = config.QuickCommands.FirstOrDefault(q => q.Label.Equals(target, StringComparison.OrdinalIgnoreCase));
if (quick != null && Can(player, quick.Permission))
{
RunServerCommand(player, quick.Command, quick.Label);
}
}
else if (action == "command" && Can(player, PermServerActions))
{
RunServerCommand(player, target, target);
}
SaveData();
}
private void Draw(BasePlayer player)
{
CuiHelper.DestroyUi(player, UiRoot);
var state = GetState(player);
var container = new CuiElementContainer();
Panel(container, UiRoot, "Overlay", "0.03 0.035 0.045 0.96", "0.12 0.10", "0.88 0.90");
Panel(container, $"{UiRoot}.Top", UiRoot, "0.08 0.09 0.10 0.98", "0 0.91", "1 1");
Label(container, $"{UiRoot}.Top.Title", $"{UiRoot}.Top", config.Title, 22, "0.025 0.05", "0.35 0.95", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Top.Status", $"{UiRoot}.Top", $"Online {BasePlayer.activePlayerList.Count}/{ConVar.Server.maxplayers}   Staff {BasePlayer.activePlayerList.Count(p => Can(p, PermUse))}", 14, "0.60 0.05", "0.94 0.95", TextAnchor.MiddleRight);
Button(container, $"{UiRoot}.Top.Close", $"{UiRoot}.Top", "X", "liveadmin.ui close", config.DangerColor, "0.955 0.18", "0.99 0.82", 14);
Panel(container, $"{UiRoot}.Nav", UiRoot, "0.06 0.065 0.075 0.98", "0 0", "0.17 0.91");
DrawNav(container, player, state);
Panel(container, $"{UiRoot}.Body", UiRoot, "0.04 0.045 0.052 0.92", "0.18 0.06", "0.985 0.89");
Panel(container, $"{UiRoot}.Footer", UiRoot, "0.08 0.09 0.10 0.96", "0.18 0.005", "0.985 0.05");
Label(container, $"{UiRoot}.Footer.Text", $"{UiRoot}.Footer", FooterText(player, state), 12, "0.015 0", "0.98 1", TextAnchor.MiddleLeft);
if (!string.IsNullOrEmpty(state.PendingAction)) DrawConfirm(container, state);
else if (state.Tab == "dashboard") DrawDashboard(container, player);
else if (state.Tab == "players") DrawPlayers(container, player, state);
else if (state.Tab == "reports") DrawReports(container, player, state);
else if (state.Tab == "manage") DrawManage(container, player, state);
else if (state.Tab == "commands") DrawCommands(container, player, state);
else if (state.Tab == "logs") DrawLogs(container, player, state);
CuiHelper.AddUi(player, container);
}
private void DrawNav(CuiElementContainer container, BasePlayer player, PanelState state)
{
var y = 0.86;
foreach (var tab in tabs)
{
if (!CanSeeTab(player, tab)) continue;
var selected = state.Tab == tab;
Button(container, $"{UiRoot}.Nav.{tab}", $"{UiRoot}.Nav", Title(tab), $"liveadmin.ui tab {tab}", selected ? config.AccentColor : "0.12 0.13 0.14 1", $"0.08 {y}", $"0.92 {y + 0.07}", 13);
y -= 0.085;
}
}
private void DrawDashboard(CuiElementContainer container, BasePlayer player)
{
Header(container, "Dashboard");
Stat(container, "Players Online", BasePlayer.activePlayerList.Count.ToString(), 0);
Stat(container, "Pending Reports", storedData.Reports.Count(r => r.Status != "Resolved").ToString(), 1);
Stat(container, "Staff Online", BasePlayer.activePlayerList.Count(p => Can(p, PermUse)).ToString(), 2);
Stat(container, "Recent Actions", storedData.Logs.Count.ToString(), 3);
var y = 0.38;
Label(container, $"{UiRoot}.Body.QuickTitle", $"{UiRoot}.Body", "Quick Actions", 16, "0.04 0.45", "0.4 0.52", TextAnchor.MiddleLeft);
foreach (var quick in config.QuickCommands.Where(q => Can(player, q.Permission)))
{
Button(container, $"{UiRoot}.Body.Quick.{quick.Label}", $"{UiRoot}.Body", quick.Label, $"liveadmin.ui act quick \"{quick.Label}\"", config.AccentColor, $"0.04 {y}", $"0.26 {y + 0.06}", 12);
y -= 0.075;
}
}
private void DrawPlayers(CuiElementContainer container, BasePlayer player, PanelState state)
{
Header(container, "Players");
if (!Can(player, PermPlayersView)) { NoAccess(container); return; }
var selected = BasePlayer.FindByID(ToUlong(state.SelectedPlayerId ?? "0"));
var list = BasePlayer.activePlayerList
.Where(p => MatchesFilter(p.displayName, state.PlayerFilter) || MatchesFilter(p.UserIDString, state.PlayerFilter))
.OrderBy(p => p.displayName)
.ToList();
var pageSize = Math.Max(5, config.PlayersPerPage);
var page = list.Skip(state.PlayerPage * pageSize).Take(pageSize).ToList();
var y = 0.74;
DrawFilterStatus(container, $"{UiRoot}.Body", "Players", state.PlayerFilter, "players", "0.04 0.84", "0.54 0.89");
foreach (var target in page)
{
Row(container, $"Player.{target.userID}", target.displayName, target.UserIDString, $"liveadmin.ui select {target.UserIDString}", y);
y -= 0.065;
}
Pager(container, state.PlayerPage, list.Count, pageSize, "players");
Panel(container, $"{UiRoot}.Body.Detail", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.58 0.08", "0.96 0.82");
if (selected == null)
{
Label(container, $"{UiRoot}.Body.Detail.Empty", $"{UiRoot}.Body.Detail", "Select a player to manage.", 14, "0.06 0.45", "0.94 0.55", TextAnchor.MiddleCenter);
return;
}
Label(container, $"{UiRoot}.Body.Detail.Name", $"{UiRoot}.Body.Detail", selected.displayName, 18, "0.05 0.84", "0.95 0.96", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Detail.Id", $"{UiRoot}.Body.Detail", selected.UserIDString, 12, "0.05 0.78", "0.95 0.86", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Detail.Groups", $"{UiRoot}.Body.Detail", $"Groups: {string.Join(", ", permission.GetUserGroups(selected.UserIDString))}", 12, "0.05 0.68", "0.95 0.76", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Detail.Notes", $"{UiRoot}.Body.Detail", $"Notes: {GetNotes(selected.UserIDString).Count}", 12, "0.05 0.62", "0.95 0.70", TextAnchor.MiddleLeft);
ActionButton(container, "TP To", $"liveadmin.ui act tpto {selected.UserIDString}", 0.05, 0.54, Can(player, PermPlayersTeleport));
ActionButton(container, "Bring", $"liveadmin.ui act bring {selected.UserIDString}", 0.39, 0.54, Can(player, PermPlayersTeleport));
ActionButton(container, "Kick", $"liveadmin.ui act kick {selected.UserIDString}", 0.05, 0.44, Can(player, PermPlayersModerate));
ActionButton(container, "Ban", $"liveadmin.ui act ban {selected.UserIDString}", 0.39, 0.44, Can(player, PermPlayersModerate), true);
ActionButton(container, "Note", $"liveadmin.ui act note {selected.UserIDString}", 0.05, 0.34, Can(player, PermPlayersModerate));
DrawGroupButtons(container, player, selected);
DrawLatestNotes(container, selected);
}
private void DrawGroupButtons(CuiElementContainer container, BasePlayer player, BasePlayer selected)
{
if (!Can(player, PermGroupsManage)) return;
var availableGroups = new HashSet<string>(permission.GetGroups(), StringComparer.OrdinalIgnoreCase);
var configuredGroups = config.CommonGroups == null ? new List<string>() : config.CommonGroups;
var groups = configuredGroups.Where(g => !string.IsNullOrEmpty(g) && availableGroups.Contains(g)).Take(3).ToList();
if (groups.Count == 0)
{
groups = GetRankedGroups().Take(3).ToList();
}
var y = 0.17;
Label(container, $"{UiRoot}.Body.Detail.GroupTitle", $"{UiRoot}.Body.Detail", "Quick Groups", 12, "0.05 0.23", "0.95 0.29", TextAnchor.MiddleLeft);
foreach (var group in groups)
{
var label = Shorten(group, 22);
Button(container, $"{UiRoot}.Body.Detail.Group.{group}", $"{UiRoot}.Body.Detail", $"+ {label}", $"liveadmin.ui act groupadd {selected.UserIDString} {group}", "0.14 0.15 0.16 1", $"0.05 {y}", $"0.45 {y + 0.052}", 10);
Button(container, $"{UiRoot}.Body.Detail.UnGroup.{group}", $"{UiRoot}.Body.Detail", $"- {label}", $"liveadmin.ui act groupremove {selected.UserIDString} {group}", "0.14 0.15 0.16 1", $"0.52 {y}", $"0.92 {y + 0.052}", 10);
y -= 0.065;
}
}
private void DrawLatestNotes(CuiElementContainer container, BasePlayer selected)
{
var notes = GetNotes(selected.UserIDString).OrderByDescending(n => n.Time).Take(2).ToList();
var y = 0.27;
foreach (var note in notes)
{
Label(container, $"{UiRoot}.Body.Detail.Note.{y}", $"{UiRoot}.Body.Detail", $"{note.StaffName}: {Shorten(note.Text, 42)}", 9, "0.05 " + y, "0.95 " + (y + 0.045), TextAnchor.MiddleLeft);
y -= 0.045;
}
}
private void DrawReports(CuiElementContainer container, BasePlayer player, PanelState state)
{
Header(container, "Reports");
if (!Can(player, PermReportsView)) { NoAccess(container); return; }
var reports = storedData.Reports.OrderByDescending(r => r.Id).Take(12).ToList();
var y = 0.78;
foreach (var report in reports)
{
var color = report.Status == "Resolved" ? "0.12 0.22 0.14 1" : report.Status == "Claimed" ? "0.13 0.18 0.28 1" : "0.25 0.18 0.09 1";
Panel(container, $"{UiRoot}.Body.Report.{report.Id}", $"{UiRoot}.Body", color, $"0.04 {y}", $"0.94 {y + 0.06}");
Label(container, $"{UiRoot}.Body.Report.{report.Id}.Text", $"{UiRoot}.Body.Report.{report.Id}", $"#{report.Id} {report.ReporterName} -> {report.TargetName}: {report.Reason}", 11, "0.015 0", "0.67 1", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.Report.{report.Id}.Claim", $"{UiRoot}.Body.Report.{report.Id}", "Claim", $"liveadmin.ui act claimreport {report.Id}", config.AccentColor, "0.70 0.18", "0.82 0.82", 10);
Button(container, $"{UiRoot}.Body.Report.{report.Id}.Resolve", $"{UiRoot}.Body.Report.{report.Id}", "Resolve", $"liveadmin.ui act resolvereport {report.Id}", config.SuccessColor, "0.84 0.18", "0.97 0.82", 10);
y -= 0.07;
}
}
private void DrawManage(CuiElementContainer container, BasePlayer player, PanelState state)
{
if (!string.IsNullOrEmpty(state.SelectedConfigPlugin))
{
DrawPluginConfig(container, player, state);
return;
}
Header(container, "Manage");
var subTabs = new[] { "plugins", "groups", "permissions" };
if (!subTabs.Contains(state.SubTab))
{
state.SubTab = "plugins";
}
var x = 0.04;
foreach (var sub in subTabs)
{
Button(container, $"{UiRoot}.Body.Sub.{sub}", $"{UiRoot}.Body", Title(sub), $"liveadmin.ui subtab {sub}", state.SubTab == sub ? config.AccentColor : "0.12 0.13 0.14 1", $"{x} 0.83", $"{x + 0.16} 0.89", 12);
x += 0.18;
}
if (state.SubTab == "plugins") DrawPlugins(container, player);
else if (state.SubTab == "groups") DrawGroups(container, player);
else DrawPermissions(container, player);
}
private void DrawCommands(CuiElementContainer container, BasePlayer player, PanelState state)
{
Header(container, "Commands");
if (!Can(player, PermCommandsView) && !Can(player, PermPluginsView) && !Can(player, PermServerActions))
{
NoAccess(container);
return;
}
state.CommandSubTab = state.CommandSubTab == "console" ? "console" : "slash";
Button(container, $"{UiRoot}.Body.Commands.Slash", $"{UiRoot}.Body", "Slash Commands", "liveadmin.ui commandsubtab slash", state.CommandSubTab == "slash" ? config.AccentColor : "0.12 0.13 0.14 1", "0.04 0.83", "0.23 0.89", 12);
Button(container, $"{UiRoot}.Body.Commands.Console", $"{UiRoot}.Body", "Console Commands", "liveadmin.ui commandsubtab console", state.CommandSubTab == "console" ? config.AccentColor : "0.12 0.13 0.14 1", "0.25 0.83", "0.47 0.89", 12);
DrawFilterStatus(container, $"{UiRoot}.Body", "Commands", state.CommandFilter, "commands", "0.52 0.83", "0.94 0.89");
var kind = state.CommandSubTab == "console" ? "console" : "slash";
var rows = GetCommandCatalog()
.Where(command => command.Kind == kind)
.Where(command => CommandMatches(command, state.CommandFilter))
.ToList();
var pageSize = CommandRowsPerPage;
var pageIndex = kind == "console" ? state.ConsoleCommandPage : state.SlashCommandPage;
pageIndex = ClampPage(pageIndex, rows.Count, pageSize);
if (kind == "console")
{
state.ConsoleCommandPage = pageIndex;
}
else
{
state.SlashCommandPage = pageIndex;
}
var prefix = kind == "console" ? "" : "/";
Label(container, $"{UiRoot}.Body.Commands.Count", $"{UiRoot}.Body", $"{Title(kind)} commands: {rows.Count}", 12, "0.04 0.765", "0.94 0.815", TextAnchor.MiddleLeft);
Panel(container, $"{UiRoot}.Body.Commands.Head", $"{UiRoot}.Body", "0.055 0.06 0.07 0.96", "0.04 0.715", "0.94 0.755");
Label(container, $"{UiRoot}.Body.Commands.Head.Command", $"{UiRoot}.Body.Commands.Head", "Command", 10, "0.02 0", "0.52 1", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Commands.Head.Plugin", $"{UiRoot}.Body.Commands.Head", "Plugin", 10, "0.55 0", "0.77 1", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Commands.Head.Source", $"{UiRoot}.Body.Commands.Head", "Source", 10, "0.80 0", "0.98 1", TextAnchor.MiddleLeft);
var y = 0.655;
var index = 0;
foreach (var entry in rows.Skip(pageIndex * pageSize).Take(pageSize))
{
var rowName = $"{UiRoot}.Body.Commands.Row.{index}";
Panel(container, rowName, $"{UiRoot}.Body", "0.075 0.08 0.09 1", $"0.04 {y}", $"0.94 {y + 0.052}");
Label(container, $"{rowName}.Command", rowName, Shorten(prefix + entry.Command, 58), 10, "0.02 0", "0.52 1", TextAnchor.MiddleLeft);
Label(container, $"{rowName}.Plugin", rowName, Shorten(entry.Plugin, 24), 9, "0.55 0", "0.77 1", TextAnchor.MiddleLeft);
Label(container, $"{rowName}.Source", rowName, Shorten(entry.Source, 24), 9, "0.80 0", "0.98 1", TextAnchor.MiddleLeft);
y -= 0.057;
index++;
}
Pager(container, pageIndex, rows.Count, pageSize, kind == "console" ? "consolecommands" : "slashcommands");
}
private void DrawPlugins(CuiElementContainer container, BasePlayer player)
{
if (!Can(player, PermPluginsView)) { NoAccess(container); return; }
var state = GetState(player);
if (!string.IsNullOrEmpty(state.SelectedConfigPlugin))
{
DrawPluginConfig(container, player, state);
return;
}
var pluginNames = GetKnownPlugins()
.Where(p => MatchesFilter(p, state.PluginFilter))
.OrderBy(p => p)
.ToList();
var pageSize = 9;
var page = pluginNames.Skip(state.PluginPage * pageSize).Take(pageSize).ToList();
var y = 0.67;
DrawFilterStatus(container, $"{UiRoot}.Body", "Plugins", state.PluginFilter, "plugins", "0.04 0.76", "0.94 0.81");
foreach (var pluginName in page)
{
Panel(container, $"{UiRoot}.Body.Plugin.{pluginName}", $"{UiRoot}.Body", "0.075 0.08 0.09 1", $"0.04 {y}", $"0.94 {y + 0.06}");
Label(container, $"{UiRoot}.Body.Plugin.{pluginName}.Name", $"{UiRoot}.Body.Plugin.{pluginName}", pluginName, 12, "0.02 0", "0.46 1", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.Plugin.{pluginName}.Config", $"{UiRoot}.Body.Plugin.{pluginName}", "Config", $"liveadmin.ui config {pluginName}", "0.15 0.16 0.18 1", "0.46 0.18", "0.58 0.82", 10);
Button(container, $"{UiRoot}.Body.Plugin.{pluginName}.Reload", $"{UiRoot}.Body.Plugin.{pluginName}", "Reload", $"liveadmin.ui act plugin {pluginName} reload", config.AccentColor, "0.60 0.18", "0.72 0.82", 10);
Button(container, $"{UiRoot}.Body.Plugin.{pluginName}.Unload", $"{UiRoot}.Body.Plugin.{pluginName}", "Unload", $"liveadmin.ui act plugin {pluginName} unload", config.WarningColor, "0.74 0.18", "0.85 0.82", 10);
Button(container, $"{UiRoot}.Body.Plugin.{pluginName}.Load", $"{UiRoot}.Body.Plugin.{pluginName}", "Load", $"liveadmin.ui act plugin {pluginName} load", config.SuccessColor, "0.87 0.18", "0.98 0.82", 10);
y -= 0.07;
}
Pager(container, state.PluginPage, pluginNames.Count, pageSize, "plugins");
}
private void DrawPluginConfig(CuiElementContainer container, BasePlayer player, PanelState state)
{
var pluginName = state.SelectedConfigPlugin;
Label(container, $"{UiRoot}.Body.Config.Title", $"{UiRoot}.Body", $"Plugin Config: {pluginName}", 20, "0.04 0.91", "0.65 0.985", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.Config.Back", $"{UiRoot}.Body", "Back", "liveadmin.ui configback", "0.12 0.13 0.14 1", "0.78 0.915", "0.90 0.975", 11);
Button(container, $"{UiRoot}.Body.Config.Reload", $"{UiRoot}.Body", "Reload", $"liveadmin.ui act plugin {pluginName} reload", config.AccentColor, "0.91 0.915", "0.98 0.975", 11);
var rows = GetConfigEntries(pluginName);
if (rows.Count == 0)
{
Label(container, $"{UiRoot}.Body.Config.Empty", $"{UiRoot}.Body", "No editable top-level config values found, or config file is missing.", 13, "0.04 0.44", "0.94 0.56", TextAnchor.MiddleCenter);
return;
}
Panel(container, $"{UiRoot}.Body.Config.Head", $"{UiRoot}.Body", "0.055 0.06 0.07 0.96", "0.04 0.815", "0.94 0.86");
Label(container, $"{UiRoot}.Body.Config.Head.Key", $"{UiRoot}.Body.Config.Head", "Setting", 10, "0.02 0", "0.40 1", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Config.Head.Value", $"{UiRoot}.Body.Config.Head", "Value", 10, "0.42 0", "0.75 1", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Config.Head.Action", $"{UiRoot}.Body.Config.Head", "Action", 10, "0.78 0", "0.97 1", TextAnchor.MiddleCenter);
var pageSize = 10;
var maxPage = Math.Max(0, (rows.Count - 1) / pageSize);
state.ConfigPage = Math.Min(state.ConfigPage, maxPage);
var page = rows.Skip(state.ConfigPage * pageSize).Take(pageSize).ToList();
var y = 0.755;
foreach (var row in page)
{
Panel(container, $"{UiRoot}.Body.Config.Row.{row.Index}", $"{UiRoot}.Body", "0.075 0.08 0.09 1", $"0.04 {y}", $"0.94 {y + 0.047}");
Label(container, $"{UiRoot}.Body.Config.Row.{row.Index}.Key", $"{UiRoot}.Body.Config.Row.{row.Index}", Shorten(row.Key, 38), 9, "0.02 0", "0.40 1", TextAnchor.MiddleLeft);
if (row.Editable && row.Type != JTokenType.Boolean)
{
Panel(container, $"{UiRoot}.Body.Config.Row.{row.Index}.Box", $"{UiRoot}.Body.Config.Row.{row.Index}", "0.02 0.025 0.03 0.98", "0.42 0.12", "0.75 0.88");
Input(container, $"{UiRoot}.Body.Config.Row.{row.Index}.Input", $"{UiRoot}.Body.Config.Row.{row.Index}.Box", row.Value, $"liveadmin.configset {row.Index}", 8, "0.03 0", "0.97 1");
}
else
{
Label(container, $"{UiRoot}.Body.Config.Row.{row.Index}.Value", $"{UiRoot}.Body.Config.Row.{row.Index}", row.Value, 9, "0.42 0", "0.75 1", TextAnchor.MiddleLeft);
}
if (row.Type == JTokenType.Boolean)
{
Button(container, $"{UiRoot}.Body.Config.Row.{row.Index}.Toggle", $"{UiRoot}.Body.Config.Row.{row.Index}", "Toggle", $"liveadmin.ui configtoggle {row.Index}", config.AccentColor, "0.79 0.12", "0.96 0.88", 9);
}
else
{
Label(container, $"{UiRoot}.Body.Config.Row.{row.Index}.Type", $"{UiRoot}.Body.Config.Row.{row.Index}", row.Editable ? "Enter" : row.Kind, 8, "0.78 0", "0.97 1", TextAnchor.MiddleCenter);
}
y -= 0.052;
}
Pager(container, state.ConfigPage, rows.Count, pageSize, "config");
}
private void DrawGroups(CuiElementContainer container, BasePlayer player)
{
if (!Can(player, PermGroupsView)) { NoAccess(container); return; }
var state = GetState(player);
var groups = GetRankedGroups()
.Where(g => MatchesFilter(g, state.GroupFilter))
.ToList();
var pageSize = 10;
var page = groups.Skip(state.GroupPage * pageSize).Take(pageSize).ToList();
var y = 0.67;
DrawFilterStatus(container, $"{UiRoot}.Body", "Groups", state.GroupFilter, "groups", "0.04 0.76", "0.94 0.81");
foreach (var group in page)
{
Panel(container, $"{UiRoot}.Body.Group.{group}", $"{UiRoot}.Body", "0.075 0.08 0.09 1", $"0.04 {y}", $"0.94 {y + 0.055}");
Label(container, $"{UiRoot}.Body.Group.{group}.Name", $"{UiRoot}.Body.Group.{group}", $"{group}   Rank {permission.GetGroupRank(group)}   Parent {permission.GetGroupParent(group)}", 11, "0.02 0", "0.70 1", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.Group.{group}.Perms", $"{UiRoot}.Body.Group.{group}", "Perms", $"liveadmin.ui selectgroup {group}", config.AccentColor, "0.76 0.18", "0.96 0.82", 10);
y -= 0.063;
}
Pager(container, state.GroupPage, groups.Count, pageSize, "groups");
}
private void DrawPermissions(CuiElementContainer container, BasePlayer player)
{
if (!Can(player, PermPermissionsView)) { NoAccess(container); return; }
var state = GetState(player);
var groups = GetRankedGroups();
if (string.IsNullOrEmpty(state.SelectedGroup) || !groups.Contains(state.SelectedGroup))
{
state.SelectedGroup = groups.FirstOrDefault();
}
if (string.IsNullOrEmpty(state.SelectedGroup))
{
Label(container, $"{UiRoot}.Body.Perms.Empty", $"{UiRoot}.Body", "No groups found.", 14, "0.04 0.45", "0.94 0.55", TextAnchor.MiddleCenter);
return;
}
var pluginsForPerms = GetPermissionPlugins().Where(p => MatchesFilter(p, state.PluginFilter)).ToList();
if (pluginsForPerms.Count == 0)
{
pluginsForPerms.Add("all");
}
if (string.IsNullOrEmpty(state.SelectedPlugin) || !pluginsForPerms.Contains(state.SelectedPlugin))
{
state.SelectedPlugin = pluginsForPerms.FirstOrDefault();
}
Panel(container, $"{UiRoot}.Body.Perms.Side", $"{UiRoot}.Body", "0.06 0.065 0.075 1", "0.04 0.08", "0.28 0.76");
DrawFilterStatus(container, $"{UiRoot}.Body.Perms.Side", "Plugins", state.PluginFilter, "permplugins", "0.08 0.91", "0.92 0.985");
var pluginY = 0.80;
var pluginPageSize = 8;
foreach (var pluginName in pluginsForPerms.Skip(state.PermissionPluginPage * pluginPageSize).Take(pluginPageSize))
{
var pluginLabel = pluginName == "all" ? "All permissions" : Shorten(pluginName, 26);
Button(container, $"{UiRoot}.Body.Perms.Side.Plugin.{pluginName}", $"{UiRoot}.Body.Perms.Side", pluginLabel, $"liveadmin.ui selectplugin {pluginName}", pluginName == state.SelectedPlugin ? config.AccentColor : "0.12 0.13 0.14 1", $"0.08 {pluginY}", $"0.92 {pluginY + 0.075}", 10);
pluginY -= 0.09;
}
Panel(container, $"{UiRoot}.Body.Perms.Detail", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.31 0.08", "0.94 0.76");
Label(container, $"{UiRoot}.Body.Perms.Detail.Title", $"{UiRoot}.Body.Perms.Detail", $"Group: {state.SelectedGroup}", 16, "0.04 0.91", "0.46 0.99", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Perms.Detail.PluginTitle", $"{UiRoot}.Body.Perms.Detail", $"Plugin: {(state.SelectedPlugin == "all" ? "All" : state.SelectedPlugin)}", 13, "0.50 0.91", "0.96 0.99", TextAnchor.MiddleRight);
Panel(container, $"{UiRoot}.Body.Perms.Detail.GroupSelect", $"{UiRoot}.Body.Perms.Detail", "0.055 0.06 0.07 0.96", "0.04 0.795", "0.96 0.89");
Label(container, $"{UiRoot}.Body.Perms.Detail.GroupSelect.Label", $"{UiRoot}.Body.Perms.Detail.GroupSelect", "Apply to:", 10, "0.02 0", "0.15 1", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.Perms.Detail.GroupSelect.Dropdown", $"{UiRoot}.Body.Perms.Detail.GroupSelect", $"{Shorten(state.SelectedGroup, 42)} {(state.GroupDropdownOpen ? "^" : "v")}", "liveadmin.ui togglegroupdropdown", config.AccentColor, "0.17 0.14", "0.97 0.86", 10, TextAnchor.MiddleLeft);
if (state.GroupDropdownOpen)
{
DrawGroupDropdown(container, state, groups);
return;
}
var currentPerms = permission.GetGroupPermissions(state.SelectedGroup, false).OrderBy(p => p).ToList();
Label(container, $"{UiRoot}.Body.Perms.Detail.Count", $"{UiRoot}.Body.Perms.Detail", $"Direct permissions: {currentPerms.Count}", 11, "0.04 0.72", "0.48 0.79", TextAnchor.MiddleLeft);
var allPermissions = GetKnownPermissions()
.Where(p => state.SelectedPlugin == "all" || p.StartsWith(state.SelectedPlugin + ".", StringComparison.OrdinalIgnoreCase))
.Where(p => MatchesFilter(p, state.PermissionFilter))
.ToList();
var permPageSize = 6;
var permPage = allPermissions.Skip(state.PermissionPage * permPageSize).Take(permPageSize).ToList();
Label(container, $"{UiRoot}.Body.Perms.Detail.Total", $"{UiRoot}.Body.Perms.Detail", $"Registered permissions: {allPermissions.Count}", 11, "0.50 0.72", "0.96 0.79", TextAnchor.MiddleRight);
DrawFilterStatus(container, $"{UiRoot}.Body.Perms.Detail", "Permissions", state.PermissionFilter, "permissions", "0.04 0.64", "0.96 0.705");
var y = 0.52;
foreach (var permName in permPage)
{
var hasPerm = currentPerms.Contains(permName);
Panel(container, $"{UiRoot}.Body.Perms.Detail.Row.{permName}", $"{UiRoot}.Body.Perms.Detail", hasPerm ? "0.10 0.18 0.12 1" : "0.11 0.12 0.13 1", $"0.04 {y}", $"0.96 {y + 0.065}");
Label(container, $"{UiRoot}.Body.Perms.Detail.Row.{permName}.Text", $"{UiRoot}.Body.Perms.Detail.Row.{permName}", permName, 10, "0.02 0", "0.58 1", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.Perms.Detail.Row.{permName}.Grant", $"{UiRoot}.Body.Perms.Detail.Row.{permName}", "Grant", $"liveadmin.ui act grantgroup {state.SelectedGroup} {permName}", config.SuccessColor, "0.64 0.18", "0.78 0.82", 10);
Button(container, $"{UiRoot}.Body.Perms.Detail.Row.{permName}.Revoke", $"{UiRoot}.Body.Perms.Detail.Row.{permName}", "Revoke", $"liveadmin.ui act revokegroup {state.SelectedGroup} {permName}", config.DangerColor, "0.80 0.18", "0.96 0.82", 10);
y -= 0.078;
}
MiniPager(container, $"{UiRoot}.Body.Perms.Side.Pager", $"{UiRoot}.Body.Perms.Side", state.PermissionPluginPage, pluginsForPerms.Count, pluginPageSize, "permissionplugins", "0.08 0.02", "0.92 0.09");
MiniPager(container, $"{UiRoot}.Body.Perms.Detail.Pager", $"{UiRoot}.Body.Perms.Detail", state.PermissionPage, allPermissions.Count, permPageSize, "permissions", "0.04 0.02", "0.50 0.09");
}
private void DrawLogs(CuiElementContainer container, BasePlayer player, PanelState state)
{
Header(container, "Logs");
var logs = storedData.Logs.OrderByDescending(l => l.Time).Take(config.LogsPerPage).ToList();
var y = 0.78;
foreach (var log in logs)
{
Label(container, $"{UiRoot}.Body.Log.{y}", $"{UiRoot}.Body", $"{log.Time}  {log.ActorName}  {log.Action}  {log.Target}  {log.Details}", 10, "0.04 " + y, "0.94 " + (y + 0.045), TextAnchor.MiddleLeft);
y -= 0.055;
}
}
private void DrawConfirm(CuiElementContainer container, PanelState state)
{
Panel(container, $"{UiRoot}.Body.ConfirmShade", $"{UiRoot}.Body", "0 0 0 0.35", "0 0", "1 1");
Panel(container, $"{UiRoot}.Body.Confirm", $"{UiRoot}.Body", "0.08 0.09 0.10 1", "0.25 0.28", "0.75 0.68");
Label(container, $"{UiRoot}.Body.Confirm.Title", $"{UiRoot}.Body.Confirm", "Confirm Action", 18, "0.06 0.72", "0.94 0.92", TextAnchor.MiddleCenter);
Label(container, $"{UiRoot}.Body.Confirm.Text", $"{UiRoot}.Body.Confirm", state.PendingMessage, 13, "0.08 0.40", "0.92 0.68", TextAnchor.MiddleCenter);
Button(container, $"{UiRoot}.Body.Confirm.Yes", $"{UiRoot}.Body.Confirm", "Confirm", "liveadmin.ui confirm", config.DangerColor, "0.12 0.12", "0.43 0.28", 12);
Button(container, $"{UiRoot}.Body.Confirm.No", $"{UiRoot}.Body.Confirm", "Cancel", "liveadmin.ui cancel", "0.18 0.19 0.20 1", "0.57 0.12", "0.88 0.28", 12);
}
private void Header(CuiElementContainer container, string text)
{
Label(container, $"{UiRoot}.Body.Header", $"{UiRoot}.Body", text, 20, "0.04 0.91", "0.70 0.985", TextAnchor.MiddleLeft);
}
private void Stat(CuiElementContainer container, string label, string value, int index)
{
var x = 0.04 + index * 0.23;
Panel(container, $"{UiRoot}.Body.Stat.{index}", $"{UiRoot}.Body", "0.075 0.08 0.09 1", $"{x} 0.68", $"{x + 0.20} 0.82");
Label(container, $"{UiRoot}.Body.Stat.{index}.Label", $"{UiRoot}.Body.Stat.{index}", label, 11, "0.06 0.55", "0.94 0.92", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Stat.{index}.Value", $"{UiRoot}.Body.Stat.{index}", value, 24, "0.06 0.08", "0.94 0.58", TextAnchor.MiddleLeft);
}
private void Row(CuiElementContainer container, string id, string left, string right, string command, double y)
{
Panel(container, $"{UiRoot}.Body.{id}", $"{UiRoot}.Body", "0.075 0.08 0.09 1", $"0.04 {y}", $"0.54 {y + 0.055}");
Button(container, $"{UiRoot}.Body.{id}.Button", $"{UiRoot}.Body.{id}", left, command, "0.075 0.08 0.09 0", "0 0", "1 1", 11, TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.{id}.Right", $"{UiRoot}.Body.{id}", right, 9, "0.58 0", "0.97 1", TextAnchor.MiddleRight);
}
private void Pager(CuiElementContainer container, int page, int total, int pageSize, string pageKey)
{
Label(container, $"{UiRoot}.Body.PageLabel", $"{UiRoot}.Body", $"Page {page + 1}", 11, "0.22 0.04", "0.36 0.09", TextAnchor.MiddleCenter);
Button(container, $"{UiRoot}.Body.Prev", $"{UiRoot}.Body", "Prev", $"liveadmin.ui page {pageKey} -1", "0.12 0.13 0.14 1", "0.04 0.04", "0.18 0.09", 10);
Button(container, $"{UiRoot}.Body.Next", $"{UiRoot}.Body", "Next", $"liveadmin.ui page {pageKey} 1", "0.12 0.13 0.14 1", "0.40 0.04", "0.54 0.09", 10);
}
private void MiniPager(CuiElementContainer container, string name, string parent, int page, int total, int pageSize, string pageKey, string min, string max)
{
Panel(container, name, parent, "0.06 0.065 0.075 1", min, max);
Button(container, $"{name}.Prev", name, "<", $"liveadmin.ui page {pageKey} -1", "0.12 0.13 0.14 1", "0.00 0.00", "0.22 1.00", 10);
Label(container, $"{name}.Label", name, $"Page {page + 1}", 9, "0.24 0", "0.74 1", TextAnchor.MiddleCenter);
Button(container, $"{name}.Next", name, ">", $"liveadmin.ui page {pageKey} 1", "0.12 0.13 0.14 1", "0.76 0.00", "1.00 1.00", 10);
}
private void DrawGroupDropdown(CuiElementContainer container, PanelState state, List<string> groups)
{
Panel(container, $"{UiRoot}.Body.Perms.Detail.GroupDropdown", $"{UiRoot}.Body.Perms.Detail", "0.045 0.05 0.06 0.99", "0.04 0.10", "0.96 0.785");
Panel(container, $"{UiRoot}.Body.Perms.Detail.GroupDropdown.Search", $"{UiRoot}.Body.Perms.Detail.GroupDropdown", "0.055 0.06 0.07 1", "0.03 0.83", "0.97 0.96");
Label(container, $"{UiRoot}.Body.Perms.Detail.GroupDropdown.Search.Label", $"{UiRoot}.Body.Perms.Detail.GroupDropdown.Search", "Search:", 9, "0.02 0", "0.16 1", TextAnchor.MiddleLeft);
Panel(container, $"{UiRoot}.Body.Perms.Detail.GroupDropdown.Search.Box", $"{UiRoot}.Body.Perms.Detail.GroupDropdown.Search", "0.02 0.025 0.03 0.98", "0.17 0.14", "0.97 0.86");
Input(container, $"{UiRoot}.Body.Perms.Detail.GroupDropdown.Search.Input", $"{UiRoot}.Body.Perms.Detail.GroupDropdown.Search.Box", state.GroupDropdownFilter, "liveadmin.filter groupdropdown", 9, "0.03 0", "0.97 1");
var filteredGroups = groups.Where(g => MatchesFilter(g, state.GroupDropdownFilter)).ToList();
var pageSize = 6;
var maxPage = Math.Max(0, (filteredGroups.Count - 1) / pageSize);
state.GroupDropdownPage = Math.Min(state.GroupDropdownPage, maxPage);
var page = filteredGroups.Skip(state.GroupDropdownPage * pageSize).Take(pageSize).ToList();
var y = 0.70;
foreach (var group in page)
{
Button(container, $"{UiRoot}.Body.Perms.Detail.GroupDropdown.Group.{group}", $"{UiRoot}.Body.Perms.Detail.GroupDropdown", $"{Shorten(group, 34)}  rank {permission.GetGroupRank(group)}", $"liveadmin.ui selectgroup {group}", group == state.SelectedGroup ? config.AccentColor : "0.12 0.13 0.14 1", $"0.03 {y}", $"0.97 {y + 0.085}", 9, TextAnchor.MiddleLeft);
y -= 0.095;
}
Button(container, $"{UiRoot}.Body.Perms.Detail.GroupDropdown.Prev", $"{UiRoot}.Body.Perms.Detail.GroupDropdown", "<", "liveadmin.ui groupdropdownpage -1", "0.12 0.13 0.14 1", "0.03 0.03", "0.16 0.13", 10);
Label(container, $"{UiRoot}.Body.Perms.Detail.GroupDropdown.Page", $"{UiRoot}.Body.Perms.Detail.GroupDropdown", $"Page {state.GroupDropdownPage + 1}", 9, "0.18 0.03", "0.82 0.13", TextAnchor.MiddleCenter);
Button(container, $"{UiRoot}.Body.Perms.Detail.GroupDropdown.Next", $"{UiRoot}.Body.Perms.Detail.GroupDropdown", ">", "liveadmin.ui groupdropdownpage 1", "0.12 0.13 0.14 1", "0.84 0.03", "0.97 0.13", 10);
}
private void DrawFilterStatus(CuiElementContainer container, string parent, string label, string filter, string filterKey, string min, string max)
{
Panel(container, $"{parent}.Filter.{filterKey}", parent, "0.055 0.06 0.07 0.96", min, max);
Label(container, $"{parent}.Filter.{filterKey}.Label", $"{parent}.Filter.{filterKey}", $"{label}:", 10, "0.03 0", "0.25 1", TextAnchor.MiddleLeft);
Panel(container, $"{parent}.Filter.{filterKey}.Box", $"{parent}.Filter.{filterKey}", "0.02 0.025 0.03 0.98", "0.27 0.14", string.IsNullOrEmpty(filter) ? "0.97 0.86" : "0.72 0.86");
Input(container, $"{parent}.Filter.{filterKey}.Input", $"{parent}.Filter.{filterKey}.Box", filter, $"liveadmin.filter {filterKey}", 10, "0.03 0", "0.97 1");
if (!string.IsNullOrEmpty(filter))
{
Button(container, $"{parent}.Filter.{filterKey}.Clear", $"{parent}.Filter.{filterKey}", "Clear", $"liveadmin.ui clearfilter {filterKey}", "0.12 0.13 0.14 1", "0.76 0.16", "0.97 0.84", 9);
}
}
private void ActionButton(CuiElementContainer container, string text, string command, double x, double y, bool enabled, bool danger = false)
{
Button(container, $"{UiRoot}.Body.Detail.Action.{text}", $"{UiRoot}.Body.Detail", text, enabled ? command : string.Empty, enabled ? danger ? config.DangerColor : config.AccentColor : "0.12 0.12 0.12 0.65", $"{x} {y}", $"{x + 0.27} {y + 0.07}", 11);
}
private void NoAccess(CuiElementContainer container)
{
Label(container, $"{UiRoot}.Body.NoAccess", $"{UiRoot}.Body", "You do not have access to this section.", 14, "0.04 0.44", "0.94 0.56", TextAnchor.MiddleCenter);
}
private void Panel(CuiElementContainer container, string name, string parent, string color, string min, string max)
{
container.Add(new CuiPanel { Image = { Color = color }, RectTransform = { AnchorMin = min, AnchorMax = max }, CursorEnabled = true }, parent, name);
}
private void Label(CuiElementContainer container, string name, string parent, string text, int size, string min, string max, TextAnchor align)
{
container.Add(new CuiLabel { Text = { Text = text, FontSize = size, Align = align, Color = "0.92 0.94 0.94 1" }, RectTransform = { AnchorMin = min, AnchorMax = max } }, parent, name);
}
private void Button(CuiElementContainer container, string name, string parent, string text, string command, string color, string min, string max, int size, TextAnchor align = TextAnchor.MiddleCenter)
{
container.Add(new CuiButton { Button = { Command = command, Color = color }, Text = { Text = text, FontSize = size, Align = align, Color = "0.96 0.98 0.98 1" }, RectTransform = { AnchorMin = min, AnchorMax = max } }, parent, name);
}
private void Input(CuiElementContainer container, string name, string parent, string text, string command, int size, string min, string max)
{
container.Add(new CuiElement
{
Name = name,
Parent = parent,
Components =
{
new CuiInputFieldComponent { Text = text, FontSize = size, Align = TextAnchor.MiddleLeft, Color = "0.92 0.94 0.94 1", Command = command, CharsLimit = 64 },
new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max }
}
});
}
private bool Can(BasePlayer player, string perm)
{
return player != null && (player.IsAdmin || permission.UserHasPermission(player.UserIDString, perm) || permission.UserHasPermission(player.UserIDString, PermOwner));
}
private bool CanSeeTab(BasePlayer player, string tab)
{
if (tab == "players") return Can(player, PermPlayersView);
if (tab == "reports") return Can(player, PermReportsView);
if (tab == "manage") return Can(player, PermPluginsView) || Can(player, PermGroupsView) || Can(player, PermPermissionsView);
if (tab == "commands") return Can(player, PermCommandsView) || Can(player, PermPluginsView) || Can(player, PermServerActions);
return true;
}
private PanelState GetState(BasePlayer player)
{
if (!states.TryGetValue(player.userID, out var state))
{
state = new PanelState();
states[player.userID] = state;
}
return state;
}
private bool SetFilter(PanelState state, string target, string value)
{
if (target == "player") target = "players";
if (target == "plugin") target = "plugins";
if (target == "permplugin" || target == "permissionplugins") target = "permplugins";
if (target == "group") target = "groups";
if (target == "permission" || target == "perm" || target == "perms") target = "permissions";
if (target == "command" || target == "slashcommands" || target == "consolecommands") target = "commands";
if (target == "players")
{
state.PlayerFilter = value;
state.PlayerPage = 0;
state.Tab = "players";
return true;
}
if (target == "plugins")
{
state.PluginFilter = value;
state.PluginPage = 0;
state.Tab = "manage";
state.SubTab = "plugins";
return true;
}
if (target == "permplugins")
{
state.PluginFilter = value;
state.PermissionPluginPage = 0;
state.Tab = "manage";
state.SubTab = "permissions";
return true;
}
if (target == "groups")
{
state.GroupFilter = value;
state.GroupPage = 0;
state.PermissionGroupPage = 0;
state.Tab = "manage";
state.SubTab = "groups";
return true;
}
if (target == "permissions")
{
state.PermissionFilter = value;
state.PermissionPage = 0;
state.Tab = "manage";
state.SubTab = "permissions";
return true;
}
if (target == "commands")
{
state.CommandFilter = value;
state.SlashCommandPage = 0;
state.ConsoleCommandPage = 0;
state.Tab = "commands";
return true;
}
return false;
}
private void ClearFilter(PanelState state, string target)
{
if (target == "player") target = "players";
if (target == "plugin") target = "plugins";
if (target == "permplugin" || target == "permissionplugins") target = "permplugins";
if (target == "group") target = "groups";
if (target == "permission" || target == "perm" || target == "perms") target = "permissions";
if (target == "command" || target == "slashcommands" || target == "consolecommands") target = "commands";
if (target == "all")
{
state.PlayerFilter = string.Empty;
state.PluginFilter = string.Empty;
state.GroupFilter = string.Empty;
state.GroupDropdownFilter = string.Empty;
state.PermissionFilter = string.Empty;
state.CommandFilter = string.Empty;
ResetPages(state);
return;
}
if (target == "players" || (target == "current" && state.Tab == "players"))
{
state.PlayerFilter = string.Empty;
state.PlayerPage = 0;
}
else if (target == "plugins" || (target == "current" && state.Tab == "manage" && state.SubTab == "plugins"))
{
state.PluginFilter = string.Empty;
state.PluginPage = 0;
}
else if (target == "permplugins")
{
state.PluginFilter = string.Empty;
state.PermissionPluginPage = 0;
}
else if (target == "groups" || (target == "current" && state.Tab == "manage" && state.SubTab == "groups"))
{
state.GroupFilter = string.Empty;
state.GroupPage = 0;
state.PermissionGroupPage = 0;
}
else if (target == "permissions" || (target == "current" && state.Tab == "manage" && state.SubTab == "permissions"))
{
state.PermissionFilter = string.Empty;
state.PermissionPage = 0;
}
else if (target == "commands" || (target == "current" && state.Tab == "commands"))
{
state.CommandFilter = string.Empty;
state.SlashCommandPage = 0;
state.ConsoleCommandPage = 0;
}
}
private void ResetPages(PanelState state)
{
state.PlayerPage = 0;
state.PluginPage = 0;
state.GroupPage = 0;
state.PermissionGroupPage = 0;
state.PermissionPluginPage = 0;
state.PermissionPage = 0;
state.GroupDropdownPage = 0;
state.SlashCommandPage = 0;
state.ConsoleCommandPage = 0;
}
private void ChangePage(PanelState state, string pageKey, int delta)
{
if (pageKey == "players") state.PlayerPage = Math.Max(0, state.PlayerPage + delta);
else if (pageKey == "plugins") state.PluginPage = Math.Max(0, state.PluginPage + delta);
else if (pageKey == "config") state.ConfigPage = Math.Max(0, state.ConfigPage + delta);
else if (pageKey == "groups") state.GroupPage = Math.Max(0, state.GroupPage + delta);
else if (pageKey == "permissiongroups") state.PermissionGroupPage = Math.Max(0, state.PermissionGroupPage + delta);
else if (pageKey == "permissionplugins") state.PermissionPluginPage = Math.Max(0, state.PermissionPluginPage + delta);
else if (pageKey == "permissions") state.PermissionPage = Math.Max(0, state.PermissionPage + delta);
else if (pageKey == "slashcommands") state.SlashCommandPage = Math.Max(0, state.SlashCommandPage + delta);
else if (pageKey == "consolecommands") state.ConsoleCommandPage = Math.Max(0, state.ConsoleCommandPage + delta);
else if (state.Tab == "players") state.PlayerPage = Math.Max(0, state.PlayerPage + delta);
else if (state.Tab == "manage" && state.SubTab == "plugins") state.PluginPage = Math.Max(0, state.PluginPage + delta);
else if (state.Tab == "manage" && state.SubTab == "groups") state.GroupPage = Math.Max(0, state.GroupPage + delta);
else if (state.Tab == "manage" && state.SubTab == "permissions") state.PermissionPage = Math.Max(0, state.PermissionPage + delta);
else if (state.Tab == "commands" && state.CommandSubTab == "console") state.ConsoleCommandPage = Math.Max(0, state.ConsoleCommandPage + delta);
else if (state.Tab == "commands") state.SlashCommandPage = Math.Max(0, state.SlashCommandPage + delta);
}
private void CycleSelectedGroup(PanelState state, int delta)
{
var groups = GetRankedGroups();
if (groups.Count == 0) return;
var index = string.IsNullOrEmpty(state.SelectedGroup) ? 0 : groups.IndexOf(state.SelectedGroup);
if (index < 0) index = 0;
index = (index + delta) % groups.Count;
if (index < 0) index += groups.Count;
state.SelectedGroup = groups[index];
}
private List<string> GetRankedGroups()
{
return permission.GetGroups()
.OrderBy(g => permission.GetGroupRank(g))
.ThenBy(g => g)
.ToList();
}
private string CleanTab(string tab)
{
tab = (tab ?? "dashboard").ToLowerInvariant();
return tabs.Contains(tab) ? tab : "dashboard";
}
private void Close(BasePlayer player)
{
CuiHelper.DestroyUi(player, UiRoot);
}
private void RequireConfirm(BasePlayer player, string action, string target, string value, string message)
{
var state = GetState(player);
state.PendingAction = action;
state.PendingTarget = target;
state.PendingValue = value;
state.PendingMessage = message;
}
private void ClearPending(PanelState state)
{
state.PendingAction = null;
state.PendingTarget = null;
state.PendingValue = null;
state.PendingMessage = null;
}
private void RunServerCommand(BasePlayer player, string command, string label)
{
ConsoleSystem.Run(ConsoleSystem.Option.Server, command);
Log(player, "ServerCommand", label, command);
}
private void UpdateReport(int id, string status, string staffName)
{
var report = storedData.Reports.FirstOrDefault(r => r.Id == id);
if (report == null) return;
report.Status = status;
report.ClaimedBy = staffName;
report.UpdatedAt = Now();
}
private void Log(BasePlayer actor, string action, string target, string details)
{
storedData.Logs.Add(new ActionLog
{
Time = Now(),
ActorId = actor?.UserIDString ?? "server",
ActorName = actor?.displayName ?? "Server",
Action = action,
Target = target,
Details = details
});
if (storedData.Logs.Count > 500)
{
storedData.Logs.RemoveRange(0, storedData.Logs.Count - 500);
}
}
private void AddNote(BasePlayer staff, string targetId, string text)
{
if (!storedData.Notes.TryGetValue(targetId, out var notes))
{
notes = new List<StaffNote>();
storedData.Notes[targetId] = notes;
}
notes.Add(new StaffNote
{
Time = Now(),
StaffId = staff.UserIDString,
StaffName = staff.displayName,
Text = text
});
if (notes.Count > 50)
{
notes.RemoveRange(0, notes.Count - 50);
}
Log(staff, "StaffNote", targetId, text);
SaveData();
}
private List<StaffNote> GetNotes(string targetId)
{
if (string.IsNullOrEmpty(targetId)) return new List<StaffNote>();
return storedData.Notes.TryGetValue(targetId, out var notes) ? notes : new List<StaffNote>();
}
private BasePlayer FindPlayer(string nameOrId)
{
if (ulong.TryParse(nameOrId, out var id))
{
var byId = BasePlayer.FindByID(id) ?? BasePlayer.FindSleeping(id);
if (byId != null) return byId;
}
return BasePlayer.activePlayerList.FirstOrDefault(p => p.displayName.IndexOf(nameOrId, StringComparison.OrdinalIgnoreCase) >= 0);
}
private string NameOf(string userId)
{
var player = BasePlayer.FindByID(ToUlong(userId));
return player?.displayName ?? userId;
}
private bool IsDangerousPermission(string perm)
{
if (string.IsNullOrEmpty(perm)) return false;
return perm == "*" || perm.EndsWith(".*") || perm.Contains("owner") || perm.Contains("admin");
}
private bool MatchesFilter(string value, string filter)
{
return string.IsNullOrEmpty(filter) || (!string.IsNullOrEmpty(value) && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
}
private string PluginConfigPath(string pluginName)
{
return Path.Combine(Interface.Oxide.ConfigDirectory, pluginName + ".json");
}
private JObject ReadPluginConfig(string pluginName)
{
try
{
var path = PluginConfigPath(pluginName);
if (!File.Exists(path)) return null;
return JObject.Parse(File.ReadAllText(path));
}
catch
{
return null;
}
}
private List<ConfigEntry> GetConfigEntries(string pluginName)
{
var obj = ReadPluginConfig(pluginName);
var rows = new List<ConfigEntry>();
if (obj == null) return rows;
var index = 0;
foreach (var prop in obj.Properties())
{
var token = prop.Value;
var editable = token.Type == JTokenType.Boolean || token.Type == JTokenType.Integer || token.Type == JTokenType.Float || token.Type == JTokenType.String;
rows.Add(new ConfigEntry
{
Index = index,
Key = prop.Name,
Value = FormatConfigValue(token),
Kind = editable ? token.Type.ToString() : token.Type == JTokenType.Array ? "List" : "Object",
Type = token.Type,
Editable = editable
});
index++;
}
return rows;
}
private string FormatConfigValue(JToken token)
{
if (token == null) return string.Empty;
if (token.Type == JTokenType.Array) return $"list ({token.Count()})";
if (token.Type == JTokenType.Object) return $"object ({token.Count()})";
return Shorten(token.ToString(Formatting.None).Trim('"'), 44);
}
private bool BackupPluginConfig(string pluginName)
{
try
{
var path = PluginConfigPath(pluginName);
if (!File.Exists(path)) return false;
var backupDir = Path.Combine(Interface.Oxide.ConfigDirectory, "LiveAdminBackups");
Directory.CreateDirectory(backupDir);
var backupPath = Path.Combine(backupDir, pluginName + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
File.Copy(path, backupPath, true);
return true;
}
catch
{
return false;
}
}
private void ToggleConfigValue(BasePlayer player, int index)
{
var state = GetState(player);
if (string.IsNullOrEmpty(state.SelectedConfigPlugin) || !Can(player, PermPluginsManage)) return;
var obj = ReadPluginConfig(state.SelectedConfigPlugin);
if (obj == null) return;
var prop = obj.Properties().Skip(index).FirstOrDefault();
if (prop == null || prop.Value.Type != JTokenType.Boolean) return;
BackupPluginConfig(state.SelectedConfigPlugin);
prop.Value = !(bool)prop.Value;
File.WriteAllText(PluginConfigPath(state.SelectedConfigPlugin), obj.ToString(Formatting.Indented));
Log(player, "ConfigToggle", state.SelectedConfigPlugin, prop.Name);
}
private void SetConfigValue(BasePlayer player, int index, string value)
{
var state = GetState(player);
if (string.IsNullOrEmpty(state.SelectedConfigPlugin) || !Can(player, PermPluginsManage)) return;
var obj = ReadPluginConfig(state.SelectedConfigPlugin);
if (obj == null) return;
var prop = obj.Properties().Skip(index).FirstOrDefault();
if (prop == null) return;
var token = prop.Value;
if (!(token.Type == JTokenType.Integer || token.Type == JTokenType.Float || token.Type == JTokenType.String)) return;
BackupPluginConfig(state.SelectedConfigPlugin);
if (token.Type == JTokenType.Integer)
{
if (long.TryParse(value, out var parsed)) prop.Value = parsed;
else return;
}
else if (token.Type == JTokenType.Float)
{
if (double.TryParse(value, out var parsed)) prop.Value = parsed;
else return;
}
else
{
prop.Value = value;
}
File.WriteAllText(PluginConfigPath(state.SelectedConfigPlugin), obj.ToString(Formatting.Indented));
Log(player, "ConfigSet", state.SelectedConfigPlugin, prop.Name);
}
private List<string> GetKnownPlugins()
{
var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
if (config.ManagedPlugins != null)
{
foreach (var pluginName in config.ManagedPlugins.Where(p => !string.IsNullOrEmpty(p)))
{
names.Add(pluginName);
}
}
AddLoadedPluginNames(names);
AddPermissionPluginNames(names);
return names.Where(n => !string.IsNullOrEmpty(n)).ToList();
}
private void AddLoadedPluginNames(HashSet<string> names)
{
try
{
var getAll = plugins.GetType().GetMethod("GetAll", new Type[0]);
if (getAll == null) return;
var loadedPlugins = getAll.Invoke(plugins, null) as System.Collections.IEnumerable;
if (loadedPlugins == null) return;
foreach (var loadedPlugin in loadedPlugins)
{
var nameProperty = loadedPlugin.GetType().GetProperty("Name");
var pluginName = nameProperty == null ? null : nameProperty.GetValue(loadedPlugin, null) as string;
if (!string.IsNullOrEmpty(pluginName))
{
names.Add(pluginName);
}
}
}
catch
{
// Some Oxide builds do not expose plugin enumeration. Permission-prefix detection still fills the list.
}
}
private void AddPermissionPluginNames(HashSet<string> names)
{
try
{
foreach (var perm in permission.GetPermissions())
{
if (string.IsNullOrEmpty(perm) || !perm.Contains(".")) continue;
var prefix = perm.Split('.')[0];
if (!string.IsNullOrEmpty(prefix))
{
names.Add(prefix);
}
}
}
catch
{
// Permission enumeration is best-effort; configured plugin names remain available.
}
}
private List<string> GetKnownPermissions()
{
var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
if (config.CommonPermissions != null)
{
foreach (var perm in config.CommonPermissions.Where(p => !string.IsNullOrEmpty(p)))
{
permissions.Add(perm);
}
}
try
{
foreach (var perm in permission.GetPermissions())
{
if (!string.IsNullOrEmpty(perm))
{
permissions.Add(perm);
}
}
}
catch
{
// Configured permissions remain available if a specific Oxide build cannot enumerate permissions.
}
return permissions.OrderBy(p => p).ToList();
}
private List<string> GetPermissionPlugins()
{
var plugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
plugins.Add("all");
foreach (var perm in GetKnownPermissions())
{
if (string.IsNullOrEmpty(perm) || !perm.Contains(".")) continue;
plugins.Add(perm.Split('.')[0]);
}
return plugins.OrderBy(p => p == "all" ? "" : p).ToList();
}
private List<CommandCatalogEntry> GetCommandCatalog()
{
var now = Time.realtimeSinceStartup;
if (commandCatalogCache != null && now - commandCatalogCachedAt < CommandCatalogCacheSeconds)
{
return commandCatalogCache;
}
var entries = new Dictionary<string, CommandCatalogEntry>(StringComparer.OrdinalIgnoreCase);
var pluginDirectory = GetPluginSourceDirectory();
if (!string.IsNullOrEmpty(pluginDirectory) && Directory.Exists(pluginDirectory))
{
foreach (var path in Directory.GetFiles(pluginDirectory, "*.cs", SearchOption.TopDirectoryOnly))
{
AddSourceCommands(entries, path);
}
}
commandCatalogCache = entries.Values
.OrderBy(entry => entry.Kind)
.ThenBy(entry => entry.Command)
.ThenBy(entry => entry.Plugin)
.ToList();
commandCatalogCachedAt = now;
return commandCatalogCache;
}
private string GetPluginSourceDirectory()
{
try
{
var configPath = Manager.ConfigPath;
if (string.IsNullOrEmpty(configPath))
{
return null;
}
if (!Path.IsPathRooted(configPath))
{
configPath = Path.GetFullPath(configPath);
}
var configDirectory = new DirectoryInfo(configPath);
var oxideDirectory = configDirectory.Parent;
if (oxideDirectory == null)
{
return null;
}
var pluginDirectory = Path.Combine(oxideDirectory.FullName, "plugins");
return Directory.Exists(pluginDirectory) ? pluginDirectory : null;
}
catch
{
return null;
}
}
private void AddSourceCommands(Dictionary<string, CommandCatalogEntry> entries, string path)
{
string source;
try
{
source = File.ReadAllText(path);
}
catch
{
return;
}
var pluginName = ResolvePluginName(path, source);
foreach (Match match in ChatCommandAttributeRegex.Matches(source))
{
AddCommandEntry(entries, "slash", match.Groups[1].Value, pluginName, "ChatCommand");
}
foreach (Match match in ConsoleCommandAttributeRegex.Matches(source))
{
AddCommandEntry(entries, "console", match.Groups[1].Value, pluginName, "ConsoleCommand");
}
foreach (Match match in CovalenceCommandAttributeRegex.Matches(source))
{
foreach (var command in QuotedStrings(match.Groups[1].Value))
{
AddCommandEntry(entries, "slash", command, pluginName, "Command");
AddCommandEntry(entries, "console", command, pluginName, "Command");
}
}
foreach (Match match in AddChatCommandRegex.Matches(source))
{
AddCommandEntry(entries, "slash", match.Groups[1].Value, pluginName, "AddChatCommand");
}
foreach (Match match in AddConsoleCommandRegex.Matches(source))
{
AddCommandEntry(entries, "console", match.Groups[1].Value, pluginName, "AddConsoleCommand");
}
foreach (Match match in AddCovalenceCommandRegex.Matches(source))
{
var command = FirstQuotedString(match.Groups[1].Value);
if (string.IsNullOrEmpty(command))
{
continue;
}
AddCommandEntry(entries, "slash", command, pluginName, "AddCovalenceCommand");
AddCommandEntry(entries, "console", command, pluginName, "AddCovalenceCommand");
}
}
private string ResolvePluginName(string path, string source)
{
var match = InfoAttributeRegex.Match(source ?? "");
if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
{
return match.Groups[1].Value.Trim();
}
return Path.GetFileNameWithoutExtension(path);
}
private IEnumerable<string> QuotedStrings(string text)
{
foreach (Match match in StringLiteralRegex.Matches(text ?? ""))
{
if (!string.IsNullOrWhiteSpace(match.Groups[1].Value))
{
yield return match.Groups[1].Value.Trim();
}
}
}
private string FirstQuotedString(string text)
{
foreach (var value in QuotedStrings(text))
{
return value;
}
return null;
}
private void AddCommandEntry(Dictionary<string, CommandCatalogEntry> entries, string kind, string command, string plugin, string source)
{
command = NormalizeCommandName(command);
if (string.IsNullOrEmpty(command))
{
return;
}
kind = kind == "console" ? "console" : "slash";
plugin = string.IsNullOrWhiteSpace(plugin) ? "Unknown" : plugin.Trim();
source = string.IsNullOrWhiteSpace(source) ? "source" : source.Trim();
var key = $"{kind}|{plugin}|{command}";
if (entries.TryGetValue(key, out var existing))
{
if (existing.Source.IndexOf(source, StringComparison.OrdinalIgnoreCase) < 0)
{
existing.Source = $"{existing.Source}, {source}";
}
return;
}
entries[key] = new CommandCatalogEntry
{
Kind = kind,
Command = command,
Plugin = plugin,
Source = source
};
}
private string NormalizeCommandName(string command)
{
command = (command ?? "").Trim().TrimStart('/');
return command.Trim();
}
private bool CommandMatches(CommandCatalogEntry entry, string filter)
{
return entry != null && (MatchesFilter(entry.Command, filter) || MatchesFilter(entry.Plugin, filter) || MatchesFilter(entry.Source, filter));
}
private int ClampPage(int page, int total, int pageSize)
{
var maxPage = Math.Max(0, (Math.Max(0, total) - 1) / Math.Max(1, pageSize));
return Math.Min(Math.Max(0, page), maxPage);
}
private string FooterText(BasePlayer player, PanelState state)
{
return $"Logged in as {player.displayName}   |   Tab: {Title(state.Tab)}   |   /admin close";
}
private void Reply(BasePlayer player, string message)
{
SendReply(player, $"<color=#1faaa4>{config.Title}</color>: {message}");
}
private static string Get(string[] args, int index, string fallback)
{
return args.Length > index ? args[index] : fallback;
}
private static int ToInt(string value)
{
return int.TryParse(value, out var number) ? number : 0;
}
private static ulong ToUlong(string value)
{
return ulong.TryParse(value, out var number) ? number : 0;
}
private static string Title(string value)
{
return string.IsNullOrEmpty(value) ? string.Empty : char.ToUpper(value[0]) + value.Substring(1);
}
private static string Shorten(string value, int maxLength)
{
if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
return value.Substring(0, Math.Max(0, maxLength - 3)) + "...";
}
private static string Now()
{
return DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
}
}
}
