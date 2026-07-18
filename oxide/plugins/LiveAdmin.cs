using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
namespace Oxide.Plugins
{
[Info("LiveAdmin", "Codex", "0.15.1")]
[Description("A live in-game staff admin panel for Rust/uMod servers.")]
public class LiveAdmin : RustPlugin
{
[PluginReference] private Plugin Backpacks;
[PluginReference] private Plugin Godmode;
[PluginReference] private Plugin Vanish;
[PluginReference] private Plugin InventoryViewer;
[PluginReference] private Plugin RaidlandsUiEscapeBridge;
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
private const string PermServerActions = "liveadmin.server.actions";
private const string PermStaffToolsView = "liveadmin.stafftools.view";
private const string PermStaffToolsModerate = "liveadmin.stafftools.moderate";
private const string PermStaffToolsFun = "liveadmin.stafftools.fun";
private const string PermStaffToolsDangerous = "liveadmin.stafftools.dangerous";
private const string PermPlayersKick = "liveadmin.players.kick";
private const string PermPlayersBan = "liveadmin.players.ban";
private const string PermPlayersMute = "liveadmin.players.mute";
private const string PermPlayersNotes = "liveadmin.players.notes";
private const string PermPlayersInventoryView = "liveadmin.players.inventory.view";
private const string PermPlayersInventoryModify = "liveadmin.players.inventory.modify";
private const string PermStaffToolsActions = "liveadmin.stafftools.actions";
private const string PermStaffToolsInventory = "liveadmin.stafftools.inventory";
private const string PermStaffToolsGroups = "liveadmin.stafftools.groups";
private const string PermStaffToolsTickets = "liveadmin.stafftools.tickets";
private const string PermServerInfo = "liveadmin.server.info";
private const string PermServerQuickActions = "liveadmin.server.quickactions";
private const string PermServerWipeView = "liveadmin.server.wipe.view";
private const string PermServerWipeManage = "liveadmin.server.wipe.manage";
private const string PermServerWipeForce = "liveadmin.server.wipe.force";
private const string PermPluginsReload = "liveadmin.plugins.reload";
private const string PermPluginsConfigView = "liveadmin.plugins.config.view";
private const string PermPluginsConfigEdit = "liveadmin.plugins.config.edit";
private const string PermPluginsConfigRestore = "liveadmin.plugins.config.restore";
private const string PermAuditView = "liveadmin.audit.view";
private const string PermAuditManage = "liveadmin.audit.manage";
private const string PermChatView = "liveadmin.chat.view";
private const string PermChatManage = "liveadmin.chat.manage";
private const string PermServerMaintenance = "liveadmin.server.maintenance";
private const string PermServerMaintenanceBypass = "liveadmin.server.maintenance.bypass";
private const string PermServerRestart = "liveadmin.server.restart";
private StoredData storedData;
private PluginConfig config;
private readonly Dictionary<ulong, PanelState> states = new Dictionary<ulong, PanelState>();
private readonly HashSet<string> registeredPermissions = new HashSet<string>();
private readonly Dictionary<ulong, Vector3> frozenPositions = new Dictionary<ulong, Vector3>();
private readonly HashSet<ulong> passivePlayers = new HashSet<ulong>();
private readonly HashSet<ulong> spinningPlayers = new HashSet<ulong>();
private readonly HashSet<ulong> staffOnDuty = new HashSet<ulong>();
private readonly HashSet<ulong> openPanels = new HashSet<ulong>();
private readonly List<ResourceSnapshot> resourceHistory = new List<ResourceSnapshot>();
private readonly List<ConsoleLine> liveConsoleBuffer = new List<ConsoleLine>();
private readonly Dictionary<string, DateTime> rateLimits = new Dictionary<string, DateTime>();
private Process currentProcess;
private DateTime lastResourceSampleAt = DateTime.MinValue;
private TimeSpan lastCpuTime = TimeSpan.Zero;
private DateTime lastDashboardRefreshAt = DateTime.MinValue;
private string activeWipeSeed;
private DateTime lastDiskSampleAt = DateTime.MinValue;
private float cachedDiskGiB;
private UiSkin activeSkin;
private int lastRestartAnnouncementSecond = -1;
private readonly string[] tabs = { "dashboard", "console", "players", "staff", "chat", "reports", "manage", "convars", "wipe", "appearance", "settings", "logs" };
private class PanelState
{
public string Tab = "dashboard";
public string SubTab = "plugins";
public string PlayerDetailTab = "overview";
public string StaffSubTab = "info";
public string ChatSubTab = "monitor";
public string ReportScope = "active";
public string StaffInventoryTab = "main";
public bool StaffInventoryDetailOpen;
public int StaffInventorySlot = -1;
public int StaffInventoryPage;
public int PlayerPage;
public int PlayerCupboardPage;
public int PluginPage;
public int GroupPage;
public int PermissionGroupPage;
public int PermissionPluginPage;
public int PermissionPage;
public int ConfigPage;
public int GroupDropdownPage;
public int TicketPage;
public int ChatPage;
public string SelectedPlayerId;
public string SelectedChatId;
public string SelectedGroup;
public string SelectedPlugin;
public string SelectedConfigPlugin;
public int SelectedReportId;
public string MuteDuration = "10m";
public string MuteReason = "Staff action";
public string NoteText = string.Empty;
public string TicketNote = string.Empty;
public string StaffReason = "Staff action";
public string StaffMessage = "Please follow the server rules.";
public string StaffItemShortname = "syringe.medical";
public string StaffItemAmount = "1";
public string ServerName = string.Empty;
public string ServerDescription = string.Empty;
public string ServerUrl = string.Empty;
public string ServerHeaderImage = string.Empty;
public string PlayerFilter = string.Empty;
public string ReportFilter = string.Empty;
public string ChatFilter = string.Empty;
public string ChatReplyText = string.Empty;
public string ChatBlacklistText = string.Empty;
public string ChatTimeoutText = "10m";
public string ConsoleCommandText = "server.save";
public string ConsoleTab = "all";
public string ConsoleFilter = string.Empty;
public bool ConsoleWrap = true;
public bool ConsolePaused;
public bool ConsoleMiniOpen;
public int ConsoleMiniPosition = 1;
public string ConVarFilter = string.Empty;
public string ConVarMaxPlayers = string.Empty;
public string ConVarSaveInterval = string.Empty;
public string ConVarHostname = string.Empty;
public string ConVarDescription = string.Empty;
public string ConVarUrl = string.Empty;
public string PluginFilter = string.Empty;
public string ConfigFilter = string.Empty;
public string GroupFilter = string.Empty;
public string GroupCreateName = string.Empty;
public string GroupCreateTitle = string.Empty;
public string GroupCreateRank = "0";
public string PermissionFilter = string.Empty;
public string GroupDropdownFilter = string.Empty;
public bool GroupDropdownOpen;
public bool NotesOpen;
public string PendingAction;
public string PendingTarget;
public string PendingValue;
public string PendingMessage;
}
private class PluginConfig
{
public int ConfigVersion = 3;
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
"liveadmin.reports.manage",
"liveadmin.stafftools.view",
"liveadmin.stafftools.moderate",
"liveadmin.stafftools.fun",
"liveadmin.stafftools.dangerous"
};
public List<string> ProtectedGroups = new List<string> { "admin", "owner" };
public string MuteCommand = "mute {id} {duration} {reason}";
public string UnmuteCommand = "unmute {id}";
public bool AllowGlobalChatAnnouncements = false;
public bool AnnounceMutes = true;
public string MuteAnnouncement = "{name} was muted for {duration}: {reason}";
public bool AnnounceUnmutes = true;
public string UnmuteAnnouncement = "{name} has been unmuted.";
public int ChatMessagesStored = 120;
public string ChatTimeoutDuration = "10m";
public string ChatTimeoutReason = "Chat moderation";
public string ChatDeleteCommand = string.Empty;
[JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
public List<string> ChatQuickReplies = new List<string>
{
"Please keep chat respectful.",
"Take this to a ticket if you need staff help.",
"Stop spamming chat.",
"That language is not allowed here."
};
public List<string> ChatWordBlacklist = new List<string>();
public bool UseInventoryViewerPlugin = true;
public string InventoryViewerCommand = "viewinv {id}";
public bool UseGodmodeForPassive = true;
public string GodmodeCommand = "god {id}";
public string VanishCommand = "vanish";
public bool SafeMode = false;
public bool EnableCommandSafety = true;
public bool AllowOwnerCommandSafetyBypass = false;
public string MaintenanceKickMessage = "Server maintenance is currently in progress. Please try again shortly.";
public bool KickPlayersWhenMaintenanceStarts = true;
public string RestartDefaultReason = "Scheduled server maintenance";
public int RestartDefaultMinutes = 5;
public int WarningsStoredPerPlayer = 50;
public bool ForceWipeDryRunDefault = true;
public List<string> BlockedCommandPrefixes = new List<string>
{
"quit",
"global.quit",
"server.stop",
"oxide.grant user",
"oxide.grant group admin",
"oxide.grant group owner",
"oxide.usergroup add",
"oxide.usergroup remove"
};
public List<string> OwnerOnlyCommandPrefixes = new List<string>
{
"oxide.grant",
"oxide.revoke",
"oxide.reload",
"oxide.unload",
"oxide.load",
"server.writecfg"
};
public bool AutoWipeEnabled = false;
public bool AutoWipeUseWeeklySchedule = true;
public string AutoWipeCadence = "Weekly";
public string AutoWipeWeekday = "Thursday";
public string AutoWipeSecondWeekday = "Monday";
public bool AutoWipeForceFirstWeekday = true;
public int AutoWipeIntervalDays = 7;
public string AutoWipeTimeUtc = "17:00";
public bool AutoWipeMap = true;
public bool AutoWipeBlueprints = false;
public bool ForceWipeBlueprints = true;
public bool ForceWipeOfflineCleanup = true;
public string AutoWipeSeed = "random";
public int AutoWipeMapSize = 3500;
public string AutoWipeCustomMapUrl = string.Empty;
public string AutoWipeAnnouncement = "Server wipe is starting now.";
public string AutoWipeDiscordCommandText = string.Empty;
public string ForceWipeDiscordCommandText = string.Empty;
public string AutoWipeCommandText = "say Auto wipe command list is not configured.";
public string ForceWipeCommandText = "say Force wipe command list is not configured.";
public string MapWipeDeletePathText = "*.sav||*.map||*.sav.*";
public string BlueprintWipeDeletePathText = "player.blueprints*.db*";
public List<string> AutoWipeCommands = new List<string> { "say Auto wipe command list is not configured." };
public List<string> ForceWipeCommands = new List<string> { "say Force wipe command list is not configured." };
public List<string> AutoWipeDiscordCommands = new List<string>();
public List<string> ForceWipeDiscordCommands = new List<string>();
public bool ForceWipeDeletesFiles = false;
public string WipeFileRoot = string.Empty;
public List<string> MapWipeDeletePaths = new List<string>
{
"*.sav",
"*.map",
"*.sav.*"
};
public List<string> BlueprintWipeDeletePaths = new List<string>
{
"player.blueprints*.db*"
};
public List<PlayerActionCommand> PlayerActions = new List<PlayerActionCommand>
{
new PlayerActionCommand { Label = "Unmute", Command = "unmute {id}", Permission = PermPlayersModerate, Confirm = true },
new PlayerActionCommand { Label = "Freeze", Command = "freeze {id}", Permission = PermPlayersModerate, Confirm = true },
new PlayerActionCommand { Label = "Spectate", Command = "spectate {id}", Permission = PermPlayersModerate, Confirm = false },
new PlayerActionCommand { Label = "Kill", Command = "killplayer {id}", Permission = PermStaffToolsDangerous, Confirm = true }
};
public List<RolePreset> RolePresets = new List<RolePreset>();
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
private class PlayerActionCommand
{
public string Label;
public string Command;
public string Permission;
public bool Confirm;
}
private class RolePreset
{
public string Name;
public string Description;
public List<string> Permissions = new List<string>();
}
private class StoredData
{
public List<ReportEntry> Reports = new List<ReportEntry>();
public List<ActionLog> Logs = new List<ActionLog>();
public List<ChatEntry> Chat = new List<ChatEntry>();
public Dictionary<string, List<StaffNote>> Notes = new Dictionary<string, List<StaffNote>>();
public List<TrackedMute> ActiveMutes = new List<TrackedMute>();
public List<WarningEntry> Warnings = new List<WarningEntry>();
public Dictionary<string, string> UserSkins = new Dictionary<string, string>();
public Dictionary<string, PlayerProfile> PlayerProfiles = new Dictionary<string, PlayerProfile>();
public string NextAutoWipeUtc;
public string LastWipeUtc;
public string PendingWipeMode;
public string PendingWipeStartedUtc;
public int PendingWipeSeed;
public int PendingWipeMapSize;
public string PendingWipeLevelUrl;
public bool MaintenanceEnabled;
public string MaintenanceReason;
public string MaintenanceEnabledBy;
public string MaintenanceEnabledUtc;
public string ScheduledRestartUtc;
public string ScheduledRestartReason;
public string ScheduledRestartBy;
public int NextReportId = 1;
}
private class PlayerProfile
{
public string Id;
public string Name;
public string FirstSeen;
public string LastSeen;
public string LastIp;
public int Connections;
}
private class UiSkin
{
public string Key;
public string Name;
public string Overlay;
public string Top;
public string Nav;
public string Body;
public string Panel;
public string PanelAlt;
public string Slot;
public string Input;
public string Button;
public string ButtonActive;
public string ButtonDisabled;
public string Accent;
public string AccentAlt;
public string Warning;
public string Danger;
public string Success;
public string Text;
public string MutedText;
public string HeaderText;
}
private class ResourceSnapshot
{
public float Cpu;
public float MemoryMb;
public float StorageUsed;
public float DiskGiB;
public string Time;
}
private class TrackedMute
{
public string TargetId;
public string TargetName;
public string Duration;
public string Reason;
public string StaffName;
public long ExpiresAtTicks;
}
private class PlayerWorldStats
{
public int Total;
public int BuildingBlocks;
public int Deployables;
public int Storage;
public int Turrets;
public int Traps;
public int Vehicles;
public int Respawns;
public int OwnedCupboards;
public int AuthorizedCupboards;
}
private class WarningEntry
{
public string Time;
public string TargetId;
public string TargetName;
public string StaffId;
public string StaffName;
public string Reason;
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
public string Priority = "Normal";
public string ClaimedBy;
public string CreatedAt;
public string UpdatedAt;
public List<TicketNote> Notes = new List<TicketNote>();
}
private class TicketNote
{
public string Time;
public string StaffId;
public string StaffName;
public string Text;
}
private class ActionLog
{
public string Time;
public string ActorId;
public string ActorName;
public string Action;
public string Target;
public string Details;
public string Result;
public string TargetName;
public string Category;
}
private class ChatEntry
{
public string Id;
public string Time;
public string PlayerId;
public string PlayerName;
public string Message;
public string Channel;
public bool Blocked;
public string MatchedWord;
public bool Deleted;
public string DeletedBy;
public string DeletedAt;
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
public JToken Token;
}
private class InventoryView
{
public string Key;
public string Title;
public ItemContainer Container;
public int Columns;
public int Rows;
public int MaxSlots;
public double GridBottom;
public double GridRight;
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
MigrateConfig();
EnsureConfigDefaults();
SaveConfig();
}
protected override void SaveConfig()
{
Config.WriteObject(config, true);
}
private static List<string> DistinctConfigValues(IEnumerable<string> values)
{
var result = new List<string>();
var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (var value in values ?? Enumerable.Empty<string>())
{
var normalized = (value ?? string.Empty).Trim();
if (normalized.Length > 0 && seen.Add(normalized)) result.Add(normalized);
}
return result;
}
private void EnsureConfigDefaults()
{
if (config.ConfigVersion < 3)
{
config.ConfigVersion = 3;
}
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
"liveadmin.stafftools.view",
"liveadmin.stafftools.moderate",
"liveadmin.stafftools.fun",
"liveadmin.stafftools.dangerous"
};
}
AddMissingCommonPermissions();
if (config.ProtectedGroups == null)
{
config.ProtectedGroups = new List<string> { "admin", "owner" };
}
if (config.BlockedCommandPrefixes == null || config.BlockedCommandPrefixes.Count == 0)
{
config.BlockedCommandPrefixes = new List<string> { "quit", "global.quit", "server.stop", "oxide.grant user", "oxide.grant group admin", "oxide.grant group owner", "oxide.usergroup add", "oxide.usergroup remove" };
}
if (config.OwnerOnlyCommandPrefixes == null || config.OwnerOnlyCommandPrefixes.Count == 0)
{
config.OwnerOnlyCommandPrefixes = new List<string> { "oxide.grant", "oxide.revoke", "oxide.reload", "oxide.unload", "oxide.load", "server.writecfg" };
}
if (config.RolePresets == null || config.RolePresets.Count == 0)
{
config.RolePresets = DefaultRolePresets();
}
if (string.IsNullOrEmpty(config.MuteCommand))
{
config.MuteCommand = "mute {id} {duration} {reason}";
}
if (string.IsNullOrEmpty(config.UnmuteCommand))
{
config.UnmuteCommand = "unmute {id}";
}
if (string.IsNullOrEmpty(config.MuteAnnouncement))
{
config.MuteAnnouncement = "{name} was muted for {duration}: {reason}";
}
if (string.IsNullOrEmpty(config.UnmuteAnnouncement))
{
config.UnmuteAnnouncement = "{name} has been unmuted.";
}
if (config.ChatMessagesStored <= 0)
{
config.ChatMessagesStored = 120;
}
if (string.IsNullOrEmpty(config.ChatTimeoutDuration))
{
config.ChatTimeoutDuration = "10m";
}
if (string.IsNullOrEmpty(config.ChatTimeoutReason))
{
config.ChatTimeoutReason = "Chat moderation";
}
if (config.ChatDeleteCommand == null)
{
config.ChatDeleteCommand = string.Empty;
}
if (config.ChatQuickReplies == null || config.ChatQuickReplies.Count == 0)
{
config.ChatQuickReplies = new List<string> { "Please keep chat respectful.", "Take this to a ticket if you need staff help.", "Stop spamming chat.", "That language is not allowed here." };
}
config.ChatQuickReplies = DistinctConfigValues(config.ChatQuickReplies);
if (config.ChatWordBlacklist == null)
{
config.ChatWordBlacklist = new List<string>();
}
if (string.IsNullOrEmpty(config.InventoryViewerCommand))
{
config.InventoryViewerCommand = "viewinv {id}";
}
if (string.IsNullOrEmpty(config.GodmodeCommand))
{
config.GodmodeCommand = "god {id}";
}
if (string.IsNullOrEmpty(config.VanishCommand))
{
config.VanishCommand = "vanish";
}
if (string.IsNullOrEmpty(config.MaintenanceKickMessage))
{
config.MaintenanceKickMessage = "Server maintenance is currently in progress. Please try again shortly.";
}
if (string.IsNullOrEmpty(config.RestartDefaultReason))
{
config.RestartDefaultReason = "Scheduled server maintenance";
}
config.RestartDefaultMinutes = Math.Max(1, Math.Min(1440, config.RestartDefaultMinutes));
if (config.WarningsStoredPerPlayer <= 0)
{
config.WarningsStoredPerPlayer = 50;
}
if (config.AutoWipeIntervalDays <= 0)
{
config.AutoWipeIntervalDays = 7;
}
config.AutoWipeCadence = NormalizeWipeCadence(config.AutoWipeCadence, config.AutoWipeUseWeeklySchedule);
config.AutoWipeUseWeeklySchedule = !config.AutoWipeCadence.Equals("Interval", StringComparison.OrdinalIgnoreCase);
if (!TryParseWeekday(config.AutoWipeWeekday, out _))
{
config.AutoWipeWeekday = "Thursday";
}
if (!TryParseWeekday(config.AutoWipeSecondWeekday, out _))
{
config.AutoWipeSecondWeekday = "Monday";
}
if (string.IsNullOrEmpty(config.AutoWipeTimeUtc))
{
config.AutoWipeTimeUtc = "17:00";
}
if (config.AutoWipeMapSize <= 0)
{
config.AutoWipeMapSize = 3500;
}
if (string.IsNullOrEmpty(config.AutoWipeSeed))
{
config.AutoWipeSeed = "random";
}
if (string.IsNullOrEmpty(config.AutoWipeAnnouncement))
{
config.AutoWipeAnnouncement = "Server wipe is starting now.";
}
if (string.IsNullOrEmpty(config.AutoWipeCommandText))
{
config.AutoWipeCommandText = string.Join("||", config.AutoWipeCommands ?? new List<string> { "say Auto wipe command list is not configured." });
}
if (string.IsNullOrEmpty(config.ForceWipeCommandText))
{
config.ForceWipeCommandText = string.Join("||", config.ForceWipeCommands ?? new List<string> { "say Force wipe command list is not configured." });
}
if (string.IsNullOrEmpty(config.MapWipeDeletePathText))
{
config.MapWipeDeletePathText = string.Join("||", config.MapWipeDeletePaths ?? new List<string>());
}
if (string.IsNullOrEmpty(config.BlueprintWipeDeletePathText))
{
config.BlueprintWipeDeletePathText = string.Join("||", config.BlueprintWipeDeletePaths ?? new List<string>());
}
if (config.AutoWipeCommands == null || config.AutoWipeCommands.Count == 0)
{
config.AutoWipeCommands = SplitConfigList(config.AutoWipeCommandText, "say Auto wipe command list is not configured.");
}
if (config.ForceWipeCommands == null || config.ForceWipeCommands.Count == 0)
{
config.ForceWipeCommands = SplitConfigList(config.ForceWipeCommandText, "say Force wipe command list is not configured.");
}
if (string.IsNullOrEmpty(config.AutoWipeDiscordCommandText) && config.AutoWipeDiscordCommands != null && config.AutoWipeDiscordCommands.Count > 0)
{
config.AutoWipeDiscordCommandText = string.Join("||", config.AutoWipeDiscordCommands);
}
if (string.IsNullOrEmpty(config.ForceWipeDiscordCommandText) && config.ForceWipeDiscordCommands != null && config.ForceWipeDiscordCommands.Count > 0)
{
config.ForceWipeDiscordCommandText = string.Join("||", config.ForceWipeDiscordCommands);
}
if (config.AutoWipeDiscordCommands == null)
{
config.AutoWipeDiscordCommands = new List<string>();
}
if (config.ForceWipeDiscordCommands == null)
{
config.ForceWipeDiscordCommands = new List<string>();
}
if (config.MapWipeDeletePaths == null || config.MapWipeDeletePaths.Count == 0)
{
config.MapWipeDeletePaths = SplitConfigList(config.MapWipeDeletePathText, "*.sav");
}
if (config.BlueprintWipeDeletePaths == null || config.BlueprintWipeDeletePaths.Count == 0)
{
config.BlueprintWipeDeletePaths = SplitConfigList(config.BlueprintWipeDeletePathText, "player.blueprints*.db*");
}
NormalizeConfigLists();
if (config.QuickCommands == null)
{
config.QuickCommands = new List<QuickCommand>();
}
DeduplicateQuickCommands();
if (config.PlayerActions == null || config.PlayerActions.Count == 0)
{
config.PlayerActions = new List<PlayerActionCommand>
{
new PlayerActionCommand { Label = "Unmute", Command = "unmute {id}", Permission = PermPlayersModerate, Confirm = true },
new PlayerActionCommand { Label = "Freeze", Command = "freeze {id}", Permission = PermPlayersModerate, Confirm = true },
new PlayerActionCommand { Label = "Spectate", Command = "spectate {id}", Permission = PermPlayersModerate, Confirm = false },
new PlayerActionCommand { Label = "Kill", Command = "killplayer {id}", Permission = PermStaffToolsDangerous, Confirm = true }
};
}
else
{
config.PlayerActions.RemoveAll(a => a != null && a.Label == "Mute 10m" && a.Command == "mute {id} 10m Staff action");
}
MigratePlayerActions();
DeduplicatePlayerActions();
NormalizeRolePresets();
}
private void MigrateConfig()
{
if (config == null) return;
if (config.ConfigVersion < 3) config.ConfigVersion = 3;
if (config.CommonPermissions == null) config.CommonPermissions = new List<string>();
AddMissingCommonPermissions();
if (config.RolePresets == null || config.RolePresets.Count == 0) config.RolePresets = DefaultRolePresets();
MigratePlayerActions();
NormalizeRolePresets();
}
private void NormalizeConfigLists()
{
config.ManagedPlugins = UniqueList(config.ManagedPlugins);
config.CommonGroups = UniqueList(config.CommonGroups);
config.CommonPermissions = UniqueList(config.CommonPermissions);
config.ProtectedGroups = UniqueList(config.ProtectedGroups);
config.BlockedCommandPrefixes = UniqueList(config.BlockedCommandPrefixes);
config.OwnerOnlyCommandPrefixes = UniqueList(config.OwnerOnlyCommandPrefixes);

config.AutoWipeCommands = NormalizeCommandList(SplitConfigList(config.AutoWipeCommandText, "say Auto wipe command list is not configured."), "say Auto wipe command list is not configured.");
config.ForceWipeCommands = NormalizeCommandList(SplitConfigList(config.ForceWipeCommandText, "say Force wipe command list is not configured."), "say Force wipe command list is not configured.");
config.AutoWipeDiscordCommands = UniqueList(SplitConfigList(config.AutoWipeDiscordCommandText, string.Empty));
config.ForceWipeDiscordCommands = UniqueList(SplitConfigList(config.ForceWipeDiscordCommandText, string.Empty));
config.MapWipeDeletePaths = UniqueList(SplitConfigList(config.MapWipeDeletePathText, "*.sav"));
config.BlueprintWipeDeletePaths = UniqueList(SplitConfigList(config.BlueprintWipeDeletePathText, "player.blueprints*.db*"));

config.AutoWipeCommandText = string.Join("||", config.AutoWipeCommands);
config.ForceWipeCommandText = string.Join("||", config.ForceWipeCommands);
config.AutoWipeDiscordCommandText = string.Join("||", config.AutoWipeDiscordCommands);
config.ForceWipeDiscordCommandText = string.Join("||", config.ForceWipeDiscordCommands);
config.MapWipeDeletePathText = string.Join("||", config.MapWipeDeletePaths);
config.BlueprintWipeDeletePathText = string.Join("||", config.BlueprintWipeDeletePaths);
}
private void AddMissingCommonPermissions()
{
if (config.CommonPermissions == null) config.CommonPermissions = new List<string>();
foreach (var perm in AllPermissionNames())
{
if (!config.CommonPermissions.Contains(perm, StringComparer.OrdinalIgnoreCase)) config.CommonPermissions.Add(perm);
}
config.CommonPermissions = UniqueList(config.CommonPermissions);
}
private List<RolePreset> DefaultRolePresets()
{
return new List<RolePreset>
{
new RolePreset { Name = "Owner", Description = "Full LiveAdmin access.", Permissions = AllPermissionNames().ToList() },
new RolePreset { Name = "Admin", Description = "Operational admin access without owner-only destructive controls.", Permissions = new List<string> { PermUse, PermPlayersView, PermPlayersModerate, PermPlayersKick, PermPlayersBan, PermPlayersMute, PermPlayersNotes, PermPlayersTeleport, PermPlayersInventoryView, PermPlayersInventoryModify, PermReportsView, PermReportsManage, PermStaffToolsView, PermStaffToolsActions, PermStaffToolsInventory, PermStaffToolsGroups, PermStaffToolsTickets, PermPluginsView, PermPluginsReload, PermServerInfo, PermServerQuickActions, PermServerMaintenance, PermServerMaintenanceBypass, PermServerRestart, PermServerWipeView, PermAuditView, PermChatView, PermChatManage } },
new RolePreset { Name = "Staff", Description = "Staff tools, tickets, notes, chat moderation, and safe player support controls.", Permissions = new List<string> { PermUse, PermPlayersView, PermPlayersModerate, PermPlayersKick, PermPlayersMute, PermPlayersNotes, PermPlayersTeleport, PermReportsView, PermReportsManage, PermStaffToolsView, PermStaffToolsActions, PermStaffToolsTickets, PermServerMaintenanceBypass, PermChatView, PermChatManage } },
new RolePreset { Name = "Moderator", Description = "Normal moderation, tickets, notes, mute, kick, and chat monitoring.", Permissions = new List<string> { PermUse, PermPlayersView, PermPlayersModerate, PermPlayersKick, PermPlayersMute, PermPlayersNotes, PermReportsView, PermReportsManage, PermStaffToolsView, PermStaffToolsActions, PermStaffToolsTickets, PermServerMaintenanceBypass, PermChatView, PermChatManage } },
new RolePreset { Name = "Senior Admin", Description = "Legacy senior-admin preset retained for existing configs.", Permissions = new List<string> { PermUse, PermPlayersView, PermPlayersModerate, PermPlayersKick, PermPlayersBan, PermPlayersMute, PermPlayersNotes, PermPlayersTeleport, PermPlayersInventoryView, PermPlayersInventoryModify, PermReportsView, PermReportsManage, PermStaffToolsView, PermStaffToolsActions, PermStaffToolsInventory, PermStaffToolsTickets, PermPluginsView, PermPluginsReload, PermServerQuickActions, PermAuditView, PermChatView, PermChatManage } },
new RolePreset { Name = "Support Staff", Description = "View players/reports and add notes with non-destructive support tools.", Permissions = new List<string> { PermUse, PermPlayersView, PermPlayersNotes, PermReportsView, PermStaffToolsView, PermStaffToolsActions, PermStaffToolsTickets, PermChatView } },
new RolePreset { Name = "Read Only", Description = "Read-only access to panels and audit logs.", Permissions = new List<string> { PermUse, PermPlayersView, PermReportsView, PermPluginsView, PermGroupsView, PermPermissionsView, PermAuditView } }
};
}
private void NormalizeRolePresets()
{
if (config.RolePresets == null) config.RolePresets = DefaultRolePresets();
foreach (var defaultPreset in DefaultRolePresets())
{
if (!config.RolePresets.Any(p => p != null && p.Name.Equals(defaultPreset.Name, StringComparison.OrdinalIgnoreCase)))
{
config.RolePresets.Add(defaultPreset);
}
}
var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var normalized = new List<RolePreset>();
foreach (var preset in config.RolePresets.Where(p => p != null && !string.IsNullOrEmpty(p.Name)))
{
if (!seen.Add(preset.Name)) continue;
preset.Permissions = UniqueList(preset.Permissions ?? new List<string>());
normalized.Add(preset);
}
config.RolePresets = normalized.Count == 0 ? DefaultRolePresets() : normalized;
}
private List<string> NormalizeCommandList(List<string> items, string placeholder)
{
var normalized = UniqueList(items);
if (normalized.Count > 1)
{
normalized.RemoveAll(item => item.Equals(placeholder, StringComparison.OrdinalIgnoreCase));
}
return normalized.Count == 0 ? new List<string> { placeholder } : normalized;
}
private List<string> UniqueList(List<string> items)
{
var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var result = new List<string>();
if (items == null) return result;
foreach (var item in items)
{
var clean = (item ?? string.Empty).Trim();
if (clean.Length == 0 || !seen.Add(clean)) continue;
result.Add(clean);
}
return result;
}
private void DeduplicatePlayerActions()
{
if (config.PlayerActions == null) return;
var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
config.PlayerActions = config.PlayerActions
.Where(a => a != null && !string.IsNullOrEmpty(a.Label) && seen.Add(a.Label))
.ToList();
}
private void MigratePlayerActions()
{
if (config.PlayerActions == null) return;
foreach (var action in config.PlayerActions)
{
if (action == null) continue;
if (!string.Equals(action.Label, "Kill", StringComparison.OrdinalIgnoreCase)) continue;
if (!string.Equals(action.Permission, PermPlayersModerate, StringComparison.OrdinalIgnoreCase)) continue;
action.Permission = PermStaffToolsDangerous;
}
}
private void DeduplicateQuickCommands()
{
if (config.QuickCommands == null) return;
var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
config.QuickCommands = config.QuickCommands
.Where(q => q != null && !string.IsNullOrEmpty(q.Label) && seen.Add(q.Label))
.ToList();
}
private void Init()
{
RegisterPermissions();
storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
EnsureDataDefaults();
Application.logMessageReceived += OnUnityLogMessage;
StartMuteTimer();
StartResourceMonitor();
StartAutoWipeTimer();
StartSpinTimer();
StartOperationsTimer();
StartConsoleRefreshTimer();
}
private void Loaded()
{
RegisterPermissions();
}
private void OnServerInitialized()
{
RegisterPermissions();
ProcessExpiredMutes();
SampleResources();
EnsureNextAutoWipe();
VerifyPendingWipe();
foreach (var player in BasePlayer.activePlayerList)
{
UpdatePlayerProfile(player, true);
}
}
private void Unload()
{
Application.logMessageReceived -= OnUnityLogMessage;
foreach (var player in BasePlayer.activePlayerList)
{
CuiHelper.DestroyUi(player, UiRoot);
}
openPanels.Clear();
passivePlayers.Clear();
spinningPlayers.Clear();
staffOnDuty.Clear();
SaveData();
}
private void OnPlayerDisconnected(BasePlayer player, string reason)
{
UpdatePlayerProfile(player, false);
LogPlayerActivity(player, "PlayerLeave", string.IsNullOrEmpty(reason) ? "Disconnected" : reason);
SaveData();
RefreshOpenPanels("dashboard");
ClearRuntimePlayerState(player);
}
private void OnPlayerConnected(BasePlayer player)
{
UpdatePlayerProfile(player, true);
LogPlayerActivity(player, "PlayerJoin", GetPlayerAddress(player));
SaveData();
RefreshOpenPanels("dashboard");
ClearRuntimePlayerState(player);
}
private object OnPlayerChat(BasePlayer player, string message, ConVar.Chat.ChatChannel channel)
{
if (player == null || string.IsNullOrEmpty(message)) return null;
var matched = MatchBlacklistedWord(message);
var blocked = !string.IsNullOrEmpty(matched);
AddChatEntry(player, message, channel.ToString(), blocked, matched);
if (blocked)
{
player.ChatMessage("<color=#d14a3c>Your message was blocked by chat moderation.</color>");
LogBlocked(null, "Chat", "Blacklist", player.UserIDString, player.displayName, matched);
RefreshOpenPanels("chat");
RefreshConsoleViews();
return true;
}
RefreshOpenPanels("chat");
RefreshConsoleViews();
return null;
}
private void ClearRuntimePlayerState(BasePlayer player)
{
if (player == null) return;
openPanels.Remove(player.userID);
spinningPlayers.Remove(player.userID);
staffOnDuty.Remove(player.userID);
}
private void UpdatePlayerProfile(BasePlayer player, bool connected)
{
if (player == null) return;
EnsureDataDefaults();
var id = player.UserIDString;
if (!storedData.PlayerProfiles.TryGetValue(id, out var profile) || profile == null)
{
profile = new PlayerProfile { Id = id, FirstSeen = Now() };
storedData.PlayerProfiles[id] = profile;
}
profile.Name = player.displayName;
profile.LastSeen = Now();
profile.LastIp = GetPlayerAddress(player);
if (connected) profile.Connections++;
SaveData();
}
private int NewPlayersToday()
{
EnsureDataDefaults();
var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
return storedData.PlayerProfiles.Values.Count(p => p != null && !string.IsNullOrEmpty(p.FirstSeen) && p.FirstSeen.StartsWith(today, StringComparison.OrdinalIgnoreCase));
}
private PlayerProfile GetPlayerProfile(string id)
{
EnsureDataDefaults();
if (string.IsNullOrEmpty(id)) return null;
return storedData.PlayerProfiles.TryGetValue(id, out var profile) ? profile : null;
}
private string ShortDate(string value)
{
if (string.IsNullOrEmpty(value)) return "unknown";
return value.Length <= 16 ? value : value.Substring(0, 16);
}
private string GetServerFpsText()
{
try
{
var type = typeof(BasePlayer).Assembly.GetType("Performance");
var current = type?.GetField("current")?.GetValue(null);
if (current == null) return "n/a";
var value = current.GetType().GetField("frameRate")?.GetValue(current) ?? current.GetType().GetProperty("frameRate")?.GetValue(current, null);
if (value == null) return "n/a";
return Convert.ToSingle(value).ToString("0");
}
catch
{
return "n/a";
}
}
private string GetEntityCountText()
{
try
{
var type = typeof(BasePlayer).Assembly.GetType("BaseNetworkable");
var entities = type?.GetField("serverEntities")?.GetValue(null);
if (entities == null) return "n/a";
var countProperty = entities.GetType().GetProperty("Count");
var count = countProperty == null ? null : countProperty.GetValue(entities, null);
return count == null ? "n/a" : count.ToString();
}
catch
{
return "n/a";
}
}
private void RegisterPermissions()
{
var added = 0;
foreach (var perm in AllPermissionNames())
{
if (registeredPermissions.Contains(perm) || permission.PermissionExists(perm, this))
{
registeredPermissions.Add(perm);
continue;
}
permission.RegisterPermission(perm, this);
registeredPermissions.Add(perm);
added++;
}
if (added > 0) Puts($"Registered {added} new LiveAdmin permissions ({registeredPermissions.Count} total).");
}
private IEnumerable<string> AllPermissionNames()
{
return new[]
{
PermUse, PermOwner, PermPlayersView, PermPlayersModerate, PermPlayersTeleport,
PermReportsView, PermReportsManage, PermPluginsView, PermPluginsManage,
PermGroupsView, PermGroupsManage, PermPermissionsView, PermPermissionsManage,
PermServerActions, PermStaffToolsView, PermStaffToolsModerate, PermStaffToolsFun,
PermStaffToolsDangerous, PermPlayersKick, PermPlayersBan, PermPlayersMute,
PermPlayersNotes, PermPlayersInventoryView, PermPlayersInventoryModify,
PermStaffToolsActions, PermStaffToolsInventory, PermStaffToolsGroups,
PermStaffToolsTickets, PermServerInfo, PermServerQuickActions,
PermServerWipeView, PermServerWipeManage, PermServerWipeForce,
PermPluginsReload, PermPluginsConfigView, PermPluginsConfigEdit,
PermPluginsConfigRestore, PermAuditView, PermAuditManage, PermChatView, PermChatManage,
PermServerMaintenance, PermServerMaintenanceBypass, PermServerRestart
};
}
private void SaveData()
{
Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);
}
private void EnsureDataDefaults()
{
if (storedData == null) storedData = new StoredData();
if (storedData.Reports == null) storedData.Reports = new List<ReportEntry>();
if (storedData.UserSkins == null) storedData.UserSkins = new Dictionary<string, string>();
foreach (var report in storedData.Reports.Where(r => r != null))
{
if (string.IsNullOrEmpty(report.Status)) report.Status = "Open";
if (string.IsNullOrEmpty(report.Priority)) report.Priority = "Normal";
if (string.IsNullOrEmpty(report.CreatedAt)) report.CreatedAt = Now();
if (string.IsNullOrEmpty(report.UpdatedAt)) report.UpdatedAt = report.CreatedAt;
if (report.Notes == null) report.Notes = new List<TicketNote>();
}
if (storedData.Logs == null) storedData.Logs = new List<ActionLog>();
if (storedData.Chat == null) storedData.Chat = new List<ChatEntry>();
if (storedData.Notes == null) storedData.Notes = new Dictionary<string, List<StaffNote>>();
if (storedData.ActiveMutes == null) storedData.ActiveMutes = new List<TrackedMute>();
if (storedData.Warnings == null) storedData.Warnings = new List<WarningEntry>();
if (storedData.PlayerProfiles == null) storedData.PlayerProfiles = new Dictionary<string, PlayerProfile>();
if (storedData.MaintenanceReason == null) storedData.MaintenanceReason = string.Empty;
if (storedData.ScheduledRestartReason == null) storedData.ScheduledRestartReason = string.Empty;
if (string.IsNullOrEmpty(storedData.NextAutoWipeUtc)) storedData.NextAutoWipeUtc = CalculateNextAutoWipe(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss");
}
private void StartMuteTimer()
{
timer.Every(30f, ProcessExpiredMutes);
}
private void StartResourceMonitor()
{
currentProcess = Process.GetCurrentProcess();
lastResourceSampleAt = DateTime.UtcNow;
lastCpuTime = currentProcess.TotalProcessorTime;
timer.Every(1f, () => SampleResources());
}
private void StartAutoWipeTimer()
{
timer.Every(60f, ProcessAutoWipe);
}
private void StartSpinTimer()
{
timer.Every(0.35f, ProcessSpinPlayers);
}
private void StartOperationsTimer()
{
timer.Every(1f, ProcessScheduledRestart);
}
private void StartConsoleRefreshTimer()
{
timer.Every(30f, () =>
{
foreach (var player in BasePlayer.activePlayerList.ToArray())
{
if (player == null || !openPanels.Contains(player.userID)) continue;
var state = GetState(player);
if (state.Tab == "console" && !state.ConsolePaused) Draw(player);
}
});
}
private void OnUnityLogMessage(string condition, string stackTrace, LogType type)
{
if (string.IsNullOrEmpty(condition)) return;
var kind = type == LogType.Error || type == LogType.Exception || type == LogType.Assert ? "errors" : type == LogType.Warning ? "warnings" : "server";
AddLiveConsoleLine(kind, DetectConsoleSource(condition, type), condition, LooksLikePluginLog(condition) ? "plugins" : "runtime");
}
private void OnPluginLoaded(Plugin plugin)
{
if (plugin == null || plugin.Name == Name) return;
AddLiveConsoleLine("plugins", plugin.Name, $"Plugin loaded ({plugin.Version})", "plugins");
}
private void OnPluginUnloaded(Plugin plugin)
{
if (plugin == null || plugin.Name == Name) return;
AddLiveConsoleLine("plugins", plugin.Name, "Plugin unloaded", "plugins");
}
private void AddLiveConsoleLine(string kind, string source, string message, string origin)
{
liveConsoleBuffer.Add(new ConsoleLine
{
Id = "runtime:" + DateTime.UtcNow.Ticks,
Time = Now(),
Kind = kind,
Origin = origin,
Source = source,
Text = message
});
if (liveConsoleBuffer.Count > 300) liveConsoleBuffer.RemoveRange(0, liveConsoleBuffer.Count - 300);
}
private bool LooksLikePluginLog(string message)
{
if (string.IsNullOrEmpty(message)) return false;
var close = message.IndexOf(']');
if (message.StartsWith("[", StringComparison.Ordinal) && close > 1 && close < 48)
{
var tag = message.Substring(1, close - 1).Trim();
if (!string.IsNullOrEmpty(tag) && plugins.Find(tag) != null) return true;
}
return message.IndexOf("oxide/plugins", StringComparison.OrdinalIgnoreCase) >= 0
|| message.IndexOf("oxide\\plugins", StringComparison.OrdinalIgnoreCase) >= 0
|| message.IndexOf("Error while compiling", StringComparison.OrdinalIgnoreCase) >= 0;
}
private string DetectConsoleSource(string message, LogType type)
{
if (!string.IsNullOrEmpty(message) && message.StartsWith("[", StringComparison.Ordinal))
{
var close = message.IndexOf(']');
if (close > 1 && close < 48) return message.Substring(1, close - 1);
}
return type.ToString();
}
private object CanUserLogin(string name, string id, string ipAddress)
{
if (!storedData.MaintenanceEnabled) return null;
if (permission.UserHasPermission(id, PermOwner) || permission.UserHasPermission(id, PermServerMaintenanceBypass)) return null;
return string.IsNullOrEmpty(storedData.MaintenanceReason)
? config.MaintenanceKickMessage
: $"{config.MaintenanceKickMessage}\nReason: {storedData.MaintenanceReason}";
}
private void ProcessScheduledRestart()
{
if (string.IsNullOrEmpty(storedData.ScheduledRestartUtc)) return;
if (!DateTime.TryParse(storedData.ScheduledRestartUtc, out var restartAt))
{
CancelScheduledRestart(null, "Invalid stored restart time");
return;
}
restartAt = DateTime.SpecifyKind(restartAt, DateTimeKind.Utc);
var seconds = (int)Math.Ceiling((restartAt - DateTime.UtcNow).TotalSeconds);
if (seconds <= 0)
{
var reason = string.IsNullOrEmpty(storedData.ScheduledRestartReason) ? config.RestartDefaultReason : storedData.ScheduledRestartReason;
storedData.ScheduledRestartUtc = string.Empty;
storedData.ScheduledRestartReason = string.Empty;
storedData.ScheduledRestartBy = string.Empty;
lastRestartAnnouncementSecond = -1;
SaveData();
PrintToChat($"<color=#e8731f>SERVER RESTARTING NOW</color> — {reason}");
Puts($"Scheduled restart executing: {reason}");
ConsoleSystem.Run(ConsoleSystem.Option.Server, "server.save");
timer.Once(3f, () => ConsoleSystem.Run(ConsoleSystem.Option.Server, $"restart 0 {ConsoleArg(reason)}"));
return;
}
var announce = seconds <= 10 || seconds == 30 || seconds == 60 || seconds == 120 || seconds == 300 || seconds == 600 || seconds == 900 || seconds == 1800;
if (!announce || lastRestartAnnouncementSecond == seconds) return;
lastRestartAnnouncementSecond = seconds;
PrintToChat($"<color=#e8731f>Server restart in {FormatCountdown(seconds)}</color> — {storedData.ScheduledRestartReason}");
RefreshOpenPanels("dashboard");
}
private string FormatCountdown(int seconds)
{
if (seconds >= 3600) return $"{seconds / 3600}h {(seconds % 3600) / 60}m";
if (seconds >= 60) return $"{seconds / 60}m {seconds % 60}s";
return $"{Math.Max(0, seconds)}s";
}
private bool ScheduleRestart(BasePlayer actor, int minutes, string reason)
{
if (minutes < 1 || minutes > 1440) return false;
reason = string.IsNullOrWhiteSpace(reason) ? config.RestartDefaultReason : reason.Trim();
storedData.ScheduledRestartUtc = DateTime.UtcNow.AddMinutes(minutes).ToString("yyyy-MM-dd HH:mm:ss");
storedData.ScheduledRestartReason = reason;
storedData.ScheduledRestartBy = actor == null ? "Console" : actor.displayName;
lastRestartAnnouncementSecond = -1;
SaveData();
PrintToChat($"<color=#e8731f>Server restart scheduled in {minutes} minute(s).</color> — {reason}");
LogSuccess(actor, "Server", "RestartScheduled", "server", "Server", $"{minutes}m | {reason}");
RefreshOpenPanels("dashboard");
return true;
}
private void CancelScheduledRestart(BasePlayer actor, string reason = "Cancelled by staff")
{
var wasScheduled = !string.IsNullOrEmpty(storedData.ScheduledRestartUtc);
storedData.ScheduledRestartUtc = string.Empty;
storedData.ScheduledRestartReason = string.Empty;
storedData.ScheduledRestartBy = string.Empty;
lastRestartAnnouncementSecond = -1;
SaveData();
if (wasScheduled)
{
PrintToChat("<color=#7ed957>The scheduled server restart has been cancelled.</color>");
LogSuccess(actor, "Server", "RestartCancelled", "server", "Server", reason);
}
RefreshOpenPanels("dashboard");
}
private void SetMaintenance(BasePlayer actor, bool enabled, string reason)
{
storedData.MaintenanceEnabled = enabled;
storedData.MaintenanceReason = enabled ? (string.IsNullOrWhiteSpace(reason) ? "Scheduled maintenance" : reason.Trim()) : string.Empty;
storedData.MaintenanceEnabledBy = enabled ? (actor == null ? "Console" : actor.displayName) : string.Empty;
storedData.MaintenanceEnabledUtc = enabled ? Now() : string.Empty;
SaveData();
LogSuccess(actor, "Server", enabled ? "MaintenanceEnabled" : "MaintenanceDisabled", "server", "Server", storedData.MaintenanceReason);
if (enabled && config.KickPlayersWhenMaintenanceStarts)
{
foreach (var target in BasePlayer.activePlayerList.ToList())
{
if (target == null || Can(target, PermOwner) || Can(target, PermServerMaintenanceBypass)) continue;
target.Kick(config.MaintenanceKickMessage);
}
}
PrintToChat(enabled ? $"<color=#e8731f>Maintenance mode enabled.</color> {storedData.MaintenanceReason}" : "<color=#7ed957>Maintenance mode disabled.</color>");
RefreshOpenPanels("dashboard");
}
[ChatCommand("lamaintenance")]
private void MaintenanceCommand(BasePlayer player, string command, string[] args)
{
if (!Can(player, PermServerMaintenance)) { Reply(player, "You do not have permission to manage maintenance mode."); return; }
if (args.Length == 0) { Reply(player, $"Maintenance is {(storedData.MaintenanceEnabled ? "ON" : "OFF")}. Usage: /lamaintenance on|off [reason]"); return; }
var enabled = args[0].Equals("on", StringComparison.OrdinalIgnoreCase);
if (!enabled && !args[0].Equals("off", StringComparison.OrdinalIgnoreCase)) { Reply(player, "Usage: /lamaintenance on|off [reason]"); return; }
SetMaintenance(player, enabled, string.Join(" ", args.Skip(1).ToArray()));
}
[ChatCommand("larestart")]
private void RestartCommand(BasePlayer player, string command, string[] args)
{
if (!Can(player, PermServerRestart)) { Reply(player, "You do not have permission to schedule restarts."); return; }
if (args.Length > 0 && args[0].Equals("cancel", StringComparison.OrdinalIgnoreCase)) { CancelScheduledRestart(player); return; }
var minutes = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : config.RestartDefaultMinutes;
var reason = string.Join(" ", args.Skip(args.Length > 0 && int.TryParse(args[0], out _) ? 1 : 0).ToArray());
if (!ScheduleRestart(player, minutes, reason)) Reply(player, "Restart delay must be between 1 and 1440 minutes. Usage: /larestart [minutes] [reason], or /larestart cancel");
}
[ConsoleCommand("liveadmin.maintenance")]
private void MaintenanceConsoleCommand(ConsoleSystem.Arg arg)
{
var player = arg.Player();
if (player != null && !Can(player, PermServerMaintenance)) { arg.ReplyWith("Permission denied."); return; }
var args = arg.Args == null ? new string[0] : arg.Args.Select(value => value.ToString()).ToArray();
if (args.Length == 0) { arg.ReplyWith($"Maintenance is {(storedData.MaintenanceEnabled ? "ON" : "OFF")}. Usage: liveadmin.maintenance on|off [reason]"); return; }
var enabled = args[0].Equals("on", StringComparison.OrdinalIgnoreCase);
if (!enabled && !args[0].Equals("off", StringComparison.OrdinalIgnoreCase)) { arg.ReplyWith("Usage: liveadmin.maintenance on|off [reason]"); return; }
SetMaintenance(player, enabled, string.Join(" ", args.Skip(1).ToArray()));
arg.ReplyWith($"Maintenance mode {(enabled ? "enabled" : "disabled")}.");
}
[ConsoleCommand("liveadmin.restart")]
private void RestartConsoleCommand(ConsoleSystem.Arg arg)
{
var player = arg.Player();
if (player != null && !Can(player, PermServerRestart)) { arg.ReplyWith("Permission denied."); return; }
var args = arg.Args == null ? new string[0] : arg.Args.Select(value => value.ToString()).ToArray();
if (args.Length > 0 && args[0].Equals("cancel", StringComparison.OrdinalIgnoreCase)) { CancelScheduledRestart(player); arg.ReplyWith("Scheduled restart cancelled."); return; }
var minutes = config.RestartDefaultMinutes;
var hasMinutes = args.Length > 0 && int.TryParse(args[0], out minutes);
if (!hasMinutes) minutes = config.RestartDefaultMinutes;
var reason = string.Join(" ", args.Skip(hasMinutes ? 1 : 0).ToArray());
arg.ReplyWith(ScheduleRestart(player, minutes, reason) ? $"Restart scheduled in {minutes} minute(s)." : "Restart delay must be between 1 and 1440 minutes.");
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
CreateReportTicket(player, target, targetName, reason, "Chat");
}
private void OnPlayerReported(BasePlayer reporter, string targetName, string targetId, string subject, string message, string type)
{
var target = BasePlayer.FindByID(ToUlong(targetId)) ?? FindPlayer(targetName);
var reason = string.IsNullOrEmpty(message) ? subject : string.IsNullOrEmpty(subject) ? message : $"{subject}: {message}";
CreateReportTicket(reporter, target, string.IsNullOrEmpty(targetName) ? targetId : targetName, reason, "F7");
}
private void OnPlayerReported(BasePlayer reporter, string targetName, string targetId, string message)
{
var target = BasePlayer.FindByID(ToUlong(targetId)) ?? FindPlayer(targetName);
CreateReportTicket(reporter, target, string.IsNullOrEmpty(targetName) ? targetId : targetName, message, "F7");
}
private void OnPlayerReported(BasePlayer reporter, BasePlayer target, string message)
{
CreateReportTicket(reporter, target, target?.displayName ?? "Unknown", message, "F7");
}
private ReportEntry CreateReportTicket(BasePlayer reporter, BasePlayer target, string fallbackTargetName, string reason, string source)
{
reason = string.IsNullOrEmpty(reason) ? "No reason provided" : reason;
var entry = new ReportEntry
{
Id = storedData.NextReportId++,
ReporterId = reporter?.UserIDString ?? "server",
ReporterName = reporter?.displayName ?? "Server",
TargetId = target?.UserIDString ?? fallbackTargetName,
TargetName = target?.displayName ?? fallbackTargetName,
Reason = reason,
Status = "Open",
Priority = "Normal",
CreatedAt = Now(),
UpdatedAt = Now(),
Notes = new List<TicketNote>()
};
storedData.Reports.Add(entry);
ApplyReportEscalation(entry);
Log(reporter, $"ReportCreated:{source}", entry.TargetName, reason);
SaveData();
if (reporter != null) Reply(reporter, $"Report #{entry.Id} submitted. Staff have been notified.");
foreach (var staff in BasePlayer.activePlayerList.Where(p => Can(p, PermReportsView)))
{
Reply(staff, $"New ticket #{entry.Id}: {entry.ReporterName} reported {entry.TargetName} - {entry.Reason}");
}
return entry;
}
[ChatCommand("lanote")]
private void NoteCommand(BasePlayer player, string command, string[] args)
{
if (!CanAny(player, PermOwner, PermPlayersModerate, PermPlayersNotes))
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
if (!CanManageStaffGroups(player) || BlockedBySafeMode(player, "laaddgroup", "Group"))
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
if (!CanTargetPlayer(player, target.UserIDString, "groupadd")) return;
permission.AddUserGroup(target.UserIDString, args[1]);
Log(player, "GroupAdd", target.UserIDString, args[1]);
SaveData();
Reply(player, $"Added {target.displayName} to {args[1]}.");
}
[ChatCommand("laremovegroup")]
private void RemoveGroupCommand(BasePlayer player, string command, string[] args)
{
if (!CanManageStaffGroups(player) || BlockedBySafeMode(player, "laremovegroup", "Group"))
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
if (!CanTargetPlayer(player, target.UserIDString, "groupremove")) return;
permission.RemoveUserGroup(target.UserIDString, args[1]);
Log(player, "GroupRemove", target.UserIDString, args[1]);
SaveData();
Reply(player, $"Removed {target.displayName} from {args[1]}.");
}
[ChatCommand("lagrantgroup")]
private void GrantGroupCommand(BasePlayer player, string command, string[] args)
{
if (!Can(player, PermPermissionsManage) || BlockedBySafeMode(player, "lagrantgroup", "Permission"))
{
Reply(player, "You do not have permission to manage permissions.");
return;
}
if (args.Length < 2)
{
Reply(player, "Use /lagrantgroup <group> <permission>");
return;
}
if (!IsValidPermissionName(args[1]) || !IsValidGroupName(args[0]))
{
Reply(player, "Invalid group or permission name.");
LogFailed(player, "Permission", "GrantGroupPermission", args[0], string.Empty, args[1]);
return;
}
if (IsDangerousPermission(args[1]) && !Can(player, PermOwner) && !IsAuthLevel2(player))
{
Reply(player, "Only owners or auth level 2 users can grant wildcard or high-risk permissions.");
return;
}
RunServerCommand(player, $"oxide.grant group {ConsoleArg(args[0])} {ConsoleArg(args[1])}", $"grant {args[1]} to {args[0]}");
Log(player, "GrantGroupPermission", args[0], args[1]);
SaveData();
RefreshOpenPanels("manage");
Reply(player, $"Granted {args[1]} to {args[0]}.");
}
[ChatCommand("larevokegroup")]
private void RevokeGroupPermissionCommand(BasePlayer player, string command, string[] args)
{
if (!Can(player, PermPermissionsManage) || BlockedBySafeMode(player, "larevokegroup", "Permission"))
{
Reply(player, "You do not have permission to manage permissions.");
return;
}
if (args.Length < 2)
{
Reply(player, "Use /larevokegroup <group> <permission>");
return;
}
if (!IsValidPermissionName(args[1]) || !IsValidGroupName(args[0]))
{
Reply(player, "Invalid group or permission name.");
LogFailed(player, "Permission", "RevokeGroupPermission", args[0], string.Empty, args[1]);
return;
}
RunServerCommand(player, $"oxide.revoke group {ConsoleArg(args[0])} {ConsoleArg(args[1])}", $"revoke {args[1]} from {args[0]}");
Log(player, "RevokeGroupPermission", args[0], args[1]);
SaveData();
RefreshOpenPanels("manage");
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
Reply(player, "Use /lafilter <players|plugins|groups|permissions> <text>");
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
Reply(player, "Filter target must be players, plugins, groups, or permissions.");
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
[ChatCommand("liveadmin.debug")]
private void DebugCommand(BasePlayer player, string command, string[] args)
{
if (!CanAny(player, PermOwner, PermAuditManage))
{
Reply(player, "You do not have access to LiveAdmin diagnostics.");
return;
}
Reply(player, $"Version 0.14.0 | Config v{config.ConfigVersion} | Perms {registeredPermissions.Count}");
Reply(player, $"Reports {storedData.Reports.Count} | Logs {storedData.Logs.Count} | Mutes {storedData.ActiveMutes.Count} | Panels {openPanels.Count}");
Reply(player, $"Frozen {frozenPositions.Count} | Passive {passivePlayers.Count} | Spinning {spinningPlayers.Count}");
Reply(player, $"Backpacks {(Backpacks == null ? "missing" : "loaded")} | InventoryViewer {(InventoryViewer == null ? "missing" : "loaded")} | Vanish {(Vanish == null ? "missing" : "loaded")} | Godmode {(Godmode == null ? "missing" : "loaded")}");
Reply(player, $"AutoWipe {config.AutoWipeEnabled} | Next {storedData.NextAutoWipeUtc} | SafeMode {config.SafeMode} | CommandSafety {config.EnableCommandSafety} | ForceDryRun {config.ForceWipeDryRunDefault}");
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
case "playerdetail":
state.PlayerDetailTab = CleanPlayerDetailTab(Get(args, 1, "overview"));
break;
case "playercupboardpage":
state.PlayerDetailTab = "cupboards";
state.PlayerCupboardPage = Math.Max(0, state.PlayerCupboardPage + ToInt(Get(args, 1, "0")));
break;
case "staffsubtab":
state.StaffSubTab = Get(args, 1, "info").ToLowerInvariant();
if (state.StaffSubTab == "inventory") state.StaffInventoryDetailOpen = false;
break;
case "chatsubtab":
state.ChatSubTab = Get(args, 1, "monitor").ToLowerInvariant();
break;
case "reportscope":
state.ReportScope = CleanReportScope(Get(args, 1, "active"));
state.TicketPage = 0;
break;
case "consoletab":
state.ConsoleTab = CleanConsoleTab(Get(args, 1, "all"));
state.Tab = "console";
break;
case "consolewrap":
state.ConsoleWrap = !state.ConsoleWrap;
break;
case "consolepause":
state.ConsolePaused = !state.ConsolePaused;
break;
case "consolemini":
state.ConsoleMiniOpen = !state.ConsoleMiniOpen;
break;
case "consoleminipos":
state.ConsoleMiniPosition = (state.ConsoleMiniPosition + 1) % 4;
break;
case "staffinv":
state.StaffSubTab = "inventory";
state.StaffInventoryTab = CleanInventoryTab(Get(args, 1, "main"));
state.StaffInventoryDetailOpen = true;
state.StaffInventorySlot = -1;
state.StaffInventoryPage = 0;
break;
case "staffinvback":
state.StaffSubTab = "inventory";
state.StaffInventoryDetailOpen = false;
state.StaffInventorySlot = -1;
state.StaffInventoryPage = 0;
break;
case "staffinvslot":
state.StaffSubTab = "inventory";
state.StaffInventoryTab = CleanInventoryTab(Get(args, 1, state.StaffInventoryTab));
state.StaffInventoryDetailOpen = true;
state.StaffInventorySlot = ToInt(Get(args, 2, "-1"), -1);
break;
case "staffinvpage":
state.StaffSubTab = "inventory";
state.StaffInventoryTab = CleanInventoryTab(Get(args, 1, state.StaffInventoryTab));
state.StaffInventoryDetailOpen = true;
state.StaffInventoryPage = Math.Max(0, state.StaffInventoryPage + ToInt(Get(args, 2, "0")));
state.StaffInventorySlot = -1;
break;
case "skin":
SetPlayerSkin(player, Get(args, 1, "default"));
break;
case "page":
ChangePage(state, Get(args, 1, "current"), ToInt(Get(args, 2, "0")));
break;
case "select":
state.SelectedPlayerId = Get(args, 1, null);
state.Tab = "players";
state.PlayerCupboardPage = 0;
break;
case "staffselect":
state.SelectedPlayerId = Get(args, 1, null);
state.Tab = "staff";
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
case "selectreport":
state.SelectedReportId = ToInt(Get(args, 1, "0"));
state.Tab = "reports";
break;
case "selectchat":
state.SelectedChatId = Get(args, 1, null);
state.Tab = "chat";
break;
case "cyclegroup":
CycleSelectedGroup(state, ToInt(Get(args, 1, "0")));
break;
case "togglegroupdropdown":
state.GroupDropdownOpen = !state.GroupDropdownOpen;
break;
case "togglenotes":
state.NotesOpen = !state.NotesOpen;
break;
case "groupdropdownpage":
state.GroupDropdownPage = Math.Max(0, state.GroupDropdownPage + ToInt(Get(args, 1, "0")));
break;
case "config":
var requestedPlugin = Get(args, 1, null);
state.SelectedConfigPlugin = KnownPluginName(requestedPlugin);
state.Tab = "manage";
state.SubTab = "plugins";
state.ConfigPage = 0;
state.ConfigFilter = string.Empty;
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
if (player == null || !CanEditPluginConfig(player)) return;
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
[ConsoleCommand("liveadmin.mutefield")]
private void MuteFieldCommand(ConsoleSystem.Arg arg)
{
var player = arg.Player();
if (player == null || !CanMute(player)) return;
var args = arg.Args == null ? new string[0] : arg.Args.Select(a => a.ToString()).ToArray();
if (args.Length == 0) return;
var state = GetState(player);
var field = args[0].ToLowerInvariant();
var value = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()).Trim() : string.Empty;
if (field == "duration")
{
state.MuteDuration = string.IsNullOrEmpty(value) ? "10m" : value;
}
else if (field == "reason")
{
state.MuteReason = string.IsNullOrEmpty(value) ? "Staff action" : value;
}
Draw(player);
}
[ConsoleCommand("liveadmin.chatfield")]
private void ChatFieldCommand(ConsoleSystem.Arg arg)
{
var player = arg.Player();
if (player == null || !CanAny(player, PermOwner, PermChatManage)) return;
var args = arg.Args == null ? new string[0] : arg.Args.Select(a => a.ToString()).ToArray();
if (args.Length == 0) return;
var state = GetState(player);
var field = args[0].ToLowerInvariant();
var value = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()).Trim() : string.Empty;
if (field == "reply") state.ChatReplyText = value;
else if (field == "blacklist") state.ChatBlacklistText = value;
else if (field == "timeout") state.ChatTimeoutText = string.IsNullOrEmpty(value) ? config.ChatTimeoutDuration : value;
Draw(player);
}
[ConsoleCommand("liveadmin.consolefield")]
private void ConsoleFieldCommand(ConsoleSystem.Arg arg)
{
var player = arg.Player();
if (player == null || !CanUseQuickActions(player)) return;
var args = arg.Args == null ? new string[0] : arg.Args.Select(a => a.ToString()).ToArray();
var state = GetState(player);
state.ConsoleCommandText = args.Length > 0 ? string.Join(" ", args).Trim() : string.Empty;
Draw(player);
}
[ConsoleCommand("liveadmin.convarfield")]
private void ConVarFieldCommand(ConsoleSystem.Arg arg)
{
var player = arg.Player();
if (player == null || !CanEditServerInfo(player)) return;
var args = arg.Args == null ? new string[0] : arg.Args.Select(a => a.ToString()).ToArray();
if (args.Length == 0) return;
var state = GetState(player);
var field = args[0].ToLowerInvariant();
var value = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()).Trim() : string.Empty;
if (field == "maxplayers") state.ConVarMaxPlayers = value;
else if (field == "saveinterval") state.ConVarSaveInterval = value;
else if (field == "hostname") state.ConVarHostname = value;
else if (field == "description") state.ConVarDescription = value;
else if (field == "url") state.ConVarUrl = value;
Draw(player);
}
[ConsoleCommand("liveadmin.notefield")]
private void NoteFieldCommand(ConsoleSystem.Arg arg)
{
var player = arg.Player();
if (player == null || !CanAny(player, PermOwner, PermPlayersModerate, PermPlayersNotes)) return;
var args = arg.Args == null ? new string[0] : arg.Args.Select(a => a.ToString()).ToArray();
var state = GetState(player);
state.NoteText = args.Length > 0 ? string.Join(" ", args).Trim() : string.Empty;
Draw(player);
}
[ConsoleCommand("liveadmin.ticketnote")]
private void TicketNoteCommand(ConsoleSystem.Arg arg)
{
var player = arg.Player();
if (player == null || !CanManageTickets(player)) return;
var args = arg.Args == null ? new string[0] : arg.Args.Select(a => a.ToString()).ToArray();
var state = GetState(player);
state.TicketNote = args.Length > 0 ? string.Join(" ", args).Trim() : string.Empty;
Draw(player);
}
[ConsoleCommand("liveadmin.noop")]
private void NoopCommand(ConsoleSystem.Arg arg)
{
}
[ConsoleCommand("liveadmin.stafffield")]
private void StaffFieldCommand(ConsoleSystem.Arg arg)
{
var player = arg.Player();
if (player == null || !CanSeeTab(player, "staff")) return;
var args = arg.Args == null ? new string[0] : arg.Args.Select(a => a.ToString()).ToArray();
if (args.Length == 0) return;
var state = GetState(player);
var field = args[0].ToLowerInvariant();
var value = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()).Trim() : string.Empty;
if (field == "reason") state.StaffReason = string.IsNullOrEmpty(value) ? "Staff action" : value;
else if (field == "message") state.StaffMessage = string.IsNullOrEmpty(value) ? "Please follow the server rules." : value;
else if (field == "item") state.StaffItemShortname = string.IsNullOrEmpty(value) ? "syringe.medical" : value;
else if (field == "amount") state.StaffItemAmount = string.IsNullOrEmpty(value) ? "1" : value;
Draw(player);
}
[ConsoleCommand("liveadmin.serverfield")]
private void ServerFieldCommand(ConsoleSystem.Arg arg)
{
var player = arg.Player();
if (player == null || !CanEditServerInfo(player)) return;
var args = arg.Args == null ? new string[0] : arg.Args.Select(a => a.ToString()).ToArray();
if (args.Length == 0) return;
var state = GetState(player);
var field = args[0].ToLowerInvariant();
var value = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()).Trim() : string.Empty;
if (field == "name") state.ServerName = value;
else if (field == "description") state.ServerDescription = value;
else if (field == "url") state.ServerUrl = value;
else if (field == "headerimage") state.ServerHeaderImage = value;
Draw(player);
}
[ConsoleCommand("liveadmin.wipefield")]
private void WipeFieldCommand(ConsoleSystem.Arg arg)
{
var player = arg.Player();
if (player == null || !CanManageWipe(player)) return;
var args = arg.Args == null ? new string[0] : arg.Args.Select(a => a.ToString()).ToArray();
if (args.Length == 0) return;
var field = args[0].ToLowerInvariant();
var value = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()).Trim() : string.Empty;
if (field == "interval")
{
config.AutoWipeIntervalDays = Math.Max(1, Math.Min(365, ToInt(value, config.AutoWipeIntervalDays)));
storedData.NextAutoWipeUtc = CalculateNextAutoWipe(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss");
}
else if (field == "cadence")
{
var cadence = NormalizeWipeCadence(value, config.AutoWipeUseWeeklySchedule);
if (!cadence.Equals(value ?? string.Empty, StringComparison.OrdinalIgnoreCase))
{
Reply(player, "Cadence must be Weekly, TwiceWeekly, or Interval.");
LogFailed(player, "Wipe", "WipeField", field, string.Empty, value);
return;
}
config.AutoWipeCadence = cadence;
config.AutoWipeUseWeeklySchedule = cadence != "Interval";
storedData.NextAutoWipeUtc = CalculateNextAutoWipe(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss");
}
else if (field == "time")
{
if (!IsValidUtcTime(value))
{
Reply(player, "UTC time must be HH:mm.");
LogFailed(player, "Wipe", "WipeField", field, string.Empty, value);
return;
}
config.AutoWipeTimeUtc = string.IsNullOrEmpty(value) ? "17:00" : value;
storedData.NextAutoWipeUtc = CalculateNextAutoWipe(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss");
}
else if (field == "weekday")
{
if (!TryParseWeekday(value, out var weekday))
{
Reply(player, "Wipe day must be a weekday, for example Thursday.");
LogFailed(player, "Wipe", "WipeField", field, string.Empty, value);
return;
}
config.AutoWipeWeekday = weekday.ToString();
storedData.NextAutoWipeUtc = CalculateNextAutoWipe(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss");
}
else if (field == "secondweekday")
{
if (!TryParseWeekday(value, out var weekday))
{
Reply(player, "Second wipe day must be a weekday, for example Monday.");
LogFailed(player, "Wipe", "WipeField", field, string.Empty, value);
return;
}
if (weekday == GetConfiguredWipeWeekday())
{
Reply(player, "The two wipe weekdays must be different.");
LogFailed(player, "Wipe", "WipeField", field, string.Empty, value);
return;
}
config.AutoWipeSecondWeekday = weekday.ToString();
storedData.NextAutoWipeUtc = CalculateNextAutoWipe(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss");
}
else if (field == "seed")
{
if (!IsSafeWipeSeed(value))
{
Reply(player, "Seed must be random or a numeric seed.");
LogFailed(player, "Wipe", "WipeField", field, string.Empty, value);
return;
}
config.AutoWipeSeed = string.IsNullOrEmpty(value) ? "random" : value;
}
else if (field == "size")
{
config.AutoWipeMapSize = Math.Max(1000, Math.Min(6000, ToInt(value, config.AutoWipeMapSize)));
}
else if (field == "mapurl")
{
if (!IsHttpUrlOrEmpty(value))
{
Reply(player, "Custom map URL must start with http:// or https://.");
LogFailed(player, "Wipe", "WipeField", field, string.Empty, value);
return;
}
config.AutoWipeCustomMapUrl = value;
}
else if (field == "announcement")
{
config.AutoWipeAnnouncement = value;
}
else if (field == "autodiscord")
{
config.AutoWipeDiscordCommandText = value;
config.AutoWipeDiscordCommands = SplitConfigList(value, string.Empty);
}
else if (field == "forcediscord")
{
config.ForceWipeDiscordCommandText = value;
config.ForceWipeDiscordCommands = SplitConfigList(value, string.Empty);
}
else if (field == "autocommands")
{
config.AutoWipeCommandText = value;
config.AutoWipeCommands = SplitConfigList(value, "say Auto wipe command list is not configured.");
}
else if (field == "forcecommands")
{
config.ForceWipeCommandText = value;
config.ForceWipeCommands = SplitConfigList(value, "say Force wipe command list is not configured.");
}
else if (field == "mappaths")
{
var paths = SplitConfigList(value, "server/{identity}/save/*.sav");
if (paths.Any(p => !IsSafeWipePattern(p)))
{
Reply(player, "Map delete paths must stay under server/{identity}/ and cannot use rooted or parent paths.");
LogFailed(player, "Wipe", "WipeField", field, string.Empty, value);
return;
}
config.MapWipeDeletePaths = paths;
config.MapWipeDeletePathText = string.Join("||", paths);
}
else if (field == "bppaths")
{
var paths = SplitConfigList(value, "server/{identity}/player.blueprints*.db");
if (paths.Any(p => !IsSafeWipePattern(p)))
{
Reply(player, "Blueprint delete paths must stay under server/{identity}/ and cannot use rooted or parent paths.");
LogFailed(player, "Wipe", "WipeField", field, string.Empty, value);
return;
}
config.BlueprintWipeDeletePaths = paths;
config.BlueprintWipeDeletePathText = string.Join("||", paths);
}
NormalizeConfigLists();
SaveConfig();
SaveData();
RefreshWipeViews(player);
}
[ConsoleCommand("liveadmin.managefield")]
private void ManageFieldCommand(ConsoleSystem.Arg arg)
{
var player = arg.Player();
if (player == null || !CanManageStaffGroups(player)) return;
var args = arg.Args == null ? new string[0] : arg.Args.Select(a => a.ToString()).ToArray();
if (args.Length == 0) return;
var state = GetState(player);
var field = args[0].ToLowerInvariant();
var value = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()).Trim() : string.Empty;
if (field == "groupname") state.GroupCreateName = value.ToLowerInvariant();
else if (field == "grouptitle") state.GroupCreateTitle = value;
else if (field == "grouprank") state.GroupCreateRank = value;
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
if (type == "quick" && args.Length > 2)
{
target = string.Join(" ", args.Skip(2).ToArray()).Trim('"');
}
if ((type == "chatreply" || type == "chatwarn") && args.Length > 3)
{
value = string.Join(" ", args.Skip(3).ToArray()).Trim();
}
var state = GetState(player);
switch (type)
{
case "staffduty":
if (!CanAny(player, PermOwner, PermStaffToolsView, PermStaffToolsActions)) return;
if (!staffOnDuty.Add(player.userID)) staffOnDuty.Remove(player.userID);
LogSuccess(player, "Staff", staffOnDuty.Contains(player.userID) ? "DutyStarted" : "DutyEnded", player.UserIDString, player.displayName, "Staff duty toggle");
RefreshOpenPanels("staff");
RefreshOpenPanels("dashboard");
break;
case "kick":
if (!CanKick(player) || BlockedBySafeMode(player, "kick", "Moderation") || IsRateLimited(player, "kick:" + target, 2.0) || !CanTargetPlayer(player, target, "kick")) return;
RequireConfirm(player, "kick", target, "Staff action", $"Kick {NameOf(target)}?");
break;
case "ban":
if (!CanBan(player) || BlockedBySafeMode(player, "ban", "Moderation") || IsRateLimited(player, "ban:" + target, 4.0) || !CanTargetPlayer(player, target, "ban")) return;
RequireConfirm(player, "ban", target, "Banned by staff", $"Ban {NameOf(target)}?");
break;
case "tpto":
if (!Can(player, PermPlayersTeleport)) return;
if (IsRateLimited(player, "tpto:" + target, 1.0) || !CanTargetPlayer(player, target, "teleport")) return;
var targetPlayer = BasePlayer.FindByID(ToUlong(target)) ?? BasePlayer.FindSleeping(ToUlong(target));
if (targetPlayer != null)
{
player.Teleport(targetPlayer.transform.position);
Log(player, "TeleportToPlayer", targetPlayer.UserIDString, targetPlayer.displayName);
}
break;
case "tptc":
if (!Can(player, PermPlayersTeleport) || IsRateLimited(player, "tptc:" + target, 1.0)) return;
var cupboards = GetPlayerCupboards(ToUlong(target));
var cupboardIndex = ToInt(value, -1);
if (cupboardIndex < 0 || cupboardIndex >= cupboards.Count)
{
Reply(player, "That tool cupboard is no longer available. Refresh the player view.");
return;
}
var cupboard = cupboards[cupboardIndex];
player.Teleport(cupboard.transform.position + Vector3.up * 1.5f);
Log(player, "TeleportToCupboard", target, $"TC {cupboardIndex + 1} at {FormatPosition(cupboard.transform.position)}");
break;
case "bring":
if (!Can(player, PermPlayersTeleport)) return;
if (IsRateLimited(player, "bring:" + target, 1.0) || !CanTargetPlayer(player, target, "bring")) return;
var bringPlayer = BasePlayer.FindByID(ToUlong(target)) ?? BasePlayer.FindSleeping(ToUlong(target));
if (bringPlayer != null)
{
bringPlayer.Teleport(player.transform.position);
Log(player, "BringPlayer", bringPlayer.UserIDString, bringPlayer.displayName);
}
break;
case "claimreport":
if (!CanManageTickets(player)) return;
UpdateReport(ToInt(target), "Claimed", player.displayName);
Log(player, "ReportClaimed", target, string.Empty);
break;
case "resolvereport":
if (!CanManageTickets(player)) return;
UpdateReport(ToInt(target), "Resolved", player.displayName);
Log(player, "ReportResolved", target, string.Empty);
break;
case "reportstatus":
if (!CanManageTickets(player)) return;
UpdateReport(ToInt(target), value, player.displayName);
Log(player, "ReportStatus", target, value);
break;
case "reportpriority":
if (!CanManageTickets(player)) return;
SetReportPriority(player, ToInt(target), value);
break;
case "ticketnote":
if (!CanManageTickets(player)) return;
AddTicketNote(player, ToInt(target), state.TicketNote);
state.TicketNote = string.Empty;
break;
case "deletenote":
if (!CanAny(player, PermOwner, PermPlayersModerate, PermPlayersNotes)) return;
DeletePlayerNote(player, target, ToInt(value, -1));
break;
case "deleteticket":
if (!CanManageTickets(player) || IsRateLimited(player, "deleteticket:" + target, 2.0)) return;
RequireConfirm(player, "deleteticket", target, string.Empty, $"Delete ticket #{target}?");
break;
case "chatdelete":
if (!CanManageChat(player) || IsRateLimited(player, "chatdelete:" + target, 1.0)) return;
DeleteChatEntry(player, target);
break;
case "chatrestore":
if (!CanManageChat(player) || IsRateLimited(player, "chatrestore:" + target, 1.0)) return;
RestoreChatEntry(player, target);
break;
case "chatwarn":
if (!CanManageChat(player) || IsRateLimited(player, "chatwarn:" + target, 1.0)) return;
WarnChatPlayer(player, target, value);
break;
case "chattimeout":
if (!CanManageChat(player) || IsRateLimited(player, "chattimeout:" + target, 2.0)) return;
TimeoutChatPlayer(player, target);
break;
case "chatreply":
if (!CanManageChat(player) || IsRateLimited(player, "chatreply:" + target, 1.0)) return;
ReplyToChatPlayer(player, target, value);
break;
case "chatblacklistadd":
if (!CanManageChat(player) || BlockedBySafeMode(player, "chatblacklistadd", "Chat")) return;
AddChatBlacklistWord(player, string.IsNullOrEmpty(value) ? state.ChatBlacklistText : value);
state.ChatBlacklistText = string.Empty;
break;
case "chatblacklistremove":
if (!CanManageChat(player) || BlockedBySafeMode(player, "chatblacklistremove", "Chat")) return;
RemoveChatBlacklistWord(player, target);
break;
case "plugin":
if (!CanReloadPlugins(player) || BlockedBySafeMode(player, "plugin", "Plugin") || IsRateLimited(player, "plugin:" + target + ":" + value, 2.0)) return;
RequireConfirm(player, $"plugin:{value}", target, string.Empty, $"{value} plugin {target}?");
break;
case "configrestore":
if (!CanRestorePluginConfig(player) || BlockedBySafeMode(player, "configrestore", "Config") || IsRateLimited(player, "configrestore:" + target, 3.0)) return;
RequireConfirm(player, "configrestore", target, string.Empty, $"Restore the latest saved config backup for {target}? The current config will be backed up first.");
break;
case "groupadd":
if (!CanManageStaffGroups(player) || BlockedBySafeMode(player, "groupadd", "Group") || IsRateLimited(player, "groupadd:" + target + ":" + value, 2.0) || !CanTargetPlayer(player, target, "groupadd")) return;
RequireConfirm(player, "groupadd", target, value, $"Add {NameOf(target)} to group {value}?");
break;
case "groupremove":
if (!CanManageStaffGroups(player) || BlockedBySafeMode(player, "groupremove", "Group") || IsRateLimited(player, "groupremove:" + target + ":" + value, 2.0) || !CanTargetPlayer(player, target, "groupremove")) return;
RequireConfirm(player, "groupremove", target, value, $"Remove {NameOf(target)} from group {value}?");
break;
case "grantgroup":
if (!Can(player, PermPermissionsManage) || BlockedBySafeMode(player, "grantgroup", "Permission") || IsRateLimited(player, "grantgroup:" + target + ":" + value, 2.0)) return;
RequireConfirm(player, "grantgroup", target, value, $"Grant {value} to group {target}?");
break;
case "revokegroup":
if (!Can(player, PermPermissionsManage) || BlockedBySafeMode(player, "revokegroup", "Permission") || IsRateLimited(player, "revokegroup:" + target + ":" + value, 2.0)) return;
RequireConfirm(player, "revokegroup", target, value, $"Revoke {value} from group {target}?");
break;
case "applypreset":
if (!Can(player, PermPermissionsManage) || BlockedBySafeMode(player, "applypreset", "Permission") || IsRateLimited(player, "applypreset:" + target + ":" + value, 3.0)) return;
RequireConfirm(player, "applypreset", target, value, $"Apply {value} preset permissions to group {target}?");
break;
case "note":
if (!CanAny(player, PermOwner, PermPlayersModerate, PermPlayersNotes)) return;
var noteTarget = BasePlayer.FindByID(ToUlong(target));
var noteText = CleanCommandText(state.NoteText);
if (noteTarget != null && !string.IsNullOrEmpty(noteText))
{
AddNote(player, noteTarget.UserIDString, noteText);
state.NoteText = string.Empty;
}
else if (string.IsNullOrEmpty(noteText))
{
Reply(player, "Enter note text first.");
}
break;
case "serverinfo":
if (!CanEditServerInfo(player) || IsRateLimited(player, "serverinfo", 2.0)) return;
ApplyServerInfo(player);
break;
case "consolecommand":
if (!CanUseQuickActions(player) || BlockedBySafeMode(player, "consolecommand", "Server") || IsRateLimited(player, "consolecommand", 1.5)) return;
var consoleCommand = CleanCommandText(state.ConsoleCommandText);
if (string.IsNullOrEmpty(consoleCommand)) return;
RequireConfirm(player, "command", consoleCommand, string.Empty, $"Run console command: {consoleCommand}?");
break;
case "consoleexport":
if (!CanUseQuickActions(player) || IsRateLimited(player, "consoleexport", 3.0)) return;
ExportConsoleLog(player);
break;
case "groupcreate":
if (!CanManageStaffGroups(player) || BlockedBySafeMode(player, "groupcreate", "Group") || IsRateLimited(player, "groupcreate", 2.0)) return;
target = state.GroupCreateName;
value = state.GroupCreateTitle + "||" + state.GroupCreateRank;
RequireConfirm(player, "groupcreate", target, value, $"Create staff group {target}?");
break;
case "groupdelete":
if (!CanManageStaffGroups(player) || BlockedBySafeMode(player, "groupdelete", "Group") || IsRateLimited(player, "groupdelete:" + target, 2.0)) return;
RequireConfirm(player, "groupdelete", target, string.Empty, $"Permanently remove group {target}? Users will lose its inherited permissions.");
break;
case "consoleclear":
if (!CanUseQuickActions(player)) return;
liveConsoleBuffer.Clear();
LogSuccess(player, "Server", "ConsoleBufferCleared", "console", "Runtime console", "Live buffer cleared");
break;
case "convarsapply":
if (!CanEditServerInfo(player) || BlockedBySafeMode(player, "convarsapply", "Server") || IsRateLimited(player, "convarsapply", 2.0)) return;
ApplyConVars(player);
break;
case "settingssafe":
if (!Can(player, PermOwner)) return;
config.SafeMode = !config.SafeMode;
SaveConfig();
break;
case "settingscommandsafety":
if (!Can(player, PermOwner)) return;
config.EnableCommandSafety = !config.EnableCommandSafety;
SaveConfig();
break;
case "settingschatannounce":
if (!Can(player, PermOwner)) return;
config.AllowGlobalChatAnnouncements = !config.AllowGlobalChatAnnouncements;
SaveConfig();
break;
case "maintenancetoggle":
if (!Can(player, PermServerMaintenance) || BlockedBySafeMode(player, "maintenancetoggle", "Server")) return;
RequireConfirm(player, "maintenancetoggle", string.Empty, string.Empty, storedData.MaintenanceEnabled ? "Disable maintenance mode and reopen the server?" : "Enable maintenance mode and block regular player joins?");
break;
case "restart5":
if (!Can(player, PermServerRestart) || BlockedBySafeMode(player, "restart5", "Server")) return;
RequireConfirm(player, "restart5", string.Empty, string.Empty, $"Schedule a server restart in {config.RestartDefaultMinutes} minutes?");
break;
case "restartcancel":
if (!Can(player, PermServerRestart)) return;
CancelScheduledRestart(player);
break;
case "wipetoggle":
if (!CanManageWipe(player) || BlockedBySafeMode(player, "wipetoggle", "Wipe")) return;
config.AutoWipeEnabled = !config.AutoWipeEnabled;
SaveConfig();
RefreshWipeViews(player);
break;
case "wipemap":
if (!CanManageWipe(player) || BlockedBySafeMode(player, "wipemap", "Wipe")) return;
config.AutoWipeMap = !config.AutoWipeMap;
SaveConfig();
RefreshWipeViews(player);
break;
case "wipebp":
if (!CanManageWipe(player) || BlockedBySafeMode(player, "wipebp", "Wipe")) return;
config.AutoWipeBlueprints = !config.AutoWipeBlueprints;
SaveConfig();
RefreshWipeViews(player);
break;
case "wipeforcebp":
if (!CanForceWipe(player) || BlockedBySafeMode(player, "wipeforcebp", "Wipe")) return;
config.ForceWipeBlueprints = !config.ForceWipeBlueprints;
SaveConfig();
RefreshWipeViews(player);
break;
case "wipeschedule":
if (!CanManageWipe(player) || BlockedBySafeMode(player, "wipeschedule", "Wipe")) return;
config.AutoWipeCadence = NextWipeCadence(config.AutoWipeCadence);
config.AutoWipeUseWeeklySchedule = config.AutoWipeCadence != "Interval";
storedData.NextAutoWipeUtc = CalculateNextAutoWipe(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss");
SaveConfig();
SaveData();
RefreshWipeViews(player);
break;
case "wipefirstforce":
if (!CanForceWipe(player) || BlockedBySafeMode(player, "wipefirstforce", "Wipe")) return;
config.AutoWipeForceFirstWeekday = !config.AutoWipeForceFirstWeekday;
storedData.NextAutoWipeUtc = CalculateNextAutoWipe(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss");
SaveConfig();
SaveData();
RefreshWipeViews(player);
break;
case "wipedryrun":
if (!CanForceWipe(player) || BlockedBySafeMode(player, "wipedryrun", "Wipe")) return;
config.ForceWipeDryRunDefault = !config.ForceWipeDryRunDefault;
SaveConfig();
RefreshWipeViews(player);
break;
case "wipedelete":
if (!CanForceWipe(player) || BlockedBySafeMode(player, "wipedelete", "Wipe")) return;
config.ForceWipeOfflineCleanup = !config.ForceWipeOfflineCleanup;
SaveConfig();
RefreshWipeViews(player);
break;
case "wipereset":
if (!CanManageWipe(player)) return;
storedData.NextAutoWipeUtc = CalculateNextAutoWipe(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss");
SaveData();
RefreshWipeViews(player);
break;
case "forcewipe":
if (!CanForceWipe(player) || BlockedBySafeMode(player, "forcewipe", "Wipe") || IsRateLimited(player, "forcewipe", 5.0)) return;
RequireConfirm(player, "forcewipe", "manual", string.Empty, config.ForceWipeOfflineCleanup ? "Force wipe now using offline map and blueprint cleanup?" : "Force wipe now without offline cleanup?");
break;
case "wipepreview":
if (!CanViewWipe(player)) return;
if (target == "files") PreviewWipeFilesOnly(player);
else PreviewWipe(player, target == "force" || target == "forcecommands" ? "ForceWipe" : "AutoWipe");
RefreshWipeViews(player);
break;
case "mute":
if (!CanMute(player) || IsRateLimited(player, "mute:" + target, 2.0) || !CanTargetPlayer(player, target, "mute")) return;
RequireConfirm(player, "mute", target, string.Empty, $"Mute {NameOf(target)} for {state.MuteDuration}: {state.MuteReason}?");
break;
case "playercmd":
var playerAction = FindPlayerAction(player, value);
if (playerAction == null || !Can(player, playerAction.Permission)) return;
if (!CanTargetPlayer(player, target, playerAction.Label)) return;
if (playerAction.Confirm) RequireConfirm(player, "playercmd", target, value, $"Run {playerAction.Label} on {NameOf(target)}?");
else RunPlayerAction(player, target, playerAction);
break;
case "quick":
if (!CanUseQuickActions(player) || BlockedBySafeMode(player, "quick", "Server")) return;
var quick = config.QuickCommands.FirstOrDefault(q => q.Label.Equals(target, StringComparison.OrdinalIgnoreCase));
if (quick == null || !Can(player, quick.Permission)) return;
if (quick.Confirm) RequireConfirm(player, "quick", quick.Label, string.Empty, $"Run {quick.Label}?");
else RunServerCommand(player, quick.Command, quick.Label);
break;
case "staffact":
if (!CanUseStaffAction(player, value)) return;
if (RequiresStaffConfirm(value)) RequireConfirm(player, "staffact", target, value, $"{Title(value)} {NameOf(target)}?");
else RunStaffAction(player, target, value);
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
if (action == "maintenancetoggle" && Can(player, PermServerMaintenance) && !BlockedBySafeMode(player, action, "Server"))
{
SetMaintenance(player, !storedData.MaintenanceEnabled, string.Empty);
}
else if (action == "restart5" && Can(player, PermServerRestart) && !BlockedBySafeMode(player, action, "Server"))
{
ScheduleRestart(player, config.RestartDefaultMinutes, config.RestartDefaultReason);
}
else if (action == "kick" && CanKick(player) && !BlockedBySafeMode(player, "kick", "Moderation") && CanTargetPlayer(player, target, "kick"))
{
var targetPlayer = BasePlayer.FindByID(ToUlong(target)) ?? BasePlayer.FindSleeping(ToUlong(target));
if (targetPlayer != null && targetPlayer.userID == player.userID)
{
Reply(player, "LiveAdmin will not kick your own active session from the panel.");
return;
}
targetPlayer?.Kick(value);
Log(player, "Kick", target, value);
}
else if (action == "ban" && CanBan(player) && !BlockedBySafeMode(player, "ban", "Moderation") && CanTargetPlayer(player, target, "ban"))
{
var targetUserId = ToUlong(target);
var targetPlayer = BasePlayer.FindByID(targetUserId) ?? BasePlayer.FindSleeping(targetUserId);
if (targetUserId != 0)
{
if (targetUserId == player.userID)
{
Reply(player, "LiveAdmin will not ban your own active session from the panel.");
return;
}
var targetName = targetPlayer?.displayName ?? NameOf(target);
ServerUsers.Set(targetUserId, ServerUsers.UserGroup.Banned, targetName, value);
if (targetPlayer != null && targetPlayer.IsConnected) targetPlayer.Kick(value);
Log(player, "Ban", target, value);
}
}
else if (action.StartsWith("plugin:") && CanReloadPlugins(player) && !BlockedBySafeMode(player, action, "Plugin"))
{
var verb = action.Substring("plugin:".Length);
RunServerCommand(player, $"oxide.{verb} {target}", $"{verb} plugin {target}");
RefreshOpenPanels("manage");
RefreshOpenPanels("dashboard");
}
else if (action == "configrestore" && CanRestorePluginConfig(player) && !BlockedBySafeMode(player, action, "Config"))
{
RestoreLatestPluginConfig(player, target);
RefreshOpenPanels("manage");
}
else if (action == "groupadd" && CanManageStaffGroups(player) && !BlockedBySafeMode(player, "groupadd", "Group") && CanTargetPlayer(player, target, "groupadd"))
{
permission.AddUserGroup(target, value);
Log(player, "GroupAdd", target, value);
}
else if (action == "groupcreate" && CanManageStaffGroups(player) && !BlockedBySafeMode(player, action, "Group"))
{
var parts = (value ?? string.Empty).Split(new[] { "||" }, StringSplitOptions.None);
var title = parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]) ? parts[0].Trim() : target;
var rank = parts.Length > 1 ? ToInt(parts[1], 0) : 0;
if (!IsValidGroupName(target) || permission.GroupExists(target))
{
Reply(player, "Enter a valid, unique group name using letters, numbers, dots, underscores, or hyphens.");
return;
}
permission.CreateGroup(target, title, rank);
state.GroupCreateName = string.Empty;
state.GroupCreateTitle = string.Empty;
state.GroupCreateRank = "0";
Log(player, "GroupCreate", target, $"Title={title}; Rank={rank}");
RefreshOpenPanels("manage");
}
else if (action == "groupdelete" && CanManageStaffGroups(player) && !BlockedBySafeMode(player, action, "Group"))
{
if (!permission.GroupExists(target) || config.ProtectedGroups.Any(g => g.Equals(target, StringComparison.OrdinalIgnoreCase)))
{
Reply(player, "That group does not exist or is protected from removal.");
return;
}
permission.RemoveGroup(target);
Log(player, "GroupDelete", target, string.Empty);
RefreshOpenPanels("manage");
}
else if (action == "groupremove" && CanManageStaffGroups(player) && !BlockedBySafeMode(player, "groupremove", "Group") && CanTargetPlayer(player, target, "groupremove"))
{
permission.RemoveUserGroup(target, value);
Log(player, "GroupRemove", target, value);
}
else if (action == "grantgroup" && Can(player, PermPermissionsManage) && !BlockedBySafeMode(player, "grantgroup", "Permission"))
{
if (!IsValidGroupName(target) || !IsValidPermissionName(value))
{
Reply(player, "Invalid group or permission name.");
LogFailed(player, "Permission", "GrantGroupPermission", target, string.Empty, value);
return;
}
if (IsDangerousPermission(value) && !Can(player, PermOwner) && !IsAuthLevel2(player))
{
Reply(player, "Only owners or auth level 2 users can grant wildcard or high-risk permissions.");
return;
}
RunServerCommand(player, $"oxide.grant group {ConsoleArg(target)} {ConsoleArg(value)}", $"grant {value} to {target}");
Log(player, "GrantGroupPermission", target, value);
RefreshOpenPanels("manage");
}
else if (action == "revokegroup" && Can(player, PermPermissionsManage) && !BlockedBySafeMode(player, "revokegroup", "Permission"))
{
if (!IsValidGroupName(target) || !IsValidPermissionName(value))
{
Reply(player, "Invalid group or permission name.");
LogFailed(player, "Permission", "RevokeGroupPermission", target, string.Empty, value);
return;
}
RunServerCommand(player, $"oxide.revoke group {ConsoleArg(target)} {ConsoleArg(value)}", $"revoke {value} from {target}");
Log(player, "RevokeGroupPermission", target, value);
RefreshOpenPanels("manage");
}
else if (action == "applypreset" && Can(player, PermPermissionsManage) && !BlockedBySafeMode(player, "applypreset", "Permission"))
{
ApplyRolePresetToGroup(player, target, value);
}
else if (action == "mute" && CanMute(player) && CanTargetPlayer(player, target, "mute"))
{
RunMuteAction(player, target);
}
else if (action == "playercmd")
{
var playerAction = FindPlayerAction(player, value);
if (playerAction != null && Can(player, playerAction.Permission))
{
RunPlayerAction(player, target, playerAction);
}
}
else if (action == "quick" && CanUseQuickActions(player) && !BlockedBySafeMode(player, "quick", "Server"))
{
var quick = config.QuickCommands.FirstOrDefault(q => q.Label.Equals(target, StringComparison.OrdinalIgnoreCase));
if (quick != null && Can(player, quick.Permission))
{
RunServerCommand(player, quick.Command, quick.Label);
}
}
else if (action == "command" && CanUseQuickActions(player) && !BlockedBySafeMode(player, "command", "Server"))
{
RunServerCommand(player, target, target);
}
else if (action == "forcewipe" && CanForceWipe(player) && !BlockedBySafeMode(player, "forcewipe", "Wipe"))
{
RunWipeCommands(player, "ForceWipe", config.ForceWipeCommands);
if (config.ForceWipeDryRunDefault)
{
RefreshWipeViews(player);
return;
}
storedData.LastWipeUtc = Now();
storedData.NextAutoWipeUtc = CalculateNextAutoWipe(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss");
SaveData();
RefreshWipeViews(player);
}
else if (action == "deleteticket" && CanManageTickets(player))
{
DeleteTicket(player, ToInt(target));
}
else if (action == "staffact" && CanUseStaffAction(player, value))
{
RunStaffAction(player, target, value);
}
SaveData();
}
private void Draw(BasePlayer player)
{
CuiHelper.DestroyUi(player, UiRoot);
openPanels.Add(player.userID);
var state = GetState(player);
activeSkin = GetPlayerSkin(player);
var container = new CuiElementContainer();
Panel(container, UiRoot, "Overlay", "Overlay", "0.12 0.08", "0.88 0.92");
Panel(container, $"{UiRoot}.FrameTop", UiRoot, activeSkin.Accent, "0 0.996", "1 1");
Panel(container, $"{UiRoot}.FrameTopAlt", UiRoot, activeSkin.AccentAlt, "0.72 0.996", "1 1");
Panel(container, $"{UiRoot}.FrameLeft", UiRoot, "0.12 0.13 0.14 0.85", "0 0", "0.002 1");
Panel(container, $"{UiRoot}.FrameRight", UiRoot, "0.12 0.13 0.14 0.85", "0.998 0", "1 1");
Panel(container, $"{UiRoot}.Top", UiRoot, "0.08 0.09 0.10 0.98", "0 0.91", "1 0.996");
Panel(container, $"{UiRoot}.Top.Brand", $"{UiRoot}.Top", activeSkin.Accent, "0.014 0.19", "0.052 0.81");
Label(container, $"{UiRoot}.Top.BrandText", $"{UiRoot}.Top.Brand", "LA", 10, "0 0", "1 1", TextAnchor.MiddleCenter);
Panel(container, $"{UiRoot}.Top.BrandStatus", $"{UiRoot}.Top", activeSkin.AccentAlt, "0.047 0.68", "0.056 0.82");
Label(container, $"{UiRoot}.Top.Title", $"{UiRoot}.Top", config.Title, 13, "0.065 0.42", "0.32 0.82", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Top.Subtitle", $"{UiRoot}.Top", "RUST SERVER CONTROL CENTER", 7, "0.065 0.15", "0.32 0.43", TextAnchor.MiddleLeft);
Panel(container, $"{UiRoot}.Top.Accent", $"{UiRoot}.Top", activeSkin.Accent, "0 0", "0.72 0.018");
Panel(container, $"{UiRoot}.Top.AccentAlt", $"{UiRoot}.Top", activeSkin.AccentAlt, "0.72 0", "1 0.018");
DrawTopSearch(container, state);
DrawSkinSelector(container, player);
Button(container, $"{UiRoot}.Top.Close", $"{UiRoot}.Top", "X", "liveadmin.ui close", config.DangerColor, "0.958 0.24", "0.985 0.76", 12);
Panel(container, $"{UiRoot}.Nav", UiRoot, "0.06 0.065 0.075 0.98", "0 0", "0.195 0.91");
DrawNav(container, player, state);
Panel(container, $"{UiRoot}.Nav.Edge", $"{UiRoot}.Nav", "0.16 0.17 0.18 0.70", "0.994 0", "1 1");
Panel(container, $"{UiRoot}.Body", UiRoot, "0.04 0.045 0.052 0.92", "0.195 0.05", "0.988 0.895");
Panel(container, $"{UiRoot}.Footer", UiRoot, "0.08 0.09 0.10 0.96", "0.195 0.006", "0.988 0.04");
Label(container, $"{UiRoot}.Footer.Text", $"{UiRoot}.Footer", FooterText(player, state), 12, "0.015 0", "0.98 1", TextAnchor.MiddleLeft);
if (!string.IsNullOrEmpty(state.PendingAction)) DrawConfirm(container, state);
else if (state.Tab == "dashboard") DrawDashboard(container, player);
else if (state.Tab == "console") DrawLiveConsole(container, player, state);
else if (state.Tab == "players") DrawPlayers(container, player, state);
else if (state.Tab == "staff") DrawStaffTools(container, player, state);
else if (state.Tab == "chat") DrawChat(container, player, state);
else if (state.Tab == "reports") DrawReports(container, player, state);
else if (state.Tab == "manage") DrawManage(container, player, state);
else if (state.Tab == "convars") DrawConVars(container, player, state);
else if (state.Tab == "wipe") DrawWipe(container, player);
else if (state.Tab == "appearance") DrawAppearance(container, player);
else if (state.Tab == "settings") DrawSettings(container, player);
else if (state.Tab == "logs") DrawLogs(container, player, state);
if (state.ConsoleMiniOpen && CanUseQuickActions(player)) DrawMiniConsole(container, state);
RegisterUiBridge(player);
CuiHelper.AddUi(player, container);
}
private void DrawNav(CuiElementContainer container, BasePlayer player, PanelState state)
{
Label(container, $"{UiRoot}.Nav.MainLabel", $"{UiRoot}.Nav", "MAIN", 7, "0.08 0.880", "0.92 0.920", TextAnchor.MiddleLeft);
DrawNavItem(container, player, state, "dashboard", "assets/icons/info.png", "Dashboard", 0.825);
DrawNavItem(container, player, state, "console", "assets/icons/broadcast.png", "Live Console", 0.765);
DrawNavItem(container, player, state, "players", "assets/icons/occupied.png", "Players", 0.705, BasePlayer.activePlayerList.Count.ToString());
DrawNavItem(container, player, state, "chat", "assets/icons/broadcast.png", "Chat", 0.645, ChatBadgeText());
Label(container, $"{UiRoot}.Nav.ToolsLabel", $"{UiRoot}.Nav", "TOOLS", 7, "0.08 0.585", "0.92 0.625", TextAnchor.MiddleLeft);
DrawNavItem(container, player, state, "staff", "assets/icons/authorize.png", "Staff Tools", 0.530);
DrawNavItem(container, player, state, "reports", "assets/icons/warning.png", "Reports", 0.470, storedData.Reports.Count(r => r.Status != "Resolved").ToString());
DrawNavItem(container, player, state, "wipe", "assets/icons/refresh.png", "Wipe", 0.410);
Label(container, $"{UiRoot}.Nav.ManageLabel", $"{UiRoot}.Nav", "MANAGEMENT", 7, "0.08 0.350", "0.92 0.390", TextAnchor.MiddleLeft);
DrawNavItem(container, player, state, "manage", "assets/icons/workshop.png", "Manage", 0.295);
DrawNavItem(container, player, state, "convars", "assets/icons/level_metal.png", "ConVars", 0.235);
DrawNavItem(container, player, state, "appearance", "assets/icons/brush.png", "Appearance", 0.175);
DrawNavItem(container, player, state, "settings", "assets/icons/gear.png", "Settings", 0.115);
DrawNavItem(container, player, state, "logs", "assets/icons/info.png", "Logs", 0.055);
}
private void DrawNavItem(CuiElementContainer container, BasePlayer player, PanelState state, string tab, string iconSprite, string label, double y, string badge = null)
{
if (!CanSeeTab(player, tab)) return;
var selected = state.Tab == tab;
var row = $"{UiRoot}.Nav.{tab}";
Panel(container, row, $"{UiRoot}.Nav", selected ? activeSkin.PanelAlt : "0 0 0 0", $"0.055 {y}", $"0.945 {y + 0.055}");
if (selected) Panel(container, $"{row}.Accent", row, activeSkin.Accent, "0 0.16", "0.012 0.84");
Panel(container, $"{row}.IconTile", row, selected ? activeSkin.Accent : activeSkin.Button, "0.055 0.16", "0.175 0.84");
SpriteImage(container, $"{row}.Icon", $"{row}.IconTile", iconSprite, selected ? "1 1 1 1" : activeSkin.Text, "0.21 0.21", "0.79 0.79");
Button(container, $"{row}.Button", row, label, $"liveadmin.ui tab {tab}", "0 0 0 0", "0.205 0", string.IsNullOrEmpty(badge) ? "0.96 1" : "0.75 1", 9, TextAnchor.MiddleLeft);
if (!string.IsNullOrEmpty(badge))
{
Panel(container, $"{row}.Badge", row, selected ? activeSkin.Accent : activeSkin.AccentAlt, "0.79 0.25", "0.94 0.75");
Label(container, $"{row}.BadgeText", $"{row}.Badge", badge, 7, "0 0", "1 1", TextAnchor.MiddleCenter);
}
}
private void DrawTopSearch(CuiElementContainer container, PanelState state)
{
var key = CurrentFilterKey(state);
var value = CurrentFilterValue(state);
Panel(container, $"{UiRoot}.Top.Search", $"{UiRoot}.Top", activeSkin.Input, "0.70 0.20", "0.942 0.80");
Panel(container, $"{UiRoot}.Top.Search.Accent", $"{UiRoot}.Top.Search", activeSkin.AccentAlt, "0.985 0", "1 1");
Label(container, $"{UiRoot}.Top.Search.Mark", $"{UiRoot}.Top.Search", "SEARCH", 6, "0.035 0", "0.18 1", TextAnchor.MiddleLeft);
Input(container, $"{UiRoot}.Top.Search.Input", $"{UiRoot}.Top.Search", value, $"liveadmin.filter {key}", 8, "0.20 0", "0.96 1");
}
private string CurrentFilterKey(PanelState state)
{
if (state.Tab == "players") return "players";
if (state.Tab == "staff") return state.StaffSubTab == "groups" ? "staffgroups" : "staffplayers";
if (state.Tab == "chat") return "chat";
if (state.Tab == "console") return "console";
if (state.Tab == "reports") return "reports";
if (state.Tab == "convars") return "convars";
if (state.Tab == "manage" && state.SubTab == "groups") return "groups";
if (state.Tab == "manage" && state.SubTab == "permissions") return "permissions";
if (state.Tab == "manage") return "plugins";
return "players";
}
private string CurrentFilterValue(PanelState state)
{
if (state.Tab == "players" || state.Tab == "staff") return state.PlayerFilter;
if (state.Tab == "chat") return state.ChatFilter;
if (state.Tab == "console") return state.ConsoleFilter;
if (state.Tab == "reports") return state.ReportFilter;
if (state.Tab == "convars") return state.ConVarFilter;
if (state.Tab == "manage" && state.SubTab == "groups") return state.GroupFilter;
if (state.Tab == "manage" && state.SubTab == "permissions") return state.PermissionFilter;
if (state.Tab == "manage") return state.PluginFilter;
return string.Empty;
}
private string ChatBadgeText()
{
var count = storedData?.Chat == null ? 0 : storedData.Chat.Count(c => c != null && !c.Deleted);
return count > 99 ? "99+" : count.ToString();
}
private void DrawSkinSelector(CuiElementContainer container, BasePlayer player)
{
var skin = GetPlayerSkin(player);
var x = 0.455;
foreach (var option in GetSkins())
{
var active = option.Key == skin.Key;
Button(container, $"{UiRoot}.Top.Skin.{option.Key}", $"{UiRoot}.Top", option.Name, $"liveadmin.ui skin {option.Key}", active ? option.Accent : option.Button, $"{x} 0.24", $"{x + 0.075} 0.76", 7);
x += 0.080;
}
}
private void DrawDashboard(CuiElementContainer container, BasePlayer player)
{
Header(container, "Dashboard");
var state = GetState(player);
Stat(container, "Players Online", BasePlayer.activePlayerList.Count.ToString(), 0);
Stat(container, "Sleepers", BasePlayer.sleepingPlayerList.Count.ToString(), 1);
Stat(container, "Staff Online", BasePlayer.activePlayerList.Count(p => Can(p, PermUse)).ToString(), 2);
Stat(container, "New Today", NewPlayersToday().ToString(), 3);
if (config.SafeMode)
{
Label(container, $"{UiRoot}.Body.SafeMode", $"{UiRoot}.Body", "Safe Mode: ON", 12, "0.74 0.645", "0.96 0.675", TextAnchor.MiddleRight);
}
DrawResourceMonitor(container);
DrawStaffCommandCenter(container, player);
DrawDashboardActivity(container);
DrawDashboardServerSnapshot(container, player);
}
private void DrawDashboardActivity(CuiElementContainer container)
{
Panel(container, $"{UiRoot}.Body.Activity", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.04 0.08", "0.58 0.34");
Label(container, $"{UiRoot}.Body.Activity.Title", $"{UiRoot}.Body.Activity", "Live Player Activity", 15, "0.04 0.82", "0.55 0.98", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Activity.Counts", $"{UiRoot}.Body.Activity", $"Joining data: {BasePlayer.activePlayerList.Count} active / {BasePlayer.sleepingPlayerList.Count} sleepers", 8, "0.58 0.84", "0.96 0.96", TextAnchor.MiddleRight);
var entries = storedData.Logs
.Where(l => l != null && (l.Category == "Connection" || l.Action == "PlayerJoin" || l.Action == "PlayerLeave"))
.OrderByDescending(l => l.Time)
.Take(5)
.ToList();
var y = 0.66;
if (entries.Count == 0)
{
var online = BasePlayer.activePlayerList.Where(p => p != null).Take(5).ToList();
if (online.Count == 0)
{
Label(container, $"{UiRoot}.Body.Activity.Empty", $"{UiRoot}.Body.Activity", "No join or leave events recorded since the panel loaded.", 10, "0.04 0.38", "0.96 0.54", TextAnchor.MiddleCenter);
return;
}
foreach (var player in online)
{
DrawActivityTextRow(container, $"Online.{player.userID}", "0.09 0.15 0.10 0.88", "ONLINE", player.displayName, $"{player.UserIDString} currently connected", y);
y -= 0.105;
}
return;
}
foreach (var entry in entries)
{
var joined = entry.Action == "PlayerJoin" || (entry.Action ?? string.Empty).IndexOf("connect", StringComparison.OrdinalIgnoreCase) >= 0;
var left = entry.Action == "PlayerLeave" || (entry.Action ?? string.Empty).IndexOf("disconnect", StringComparison.OrdinalIgnoreCase) >= 0;
var targetId = string.IsNullOrEmpty(entry.Target) ? entry.ActorId : entry.Target;
var profile = GetPlayerProfile(targetId);
var name = !string.IsNullOrEmpty(entry.TargetName) ? entry.TargetName : profile?.Name;
if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(entry.ActorName) && entry.ActorName != "Server") name = entry.ActorName;
if (string.IsNullOrEmpty(name)) name = targetId;
var detail = joined ? "Connected" : "Disconnected";
if (!string.IsNullOrEmpty(entry.Details)) detail = entry.Details;
var when = string.IsNullOrEmpty(entry.Time) ? "now" : entry.Time;
DrawActivityTextRow(container, entry.Action + entry.Time + entry.Target, joined ? "0.09 0.15 0.10 0.88" : "0.16 0.10 0.08 0.88", joined ? "JOIN" : left ? "LEFT" : "CONN", name, $"{when}  {detail}", y);
y -= 0.105;
}
}
private void DrawActivityTextRow(CuiElementContainer container, string id, string color, string state, string name, string details, double y)
{
var text = $"{state}    {Shorten(name, 24)}    {Shorten(details, 62)}";
container.Add(new CuiButton
{
Button = { Command = string.Empty, Color = ThemedColor(color) },
Text = { Text = text, FontSize = 8, Align = TextAnchor.MiddleLeft, Color = Skin().HeaderText },
RectTransform = { AnchorMin = $"0.04 {y}", AnchorMax = $"0.96 {y + 0.09}" }
}, $"{UiRoot}.Body.Activity", $"{UiRoot}.Body.Activity.Row.{SafeName(id)}");
}
private void DrawDashboardServerSnapshot(CuiElementContainer container, BasePlayer player)
{
Panel(container, $"{UiRoot}.Body.ServerSnapshot", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.60 0.08", "0.96 0.34");
Label(container, $"{UiRoot}.Body.ServerSnapshot.Title", $"{UiRoot}.Body.ServerSnapshot", "Server Snapshot", 15, "0.05 0.82", "0.95 0.98", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.ServerSnapshot.Identity", $"{UiRoot}.Body.ServerSnapshot", $"Identity {Shorten(ConVar.Server.identity ?? "default", 22)}   Uptime {FormatDuration(TimeSpan.FromSeconds(Time.realtimeSinceStartup))}", 8, "0.05 0.68", "0.95 0.79", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.ServerSnapshot.Map", $"{UiRoot}.Body.ServerSnapshot", $"Seed {ConVar.Server.seed}   Size {ConVar.Server.worldsize}", 8, "0.05 0.56", "0.95 0.67", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.ServerSnapshot.Plugin", $"{UiRoot}.Body.ServerSnapshot", $"Plugins {GetKnownPlugins().Count}   Permissions {GetKnownPermissions().Count}", 8, "0.05 0.44", "0.95 0.55", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.ServerSnapshot.Wipe", $"{UiRoot}.Body.ServerSnapshot", $"Next wipe {Shorten(storedData.NextAutoWipeUtc ?? "not scheduled", 30)}", 8, "0.05 0.32", "0.95 0.43", TextAnchor.MiddleLeft);
var restartStatus = string.IsNullOrEmpty(storedData.ScheduledRestartUtc) ? "No restart scheduled" : $"Restart {storedData.ScheduledRestartUtc} UTC";
Label(container, $"{UiRoot}.Body.ServerSnapshot.Operations", $"{UiRoot}.Body.ServerSnapshot", $"Maintenance {(storedData.MaintenanceEnabled ? "ON" : "OFF")}   |   {Shorten(restartStatus, 34)}", 8, "0.05 0.20", "0.95 0.31", TextAnchor.MiddleLeft);
if (Can(player, PermServerMaintenance))
Button(container, $"{UiRoot}.Body.ServerSnapshot.Maintenance", $"{UiRoot}.Body.ServerSnapshot", storedData.MaintenanceEnabled ? "End Maintenance" : "Maintenance", "liveadmin.ui act maintenancetoggle", storedData.MaintenanceEnabled ? config.DangerColor : config.WarningColor, "0.05 0.04", "0.32 0.17", 7);
if (Can(player, PermServerRestart))
{
Button(container, $"{UiRoot}.Body.ServerSnapshot.Restart", $"{UiRoot}.Body.ServerSnapshot", $"Restart {config.RestartDefaultMinutes}m", "liveadmin.ui act restart5", config.AccentColor, "0.35 0.04", "0.62 0.17", 7);
Button(container, $"{UiRoot}.Body.ServerSnapshot.CancelRestart", $"{UiRoot}.Body.ServerSnapshot", "Cancel Restart", string.IsNullOrEmpty(storedData.ScheduledRestartUtc) ? string.Empty : "liveadmin.ui act restartcancel", config.DangerColor, "0.65 0.04", "0.95 0.17", 7);
}
}
private void DrawLiveConsole(CuiElementContainer container, BasePlayer player, PanelState state)
{
Header(container, "Live Console");
if (!CanUseQuickActions(player)) { NoAccess(container); return; }
state.ConsoleTab = CleanConsoleTab(state.ConsoleTab);
Panel(container, $"{UiRoot}.Body.Console.Toolbar", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.04 0.78", "0.96 0.875");
DrawConsoleCategoryButton(container, state, "all", "All", 0.025, 0.040);
DrawConsoleCategoryButton(container, state, "server", "Runtime", 0.075, 0.065);
DrawConsoleCategoryButton(container, state, "plugins", "Plugins", 0.150, 0.065);
DrawConsoleCategoryButton(container, state, "warnings", "Warnings", 0.225, 0.080);
DrawConsoleCategoryButton(container, state, "errors", "Errors", 0.315, 0.060);
DrawConsoleCategoryButton(container, state, "chat", "Chat", 0.385, 0.050);
DrawConsoleCategoryButton(container, state, "commands", "Commands", 0.445, 0.080);
DrawConsoleCategoryButton(container, state, "connections", "Connections", 0.535, 0.095);
Panel(container, $"{UiRoot}.Body.Console.Toolbar.Search", $"{UiRoot}.Body.Console.Toolbar", "0.02 0.025 0.03 0.98", "0.64 0.20", "0.94 0.78");
Input(container, $"{UiRoot}.Body.Console.Toolbar.Search.Input", $"{UiRoot}.Body.Console.Toolbar.Search", state.ConsoleFilter, "liveadmin.filter console", 8, "0.03 0", "0.97 1");
Panel(container, $"{UiRoot}.Body.Console.Feed", $"{UiRoot}.Body", "0.045 0.05 0.055 0.96", "0.04 0.26", "0.96 0.76");
var lines = GetConsoleLines(state.ConsoleTab, state.ConsoleFilter).Take(state.ConsoleWrap ? 8 : 11).ToList();
Label(container, $"{UiRoot}.Body.Console.Feed.Status", $"{UiRoot}.Body.Console.Feed", $"{(state.ConsolePaused ? "PAUSED" : "LIVE 30s")}   Runtime {liveConsoleBuffer.Count}   Plugins {liveConsoleBuffer.Count(l => l.Origin == "plugins")}   Errors {liveConsoleBuffer.Count(l => l.Kind == "errors")}   Warnings {liveConsoleBuffer.Count(l => l.Kind == "warnings")}", 7, "0.52 0.955", "0.975 0.995", TextAnchor.MiddleRight);
var y = 0.88;
if (lines.Count == 0)
{
Label(container, $"{UiRoot}.Body.Console.Feed.Empty", $"{UiRoot}.Body.Console.Feed", "No console entries match this view.", 11, "0.04 0.42", "0.96 0.58", TextAnchor.MiddleCenter);
}
foreach (var line in lines)
{
DrawConsoleLine(container, $"{UiRoot}.Body.Console.Feed", line, y, state.ConsoleWrap);
y -= state.ConsoleWrap ? 0.105 : 0.075;
}
Panel(container, $"{UiRoot}.Body.Console.Command", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.04 0.08", "0.70 0.235");
Label(container, $"{UiRoot}.Body.Console.Command.Title", $"{UiRoot}.Body.Console.Command", "Run Command", 12, "0.035 0.68", "0.35 0.94", TextAnchor.MiddleLeft);
Panel(container, $"{UiRoot}.Body.Console.Command.Box", $"{UiRoot}.Body.Console.Command", "0.02 0.025 0.03 0.98", "0.035 0.18", "0.76 0.58");
Input(container, $"{UiRoot}.Body.Console.Command.Input", $"{UiRoot}.Body.Console.Command.Box", state.ConsoleCommandText, "liveadmin.consolefield", 9, "0.025 0", "0.975 1");
Button(container, $"{UiRoot}.Body.Console.Command.Run", $"{UiRoot}.Body.Console.Command", "Run", "liveadmin.ui act consolecommand", config.WarningColor, "0.79 0.18", "0.96 0.58", 9);
Panel(container, $"{UiRoot}.Body.Console.Options", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.72 0.08", "0.96 0.235");
Button(container, $"{UiRoot}.Body.Console.Pause", $"{UiRoot}.Body.Console.Options", state.ConsolePaused ? "Resume" : "Pause", "liveadmin.ui consolepause", state.ConsolePaused ? config.WarningColor : config.SuccessColor, "0.04 0.55", "0.27 0.84", 7);
Button(container, $"{UiRoot}.Body.Console.Wrap", $"{UiRoot}.Body.Console.Options", state.ConsoleWrap ? "Wrap ON" : "Wrap OFF", "liveadmin.ui consolewrap", state.ConsoleWrap ? config.SuccessColor : "0.12 0.13 0.14 1", "0.29 0.55", "0.52 0.84", 7);
Button(container, $"{UiRoot}.Body.Console.Export", $"{UiRoot}.Body.Console.Options", "Export", "liveadmin.ui act consoleexport", config.AccentColor, "0.54 0.55", "0.76 0.84", 7);
Button(container, $"{UiRoot}.Body.Console.Clear", $"{UiRoot}.Body.Console.Options", "Clear", "liveadmin.ui act consoleclear", config.DangerColor, "0.78 0.55", "0.96 0.84", 7);
Button(container, $"{UiRoot}.Body.Console.Mini", $"{UiRoot}.Body.Console.Options", state.ConsoleMiniOpen ? "Mini ON" : "Mini Console", "liveadmin.ui consolemini", state.ConsoleMiniOpen ? config.SuccessColor : config.WarningColor, "0.25 0.12", "0.75 0.42", 7);
}
private void DrawConsoleCategoryButton(CuiElementContainer container, PanelState state, string key, string label, double x, double width)
{
var active = CleanConsoleTab(state.ConsoleTab) == key;
Button(container, $"{UiRoot}.Body.Console.Tab.{key}", $"{UiRoot}.Body.Console.Toolbar", label, $"liveadmin.ui consoletab {key}", active ? config.AccentColor : "0.12 0.13 0.14 1", $"{x} 0.20", $"{x + width} 0.78", 8);
}
private void DrawConsoleLine(CuiElementContainer container, string parent, ConsoleLine line, double y, bool wrap)
{
var color = line.Kind == "errors" ? "0.26 0.08 0.06 0.88" : line.Kind == "warnings" ? "0.25 0.18 0.06 0.88" : line.Kind == "plugins" || line.Origin == "plugins" ? "0.12 0.08 0.20 0.88" : line.Kind == "server" ? "0.07 0.09 0.14 0.88" : line.Kind == "connections" ? "0.09 0.15 0.10 0.88" : line.Kind == "chat" ? "0.08 0.10 0.13 0.88" : "0.075 0.08 0.09 0.88";
var row = $"{parent}.Line.{SafeName(line.Id)}";
var text = wrap
? $"{line.Time}  [{line.Kind.ToUpperInvariant()}]  {Shorten(line.Source, 18)}\n{Shorten(line.Text, 118)}"
: $"{line.Time}  [{line.Kind.ToUpperInvariant()}]  {Shorten(line.Source, 18)}  {Shorten(line.Text, 128)}";
container.Add(new CuiButton
{
Button = { Command = string.Empty, Color = ThemedColor(color) },
Text = { Text = text, FontSize = 7, Align = TextAnchor.MiddleLeft, Color = Skin().HeaderText },
RectTransform = { AnchorMin = $"0.025 {y}", AnchorMax = $"0.975 {y + (wrap ? 0.085 : 0.058)}" }
}, parent, row);
}
private void DrawMiniConsole(CuiElementContainer container, PanelState state)
{
var bounds = MiniConsoleBounds(state.ConsoleMiniPosition);
Panel(container, $"{UiRoot}.MiniConsole", UiRoot, "0.025 0.025 0.022 0.96", bounds[0], bounds[1]);
Label(container, $"{UiRoot}.MiniConsole.Title", $"{UiRoot}.MiniConsole", "Mini Console", 10, "0.04 0.84", "0.50 0.98", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.MiniConsole.Move", $"{UiRoot}.MiniConsole", "Move", "liveadmin.ui consoleminipos", "0.12 0.13 0.14 1", "0.58 0.86", "0.75 0.98", 7);
Button(container, $"{UiRoot}.MiniConsole.Close", $"{UiRoot}.MiniConsole", "X", "liveadmin.ui consolemini", config.DangerColor, "0.80 0.86", "0.95 0.98", 8);
var y = 0.68;
foreach (var line in GetConsoleLines("all", string.Empty).Take(4))
{
container.Add(new CuiButton
{
Button = { Command = string.Empty, Color = "0 0 0 0" },
Text = { Text = $"{line.Time} {Shorten(line.Text, 46)}", FontSize = 7, Align = TextAnchor.MiddleLeft, Color = Skin().HeaderText },
RectTransform = { AnchorMin = $"0.04 {y}", AnchorMax = $"0.96 {y + 0.13}" }
}, $"{UiRoot}.MiniConsole", $"{UiRoot}.MiniConsole.Line.{SafeName(line.Id)}");
y -= 0.16;
}
}
private string[] MiniConsoleBounds(int position)
{
switch (Math.Abs(position) % 4)
{
case 0: return new[] { "0.02 0.70", "0.32 0.93" };
case 1: return new[] { "0.68 0.70", "0.98 0.93" };
case 2: return new[] { "0.02 0.08", "0.32 0.31" };
default: return new[] { "0.68 0.08", "0.98 0.31" };
}
}
private class ConsoleLine
{
public string Id;
public string Time;
public string Kind;
public string Origin;
public string Source;
public string Text;
}
private List<ConsoleLine> GetConsoleLines(string category, string filter)
{
category = CleanConsoleTab(category);
var lines = new List<ConsoleLine>();
lines.AddRange(liveConsoleBuffer.Where(line => line != null));
if (storedData?.Chat != null)
{
foreach (var entry in storedData.Chat.Where(c => c != null && !c.Deleted))
{
lines.Add(new ConsoleLine
{
Id = "chat:" + entry.Id,
Time = entry.Time,
Kind = entry.Blocked ? "errors" : "chat",
Origin = "chat",
Source = entry.PlayerName,
Text = $"{entry.Channel}: {entry.Message}"
});
}
}
if (storedData?.Logs != null)
{
foreach (var log in storedData.Logs.Where(l => l != null))
{
lines.Add(new ConsoleLine
{
Id = "log:" + log.Time + ":" + log.Action + ":" + log.Target,
Time = log.Time,
Kind = ConsoleKind(log),
Origin = string.Equals(log.Category ?? CategorizeAction(log.Action), "Plugin", StringComparison.OrdinalIgnoreCase) ? "plugins" : "audit",
Source = string.IsNullOrEmpty(log.ActorName) ? "Server" : log.ActorName,
Text = ConsoleLogText(log)
});
}
}
return lines
.Where(l => ConsoleCategoryMatches(category, l))
.Where(l => ConsoleTextMatches(l, filter))
.OrderByDescending(l => l.Time)
.ToList();
}
private string ConsoleLogText(ActionLog log)
{
var result = string.IsNullOrEmpty(log.Result) ? "Success" : log.Result;
var category = string.IsNullOrEmpty(log.Category) ? CategorizeAction(log.Action) : log.Category;
var target = !string.IsNullOrEmpty(log.TargetName) ? log.TargetName : log.Target;
var detail = string.IsNullOrEmpty(log.Details) ? string.Empty : " - " + log.Details;
return $"{result}/{category} {log.Action} {target}{detail}";
}
private string ConsoleKind(ActionLog log)
{
var category = (log.Category ?? CategorizeAction(log.Action)).ToLowerInvariant();
var result = (log.Result ?? string.Empty).ToLowerInvariant();
if (result == "failed" || result == "blocked" || category == "security") return "errors";
if (category == "chat") return "chat";
if (category == "connection") return "connections";
if (category == "server" || (log.Action ?? string.Empty).IndexOf("command", StringComparison.OrdinalIgnoreCase) >= 0) return "commands";
return "all";
}
private bool ConsoleCategoryMatches(string category, ConsoleLine line)
{
if (line == null) return false;
if (category == "plugins") return line.Origin == "plugins" || line.Kind == "plugins";
return category == "all" || line.Kind == category;
}
private bool ConsoleTextMatches(ConsoleLine line, string filter)
{
if (string.IsNullOrEmpty(filter)) return true;
return MatchesFilter(line.Time, filter) || MatchesFilter(line.Kind, filter) || MatchesFilter(line.Source, filter) || MatchesFilter(line.Text, filter);
}
private string CleanConsoleTab(string value)
{
value = (value ?? "all").ToLowerInvariant();
return value == "server" || value == "plugins" || value == "warnings" || value == "chat" || value == "commands" || value == "connections" || value == "errors" ? value : "all";
}
private void ExportConsoleLog(BasePlayer player)
{
var state = GetState(player);
var lines = GetConsoleLines(state.ConsoleTab, state.ConsoleFilter);
var folder = Path.Combine(Interface.Oxide.RootDirectory, "logs", "LiveAdmin");
Directory.CreateDirectory(folder);
var file = Path.Combine(folder, $"console-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
File.WriteAllLines(file, lines.Select(l => $"{l.Time} [{l.Kind}] {l.Source}: {l.Text}").ToArray());
Reply(player, $"Console exported: {file}");
Log(player, "ConsoleExport", state.ConsoleTab, $"{lines.Count} lines");
}
private void DrawStaffCommandCenter(CuiElementContainer container, BasePlayer player)
{
Panel(container, $"{UiRoot}.Body.StaffCenter", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.74 0.38", "0.96 0.64");
Label(container, $"{UiRoot}.Body.StaffCenter.Title", $"{UiRoot}.Body.StaffCenter", "Server Pulse", 14, "0.08 0.82", "0.92 0.98", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffCenter.Fps", $"{UiRoot}.Body.StaffCenter", $"FPS {GetServerFpsText()}   Entities {GetEntityCountText()}", 9, "0.08 0.64", "0.92 0.78", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffCenter.State", $"{UiRoot}.Body.StaffCenter", $"On duty {staffOnDuty.Count}   Mutes {storedData.ActiveMutes.Count}   Frozen {frozenPositions.Count}", 9, "0.08 0.48", "0.92 0.62", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffCenter.Reports", $"{UiRoot}.Body.StaffCenter", $"Open reports: {storedData.Reports.Count(r => r.Status != "Resolved")}", 9, "0.08 0.32", "0.92 0.46", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffCenter.Alerts", $"{UiRoot}.Body.StaffCenter", $"Runtime errors {liveConsoleBuffer.Count(l => l.Kind == "errors")}   Flagged chat {storedData.Chat.Count(c => c != null && c.Blocked && !c.Deleted)}", 8, "0.08 0.22", "0.92 0.34", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.StaffCenter.Vanish", $"{UiRoot}.Body.StaffCenter", "Vanish", CanUseStaffAction(player, "vanishself") ? $"liveadmin.ui act staffact {player.UserIDString} vanishself" : string.Empty, config.AccentColor, "0.08 0.05", "0.34 0.19", 8);
Button(container, $"{UiRoot}.Body.StaffCenter.God", $"{UiRoot}.Body.StaffCenter", "God", CanUseStaffAction(player, "godself") ? $"liveadmin.ui act staffact {player.UserIDString} godself" : string.Empty, config.SuccessColor, "0.37 0.05", "0.63 0.19", 8);
Button(container, $"{UiRoot}.Body.StaffCenter.Noclip", $"{UiRoot}.Body.StaffCenter", "Noclip", CanUseStaffAction(player, "noclipself") ? $"liveadmin.ui act staffact {player.UserIDString} noclipself" : string.Empty, config.WarningColor, "0.66 0.05", "0.92 0.19", 8);
}
private void DrawPlayers(CuiElementContainer container, BasePlayer player, PanelState state)
{
Header(container, "Players");
if (!Can(player, PermPlayersView)) { NoAccess(container); return; }
var selectedId = ToUlong(state.SelectedPlayerId ?? "0");
var selected = BasePlayer.FindByID(selectedId) ?? BasePlayer.FindSleeping(selectedId);
var list = BasePlayer.activePlayerList
.Concat(BasePlayer.sleepingPlayerList)
.Where(p => p != null)
.GroupBy(p => p.userID)
.Select(group => group.First())
.Where(p => MatchesFilter(p.displayName, state.PlayerFilter) || MatchesFilter(p.UserIDString, state.PlayerFilter))
.OrderByDescending(p => p.IsConnected)
.ThenBy(p => p.displayName)
.ToList();
var pageSize = Math.Max(5, Math.Min(9, config.PlayersPerPage));
var page = list.Skip(state.PlayerPage * pageSize).Take(pageSize).ToList();
Panel(container, $"{UiRoot}.Body.PlayerList", $"{UiRoot}.Body", "0.06 0.065 0.075 1", "0.04 0.08", "0.31 0.89");
var y = 0.78;
DrawFilterStatus(container, $"{UiRoot}.Body.PlayerList", "Players", state.PlayerFilter, "players", "0.05 0.90", "0.95 0.975");
foreach (var target in page)
{
var row = $"{UiRoot}.Body.PlayerList.Player.{target.userID}";
Panel(container, row, $"{UiRoot}.Body.PlayerList", selected != null && selected.userID == target.userID ? activeSkin.PanelAlt : activeSkin.Button, $"0.05 {y}", $"0.95 {y + 0.067}");
Panel(container, $"{row}.State", row, target.IsConnected ? config.SuccessColor : config.WarningColor, "0.025 0.30", "0.04 0.70");
Button(container, $"{row}.Select", row, Shorten(target.displayName, 22), $"liveadmin.ui select {target.UserIDString}", "0 0 0 0", "0.07 0.30", "0.74 0.96", 9, TextAnchor.MiddleLeft);
Label(container, $"{row}.Health", row, $"{target.health:0} HP", 7, "0.74 0.08", "0.96 0.92", TextAnchor.MiddleRight);
y -= 0.078;
}
MiniPager(container, $"{UiRoot}.Body.PlayerList.Pager", $"{UiRoot}.Body.PlayerList", state.PlayerPage, list.Count, pageSize, "players", "0.05 0.02", "0.95 0.09");
Panel(container, $"{UiRoot}.Body.Detail", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.33 0.08", "0.96 0.89");
if (selected == null)
{
Label(container, $"{UiRoot}.Body.Detail.Empty", $"{UiRoot}.Body.Detail", "Select a player to open their staff profile.", 14, "0.06 0.45", "0.94 0.55", TextAnchor.MiddleCenter);
return;
}
DrawPlayerProfileHeader(container, state, selected);
if (state.PlayerDetailTab == "world") DrawPlayerWorldTab(container, player, selected);
else if (state.PlayerDetailTab == "cupboards") DrawPlayerCupboardsTab(container, player, state, selected);
else if (state.PlayerDetailTab == "history") DrawPlayerHistoryTab(container, selected);
else DrawPlayerOverviewTab(container, player, state, selected);
}
private void DrawPlayerProfileHeader(CuiElementContainer container, PanelState state, BasePlayer selected)
{
var profile = GetPlayerProfile(selected.UserIDString);
Label(container, $"{UiRoot}.Body.Detail.Name", $"{UiRoot}.Body.Detail", Shorten(selected.displayName, 34), 17, "0.04 0.91", "0.58 0.985", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Detail.Status", $"{UiRoot}.Body.Detail", $"{(selected.IsConnected ? "ONLINE" : "SLEEPING")}  |  {selected.health:0} HP  |  Ping {GetPlayerPing(selected)}", 8, "0.60 0.925", "0.96 0.98", TextAnchor.MiddleRight);
Label(container, $"{UiRoot}.Body.Detail.Identity", $"{UiRoot}.Body.Detail", $"{selected.UserIDString}   |   Seen {ShortDate(profile?.LastSeen)}", 8, "0.04 0.865", "0.96 0.91", TextAnchor.MiddleLeft);
var tabs = new[] { "overview", "world", "cupboards", "history" };
var x = 0.04;
foreach (var tab in tabs)
{
Button(container, $"{UiRoot}.Body.Detail.Tab.{tab}", $"{UiRoot}.Body.Detail", Title(tab), $"liveadmin.ui playerdetail {tab}", state.PlayerDetailTab == tab ? config.AccentColor : "0.12 0.13 0.14 1", $"{x} 0.805", $"{x + 0.205} 0.855", 8);
x += 0.22;
}
}
private void DrawPlayerOverviewTab(CuiElementContainer container, BasePlayer player, PanelState state, BasePlayer selected)
{
var profile = GetPlayerProfile(selected.UserIDString);
var groups = permission.GetUserGroups(selected.UserIDString);
var openReports = storedData.Reports.Count(r => r.TargetId == selected.UserIDString && r.Status != "Resolved");
var warnings = storedData.Warnings.Count(w => w != null && w.TargetId == selected.UserIDString);
var address = selected.IsConnected ? GetPlayerAddress(selected) : profile?.LastIp ?? "n/a";
DrawInfoCardIn(container, $"{UiRoot}.Body.Detail", "Connection", $"IP {address}", $"Ping {GetPlayerPing(selected)}", $"Session {GetConnectionAge(selected)}", 0.04, 0.65, 0.33, 0.78);
DrawInfoCardIn(container, $"{UiRoot}.Body.Detail", "Account", $"First {ShortDate(profile?.FirstSeen)}", $"Connections {profile?.Connections ?? 0}", $"Auth {selected.net?.connection?.authLevel ?? 0}", 0.355, 0.65, 0.645, 0.78);
DrawInfoCardIn(container, $"{UiRoot}.Body.Detail", "Staff Record", $"Reports {openReports}", $"Warnings {warnings}", $"Notes {GetNotes(selected.UserIDString).Count}", 0.67, 0.65, 0.96, 0.78);
Panel(container, $"{UiRoot}.Body.Detail.Groups", $"{UiRoot}.Body.Detail", "0.055 0.06 0.07 0.96", "0.04 0.575", "0.96 0.635");
Label(container, $"{UiRoot}.Body.Detail.Groups.Text", $"{UiRoot}.Body.Detail.Groups", $"GROUPS   {Shorten(string.Join(", ", groups), 92)}", 8, "0.025 0", "0.975 1", TextAnchor.MiddleLeft);
ActionButton(container, "TP To", $"liveadmin.ui act tpto {selected.UserIDString}", 0.05, 0.49, Can(player, PermPlayersTeleport));
ActionButton(container, "Bring", $"liveadmin.ui act bring {selected.UserIDString}", 0.365, 0.49, Can(player, PermPlayersTeleport));
ActionButton(container, "Kick", $"liveadmin.ui act kick {selected.UserIDString}", 0.68, 0.49, CanKick(player));
Label(container, $"{UiRoot}.Body.Detail.NoteTitle", $"{UiRoot}.Body.Detail", "STAFF NOTE", 8, "0.05 0.405", "0.18 0.455", TextAnchor.MiddleLeft);
Panel(container, $"{UiRoot}.Body.Detail.Note.Box", $"{UiRoot}.Body.Detail", "0.02 0.025 0.03 0.98", "0.18 0.405", "0.73 0.455");
Input(container, $"{UiRoot}.Body.Detail.Note.Input", $"{UiRoot}.Body.Detail.Note.Box", state.NoteText, "liveadmin.notefield", 8, "0.03 0", "0.97 1");
Button(container, $"{UiRoot}.Body.Detail.Note.Add", $"{UiRoot}.Body.Detail", "Add", $"liveadmin.ui act note {selected.UserIDString}", config.AccentColor, "0.75 0.405", "0.92 0.455", 8);
DrawModerationTools(container, player, state, selected);
}
private void DrawPlayerWorldTab(CuiElementContainer container, BasePlayer player, BasePlayer selected)
{
var stats = GetPlayerWorldStats(selected.userID);
DrawWorldMetric(container, "Total", stats.Total, 0.04, 0.65);
DrawWorldMetric(container, "Building", stats.BuildingBlocks, 0.275, 0.65);
DrawWorldMetric(container, "Deployables", stats.Deployables, 0.51, 0.65);
DrawWorldMetric(container, "Storage", stats.Storage, 0.745, 0.65);
DrawWorldMetric(container, "Turrets", stats.Turrets, 0.04, 0.49);
DrawWorldMetric(container, "Traps", stats.Traps, 0.275, 0.49);
DrawWorldMetric(container, "Vehicles", stats.Vehicles, 0.51, 0.49);
DrawWorldMetric(container, "Respawns", stats.Respawns, 0.745, 0.49);
Panel(container, $"{UiRoot}.Body.Detail.WorldSummary", $"{UiRoot}.Body.Detail", "0.055 0.06 0.07 0.96", "0.04 0.28", "0.96 0.43");
Label(container, $"{UiRoot}.Body.Detail.WorldSummary.Title", $"{UiRoot}.Body.Detail.WorldSummary", "BUILDING PRIVILEGE", 9, "0.035 0.62", "0.50 0.94", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Detail.WorldSummary.Value", $"{UiRoot}.Body.Detail.WorldSummary", $"Owned TCs {stats.OwnedCupboards}   |   Authorized TCs {stats.AuthorizedCupboards}   |   Position {FormatPosition(selected.transform.position)}", 9, "0.035 0.16", "0.96 0.58", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.Detail.WorldToPlayer", $"{UiRoot}.Body.Detail", "Teleport To Player", Can(player, PermPlayersTeleport) ? $"liveadmin.ui act tpto {selected.UserIDString}" : string.Empty, config.AccentColor, "0.04 0.16", "0.31 0.23", 8);
Button(container, $"{UiRoot}.Body.Detail.WorldCupboards", $"{UiRoot}.Body.Detail", "Open Cupboards", "liveadmin.ui playerdetail cupboards", config.WarningColor, "0.335 0.16", "0.62 0.23", 8);
Label(container, $"{UiRoot}.Body.Detail.WorldHint", $"{UiRoot}.Body.Detail", "Counts include live map entities whose OwnerID matches this player.", 8, "0.04 0.07", "0.96 0.13", TextAnchor.MiddleLeft);
}
private void DrawPlayerCupboardsTab(CuiElementContainer container, BasePlayer player, PanelState state, BasePlayer selected)
{
var cupboards = GetPlayerCupboards(selected.userID);
Label(container, $"{UiRoot}.Body.Detail.CupboardsSummary", $"{UiRoot}.Body.Detail", $"{cupboards.Count} tool cupboard(s) owned by or authorizing this player", 10, "0.04 0.735", "0.96 0.79", TextAnchor.MiddleLeft);
if (cupboards.Count == 0)
{
Label(container, $"{UiRoot}.Body.Detail.CupboardsEmpty", $"{UiRoot}.Body.Detail", "No matching tool cupboards exist on the current map.", 11, "0.04 0.42", "0.96 0.55", TextAnchor.MiddleCenter);
return;
}
var pageSize = 7;
var maxPage = Math.Max(0, (cupboards.Count - 1) / pageSize);
state.PlayerCupboardPage = Math.Min(state.PlayerCupboardPage, maxPage);
var start = state.PlayerCupboardPage * pageSize;
var y = 0.65;
for (var i = start; i < Math.Min(start + pageSize, cupboards.Count); i++)
{
var tc = cupboards[i];
var owned = tc.OwnerID == selected.userID;
var row = $"{UiRoot}.Body.Detail.Cupboard.{i}";
Panel(container, row, $"{UiRoot}.Body.Detail", owned ? "0.10 0.18 0.12 1" : "0.11 0.12 0.13 1", $"0.04 {y}", $"0.96 {y + 0.072}");
Label(container, $"{row}.Text", row, $"TC {i + 1}   {(owned ? "OWNER" : "AUTHORIZED")}   {FormatPosition(tc.transform.position)}   Auth {(tc.authorizedPlayers == null ? 0 : tc.authorizedPlayers.Count)}", 8, "0.025 0", "0.73 1", TextAnchor.MiddleLeft);
Button(container, $"{row}.Teleport", row, "Teleport", Can(player, PermPlayersTeleport) ? $"liveadmin.ui act tptc {selected.UserIDString} {i}" : string.Empty, config.AccentColor, "0.77 0.18", "0.97 0.82", 8);
y -= 0.085;
}
if (maxPage > 0)
{
Button(container, $"{UiRoot}.Body.Detail.CupboardsPrev", $"{UiRoot}.Body.Detail", "Prev", state.PlayerCupboardPage > 0 ? "liveadmin.ui playercupboardpage -1" : string.Empty, "0.12 0.13 0.14 1", "0.04 0.05", "0.18 0.11", 8);
Label(container, $"{UiRoot}.Body.Detail.CupboardsPage", $"{UiRoot}.Body.Detail", $"Page {state.PlayerCupboardPage + 1}/{maxPage + 1}", 8, "0.39 0.05", "0.61 0.11", TextAnchor.MiddleCenter);
Button(container, $"{UiRoot}.Body.Detail.CupboardsNext", $"{UiRoot}.Body.Detail", "Next", state.PlayerCupboardPage < maxPage ? "liveadmin.ui playercupboardpage 1" : string.Empty, "0.12 0.13 0.14 1", "0.82 0.05", "0.96 0.11", 8);
}
}
private void DrawPlayerHistoryTab(CuiElementContainer container, BasePlayer selected)
{
var entries = storedData.Logs.Where(l => l != null && (l.Target == selected.UserIDString || l.ActorId == selected.UserIDString)).OrderByDescending(l => l.Time).Take(7).ToList();
var warnings = storedData.Warnings.Where(w => w != null && w.TargetId == selected.UserIDString).OrderByDescending(w => w.Time).Take(3).ToList();
Label(container, $"{UiRoot}.Body.Detail.HistorySummary", $"{UiRoot}.Body.Detail", $"Warnings {storedData.Warnings.Count(w => w != null && w.TargetId == selected.UserIDString)}   |   Notes {GetNotes(selected.UserIDString).Count}   |   Related audit events {entries.Count}", 9, "0.04 0.735", "0.96 0.79", TextAnchor.MiddleLeft);
var y = 0.65;
var warningIndex = 0;
foreach (var warning in warnings)
{
var warningName = $"{UiRoot}.Body.Detail.History.Warning.{warningIndex}";
Panel(container, warningName, $"{UiRoot}.Body.Detail", "0.25 0.14 0.06 0.90", $"0.04 {y}", $"0.96 {y + 0.065}");
Label(container, $"{warningName}.Text", warningName, $"WARNING  {warning.Time}  {warning.StaffName}  {Shorten(warning.Reason, 64)}", 8, "0.02 0", "0.98 1", TextAnchor.MiddleLeft);
y -= 0.075;
warningIndex++;
}
foreach (var entry in entries.Take(Math.Max(0, 7 - warnings.Count)))
{
var name = $"{UiRoot}.Body.Detail.History.Log.{SafeName(entry.Time + entry.Action)}";
Panel(container, name, $"{UiRoot}.Body.Detail", "0.055 0.06 0.07 0.96", $"0.04 {y}", $"0.96 {y + 0.065}");
Label(container, $"{name}.Text", name, $"{entry.Time}  [{entry.Category}]  {entry.Action}  {Shorten(entry.Details, 58)}", 8, "0.02 0", "0.98 1", TextAnchor.MiddleLeft);
y -= 0.075;
}
}
private void DrawPlayerNotes(CuiElementContainer container, BasePlayer selected)
{
Panel(container, $"{UiRoot}.Body.Detail.NotesPanel", $"{UiRoot}.Body.Detail", "0.045 0.05 0.06 0.99", "0.04 0.08", "0.96 0.60");
Label(container, $"{UiRoot}.Body.Detail.NotesPanel.Title", $"{UiRoot}.Body.Detail.NotesPanel", $"Notes: {selected.displayName}", 13, "0.04 0.84", "0.70 0.98", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.Detail.NotesPanel.Close", $"{UiRoot}.Body.Detail.NotesPanel", "Close", "liveadmin.ui togglenotes", "0.12 0.13 0.14 1", "0.76 0.86", "0.96 0.97", 9);
var notes = GetNotes(selected.UserIDString).OrderByDescending(n => n.Time).Take(5).ToList();
if (notes.Count == 0)
{
Label(container, $"{UiRoot}.Body.Detail.NotesPanel.Empty", $"{UiRoot}.Body.Detail.NotesPanel", "No notes for this player.", 10, "0.04 0.42", "0.96 0.58", TextAnchor.MiddleCenter);
return;
}
var y = 0.70;
var index = 0;
foreach (var note in notes)
{
Panel(container, $"{UiRoot}.Body.Detail.NotesPanel.Row.{index}", $"{UiRoot}.Body.Detail.NotesPanel", "0.075 0.08 0.09 1", $"0.04 {y}", $"0.96 {y + 0.11}");
Label(container, $"{UiRoot}.Body.Detail.NotesPanel.Row.{index}.Meta", $"{UiRoot}.Body.Detail.NotesPanel.Row.{index}", $"{note.Time}  {note.StaffName}", 8, "0.03 0.55", "0.97 0.95", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Detail.NotesPanel.Row.{index}.Text", $"{UiRoot}.Body.Detail.NotesPanel.Row.{index}", Shorten(note.Text, 70), 9, "0.03 0.05", "0.97 0.58", TextAnchor.MiddleLeft);
y -= 0.125;
index++;
}
}
private void DrawStaffTools(CuiElementContainer container, BasePlayer player, PanelState state)
{
Header(container, "Staff Tools");
if (!CanSeeTab(player, "staff")) { NoAccess(container); return; }
var staffTabs = new[] { "activity", "info", "actions", "fun", "inventory", "notes", "groups", "tickets" }
.Where(tab => tab != "inventory" || CanViewInventory(player))
.Where(tab => tab != "groups" || CanManageStaffGroups(player))
.Where(tab => tab != "tickets" || CanAny(player, PermOwner, PermReportsView, PermReportsManage, PermStaffToolsTickets))
.Where(tab => tab != "fun" || Can(player, PermStaffToolsFun))
.ToArray();
var tabX = 0.04;
foreach (var tab in staffTabs)
{
Button(container, $"{UiRoot}.Body.StaffSub.{tab}", $"{UiRoot}.Body", Title(tab), $"liveadmin.ui staffsubtab {tab}", state.StaffSubTab == tab ? config.AccentColor : "0.12 0.13 0.14 1", $"{tabX} 0.83", $"{tabX + 0.088} 0.89", 8);
tabX += 0.098;
}
if (state.StaffSubTab == "activity")
{
DrawStaffActivityTab(container, player);
return;
}
var selected = BasePlayer.FindByID(ToUlong(state.SelectedPlayerId ?? "0"));
var list = BasePlayer.activePlayerList
.Where(p => MatchesFilter(p.displayName, state.PlayerFilter) || MatchesFilter(p.UserIDString, state.PlayerFilter))
.OrderBy(p => p.displayName)
.ToList();
if (selected != null && state.StaffSubTab == "inventory" && state.StaffInventoryDetailOpen)
{
Panel(container, $"{UiRoot}.Body.StaffDetail", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.04 0.08", "0.96 0.82");
DrawStaffInventoryTab(container, player, state, selected);
return;
}
var pageSize = 9;
var page = list.Skip(state.PlayerPage * pageSize).Take(pageSize).ToList();
Panel(container, $"{UiRoot}.Body.StaffList", $"{UiRoot}.Body", "0.06 0.065 0.075 1", "0.04 0.08", "0.35 0.82");
DrawFilterStatus(container, $"{UiRoot}.Body.StaffList", "Players", state.PlayerFilter, "staffplayers", "0.05 0.90", "0.95 0.975");
var y = 0.78;
foreach (var target in page)
{
var selectedColor = selected != null && selected.userID == target.userID ? config.AccentColor : "0.12 0.13 0.14 1";
Button(container, $"{UiRoot}.Body.StaffList.Player.{target.userID}", $"{UiRoot}.Body.StaffList", $"{Shorten(target.displayName, 22)}  {target.health:0}hp", $"liveadmin.ui staffselect {target.UserIDString}", selectedColor, $"0.05 {y}", $"0.95 {y + 0.065}", 9, TextAnchor.MiddleLeft);
y -= 0.075;
}
MiniPager(container, $"{UiRoot}.Body.StaffList.Pager", $"{UiRoot}.Body.StaffList", state.PlayerPage, list.Count, pageSize, "players", "0.05 0.02", "0.95 0.09");
Panel(container, $"{UiRoot}.Body.StaffDetail", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.38 0.08", "0.96 0.82");
if (selected == null)
{
Label(container, $"{UiRoot}.Body.StaffDetail.Empty", $"{UiRoot}.Body.StaffDetail", "Select a player to use staff tools.", 14, "0.06 0.45", "0.94 0.55", TextAnchor.MiddleCenter);
return;
}
if (state.StaffSubTab == "actions") DrawStaffActionsTab(container, player, state, selected);
else if (state.StaffSubTab == "fun") DrawStaffFunTab(container, player, state, selected);
else if (state.StaffSubTab == "inventory") DrawStaffInventoryTab(container, player, state, selected);
else if (state.StaffSubTab == "notes") DrawStaffNotesTab(container, player, state, selected);
else if (state.StaffSubTab == "groups") DrawStaffGroupsTab(container, player, state, selected);
else if (state.StaffSubTab == "tickets") DrawStaffTicketsTab(container, player, state, selected);
else DrawStaffInfoTab(container, player, state, selected);
}
private void DrawStaffActivityTab(CuiElementContainer container, BasePlayer viewer)
{
Panel(container, $"{UiRoot}.Body.StaffActivity", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.04 0.08", "0.96 0.82");
var staff = BasePlayer.activePlayerList.Where(target => target != null && Can(target, PermUse)).OrderByDescending(target => staffOnDuty.Contains(target.userID)).ThenBy(target => target.displayName).ToList();
Label(container, $"{UiRoot}.Body.StaffActivity.Title", $"{UiRoot}.Body.StaffActivity", "Staff Operations", 16, "0.04 0.88", "0.45 0.98", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffActivity.Summary", $"{UiRoot}.Body.StaffActivity", $"Online {staff.Count}   On duty {staff.Count(target => staffOnDuty.Contains(target.userID))}   Open cases {storedData.Reports.Count(report => report.Status != "Resolved")}", 9, "0.48 0.90", "0.96 0.97", TextAnchor.MiddleRight);
Button(container, $"{UiRoot}.Body.StaffActivity.Duty", $"{UiRoot}.Body.StaffActivity", staffOnDuty.Contains(viewer.userID) ? "Go Off Duty" : "Go On Duty", "liveadmin.ui act staffduty", staffOnDuty.Contains(viewer.userID) ? config.SuccessColor : config.WarningColor, "0.04 0.79", "0.22 0.86", 9);
Label(container, $"{UiRoot}.Body.StaffActivity.Hint", $"{UiRoot}.Body.StaffActivity", "Duty state lasts for the current staff session and is recorded in the audit log.", 8, "0.25 0.79", "0.96 0.86", TextAnchor.MiddleLeft);
var y = 0.68;
foreach (var member in staff.Take(7))
{
var lastAction = storedData.Logs.Where(log => log != null && log.ActorId == member.UserIDString && log.Category != "Connection").OrderByDescending(log => log.Time).FirstOrDefault();
var assignedCases = storedData.Reports.Count(report => report != null && report.Status != "Resolved" && report.ClaimedBy == member.displayName);
var row = $"{UiRoot}.Body.StaffActivity.Member.{member.userID}";
Panel(container, row, $"{UiRoot}.Body.StaffActivity", staffOnDuty.Contains(member.userID) ? "0.10 0.18 0.12 1" : "0.10 0.11 0.12 1", $"0.04 {y}", $"0.96 {y + 0.075}");
Label(container, $"{row}.Name", row, $"{(staffOnDuty.Contains(member.userID) ? "ON DUTY" : "AVAILABLE")}   {Shorten(member.displayName, 20)}   Auth {member.net?.connection?.authLevel ?? 0}   Cases {assignedCases}", 8, "0.025 0.48", "0.50 0.96", TextAnchor.MiddleLeft);
Label(container, $"{row}.Action", row, lastAction == null ? "No recent staff action" : $"{lastAction.Time}  {lastAction.Action}  {Shorten(lastAction.TargetName ?? lastAction.Target, 18)}", 7, "0.50 0", "0.97 1", TextAnchor.MiddleRight);
y -= 0.088;
}
}
private void DrawStaffInfoTab(CuiElementContainer container, BasePlayer player, PanelState state, BasePlayer selected)
{
var groups = permission.GetUserGroups(selected.UserIDString);
var protectedTarget = IsProtectedTarget(selected);
var position = selected.transform.position;
var reports = storedData.Reports.Count(r => r.TargetId == selected.UserIDString);
var relatedTickets = storedData.Reports.Count(r => r.TargetId == selected.UserIDString || r.ReporterId == selected.UserIDString);
var profile = GetPlayerProfile(selected.UserIDString);
Label(container, $"{UiRoot}.Body.StaffDetail.Name", $"{UiRoot}.Body.StaffDetail", selected.displayName, 18, "0.04 0.90", "0.66 0.985", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffDetail.Status", $"{UiRoot}.Body.StaffDetail", protectedTarget ? "Protected account" : "Standard account", 9, "0.68 0.91", "0.96 0.97", TextAnchor.MiddleRight);
DrawInfoCard(container, "Vitals", $"HP {selected.health:0}", $"Ping {GetPlayerPing(selected)}", $"Connected {GetConnectionAge(selected)}", 0.04, 0.76, 0.29, 0.88);
DrawInfoCard(container, "State", $"Frozen {(frozenPositions.ContainsKey(selected.userID) ? "Yes" : "No")}", $"Passive {(passivePlayers.Contains(selected.userID) ? "Yes" : "No")}", $"Sleeping {(selected.IsSleeping() ? "Yes" : "No")}", 0.31, 0.76, 0.56, 0.88);
DrawInfoCard(container, "Cases", $"Reports {reports}", $"Tickets {relatedTickets}", $"Notes {GetNotes(selected.UserIDString).Count}  |  Warnings {storedData.Warnings.Count(w => w != null && w.TargetId == selected.UserIDString)}", 0.58, 0.76, 0.83, 0.88);
DrawInfoCard(container, "Access", $"Auth {selected.net?.connection?.authLevel ?? 0}", $"Groups {groups.Length}", $"Mutes {storedData.ActiveMutes.Count(m => m.TargetId == selected.UserIDString)}", 0.04, 0.60, 0.29, 0.72);
Panel(container, $"{UiRoot}.Body.StaffDetail.Identity", $"{UiRoot}.Body.StaffDetail", "0.055 0.06 0.07 0.96", "0.31 0.60", "0.96 0.72");
Label(container, $"{UiRoot}.Body.StaffDetail.Identity.Title", $"{UiRoot}.Body.StaffDetail.Identity", "Identity", 10, "0.03 0.58", "0.28 0.96", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffDetail.Identity.SteamLabel", $"{UiRoot}.Body.StaffDetail.Identity", "Steam ID", 8, "0.03 0.12", "0.16 0.55", TextAnchor.MiddleLeft);
Panel(container, $"{UiRoot}.Body.StaffDetail.Identity.SteamBox", $"{UiRoot}.Body.StaffDetail.Identity", "0.02 0.025 0.03 0.98", "0.17 0.16", "0.54 0.54");
Input(container, $"{UiRoot}.Body.StaffDetail.Identity.SteamInput", $"{UiRoot}.Body.StaffDetail.Identity.SteamBox", selected.UserIDString, "liveadmin.noop", 8, "0.03 0", "0.97 1");
Label(container, $"{UiRoot}.Body.StaffDetail.Identity.Ip", $"{UiRoot}.Body.StaffDetail.Identity", $"IP {GetPlayerAddress(selected)}", 8, "0.58 0.36", "0.97 0.72", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffDetail.Identity.Seen", $"{UiRoot}.Body.StaffDetail.Identity", $"First {ShortDate(profile?.FirstSeen)}   Last {ShortDate(profile?.LastSeen)}", 7, "0.58 0.04", "0.97 0.34", TextAnchor.MiddleLeft);
Panel(container, $"{UiRoot}.Body.StaffDetail.GroupsBox", $"{UiRoot}.Body.StaffDetail", "0.055 0.06 0.07 0.96", "0.04 0.42", "0.96 0.56");
Label(container, $"{UiRoot}.Body.StaffDetail.GroupsBox.Title", $"{UiRoot}.Body.StaffDetail.GroupsBox", "Groups", 10, "0.02 0.58", "0.98 0.96", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffDetail.GroupsBox.Value", $"{UiRoot}.Body.StaffDetail.GroupsBox", Shorten(string.Join(", ", groups), 115), 8, "0.02 0.08", "0.98 0.58", TextAnchor.MiddleLeft);
Panel(container, $"{UiRoot}.Body.StaffDetail.WorldBox", $"{UiRoot}.Body.StaffDetail", "0.055 0.06 0.07 0.96", "0.04 0.24", "0.96 0.38");
Label(container, $"{UiRoot}.Body.StaffDetail.WorldBox.Title", $"{UiRoot}.Body.StaffDetail.WorldBox", "World / Movement", 10, "0.02 0.58", "0.98 0.96", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffDetail.WorldBox.Value", $"{UiRoot}.Body.StaffDetail.WorldBox", $"Position {position.x:0}, {position.y:0}, {position.z:0}   Mounted {(selected.GetMounted() != null ? "Yes" : "No")}   Wounded {(selected.IsWounded() ? "Yes" : "No")}   Inventory {CountInventoryItems(selected)} items", 8, "0.02 0.08", "0.98 0.58", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffDetail.InfoHint", $"{UiRoot}.Body.StaffDetail", "Use Inventory, Notes, Groups, and Tickets for direct player management.", 9, "0.04 0.12", "0.96 0.19", TextAnchor.MiddleLeft);
}
private void DrawStaffActionsTab(CuiElementContainer container, BasePlayer player, PanelState state, BasePlayer selected)
{
DrawStaffField(container, "Reason", "reason", state.StaffReason, 0.705, 0.04, 0.48);
DrawStaffField(container, "Message", "message", state.StaffMessage, 0.705, 0.52, 0.96);
DrawStaffField(container, "Item", "item", state.StaffItemShortname, 0.595, 0.04, 0.32);
DrawStaffField(container, "Amount", "amount", state.StaffItemAmount, 0.595, 0.35, 0.48);
Label(container, $"{UiRoot}.Body.StaffDetail.Moderation", $"{UiRoot}.Body.StaffDetail", "Moderation", 11, "0.04 0.51", "0.96 0.56", TextAnchor.MiddleLeft);
StaffToolButton(container, "Heal", selected, "heal", 0.04, 0.45, CanUseStaffAction(player, "heal"));
StaffToolButton(container, "Kill", selected, "kill", 0.20, 0.45, Can(player, PermStaffToolsDangerous), true);
StaffToolButton(container, "Freeze", selected, "freeze", 0.36, 0.45, CanUseStaffAction(player, "freeze"));
StaffToolButton(container, "Spectate", selected, "spectate", 0.52, 0.45, CanUseStaffAction(player, "spectate"));
StaffToolButton(container, "Warn", selected, "warn", 0.68, 0.45, CanUseStaffAction(player, "warn"));
StaffToolButton(container, "Strip", selected, "strip", 0.84, 0.45, Can(player, PermStaffToolsDangerous), true);
Label(container, $"{UiRoot}.Body.StaffDetail.Items", $"{UiRoot}.Body.StaffDetail", "Items / Server", 11, "0.04 0.36", "0.96 0.41", TextAnchor.MiddleLeft);
StaffToolButton(container, "Give Item", selected, "give", 0.04, 0.30, CanModifyInventory(player));
StaffToolButton(container, "Save", selected, "save", 0.20, 0.30, CanUseQuickActions(player));
StaffToolButton(container, "Vanish", selected, "vanishself", 0.36, 0.30, CanUseStaffAction(player, "vanishself"));
Label(container, $"{UiRoot}.Body.StaffDetail.Comms", $"{UiRoot}.Body.StaffDetail", "Communication / Server", 11, "0.04 0.21", "0.96 0.26", TextAnchor.MiddleLeft);
StaffToolButton(container, "Broadcast", selected, "broadcast", 0.04, 0.15, CanUseQuickActions(player));
StaffToolButton(container, "Kick", selected, "kick", 0.20, 0.15, CanKick(player), true);
StaffToolButton(container, "Ban", selected, "ban", 0.36, 0.15, Can(player, PermStaffToolsDangerous) && CanBan(player), true);
}
private void DrawStaffFunTab(CuiElementContainer container, BasePlayer player, PanelState state, BasePlayer selected)
{
DrawStaffField(container, "Reason", "reason", state.StaffReason, 0.705, 0.04, 0.48);
DrawStaffField(container, "Message", "message", state.StaffMessage, 0.705, 0.52, 0.96);
Label(container, $"{UiRoot}.Body.StaffDetail.FunTitle", $"{UiRoot}.Body.StaffDetail", "Fun / Controlled Player Effects", 13, "0.04 0.58", "0.96 0.66", TextAnchor.MiddleLeft);
StaffToolButton(container, "Launch", selected, "launch", 0.04, 0.48, Can(player, PermStaffToolsFun));
StaffToolButton(container, spinningPlayers.Contains(selected.userID) ? "Stop Spin" : "Spin", selected, "spin", 0.20, 0.48, Can(player, PermStaffToolsFun), spinningPlayers.Contains(selected.userID));
StaffToolButton(container, "Random TP", selected, "randomtp", 0.36, 0.48, Can(player, PermStaffToolsFun));
StaffToolButton(container, "Passive", selected, "passive", 0.52, 0.48, Can(player, PermStaffToolsFun), passivePlayers.Contains(selected.userID));
StaffToolButton(container, "Timeout", selected, "timeout", 0.68, 0.48, Can(player, PermStaffToolsFun));
StaffToolButton(container, "Fake Msg", selected, "fake", 0.84, 0.48, Can(player, PermStaffToolsFun));
Label(container, $"{UiRoot}.Body.StaffDetail.FunHint", $"{UiRoot}.Body.StaffDetail", "Spin and Passive are toggles. Timeout teleports the player to staff and freezes them.", 9, "0.04 0.38", "0.96 0.45", TextAnchor.MiddleLeft);
}
private void DrawStaffInventoryTab(CuiElementContainer container, BasePlayer player, PanelState state, BasePlayer selected)
{
state.StaffInventoryTab = CleanInventoryTab(state.StaffInventoryTab);
if (!state.StaffInventoryDetailOpen)
{
DrawStaffInventorySelector(container, player, selected);
return;
}
var view = GetInventoryView(selected, state.StaffInventoryTab);
var pageSize = view.Columns * view.Rows;
var itemCount = view.Container == null ? 0 : view.Container.itemList.Count;
var maxInventoryPage = Math.Max(0, (itemCount - 1) / Math.Max(1, pageSize));
state.StaffInventoryPage = Math.Min(state.StaffInventoryPage, maxInventoryPage);
var selectedInventoryItem = GetInventoryItemAt(view.Container, state.StaffInventorySlot);
var visibleRows = view.Rows;
var gridTop = 0.655;
var gridBottom = view.GridBottom;
var gridRight = view.GridRight;
var visibleSlots = Math.Min(pageSize, Math.Max(pageSize, itemCount - state.StaffInventoryPage * pageSize));
Label(container, $"{UiRoot}.Body.StaffDetail.InventoryTitle", $"{UiRoot}.Body.StaffDetail", "INVENTORY", 17, "0.04 0.90", "0.36 0.985", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffDetail.InventoryPlayer", $"{UiRoot}.Body.StaffDetail", Shorten(selected.displayName, 28), 10, "0.38 0.91", "0.64 0.97", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.StaffDetail.InventoryBack", $"{UiRoot}.Body.StaffDetail", "Back", "liveadmin.ui staffinvback", "0.12 0.13 0.14 1", "0.61 0.915", "0.68 0.975", 8);
Button(container, $"{UiRoot}.Body.StaffDetail.InventoryRefresh", $"{UiRoot}.Body.StaffDetail", "Refresh", $"liveadmin.ui staffinv {view.Key}", "0.12 0.13 0.14 1", "0.875 0.915", "0.96 0.975", 8);
DrawInventoryTabButton(container, state, "main", "Main", 0.04, selected.inventory?.containerMain);
DrawInventoryTabButton(container, state, "belt", "Belt", 0.18, selected.inventory?.containerBelt);
DrawInventoryTabButton(container, state, "wear", "Wear", 0.32, selected.inventory?.containerWear);
DrawInventoryTabButton(container, state, "backpack", "Backpack", 0.46, GetBackpackContainer(selected));
Panel(container, $"{UiRoot}.Body.StaffDetail.InventoryGivePanel", $"{UiRoot}.Body.StaffDetail", "0.045 0.05 0.055 0.94", "0.04 0.695", "0.96 0.820");
DrawStaffField(container, "Item", "item", state.StaffItemShortname, 0.740, 0.065, 0.31);
DrawStaffField(container, "Amount", "amount", state.StaffItemAmount, 0.740, 0.33, 0.45);
Button(container, $"{UiRoot}.Body.StaffDetail.InventoryGiveCurrent", $"{UiRoot}.Body.StaffDetail", $"Add To {view.Title}", CanModifyInventory(player) ? $"liveadmin.ui act staffact {selected.UserIDString} give{view.Key}" : string.Empty, config.SuccessColor, "0.47 0.735", "0.62 0.795", 8);
Label(container, $"{UiRoot}.Body.StaffDetail.InventorySelected", $"{UiRoot}.Body.StaffDetail", SelectedInventoryText(selectedInventoryItem, state.StaffInventorySlot), 8, "0.64 0.735", "0.75 0.795", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.StaffDetail.InventorySetStack", $"{UiRoot}.Body.StaffDetail", "Set Stack", selectedInventoryItem != null && CanModifyInventory(player) ? $"liveadmin.ui act staffact {selected.UserIDString} setstack:{view.Key}:{state.StaffInventorySlot}" : string.Empty, selectedInventoryItem != null ? config.AccentColor : "0.12 0.12 0.12 0.65", "0.755 0.735", "0.835 0.795", 7);
Button(container, $"{UiRoot}.Body.StaffDetail.InventoryRemoveOne", $"{UiRoot}.Body.StaffDetail", "Remove 1", selectedInventoryItem != null && CanModifyInventory(player) ? $"liveadmin.ui act staffact {selected.UserIDString} removeinv:{view.Key}:{state.StaffInventorySlot}:1" : string.Empty, selectedInventoryItem != null ? config.WarningColor : "0.12 0.12 0.12 0.65", "0.845 0.735", "0.915 0.795", 7);
Button(container, $"{UiRoot}.Body.StaffDetail.InventoryRemoveStack", $"{UiRoot}.Body.StaffDetail", "Stack", selectedInventoryItem != null && CanModifyInventory(player) ? $"liveadmin.ui act staffact {selected.UserIDString} removeinv:{view.Key}:{state.StaffInventorySlot}:stack" : string.Empty, selectedInventoryItem != null ? config.DangerColor : "0.12 0.12 0.12 0.65", "0.925 0.735", "0.975 0.795", 7);
Label(container, $"{UiRoot}.Body.StaffDetail.InventoryCount", $"{UiRoot}.Body.StaffDetail", $"{view.Title}: {itemCount} items", 10, "0.73 0.835", "0.95 0.890", TextAnchor.MiddleRight);
DrawInventoryContainer(container, player, state, selected, view.Key, view.Title, view.Container, state.StaffInventoryPage * pageSize, 0.04, gridBottom, gridRight, gridTop, view.Columns, visibleRows, visibleSlots);
if (maxInventoryPage > 0)
{
var pagerY = Math.Max(0.155, gridBottom - 0.075);
Button(container, $"{UiRoot}.Body.StaffDetail.InventoryPagePrev", $"{UiRoot}.Body.StaffDetail", "<", $"liveadmin.ui staffinvpage {view.Key} -1", "0.12 0.13 0.14 1", $"{0.04:0.###} {pagerY:0.###}", $"{0.09:0.###} {pagerY + 0.055:0.###}", 8);
Label(container, $"{UiRoot}.Body.StaffDetail.InventoryPageLabel", $"{UiRoot}.Body.StaffDetail", $"Page {state.StaffInventoryPage + 1}/{maxInventoryPage + 1}", 8, $"{0.10:0.###} {pagerY:0.###}", $"{Math.Min(gridRight - 0.07, 0.36):0.###} {pagerY + 0.055:0.###}", TextAnchor.MiddleCenter);
Button(container, $"{UiRoot}.Body.StaffDetail.InventoryPageNext", $"{UiRoot}.Body.StaffDetail", ">", $"liveadmin.ui staffinvpage {view.Key} 1", "0.12 0.13 0.14 1", $"{Math.Min(gridRight - 0.06, 0.37):0.###} {pagerY:0.###}", $"{Math.Min(gridRight, 0.42):0.###} {pagerY + 0.055:0.###}", 8);
}
Label(container, $"{UiRoot}.Body.StaffDetail.InventoryHint", $"{UiRoot}.Body.StaffDetail", "Click an item to select it, then use Set Stack, Remove 1, or Stack. Amount controls added items and stack edits.", 8, "0.04 0.08", "0.96 0.13", TextAnchor.MiddleLeft);
}
private void DrawStaffInventorySelector(CuiElementContainer container, BasePlayer player, BasePlayer selected)
{
Label(container, $"{UiRoot}.Body.StaffDetail.InventorySelectTitle", $"{UiRoot}.Body.StaffDetail", "INVENTORY", 17, "0.04 0.90", "0.36 0.985", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffDetail.InventorySelectPlayer", $"{UiRoot}.Body.StaffDetail", Shorten(selected.displayName, 28), 10, "0.38 0.91", "0.64 0.97", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.StaffDetail.InventorySelectRefresh", $"{UiRoot}.Body.StaffDetail", "Refresh", "liveadmin.ui staffsubtab inventory", "0.12 0.13 0.14 1", "0.875 0.915", "0.96 0.975", 8);
DrawInventoryContainerCard(container, player, selected, "main", "Main Inventory", selected.inventory?.containerMain, 0.04, 0.60, 0.47, 0.82);
DrawInventoryContainerCard(container, player, selected, "belt", "Belt", selected.inventory?.containerBelt, 0.53, 0.60, 0.96, 0.82);
DrawInventoryContainerCard(container, player, selected, "wear", "Wear / Armour", selected.inventory?.containerWear, 0.04, 0.32, 0.47, 0.54);
DrawInventoryContainerCard(container, player, selected, "backpack", "Backpack", GetBackpackContainer(selected), 0.53, 0.32, 0.96, 0.54);
Label(container, $"{UiRoot}.Body.StaffDetail.InventorySelectHint", $"{UiRoot}.Body.StaffDetail", "Select a container to open the full quick-action inventory page.", 9, "0.04 0.18", "0.96 0.25", TextAnchor.MiddleLeft);
}
private void DrawInventoryContainerCard(CuiElementContainer container, BasePlayer player, BasePlayer selected, string key, string title, ItemContainer itemContainer, double x1, double y1, double x2, double y2)
{
var count = itemContainer == null ? 0 : itemContainer.itemList.Count;
var card = $"{UiRoot}.Body.StaffDetail.InventoryCard.{key}";
Panel(container, card, $"{UiRoot}.Body.StaffDetail", "0.055 0.060 0.055 0.96", $"{x1} {y1}", $"{x2} {y2}");
Label(container, $"{card}.Title", card, title, 13, "0.06 0.66", "0.66 0.94", TextAnchor.MiddleLeft);
Label(container, $"{card}.Count", card, $"{count} items", 11, "0.06 0.40", "0.66 0.64", TextAnchor.MiddleLeft);
Button(container, $"{card}.OpenPage", card, "Open", $"liveadmin.ui staffinv {key}", config.AccentColor, "0.67 0.28", "0.94 0.78", 8);
}
private void DrawInventoryTabButton(CuiElementContainer container, PanelState state, string key, string title, double x, ItemContainer itemContainer)
{
var active = state.StaffInventoryTab == key;
Button(container, $"{UiRoot}.Body.StaffDetail.InventoryTab.{key}", $"{UiRoot}.Body.StaffDetail", $"{title} {(itemContainer == null ? 0 : itemContainer.itemList.Count)}", $"liveadmin.ui staffinv {key}", active ? config.AccentColor : "0.12 0.13 0.14 1", $"{x} 0.855", $"{x + 0.12} 0.905", 8);
}
private void DrawInventoryContainer(CuiElementContainer container, BasePlayer player, PanelState state, BasePlayer selected, string key, string title, ItemContainer itemContainer, int startSlot, double x1, double y1, double x2, double y2, int columns, int rows, int maxSlots)
{
Panel(container, $"{UiRoot}.Body.StaffDetail.Inventory.{key}", $"{UiRoot}.Body.StaffDetail", "0.035 0.04 0.04 0.72", $"{x1} {y1}", $"{x2} {y2}");
Label(container, $"{UiRoot}.Body.StaffDetail.Inventory.{key}.Title", $"{UiRoot}.Body.StaffDetail.Inventory.{key}", $"{title} {(itemContainer == null ? 0 : itemContainer.itemList.Count)}", 9, "0.02 0.90", "0.98 0.995", TextAnchor.MiddleLeft);
var usableTop = 0.86;
var usableBottom = 0.05;
var gap = 0.01;
var slotW = (1.0 - gap * (columns + 1)) / columns;
var slotH = (usableTop - usableBottom - gap * (rows + 1)) / rows;
for (var visualSlot = 0; visualSlot < maxSlots; visualSlot++)
{
var col = visualSlot % columns;
var row = visualSlot / columns;
if (row >= rows) break;
var sx1 = gap + col * (slotW + gap);
var sy2 = usableTop - gap - row * (slotH + gap);
var sy1 = sy2 - slotH;
var sx2 = sx1 + slotW;
var actualSlot = startSlot + visualSlot;
var item = GetInventoryItemAt(itemContainer, actualSlot);
DrawInventorySlot(container, player, state, selected, key, actualSlot, item, sx1, sy1, sx2, sy2);
}
}
private void DrawInventorySlot(CuiElementContainer container, BasePlayer player, PanelState state, BasePlayer selected, string key, int slot, Item item, double x1, double y1, double x2, double y2)
{
var parent = $"{UiRoot}.Body.StaffDetail.Inventory.{key}";
var slotName = $"{parent}.Slot.{slot}";
var selectedSlot = item != null && state.StaffInventorySlot == slot;
Panel(container, slotName, parent, item == null ? "0.30 0.32 0.27 0.32" : selectedSlot ? "0.12 0.58 0.55 0.62" : "0.36 0.38 0.31 0.50", $"{x1:0.###} {y1:0.###}", $"{x2:0.###} {y2:0.###}");
if (item == null) return;
var label = item.info == null ? "unknown" : item.info.displayName?.english ?? item.info.shortname;
ItemImage(container, $"{slotName}.Icon", slotName, item, "0.15 0.25", "0.85 0.86");
if (item.amount > 1)
{
Panel(container, $"{slotName}.AmountShade", slotName, "0.02 0.025 0.03 0.70", "0.42 0.17", "0.98 0.38");
Label(container, $"{slotName}.Amount", slotName, $"x{FormatItemAmount(item.amount)}", 10, "0.40 0.16", "0.96 0.39", TextAnchor.MiddleRight);
}
Panel(container, $"{slotName}.NameShade", slotName, "0.02 0.025 0.03 0.68", "0.00 0.00", "1.00 0.17");
Label(container, $"{slotName}.Name", slotName, SlotLabel(label), 7, "0.04 0.00", "0.96 0.17", TextAnchor.MiddleCenter);
Button(container, $"{slotName}.Select", slotName, string.Empty, $"liveadmin.ui staffinvslot {key} {slot}", "0 0 0 0", "0.00 0.00", "1.00 1.00", 1);
}
private InventoryView GetInventoryView(BasePlayer selected, string key)
{
key = CleanInventoryTab(key);
if (key == "belt") return new InventoryView { Key = "belt", Title = "Belt", Container = selected.inventory?.containerBelt, Columns = 6, Rows = 1, MaxSlots = 6, GridBottom = 0.47, GridRight = 0.48 };
if (key == "wear") return new InventoryView { Key = "wear", Title = "Wear", Container = selected.inventory?.containerWear, Columns = 4, Rows = 2, MaxSlots = 8, GridBottom = 0.36, GridRight = 0.38 };
if (key == "backpack") return new InventoryView { Key = "backpack", Title = "Backpack", Container = GetBackpackContainer(selected), Columns = 10, Rows = 1, MaxSlots = 10, GridBottom = 0.47, GridRight = 0.72 };
return new InventoryView { Key = "main", Title = "Main", Container = selected.inventory?.containerMain, Columns = 10, Rows = 1, MaxSlots = 10, GridBottom = 0.47, GridRight = 0.72 };
}
private int InventoryVisibleRows(InventoryView view)
{
var count = view?.Container == null ? 0 : view.Container.itemList.Count;
var rowsNeeded = Math.Max(1, (int)Math.Ceiling(count / (double)Math.Max(1, view.Columns)));
return Math.Max(1, Math.Min(view.Rows, rowsNeeded));
}
private string CleanInventoryTab(string key)
{
key = (key ?? "main").ToLowerInvariant();
return key == "belt" || key == "wear" || key == "backpack" ? key : "main";
}
private string SlotLabel(string value)
{
return ShortenCompact(value, 16);
}
private Item GetInventoryItemAt(ItemContainer itemContainer, int slot)
{
if (itemContainer == null || slot < 0) return null;
return slot < itemContainer.itemList.Count ? itemContainer.itemList[slot] : null;
}
private string SelectedInventoryText(Item item, int slot)
{
if (item == null) return "Select an item";
var label = item.info == null ? "unknown" : item.info.displayName?.english ?? item.info.shortname;
return $"Slot {slot + 1}: {ShortenCompact(label, 18)}";
}
private string FormatItemAmount(int amount)
{
if (amount >= 1000000) return $"{amount / 1000000f:0.#}m";
if (amount >= 1000) return $"{amount / 1000f:0.#}k";
return amount.ToString();
}
private int ClampItemAmount(int amount)
{
return Math.Max(1, Math.Min(10000, amount));
}
private bool IsValidItemShortname(string value)
{
if (string.IsNullOrEmpty(value) || value.Length > 80) return false;
return value.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-');
}
private bool IsValidDuration(string value)
{
if (string.IsNullOrEmpty(value) || value.Length < 2) return false;
var unit = char.ToLowerInvariant(value[value.Length - 1]);
if (unit != 's' && unit != 'm' && unit != 'h' && unit != 'd') return false;
return int.TryParse(value.Substring(0, value.Length - 1), out var amount) && amount > 0;
}
private bool IsHttpUrlOrEmpty(string value)
{
return string.IsNullOrEmpty(value) || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
private bool IsValidUtcTime(string value)
{
if (string.IsNullOrEmpty(value)) return true;
var parts = value.Split(':');
if (parts.Length != 2) return false;
return int.TryParse(parts[0], out var hour) && int.TryParse(parts[1], out var minute) && hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59;
}
private bool IsSafeNameToken(string value)
{
return !string.IsNullOrEmpty(value) && value.Length <= 80 && value.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c == ':' || c == '/');
}
private bool IsSafeWipeSeed(string value)
{
if (string.IsNullOrEmpty(value)) return true;
if (value.Equals("random", StringComparison.OrdinalIgnoreCase)) return true;
return value.Length <= 12 && value.All(char.IsDigit);
}
private bool IsValidGroupName(string value)
{
return !string.IsNullOrEmpty(value) && value.Length <= 64 && value.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.');
}
private bool IsValidPermissionName(string value)
{
return !string.IsNullOrEmpty(value) && value.Length <= 120 && value.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == '*');
}
private void DrawInfoCard(CuiElementContainer container, string title, string line1, string line2, string line3, double x1, double y1, double x2, double y2)
{
Panel(container, $"{UiRoot}.Body.StaffDetail.InfoCard.{title}", $"{UiRoot}.Body.StaffDetail", "0.055 0.06 0.07 0.96", $"{x1} {y1}", $"{x2} {y2}");
Label(container, $"{UiRoot}.Body.StaffDetail.InfoCard.{title}.Title", $"{UiRoot}.Body.StaffDetail.InfoCard.{title}", title, 9, "0.05 0.66", "0.95 0.96", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffDetail.InfoCard.{title}.One", $"{UiRoot}.Body.StaffDetail.InfoCard.{title}", line1, 8, "0.05 0.40", "0.95 0.68", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffDetail.InfoCard.{title}.Two", $"{UiRoot}.Body.StaffDetail.InfoCard.{title}", line2, 8, "0.05 0.20", "0.95 0.48", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffDetail.InfoCard.{title}.Three", $"{UiRoot}.Body.StaffDetail.InfoCard.{title}", line3, 8, "0.05 0.00", "0.95 0.28", TextAnchor.MiddleLeft);
}
private void DrawInfoCardIn(CuiElementContainer container, string parent, string title, string line1, string line2, string line3, double x1, double y1, double x2, double y2)
{
var name = $"{parent}.InfoCard.{SafeName(title)}";
Panel(container, name, parent, "0.055 0.06 0.07 0.96", $"{x1} {y1}", $"{x2} {y2}");
Label(container, $"{name}.Title", name, title.ToUpperInvariant(), 8, "0.05 0.70", "0.95 0.96", TextAnchor.MiddleLeft);
Label(container, $"{name}.One", name, line1, 7, "0.05 0.45", "0.95 0.70", TextAnchor.MiddleLeft);
Label(container, $"{name}.Two", name, line2, 7, "0.05 0.22", "0.95 0.47", TextAnchor.MiddleLeft);
Label(container, $"{name}.Three", name, line3, 7, "0.05 0.00", "0.95 0.25", TextAnchor.MiddleLeft);
}
private void DrawWorldMetric(CuiElementContainer container, string label, int value, double x, double y)
{
var name = $"{UiRoot}.Body.Detail.World.{SafeName(label)}";
Panel(container, name, $"{UiRoot}.Body.Detail", "0.055 0.06 0.07 0.96", $"{x} {y}", $"{x + 0.205} {y + 0.13}");
Panel(container, $"{name}.Accent", name, activeSkin.Accent, "0 0", "0.012 1");
Label(container, $"{name}.Label", name, label.ToUpperInvariant(), 7, "0.08 0.58", "0.94 0.90", TextAnchor.MiddleLeft);
Label(container, $"{name}.Value", name, value.ToString(), 18, "0.08 0.05", "0.94 0.58", TextAnchor.MiddleLeft);
}
private PlayerWorldStats GetPlayerWorldStats(ulong userId)
{
var result = new PlayerWorldStats();
foreach (var networkable in BaseNetworkable.serverEntities)
{
var entity = networkable as BaseEntity;
if (entity == null || entity.IsDestroyed || entity.OwnerID != userId) continue;
result.Total++;
var prefab = (entity.ShortPrefabName ?? string.Empty).ToLowerInvariant();
if (entity is BuildingBlock) result.BuildingBlocks++;
else if (entity is BuildingPrivlidge) result.OwnedCupboards++;
else if (entity is AutoTurret) result.Turrets++;
else if (entity is SleepingBag) result.Respawns++;
else if (entity is BaseVehicle) result.Vehicles++;
else if (prefab.Contains("trap") || prefab.Contains("landmine") || prefab.Contains("bear") || prefab.Contains("sam_site") || prefab.Contains("guntrap")) result.Traps++;
else if (entity is StorageContainer) result.Storage++;
else result.Deployables++;
}
result.AuthorizedCupboards = BaseNetworkable.serverEntities.OfType<BuildingPrivlidge>().Count(tc => tc != null && !tc.IsDestroyed && tc.authorizedPlayers != null && tc.authorizedPlayers.Contains(userId));
return result;
}
private List<BuildingPrivlidge> GetPlayerCupboards(ulong userId)
{
return BaseNetworkable.serverEntities
.OfType<BuildingPrivlidge>()
.Where(tc => tc != null && !tc.IsDestroyed && (tc.OwnerID == userId || (tc.authorizedPlayers != null && tc.authorizedPlayers.Contains(userId))))
.OrderByDescending(tc => tc.OwnerID == userId)
.ThenBy(tc => tc.transform.position.x)
.ThenBy(tc => tc.transform.position.z)
.ToList();
}
private string FormatPosition(Vector3 position)
{
return $"{position.x:0}, {position.y:0}, {position.z:0}";
}
private string CleanPlayerDetailTab(string value)
{
value = (value ?? "overview").ToLowerInvariant();
return value == "world" || value == "cupboards" || value == "history" ? value : "overview";
}
private string CleanReportScope(string value)
{
value = (value ?? "active").ToLowerInvariant();
return value == "mine" || value == "high" || value == "resolved" ? value : "active";
}
private string ReportAgeText(ReportEntry report)
{
if (report == null || !DateTime.TryParse(report.CreatedAt, out var created)) return "unknown";
created = DateTime.SpecifyKind(created, DateTimeKind.Utc);
var age = DateTime.UtcNow - created;
if (age < TimeSpan.Zero) age = TimeSpan.Zero;
return $"{FormatDuration(age)}{(report.Status != "Resolved" && age.TotalHours >= 2 ? " OVERDUE" : string.Empty)}";
}
private void DrawStaffNotesTab(CuiElementContainer container, BasePlayer player, PanelState state, BasePlayer selected)
{
Label(container, $"{UiRoot}.Body.StaffDetail.NotesTab.Title", $"{UiRoot}.Body.StaffDetail", $"Notes for {selected.displayName}", 14, "0.04 0.89", "0.96 0.98", TextAnchor.MiddleLeft);
Panel(container, $"{UiRoot}.Body.StaffDetail.NotesTab.Box", $"{UiRoot}.Body.StaffDetail", "0.02 0.025 0.03 0.98", "0.04 0.80", "0.74 0.865");
Input(container, $"{UiRoot}.Body.StaffDetail.NotesTab.Input", $"{UiRoot}.Body.StaffDetail.NotesTab.Box", state.NoteText, "liveadmin.notefield", 8, "0.03 0", "0.97 1");
Button(container, $"{UiRoot}.Body.StaffDetail.NotesTab.Add", $"{UiRoot}.Body.StaffDetail", "Add Note", CanAny(player, PermOwner, PermPlayersModerate, PermPlayersNotes) ? $"liveadmin.ui act note {selected.UserIDString}" : string.Empty, config.AccentColor, "0.76 0.80", "0.96 0.865", 8);
var notes = GetNotes(selected.UserIDString).OrderByDescending(n => n.Time).Take(7).ToList();
var y = 0.68;
if (notes.Count == 0)
{
Label(container, $"{UiRoot}.Body.StaffDetail.NotesTab.Empty", $"{UiRoot}.Body.StaffDetail", "No notes for this player.", 10, "0.04 0.45", "0.96 0.55", TextAnchor.MiddleCenter);
return;
}
for (var i = 0; i < notes.Count; i++)
{
var note = notes[i];
Panel(container, $"{UiRoot}.Body.StaffDetail.NotesTab.Row.{i}", $"{UiRoot}.Body.StaffDetail", "0.10 0.11 0.12 1", $"0.04 {y}", $"0.96 {y + 0.075}");
Label(container, $"{UiRoot}.Body.StaffDetail.NotesTab.Row.{i}.Meta", $"{UiRoot}.Body.StaffDetail.NotesTab.Row.{i}", $"{note.Time} {note.StaffName}", 7, "0.025 0.48", "0.78 0.98", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.StaffDetail.NotesTab.Row.{i}.Text", $"{UiRoot}.Body.StaffDetail.NotesTab.Row.{i}", Shorten(note.Text, 78), 8, "0.025 0.02", "0.78 0.52", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.StaffDetail.NotesTab.Row.{i}.Del", $"{UiRoot}.Body.StaffDetail.NotesTab.Row.{i}", "Delete", CanAny(player, PermOwner, PermPlayersModerate, PermPlayersNotes) ? $"liveadmin.ui act deletenote {selected.UserIDString} {i}" : string.Empty, config.DangerColor, "0.82 0.16", "0.97 0.84", 7);
y -= 0.085;
}
}
private void DrawStaffGroupsTab(CuiElementContainer container, BasePlayer player, PanelState state, BasePlayer selected)
{
var playerGroups = new HashSet<string>(permission.GetUserGroups(selected.UserIDString), StringComparer.OrdinalIgnoreCase);
Label(container, $"{UiRoot}.Body.StaffDetail.GroupsTab.Title", $"{UiRoot}.Body.StaffDetail", $"Groups for {selected.displayName}", 14, "0.04 0.89", "0.96 0.98", TextAnchor.MiddleLeft);
DrawFilterStatus(container, $"{UiRoot}.Body.StaffDetail", "Groups", state.GroupFilter, "staffgroups", "0.04 0.80", "0.96 0.86");
var groups = GetRankedGroups().Where(g => MatchesFilter(g, state.GroupFilter)).ToList();
var pageSize = 8;
var page = groups.Skip(state.GroupPage * pageSize).Take(pageSize).ToList();
var y = 0.68;
foreach (var group in page)
{
var hasGroup = playerGroups.Contains(group);
Panel(container, $"{UiRoot}.Body.StaffDetail.GroupRow.{group}", $"{UiRoot}.Body.StaffDetail", hasGroup ? "0.10 0.18 0.12 1" : "0.10 0.11 0.12 1", $"0.04 {y}", $"0.96 {y + 0.065}");
Label(container, $"{UiRoot}.Body.StaffDetail.GroupRow.{group}.Name", $"{UiRoot}.Body.StaffDetail.GroupRow.{group}", $"{group}   Rank {permission.GetGroupRank(group)}", 9, "0.03 0", "0.58 1", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.StaffDetail.GroupRow.{group}.Add", $"{UiRoot}.Body.StaffDetail.GroupRow.{group}", "Grant", CanManageStaffGroups(player) ? $"liveadmin.ui act groupadd {selected.UserIDString} {group}" : string.Empty, config.SuccessColor, "0.64 0.16", "0.79 0.84", 8);
Button(container, $"{UiRoot}.Body.StaffDetail.GroupRow.{group}.Remove", $"{UiRoot}.Body.StaffDetail.GroupRow.{group}", "Remove", CanManageStaffGroups(player) ? $"liveadmin.ui act groupremove {selected.UserIDString} {group}" : string.Empty, config.DangerColor, "0.81 0.16", "0.97 0.84", 8);
y -= 0.078;
}
MiniPager(container, $"{UiRoot}.Body.StaffDetail.GroupPager", $"{UiRoot}.Body.StaffDetail", state.GroupPage, groups.Count, pageSize, "staffgroups", "0.04 0.04", "0.50 0.105");
}
private void DrawStaffTicketsTab(CuiElementContainer container, BasePlayer player, PanelState state, BasePlayer selected)
{
Label(container, $"{UiRoot}.Body.StaffDetail.TicketsTitle", $"{UiRoot}.Body.StaffDetail", $"Tickets for {selected.displayName}", 14, "0.04 0.89", "0.96 0.98", TextAnchor.MiddleLeft);
var tickets = storedData.Reports
.Where(r => r.TargetId == selected.UserIDString || r.ReporterId == selected.UserIDString)
.OrderByDescending(r => r.Id)
.Take(8)
.ToList();
var y = 0.78;
if (tickets.Count == 0)
{
Label(container, $"{UiRoot}.Body.StaffDetail.TicketsEmpty", $"{UiRoot}.Body.StaffDetail", "No tickets involving this player.", 11, "0.04 0.45", "0.96 0.55", TextAnchor.MiddleCenter);
return;
}
foreach (var ticket in tickets)
{
DrawTicketRow(container, $"{UiRoot}.Body.StaffDetail", ticket, y, true);
y -= 0.09;
}
}
private void DrawStaffField(CuiElementContainer container, string label, string key, string value, double y, double x1, double x2)
{
Label(container, $"{UiRoot}.Body.StaffDetail.{key}.Label", $"{UiRoot}.Body.StaffDetail", label, 8, $"{x1} {y + 0.065}", $"{x2} {y + 0.115}", TextAnchor.MiddleLeft);
Panel(container, $"{UiRoot}.Body.StaffDetail.{key}.Box", $"{UiRoot}.Body.StaffDetail", "0.02 0.025 0.03 0.98", $"{x1} {y}", $"{x2} {y + 0.06}");
Input(container, $"{UiRoot}.Body.StaffDetail.{key}.Input", $"{UiRoot}.Body.StaffDetail.{key}.Box", value, $"liveadmin.stafffield {key}", 8, "0.03 0", "0.97 1");
}
private void StaffToolButton(CuiElementContainer container, string label, BasePlayer selected, string action, double x, double y, bool enabled, bool danger = false)
{
var color = !enabled ? "0.12 0.12 0.12 0.65" : danger ? config.DangerColor : action == "passive" && passivePlayers.Contains(selected.userID) ? config.SuccessColor : config.AccentColor;
if (enabled && action == "passive" && passivePlayers.Contains(selected.userID)) color = config.SuccessColor;
Button(container, $"{UiRoot}.Body.StaffDetail.Tool.{action}", $"{UiRoot}.Body.StaffDetail", label, enabled ? $"liveadmin.ui act staffact {selected.UserIDString} {action}" : string.Empty, color, $"{x} {y}", $"{Math.Min(0.98, x + 0.13)} {y + 0.055}", 8);
}
private void DrawModerationTools(CuiElementContainer container, BasePlayer player, PanelState state, BasePlayer selected)
{
Label(container, $"{UiRoot}.Body.Detail.MuteTitle", $"{UiRoot}.Body.Detail", "Mute", 11, "0.05 0.265", "0.95 0.315", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Detail.MuteDuration.Label", $"{UiRoot}.Body.Detail", "Time", 8, "0.05 0.215", "0.15 0.255", TextAnchor.MiddleLeft);
Panel(container, $"{UiRoot}.Body.Detail.MuteDuration.Box", $"{UiRoot}.Body.Detail", "0.02 0.025 0.03 0.98", "0.15 0.205", "0.31 0.255");
Input(container, $"{UiRoot}.Body.Detail.MuteDuration.Input", $"{UiRoot}.Body.Detail.MuteDuration.Box", state.MuteDuration, "liveadmin.mutefield duration", 9, "0.05 0", "0.95 1");
Label(container, $"{UiRoot}.Body.Detail.MuteReason.Label", $"{UiRoot}.Body.Detail", "Reason", 8, "0.33 0.215", "0.45 0.255", TextAnchor.MiddleLeft);
Panel(container, $"{UiRoot}.Body.Detail.MuteReason.Box", $"{UiRoot}.Body.Detail", "0.02 0.025 0.03 0.98", "0.45 0.205", "0.72 0.255");
Input(container, $"{UiRoot}.Body.Detail.MuteReason.Input", $"{UiRoot}.Body.Detail.MuteReason.Box", state.MuteReason, "liveadmin.mutefield reason", 9, "0.05 0", "0.95 1");
Button(container, $"{UiRoot}.Body.Detail.MuteApply", $"{UiRoot}.Body.Detail", "Apply", CanMute(player) ? $"liveadmin.ui act mute {selected.UserIDString}" : string.Empty, config.WarningColor, "0.74 0.205", "0.92 0.255", 9);
Label(container, $"{UiRoot}.Body.Detail.ToolsTitle", $"{UiRoot}.Body.Detail", "Staff Tools", 10, "0.135 0.182", "0.95 0.202", TextAnchor.MiddleLeft);
var actions = GetPlayerActions(player).Take(4).ToList();
var actionIndex = 0;
foreach (var action in actions)
{
var xMin = actionIndex % 2 == 0 ? 0.05 : 0.52;
var xMax = actionIndex % 2 == 0 ? 0.45 : 0.92;
var yMin = actionIndex < 2 ? 0.075 : 0.012;
Button(container, $"{UiRoot}.Body.Detail.PlayerAction.{actionIndex}", $"{UiRoot}.Body.Detail", Shorten(action.Label, 16), $"liveadmin.ui act playercmd {selected.UserIDString} {actionIndex}", action.Confirm ? config.WarningColor : config.AccentColor, $"{xMin} {yMin}", $"{xMax} {yMin + 0.052}", 9);
actionIndex++;
}
}
private void DrawReports(CuiElementContainer container, BasePlayer player, PanelState state)
{
Header(container, "Tickets");
if (!Can(player, PermReportsView)) { NoAccess(container); return; }
state.ReportScope = CleanReportScope(state.ReportScope);
Button(container, $"{UiRoot}.Body.Tickets.Scope.Active", $"{UiRoot}.Body", "Active", "liveadmin.ui reportscope active", state.ReportScope == "active" ? config.AccentColor : "0.12 0.13 0.14 1", "0.04 0.835", "0.15 0.895", 9);
Button(container, $"{UiRoot}.Body.Tickets.Scope.Mine", $"{UiRoot}.Body", "My Queue", "liveadmin.ui reportscope mine", state.ReportScope == "mine" ? config.AccentColor : "0.12 0.13 0.14 1", "0.17 0.835", "0.30 0.895", 9);
Button(container, $"{UiRoot}.Body.Tickets.Scope.High", $"{UiRoot}.Body", "High", "liveadmin.ui reportscope high", state.ReportScope == "high" ? config.DangerColor : "0.12 0.13 0.14 1", "0.32 0.835", "0.43 0.895", 9);
Button(container, $"{UiRoot}.Body.Tickets.Scope.Resolved", $"{UiRoot}.Body", "Resolved", "liveadmin.ui reportscope resolved", state.ReportScope == "resolved" ? config.SuccessColor : "0.12 0.13 0.14 1", "0.45 0.835", "0.58 0.895", 9);
Label(container, $"{UiRoot}.Body.Tickets.QueueStats", $"{UiRoot}.Body", $"Open {storedData.Reports.Count(report => report.Status != "Resolved")}   High {storedData.Reports.Count(report => report.Status != "Resolved" && report.Priority == "High")}   Mine {storedData.Reports.Count(report => report.Status != "Resolved" && report.ClaimedBy == player.displayName)}", 8, "0.60 0.845", "0.96 0.89", TextAnchor.MiddleRight);
var reports = storedData.Reports
.Where(r => state.ReportScope == "resolved" ? r.Status == "Resolved" : state.ReportScope == "mine" ? r.Status != "Resolved" && r.ClaimedBy == player.displayName : state.ReportScope == "high" ? r.Status != "Resolved" && r.Priority == "High" : r.Status != "Resolved")
.Where(r => MatchesFilter(r.ReporterName, state.ReportFilter) || MatchesFilter(r.TargetName, state.ReportFilter) || MatchesFilter(r.Reason, state.ReportFilter) || MatchesFilter(r.Status, state.ReportFilter) || MatchesFilter(r.Priority, state.ReportFilter) || MatchesFilter(r.Id.ToString(), state.ReportFilter))
.OrderBy(r => r.Status == "Resolved" ? 1 : 0)
.ThenByDescending(r => r.Priority == "High" ? 2 : r.Priority == "Medium" ? 1 : 0)
.ThenByDescending(r => r.Id)
.ToList();
Panel(container, $"{UiRoot}.Body.Tickets.List", $"{UiRoot}.Body", "0.06 0.065 0.075 1", "0.04 0.08", "0.47 0.82");
DrawFilterStatus(container, $"{UiRoot}.Body.Tickets.List", "Tickets", state.ReportFilter, "reports", "0.04 0.90", "0.96 0.975");
var pageSize = 7;
state.TicketPage = Math.Min(state.TicketPage, Math.Max(0, (reports.Count - 1) / pageSize));
var page = reports.Skip(state.TicketPage * pageSize).Take(pageSize).ToList();
var y = 0.78;
foreach (var report in page)
{
DrawTicketRow(container, $"{UiRoot}.Body.Tickets.List", report, y, false);
y -= 0.095;
}
MiniPager(container, $"{UiRoot}.Body.Tickets.Pager", $"{UiRoot}.Body.Tickets.List", state.TicketPage, reports.Count, pageSize, "tickets", "0.04 0.02", "0.96 0.09");
var selected = reports.FirstOrDefault(r => r.Id == state.SelectedReportId) ?? reports.FirstOrDefault();
if (selected != null) state.SelectedReportId = selected.Id;
Panel(container, $"{UiRoot}.Body.Tickets.Detail", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.50 0.08", "0.96 0.82");
if (selected == null)
{
Label(container, $"{UiRoot}.Body.Tickets.Detail.Empty", $"{UiRoot}.Body.Tickets.Detail", "No tickets found.", 13, "0.04 0.45", "0.96 0.55", TextAnchor.MiddleCenter);
return;
}
DrawTicketDetail(container, player, state, selected);
}
private void DrawTicketRow(CuiElementContainer container, string parent, ReportEntry report, double y, bool compact)
{
var color = report.Status == "Resolved" ? "0.10 0.18 0.12 1" : report.Status == "Claimed" || report.Status == "Investigating" ? "0.13 0.18 0.28 1" : "0.25 0.18 0.09 1";
var height = compact ? 0.075 : 0.08;
var text = $"#{report.Id} [{report.Priority}/{report.Status}] {ReportAgeText(report)}  {Shorten(report.ReporterName, 14)} -> {Shorten(report.TargetName, 14)}    {Shorten(report.Reason, compact ? 38 : 48)}";
container.Add(new CuiButton
{
Button = { Command = $"liveadmin.ui selectreport {report.Id}", Color = ThemedColor(color) },
Text = { Text = text, FontSize = 8, Align = TextAnchor.MiddleLeft, Color = Skin().HeaderText },
RectTransform = { AnchorMin = $"0.04 {y}", AnchorMax = $"0.96 {y + height}" }
}, parent, $"{parent}.Ticket.{report.Id}");
}
private void DrawTicketDetail(CuiElementContainer container, BasePlayer player, PanelState state, ReportEntry report)
{
Label(container, $"{UiRoot}.Body.Tickets.Detail.Title", $"{UiRoot}.Body.Tickets.Detail", $"Ticket #{report.Id}", 16, "0.04 0.91", "0.40 0.99", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Tickets.Detail.State", $"{UiRoot}.Body.Tickets.Detail", $"{report.Status}   {report.Priority}", 11, "0.42 0.91", "0.96 0.99", TextAnchor.MiddleRight);
Label(container, $"{UiRoot}.Body.Tickets.Detail.People", $"{UiRoot}.Body.Tickets.Detail", $"{report.ReporterName} -> {report.TargetName}", 10, "0.04 0.82", "0.96 0.90", TextAnchor.MiddleLeft);
var canManage = CanManageTickets(player);
Button(container, $"{UiRoot}.Body.Tickets.Detail.ReporterProfile", $"{UiRoot}.Body.Tickets.Detail", "Reporter Profile", $"liveadmin.ui select {report.ReporterId}", config.AccentColor, "0.04 0.755", "0.22 0.81", 7);
Button(container, $"{UiRoot}.Body.Tickets.Detail.ReporterTp", $"{UiRoot}.Body.Tickets.Detail", "TP Reporter", Can(player, PermPlayersTeleport) ? $"liveadmin.ui act tpto {report.ReporterId}" : string.Empty, config.WarningColor, "0.24 0.755", "0.38 0.81", 7);
Button(container, $"{UiRoot}.Body.Tickets.Detail.TargetProfile", $"{UiRoot}.Body.Tickets.Detail", "Target Profile", $"liveadmin.ui select {report.TargetId}", config.AccentColor, "0.42 0.755", "0.60 0.81", 7);
Button(container, $"{UiRoot}.Body.Tickets.Detail.TargetTp", $"{UiRoot}.Body.Tickets.Detail", "TP Target", Can(player, PermPlayersTeleport) ? $"liveadmin.ui act tpto {report.TargetId}" : string.Empty, config.WarningColor, "0.62 0.755", "0.76 0.81", 7);
Label(container, $"{UiRoot}.Body.Tickets.Detail.Related", $"{UiRoot}.Body.Tickets.Detail", $"Related {storedData.Reports.Count(item => item.TargetId == report.TargetId)}   Warnings {storedData.Warnings.Count(item => item != null && item.TargetId == report.TargetId)}", 7, "0.78 0.755", "0.98 0.81", TextAnchor.MiddleRight);
Label(container, $"{UiRoot}.Body.Tickets.Detail.Reason", $"{UiRoot}.Body.Tickets.Detail", Shorten(report.Reason, 90), 9, "0.04 0.685", "0.96 0.745", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Tickets.Detail.Meta", $"{UiRoot}.Body.Tickets.Detail", $"Age {ReportAgeText(report)}   Created {report.CreatedAt}   Updated {report.UpdatedAt}   Claimed {report.ClaimedBy}", 8, "0.04 0.635", "0.96 0.685", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.Tickets.Detail.Claim", $"{UiRoot}.Body.Tickets.Detail", "Claim", canManage ? $"liveadmin.ui act reportstatus {report.Id} Claimed" : string.Empty, config.AccentColor, "0.04 0.57", "0.18 0.63", 8);
Button(container, $"{UiRoot}.Body.Tickets.Detail.Investigate", $"{UiRoot}.Body.Tickets.Detail", "Investigate", canManage ? $"liveadmin.ui act reportstatus {report.Id} Investigating" : string.Empty, config.WarningColor, "0.20 0.57", "0.38 0.63", 8);
Button(container, $"{UiRoot}.Body.Tickets.Detail.Resolve", $"{UiRoot}.Body.Tickets.Detail", "Resolve", canManage ? $"liveadmin.ui act reportstatus {report.Id} Resolved" : string.Empty, config.SuccessColor, "0.40 0.57", "0.56 0.63", 8);
Button(container, $"{UiRoot}.Body.Tickets.Detail.Reopen", $"{UiRoot}.Body.Tickets.Detail", "Reopen", canManage ? $"liveadmin.ui act reportstatus {report.Id} Open" : string.Empty, "0.12 0.13 0.14 1", "0.58 0.57", "0.72 0.63", 8);
Button(container, $"{UiRoot}.Body.Tickets.Detail.Delete", $"{UiRoot}.Body.Tickets.Detail", "Delete", canManage ? $"liveadmin.ui act deleteticket {report.Id}" : string.Empty, config.DangerColor, "0.74 0.57", "0.84 0.63", 8);
Button(container, $"{UiRoot}.Body.Tickets.Detail.Low", $"{UiRoot}.Body.Tickets.Detail", "Low", canManage ? $"liveadmin.ui act reportpriority {report.Id} Low" : string.Empty, "0.12 0.13 0.14 1", "0.86 0.57", "0.90 0.63", 7);
Button(container, $"{UiRoot}.Body.Tickets.Detail.Med", $"{UiRoot}.Body.Tickets.Detail", "Med", canManage ? $"liveadmin.ui act reportpriority {report.Id} Medium" : string.Empty, config.WarningColor, "0.91 0.57", "0.94 0.63", 7);
Button(container, $"{UiRoot}.Body.Tickets.Detail.High", $"{UiRoot}.Body.Tickets.Detail", "High", canManage ? $"liveadmin.ui act reportpriority {report.Id} High" : string.Empty, config.DangerColor, "0.95 0.57", "0.99 0.63", 7);
Panel(container, $"{UiRoot}.Body.Tickets.Detail.NoteBox", $"{UiRoot}.Body.Tickets.Detail", "0.02 0.025 0.03 0.98", "0.04 0.47", "0.76 0.54");
Input(container, $"{UiRoot}.Body.Tickets.Detail.NoteInput", $"{UiRoot}.Body.Tickets.Detail.NoteBox", state.TicketNote, "liveadmin.ticketnote", 8, "0.03 0", "0.97 1");
Button(container, $"{UiRoot}.Body.Tickets.Detail.NoteAdd", $"{UiRoot}.Body.Tickets.Detail", "Add Note", canManage ? $"liveadmin.ui act ticketnote {report.Id}" : string.Empty, config.AccentColor, "0.78 0.47", "0.97 0.54", 8);
Label(container, $"{UiRoot}.Body.Tickets.Detail.NotesTitle", $"{UiRoot}.Body.Tickets.Detail", "Ticket Notes", 10, "0.04 0.39", "0.96 0.45", TextAnchor.MiddleLeft);
var notes = (report.Notes ?? new List<TicketNote>()).OrderByDescending(n => n.Time).Take(4).ToList();
var y = 0.30;
if (notes.Count == 0) Label(container, $"{UiRoot}.Body.Tickets.Detail.NoNotes", $"{UiRoot}.Body.Tickets.Detail", "No ticket notes yet.", 9, "0.04 0.28", "0.96 0.34", TextAnchor.MiddleLeft);
for (var noteIndex = 0; noteIndex < notes.Count; noteIndex++)
{
var note = notes[noteIndex];
var noteName = $"{UiRoot}.Body.Tickets.Detail.Note.{noteIndex}";
Panel(container, noteName, $"{UiRoot}.Body.Tickets.Detail", "0.10 0.11 0.12 1", $"0.04 {y}", $"0.96 {y + 0.075}");
Label(container, $"{noteName}.Meta", noteName, $"{note.Time} {note.StaffName}", 7, "0.03 0.48", "0.97 0.98", TextAnchor.MiddleLeft);
Label(container, $"{noteName}.Text", noteName, Shorten(note.Text, 72), 8, "0.03 0.02", "0.97 0.52", TextAnchor.MiddleLeft);
y -= 0.085;
}
}
private void DrawChat(CuiElementContainer container, BasePlayer player, PanelState state)
{
Header(container, "Chat");
if (!CanViewChat(player)) { NoAccess(container); return; }
EnsureDataDefaults();
if (state.ChatSubTab != "blacklist" && state.ChatSubTab != "flagged" && state.ChatSubTab != "archived") state.ChatSubTab = "monitor";
Button(container, $"{UiRoot}.Body.Chat.Sub.Monitor", $"{UiRoot}.Body", "Monitor", "liveadmin.ui chatsubtab monitor", state.ChatSubTab == "monitor" ? config.AccentColor : "0.12 0.13 0.14 1", "0.04 0.835", "0.16 0.895", 10);
Button(container, $"{UiRoot}.Body.Chat.Sub.Flagged", $"{UiRoot}.Body", "Flagged", "liveadmin.ui chatsubtab flagged", state.ChatSubTab == "flagged" ? config.WarningColor : "0.12 0.13 0.14 1", "0.18 0.835", "0.30 0.895", 10);
Button(container, $"{UiRoot}.Body.Chat.Sub.Archived", $"{UiRoot}.Body", "Hidden", "liveadmin.ui chatsubtab archived", state.ChatSubTab == "archived" ? config.DangerColor : "0.12 0.13 0.14 1", "0.32 0.835", "0.44 0.895", 10);
Button(container, $"{UiRoot}.Body.Chat.Sub.Blacklist", $"{UiRoot}.Body", "Blacklist", "liveadmin.ui chatsubtab blacklist", state.ChatSubTab == "blacklist" ? config.AccentColor : "0.12 0.13 0.14 1", "0.46 0.835", "0.58 0.895", 10);
if (state.ChatSubTab == "blacklist")
{
DrawChatBlacklist(container, player, state);
return;
}
DrawFilterStatus(container, $"{UiRoot}.Body", "Chat", state.ChatFilter, "chat", "0.04 0.765", "0.58 0.815");
var totalChat = storedData.Chat.Count(c => c != null && !c.Deleted);
var blockedChat = storedData.Chat.Count(c => c != null && c.Blocked && !c.Deleted);
var deletedChat = storedData.Chat.Count(c => c != null && c.Deleted);
Label(container, $"{UiRoot}.Body.Chat.Stats", $"{UiRoot}.Body", $"Captured {totalChat}   Flagged {blockedChat}   Hidden {deletedChat}   Blacklist {config.ChatWordBlacklist.Count}", 8, "0.60 0.765", "0.96 0.815", TextAnchor.MiddleRight);
var messages = (storedData.Chat ?? new List<ChatEntry>())
.Where(c => c != null && (state.ChatSubTab == "archived" ? c.Deleted : !c.Deleted) && (state.ChatSubTab != "flagged" || c.Blocked) && (MatchesFilter(c.PlayerName, state.ChatFilter) || MatchesFilter(c.PlayerId, state.ChatFilter) || MatchesFilter(c.Message, state.ChatFilter) || MatchesFilter(c.Channel, state.ChatFilter) || MatchesFilter(c.MatchedWord, state.ChatFilter)))
.OrderByDescending(c => c.Time)
.ToList();
var pageSize = 10;
var maxPage = Math.Max(0, (messages.Count - 1) / pageSize);
state.ChatPage = Math.Min(state.ChatPage, maxPage);
var page = messages.Skip(state.ChatPage * pageSize).Take(pageSize).ToList();
Panel(container, $"{UiRoot}.Body.Chat.List", $"{UiRoot}.Body", "0.055 0.058 0.052 0.90", "0.04 0.14", "0.62 0.745");
var y = 0.88;
foreach (var entry in page)
{
var selected = state.SelectedChatId == entry.Id;
var color = entry.Blocked ? "0.26 0.08 0.06 0.86" : selected ? config.AccentColor : "0.075 0.080 0.072 0.86";
var row = $"{UiRoot}.Body.Chat.Row.{SafeName(entry.Id)}";
var text = $"{entry.Time}  {Shorten(entry.PlayerName, 18)}  [{entry.Channel}]    {Shorten(entry.Message, 76)}";
container.Add(new CuiButton
{
Button = { Command = $"liveadmin.ui selectchat {entry.Id}", Color = ThemedColor(color) },
Text = { Text = text, FontSize = 8, Align = TextAnchor.MiddleLeft, Color = Skin().HeaderText },
RectTransform = { AnchorMin = $"0.025 {y}", AnchorMax = $"0.975 {y + 0.075}" }
}, $"{UiRoot}.Body.Chat.List", row);
y -= 0.084;
}
Pager(container, state.ChatPage, messages.Count, pageSize, "chat");
Panel(container, $"{UiRoot}.Body.Chat.Detail", $"{UiRoot}.Body", "0.075 0.080 0.072 0.94", "0.64 0.14", "0.96 0.745");
var selectedEntry = messages.FirstOrDefault(entry => entry.Id == state.SelectedChatId) ?? page.FirstOrDefault();
if (selectedEntry == null)
{
Label(container, $"{UiRoot}.Body.Chat.Empty", $"{UiRoot}.Body.Chat.Detail", "No chat messages captured yet.", 11, "0.06 0.78", "0.94 0.92", TextAnchor.MiddleCenter);
return;
}
state.SelectedChatId = selectedEntry.Id;
var canManage = CanManageChat(player);
var playerWarnings = storedData.Warnings.Count(w => w != null && w.TargetId == selectedEntry.PlayerId);
var playerReports = storedData.Reports.Count(r => r != null && r.TargetId == selectedEntry.PlayerId && r.Status != "Resolved");
var playerMessages = storedData.Chat.Count(c => c != null && c.PlayerId == selectedEntry.PlayerId);
Label(container, $"{UiRoot}.Body.Chat.Detail.Title", $"{UiRoot}.Body.Chat.Detail", Shorten(selectedEntry.PlayerName, 22), 15, "0.05 0.90", "0.63 0.99", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.Chat.Detail.Profile", $"{UiRoot}.Body.Chat.Detail", "Player Profile", $"liveadmin.ui select {selectedEntry.PlayerId}", config.AccentColor, "0.68 0.91", "0.95 0.975", 8);
Label(container, $"{UiRoot}.Body.Chat.Detail.Id", $"{UiRoot}.Body.Chat.Detail", selectedEntry.PlayerId, 8, "0.05 0.84", "0.95 0.90", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Chat.Detail.Context", $"{UiRoot}.Body.Chat.Detail", $"Messages {playerMessages}   Warnings {playerWarnings}   Open reports {playerReports}   {(selectedEntry.Blocked ? "FLAGGED: " + selectedEntry.MatchedWord : selectedEntry.Channel)}", 7, "0.05 0.79", "0.95 0.84", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Chat.Detail.Msg", $"{UiRoot}.Body.Chat.Detail", Shorten(selectedEntry.Message, 120), 9, "0.05 0.71", "0.95 0.79", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.Chat.Warn", $"{UiRoot}.Body.Chat.Detail", "Warn", canManage ? $"liveadmin.ui act chatwarn {selectedEntry.Id}" : string.Empty, config.WarningColor, "0.05 0.63", "0.30 0.70", 8);
Button(container, $"{UiRoot}.Body.Chat.Timeout", $"{UiRoot}.Body.Chat.Detail", "Timeout", canManage ? $"liveadmin.ui act chattimeout {selectedEntry.Id}" : string.Empty, config.DangerColor, "0.35 0.63", "0.62 0.70", 8);
Button(container, $"{UiRoot}.Body.Chat.Delete", $"{UiRoot}.Body.Chat.Detail", selectedEntry.Deleted ? "Restore Msg" : "Hide Msg", canManage ? $"liveadmin.ui act {(selectedEntry.Deleted ? "chatrestore" : "chatdelete")} {selectedEntry.Id}" : string.Empty, selectedEntry.Deleted ? config.SuccessColor : config.DangerColor, "0.67 0.63", "0.95 0.70", 8);
if (string.IsNullOrEmpty(config.ChatDeleteCommand))
{
Label(container, $"{UiRoot}.Body.Chat.DeleteHint", $"{UiRoot}.Body.Chat.Detail", "Delete hides it here. Set ChatDeleteCommand for external chat removal.", 7, "0.05 0.595", "0.95 0.625", TextAnchor.MiddleLeft);
}
Label(container, $"{UiRoot}.Body.Chat.TimeoutLabel", $"{UiRoot}.Body.Chat.Detail", "Timeout duration", 8, "0.05 0.525", "0.38 0.585", TextAnchor.MiddleLeft);
Panel(container, $"{UiRoot}.Body.Chat.TimeoutBox", $"{UiRoot}.Body.Chat.Detail", "0.02 0.025 0.03 0.98", "0.40 0.525", "0.95 0.585");
Input(container, $"{UiRoot}.Body.Chat.TimeoutInput", $"{UiRoot}.Body.Chat.TimeoutBox", string.IsNullOrEmpty(state.ChatTimeoutText) ? config.ChatTimeoutDuration : state.ChatTimeoutText, "liveadmin.chatfield timeout", 8, "0.03 0", "0.97 1");
Panel(container, $"{UiRoot}.Body.Chat.ReplyBox", $"{UiRoot}.Body.Chat.Detail", "0.02 0.025 0.03 0.98", "0.05 0.415", "0.95 0.495");
Input(container, $"{UiRoot}.Body.Chat.ReplyInput", $"{UiRoot}.Body.Chat.ReplyBox", state.ChatReplyText, "liveadmin.chatfield reply", 8, "0.03 0", "0.97 1");
Button(container, $"{UiRoot}.Body.Chat.ReplySend", $"{UiRoot}.Body.Chat.Detail", "Send Reply", canManage ? $"liveadmin.ui act chatreply {selectedEntry.Id}" : string.Empty, config.AccentColor, "0.62 0.33", "0.95 0.395", 8);
var quickY = 0.235;
foreach (var reply in (config.ChatQuickReplies ?? new List<string>()).Take(4))
{
Button(container, $"{UiRoot}.Body.Chat.Quick.{SafeName(reply)}", $"{UiRoot}.Body.Chat.Detail", Shorten(reply, 42), canManage ? $"liveadmin.ui act chatreply {selectedEntry.Id} {reply}" : string.Empty, "0.12 0.13 0.14 1", $"0.05 {quickY}", $"0.95 {quickY + 0.055}", 7, TextAnchor.MiddleLeft);
quickY -= 0.065;
}
}
private void DrawChatBlacklist(CuiElementContainer container, BasePlayer player, PanelState state)
{
var canManage = CanManageChat(player);
Panel(container, $"{UiRoot}.Body.Chat.BlacklistPanel", $"{UiRoot}.Body", "0.055 0.058 0.052 0.92", "0.04 0.14", "0.96 0.745");
Label(container, $"{UiRoot}.Body.Chat.BlacklistPanel.Title", $"{UiRoot}.Body.Chat.BlacklistPanel", "Word Blacklist", 16, "0.04 0.88", "0.40 0.98", TextAnchor.MiddleLeft);
Panel(container, $"{UiRoot}.Body.Chat.BlacklistPanel.Box", $"{UiRoot}.Body.Chat.BlacklistPanel", "0.02 0.025 0.03 0.98", "0.04 0.76", "0.68 0.84");
Input(container, $"{UiRoot}.Body.Chat.BlacklistPanel.Input", $"{UiRoot}.Body.Chat.BlacklistPanel.Box", state.ChatBlacklistText, "liveadmin.chatfield blacklist", 9, "0.03 0", "0.97 1");
Button(container, $"{UiRoot}.Body.Chat.BlacklistPanel.Add", $"{UiRoot}.Body.Chat.BlacklistPanel", "Add Word", canManage ? "liveadmin.ui act chatblacklistadd selected" : string.Empty, config.AccentColor, "0.72 0.76", "0.94 0.84", 9);
var words = (config.ChatWordBlacklist ?? new List<string>()).OrderBy(w => w).ToList();
var y = 0.64;
if (words.Count == 0)
{
Label(container, $"{UiRoot}.Body.Chat.BlacklistPanel.Empty", $"{UiRoot}.Body.Chat.BlacklistPanel", "No blacklisted words configured.", 11, "0.04 0.58", "0.94 0.68", TextAnchor.MiddleLeft);
return;
}
foreach (var word in words.Take(8))
{
var row = $"{UiRoot}.Body.Chat.BlacklistPanel.Word.{SafeName(word)}";
Panel(container, row, $"{UiRoot}.Body.Chat.BlacklistPanel", "0.075 0.080 0.072 0.90", $"0.04 {y}", $"0.94 {y + 0.075}");
Label(container, $"{row}.Text", row, word, 10, "0.03 0", "0.70 1", TextAnchor.MiddleLeft);
Button(container, $"{row}.Remove", row, "Remove", canManage ? $"liveadmin.ui act chatblacklistremove {word}" : string.Empty, config.DangerColor, "0.76 0.16", "0.96 0.84", 8);
y -= 0.085;
}
}
private void DrawWipe(CuiElementContainer container, BasePlayer player)
{
Header(container, "Wipe");
if (!CanViewWipe(player)) { NoAccess(container); return; }
DrawAutoWipePanel(container, player);
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
var rowName = $"{UiRoot}.Body.Plugin.{SafeName(pluginName)}";
Panel(container, rowName, $"{UiRoot}.Body", "0.075 0.08 0.09 1", $"0.04 {y}", $"0.94 {y + 0.06}");
var loaded = plugins.Find(pluginName) != null;
Label(container, $"{rowName}.Name", rowName, $"{pluginName}   {(loaded ? "LOADED" : "UNLOADED")}", 11, "0.02 0", "0.46 1", TextAnchor.MiddleLeft);
Button(container, $"{rowName}.Config", rowName, "Config", CanViewPluginConfig(player) ? $"liveadmin.ui config {pluginName}" : string.Empty, "0.15 0.16 0.18 1", "0.46 0.18", "0.58 0.82", 10);
Button(container, $"{rowName}.Reload", rowName, "Reload", CanReloadPlugins(player) ? $"liveadmin.ui act plugin {pluginName} reload" : string.Empty, config.AccentColor, "0.60 0.18", "0.72 0.82", 10);
Button(container, $"{rowName}.Unload", rowName, "Unload", CanReloadPlugins(player) ? $"liveadmin.ui act plugin {pluginName} unload" : string.Empty, config.WarningColor, "0.74 0.18", "0.85 0.82", 10);
Button(container, $"{rowName}.Load", rowName, "Load", CanReloadPlugins(player) ? $"liveadmin.ui act plugin {pluginName} load" : string.Empty, config.SuccessColor, "0.87 0.18", "0.98 0.82", 10);
y -= 0.07;
}
Pager(container, state.PluginPage, pluginNames.Count, pageSize, "plugins");
}
private void DrawPluginConfig(CuiElementContainer container, BasePlayer player, PanelState state)
{
var pluginName = state.SelectedConfigPlugin;
if (!CanViewPluginConfig(player)) { NoAccess(container); return; }
Label(container, $"{UiRoot}.Body.Config.Title", $"{UiRoot}.Body", $"Plugin Config: {pluginName}", 20, "0.04 0.91", "0.65 0.985", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.Config.Back", $"{UiRoot}.Body", "Back", "liveadmin.ui configback", "0.12 0.13 0.14 1", "0.66 0.915", "0.76 0.975", 10);
Button(container, $"{UiRoot}.Body.Config.Restore", $"{UiRoot}.Body", "Restore Backup", CanRestorePluginConfig(player) && HasPluginConfigBackup(pluginName) ? $"liveadmin.ui act configrestore {pluginName}" : string.Empty, config.WarningColor, "0.77 0.915", "0.90 0.975", 9);
Button(container, $"{UiRoot}.Body.Config.Reload", $"{UiRoot}.Body", "Apply + Reload", CanReloadPlugins(player) ? $"liveadmin.ui act plugin {pluginName} reload" : string.Empty, config.AccentColor, "0.91 0.915", "0.99 0.975", 8);
var allRows = GetConfigEntries(pluginName);
var rows = allRows.Where(row => MatchesFilter(row.Key, state.ConfigFilter) || MatchesFilter(row.Value, state.ConfigFilter)).ToList();
if (rows.Count == 0)
{
Label(container, $"{UiRoot}.Body.Config.Empty", $"{UiRoot}.Body", allRows.Count == 0 ? "No configuration values were found, or the config file is missing." : "No config paths or values match this search.", 13, "0.04 0.44", "0.94 0.56", TextAnchor.MiddleCenter);
return;
}
DrawFilterStatus(container, $"{UiRoot}.Body", "Config paths / values", state.ConfigFilter, "configvalues", "0.04 0.865", "0.62 0.91");
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
if (row.Editable && row.Type != JTokenType.Boolean && CanEditPluginConfig(player))
{
Panel(container, $"{UiRoot}.Body.Config.Row.{row.Index}.Box", $"{UiRoot}.Body.Config.Row.{row.Index}", "0.02 0.025 0.03 0.98", "0.42 0.12", "0.75 0.88");
Input(container, $"{UiRoot}.Body.Config.Row.{row.Index}.Input", $"{UiRoot}.Body.Config.Row.{row.Index}.Box", row.Value, $"liveadmin.configset {row.Index}", 8, "0.03 0", "0.97 1");
}
else
{
Label(container, $"{UiRoot}.Body.Config.Row.{row.Index}.Value", $"{UiRoot}.Body.Config.Row.{row.Index}", row.Value, 9, "0.42 0", "0.75 1", TextAnchor.MiddleLeft);
}
if (row.Type == JTokenType.Boolean && CanEditPluginConfig(player))
{
Button(container, $"{UiRoot}.Body.Config.Row.{row.Index}.Toggle", $"{UiRoot}.Body.Config.Row.{row.Index}", "Toggle", $"liveadmin.ui configtoggle {row.Index}", config.AccentColor, "0.79 0.12", "0.96 0.88", 9);
}
else
{
Label(container, $"{UiRoot}.Body.Config.Row.{row.Index}.Type", $"{UiRoot}.Body.Config.Row.{row.Index}", row.Type == JTokenType.Array ? "JSON / || list" : row.Editable ? "Enter" : row.Kind, 8, "0.78 0", "0.97 1", TextAnchor.MiddleCenter);
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
Panel(container, $"{UiRoot}.Body.Groups.Create", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.04 0.755", "0.94 0.815");
Input(container, $"{UiRoot}.Body.Groups.Create.Name", $"{UiRoot}.Body.Groups.Create", state.GroupCreateName, "liveadmin.managefield groupname", 8, "0.02 0.12", "0.27 0.88");
Input(container, $"{UiRoot}.Body.Groups.Create.Title", $"{UiRoot}.Body.Groups.Create", state.GroupCreateTitle, "liveadmin.managefield grouptitle", 8, "0.29 0.12", "0.56 0.88");
Input(container, $"{UiRoot}.Body.Groups.Create.Rank", $"{UiRoot}.Body.Groups.Create", state.GroupCreateRank, "liveadmin.managefield grouprank", 8, "0.58 0.12", "0.70 0.88");
Button(container, $"{UiRoot}.Body.Groups.Create.Action", $"{UiRoot}.Body.Groups.Create", "Create Group", CanManageStaffGroups(player) && !string.IsNullOrWhiteSpace(state.GroupCreateName) ? "liveadmin.ui act groupcreate" : string.Empty, config.SuccessColor, "0.73 0.12", "0.98 0.88", 9);
var pageSize = 8;
var page = groups.Skip(state.GroupPage * pageSize).Take(pageSize).ToList();
var y = 0.61;
DrawFilterStatus(container, $"{UiRoot}.Body", "Groups", state.GroupFilter, "groups", "0.04 0.68", "0.94 0.735");
foreach (var group in page)
{
Panel(container, $"{UiRoot}.Body.Group.{group}", $"{UiRoot}.Body", "0.075 0.08 0.09 1", $"0.04 {y}", $"0.94 {y + 0.055}");
Label(container, $"{UiRoot}.Body.Group.{group}.Name", $"{UiRoot}.Body.Group.{group}", $"{group}   Rank {permission.GetGroupRank(group)}   Parent {permission.GetGroupParent(group)}", 11, "0.02 0", "0.64 1", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.Group.{group}.Perms", $"{UiRoot}.Body.Group.{group}", "Permissions", $"liveadmin.ui selectgroup {group}", config.AccentColor, "0.65 0.18", "0.82 0.82", 9);
var protectedGroup = config.ProtectedGroups.Any(g => g.Equals(group, StringComparison.OrdinalIgnoreCase));
Button(container, $"{UiRoot}.Body.Group.{group}.Delete", $"{UiRoot}.Body.Group.{group}", protectedGroup ? "Protected" : "Remove", CanManageStaffGroups(player) && !protectedGroup ? $"liveadmin.ui act groupdelete {group}" : string.Empty, protectedGroup ? "0.15 0.16 0.18 1" : config.DangerColor, "0.84 0.18", "0.98 0.82", 8);
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
Button(container, $"{UiRoot}.Body.Perms.Side.Plugin.{SafeName(pluginName)}", $"{UiRoot}.Body.Perms.Side", pluginLabel, $"liveadmin.ui selectplugin {pluginName}", pluginName == state.SelectedPlugin ? config.AccentColor : "0.12 0.13 0.14 1", $"0.08 {pluginY}", $"0.92 {pluginY + 0.075}", 10);
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
Label(container, $"{UiRoot}.Body.Perms.Detail.PresetsTitle", $"{UiRoot}.Body.Perms.Detail", "Role presets:", 9, "0.04 0.735", "0.18 0.785", TextAnchor.MiddleLeft);
DrawRolePresetButton(container, player, state.SelectedGroup, "Admin", 0.18, 0.735);
DrawRolePresetButton(container, player, state.SelectedGroup, "Staff", 0.34, 0.735);
DrawRolePresetButton(container, player, state.SelectedGroup, "Moderator", 0.50, 0.735);
Label(container, $"{UiRoot}.Body.Perms.Detail.Count", $"{UiRoot}.Body.Perms.Detail", $"Direct permissions: {currentPerms.Count}", 10, "0.68 0.735", "0.96 0.785", TextAnchor.MiddleRight);
var allPermissions = GetKnownPermissions()
.Where(p => state.SelectedPlugin == "all" || p.StartsWith(state.SelectedPlugin + ".", StringComparison.OrdinalIgnoreCase))
.Where(p => MatchesFilter(p, state.PermissionFilter))
.ToList();
var permPageSize = 6;
var permPage = allPermissions.Skip(state.PermissionPage * permPageSize).Take(permPageSize).ToList();
Label(container, $"{UiRoot}.Body.Perms.Detail.Total", $"{UiRoot}.Body.Perms.Detail", $"Registered permissions: {allPermissions.Count}", 10, "0.50 0.645", "0.96 0.70", TextAnchor.MiddleRight);
DrawFilterStatus(container, $"{UiRoot}.Body.Perms.Detail", "Permissions", state.PermissionFilter, "permissions", "0.04 0.575", "0.96 0.635");
var y = 0.46;
foreach (var permName in permPage)
{
var hasPerm = currentPerms.Contains(permName);
var rowName = $"{UiRoot}.Body.Perms.Detail.Row.{SafeName(permName)}";
Panel(container, rowName, $"{UiRoot}.Body.Perms.Detail", hasPerm ? "0.10 0.18 0.12 1" : "0.11 0.12 0.13 1", $"0.04 {y}", $"0.96 {y + 0.065}");
Label(container, $"{rowName}.Text", rowName, permName, 10, "0.02 0", "0.58 1", TextAnchor.MiddleLeft);
Button(container, $"{rowName}.Grant", rowName, "Grant", $"liveadmin.ui act grantgroup {state.SelectedGroup} {permName}", config.SuccessColor, "0.64 0.18", "0.78 0.82", 10);
Button(container, $"{rowName}.Revoke", rowName, "Revoke", $"liveadmin.ui act revokegroup {state.SelectedGroup} {permName}", config.DangerColor, "0.80 0.18", "0.96 0.82", 10);
y -= 0.078;
}
MiniPager(container, $"{UiRoot}.Body.Perms.Side.Pager", $"{UiRoot}.Body.Perms.Side", state.PermissionPluginPage, pluginsForPerms.Count, pluginPageSize, "permissionplugins", "0.08 0.02", "0.92 0.09");
MiniPager(container, $"{UiRoot}.Body.Perms.Detail.Pager", $"{UiRoot}.Body.Perms.Detail", state.PermissionPage, allPermissions.Count, permPageSize, "permissions", "0.04 0.02", "0.50 0.09");
}
private void DrawRolePresetButton(CuiElementContainer container, BasePlayer player, string group, string preset, double x, double y)
{
Button(container, $"{UiRoot}.Body.Perms.Detail.Preset.{preset}", $"{UiRoot}.Body.Perms.Detail", preset, Can(player, PermPermissionsManage) ? $"liveadmin.ui act applypreset {group} {preset}" : string.Empty, "0.12 0.13 0.14 1", $"{x} {y}", $"{x + 0.145} {y + 0.055}", 8);
}
private void DrawConVars(CuiElementContainer container, BasePlayer player, PanelState state)
{
Header(container, "ConVars");
if (!CanEditServerInfo(player)) { NoAccess(container); return; }
SeedConVarFields(state);
Panel(container, $"{UiRoot}.Body.ConVars.Main", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.04 0.16", "0.96 0.82");
Label(container, $"{UiRoot}.Body.ConVars.Title", $"{UiRoot}.Body.ConVars.Main", "Common Server ConVars", 15, "0.04 0.88", "0.55 0.98", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.ConVars.Hint", $"{UiRoot}.Body.ConVars.Main", "Changes are applied through console commands and written to server config where supported.", 9, "0.04 0.80", "0.94 0.88", TextAnchor.MiddleLeft);
DrawConVarField(container, "Hostname", "hostname", state.ConVarHostname, 0.66);
DrawConVarField(container, "Description", "description", state.ConVarDescription, 0.52);
DrawConVarField(container, "Server URL", "url", state.ConVarUrl, 0.38);
DrawConVarField(container, "Max Players", "maxplayers", state.ConVarMaxPlayers, 0.24);
DrawConVarField(container, "Save Interval", "saveinterval", state.ConVarSaveInterval, 0.10);
Button(container, $"{UiRoot}.Body.ConVars.Apply", $"{UiRoot}.Body.ConVars.Main", "Apply ConVars", "liveadmin.ui act convarsapply", config.SuccessColor, "0.76 0.88", "0.96 0.97", 10);
}
private void DrawAppearance(CuiElementContainer container, BasePlayer player)
{
Header(container, "Appearance");
Panel(container, $"{UiRoot}.Body.Appearance.Main", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.04 0.42", "0.96 0.82");
Label(container, $"{UiRoot}.Body.Appearance.Title", $"{UiRoot}.Body.Appearance.Main", "Interface Skin", 15, "0.04 0.82", "0.60 0.96", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Appearance.Hint", $"{UiRoot}.Body.Appearance.Main", "Skins only change LiveAdmin visuals. Your selection is stored per user.", 9, "0.04 0.70", "0.94 0.82", TextAnchor.MiddleLeft);
var skin = GetPlayerSkin(player);
var x = 0.04;
foreach (var option in GetSkins())
{
var active = option.Key == skin.Key;
Panel(container, $"{UiRoot}.Body.Appearance.Skin.{option.Key}", $"{UiRoot}.Body.Appearance.Main", active ? option.Accent : option.Panel, $"{x} 0.34", $"{x + 0.28} 0.62");
Label(container, $"{UiRoot}.Body.Appearance.Skin.{option.Key}.Name", $"{UiRoot}.Body.Appearance.Skin.{option.Key}", option.Name, 12, "0.06 0.48", "0.94 0.90", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Appearance.Skin.{option.Key}.Text", $"{UiRoot}.Body.Appearance.Skin.{option.Key}", active ? "Selected" : "Click to apply", 8, "0.06 0.14", "0.94 0.42", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.Appearance.Skin.{option.Key}.Button", $"{UiRoot}.Body.Appearance.Skin.{option.Key}", string.Empty, $"liveadmin.ui skin {option.Key}", "0 0 0 0", "0 0", "1 1", 1);
x += 0.31;
}
Panel(container, $"{UiRoot}.Body.Appearance.Accents", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.04 0.16", "0.96 0.38");
Label(container, $"{UiRoot}.Body.Appearance.Accents.Title", $"{UiRoot}.Body.Appearance.Accents", "Current Palette", 13, "0.04 0.70", "0.45 0.94", TextAnchor.MiddleLeft);
DrawSwatch(container, $"{UiRoot}.Body.Appearance.Accent", $"{UiRoot}.Body.Appearance.Accents", "Accent", Skin().Accent, 0.04);
DrawSwatch(container, $"{UiRoot}.Body.Appearance.Warning", $"{UiRoot}.Body.Appearance.Accents", "Warning", Skin().Warning, 0.24);
DrawSwatch(container, $"{UiRoot}.Body.Appearance.Danger", $"{UiRoot}.Body.Appearance.Accents", "Danger", Skin().Danger, 0.44);
DrawSwatch(container, $"{UiRoot}.Body.Appearance.Success", $"{UiRoot}.Body.Appearance.Accents", "Success", Skin().Success, 0.64);
}
private void DrawSettings(CuiElementContainer container, BasePlayer player)
{
Header(container, "Settings");
Panel(container, $"{UiRoot}.Body.Settings.Main", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.04 0.20", "0.96 0.82");
Label(container, $"{UiRoot}.Body.Settings.Title", $"{UiRoot}.Body.Settings.Main", "LiveAdmin Safety & Behaviour", 15, "0.04 0.88", "0.70 0.98", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Settings.Hint", $"{UiRoot}.Body.Settings.Main", Can(player, PermOwner) ? "Owner-only toggles for panel behaviour." : "Owner permission required to change these settings.", 9, "0.04 0.80", "0.94 0.88", TextAnchor.MiddleLeft);
DrawSettingsToggle(container, player, "Safe Mode", "Blocks high-risk actions while enabled.", config.SafeMode, "settingssafe", 0.62);
DrawSettingsToggle(container, player, "Command Safety", "Blocks unsafe command patterns and protected prefixes.", config.EnableCommandSafety, "settingscommandsafety", 0.44);
DrawSettingsToggle(container, player, "Global Chat Announcements", "Allows configured mute/unmute announcements to global chat.", config.AllowGlobalChatAnnouncements, "settingschatannounce", 0.26);
}
private void DrawLogs(CuiElementContainer container, BasePlayer player, PanelState state)
{
Header(container, "Logs");
if (!CanAny(player, PermOwner, PermAuditView)) { NoAccess(container); return; }
var logs = storedData.Logs.OrderByDescending(l => l.Time).Take(config.LogsPerPage).ToList();
var y = 0.78;
foreach (var log in logs)
{
var result = string.IsNullOrEmpty(log.Result) ? "Success" : log.Result;
var category = string.IsNullOrEmpty(log.Category) ? CategorizeAction(log.Action) : log.Category;
Label(container, $"{UiRoot}.Body.Log.{y}", $"{UiRoot}.Body", $"{log.Time}  [{result}/{category}]  {log.ActorName}  {log.Action}  {log.Target}  {log.Details}", 9, "0.04 " + y, "0.94 " + (y + 0.045), TextAnchor.MiddleLeft);
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
Panel(container, $"{UiRoot}.Body.HeaderAccent", $"{UiRoot}.Body", activeSkin.Accent, "0.04 0.925", "0.045 0.982");
Label(container, $"{UiRoot}.Body.Header", $"{UiRoot}.Body", text, 20, "0.058 0.925", "0.70 0.985", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.HeaderHint", $"{UiRoot}.Body", "LIVEADMIN  /  SERVER MANAGEMENT", 7, "0.70 0.938", "0.96 0.975", TextAnchor.MiddleRight);
Panel(container, $"{UiRoot}.Body.HeaderRule", $"{UiRoot}.Body", activeSkin.Slot, "0.04 0.905", "0.96 0.907");
Panel(container, $"{UiRoot}.Body.HeaderRuleAccent", $"{UiRoot}.Body", activeSkin.AccentAlt, "0.86 0.905", "0.96 0.907");
}
private void Stat(CuiElementContainer container, string label, string value, int index)
{
var x = 0.04 + index * 0.23;
Panel(container, $"{UiRoot}.Body.Stat.{index}", $"{UiRoot}.Body", "0.075 0.08 0.09 1", $"{x} 0.68", $"{x + 0.20} 0.82");
Panel(container, $"{UiRoot}.Body.Stat.{index}.Accent", $"{UiRoot}.Body.Stat.{index}", index % 2 == 0 ? activeSkin.Accent : activeSkin.AccentAlt, "0 0", "0.012 1");
Label(container, $"{UiRoot}.Body.Stat.{index}.Label", $"{UiRoot}.Body.Stat.{index}", label, 11, "0.06 0.55", "0.94 0.92", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Stat.{index}.Value", $"{UiRoot}.Body.Stat.{index}", value, 24, "0.06 0.08", "0.94 0.58", TextAnchor.MiddleLeft);
}
private void DrawResourceMonitor(CuiElementContainer container)
{
SampleResources(false);
var latest = resourceHistory.LastOrDefault() ?? new ResourceSnapshot();
var cpuLimit = GetCpuLimitPercent();
var memoryLimit = GetMemoryLimitMiB();
var diskLimit = GetDiskLimitGiB();
Panel(container, $"{UiRoot}.Body.Resources", $"{UiRoot}.Body", "0.075 0.08 0.09 1", "0.04 0.38", "0.72 0.64");
Label(container, $"{UiRoot}.Body.Resources.Title", $"{UiRoot}.Body.Resources", "Server Resources", 15, "0.025 0.82", "0.35 0.98", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Resources.Values", $"{UiRoot}.Body.Resources", $"Updated {latest.Time}", 8, "0.55 0.84", "0.97 0.98", TextAnchor.MiddleRight);
DrawConsoleGraphCard(container, $"{UiRoot}.Body.Resources.Cpu", $"{UiRoot}.Body.Resources", "CPU Load +", $"{latest.Cpu:0.00}% / {cpuLimit:0}%", resourceHistory.Select(s => s.Cpu).ToList(), cpuLimit, 0.025, 0.10, 0.315);
DrawConsoleGraphCard(container, $"{UiRoot}.Body.Resources.Memory", $"{UiRoot}.Body.Resources", "Memory", $"{FormatGiB(latest.MemoryMb / 1024f)} / {FormatGiB(memoryLimit / 1024f)}", resourceHistory.Select(s => s.MemoryMb).ToList(), memoryLimit, 0.342, 0.10, 0.632);
DrawConsoleGraphCard(container, $"{UiRoot}.Body.Resources.Disk", $"{UiRoot}.Body.Resources", "Disk +", $"{FormatGiB(latest.DiskGiB)} / {FormatGiB(diskLimit)}", resourceHistory.Select(s => s.DiskGiB).ToList(), diskLimit, 0.659, 0.10, 0.965);
}
private void DrawConsoleGraphCard(CuiElementContainer container, string name, string parent, string label, string value, List<float> values, float max, double x1, double y1, double x2)
{
Panel(container, name, parent, "0.045 0.04 0.07 1", $"{x1} {y1}", $"{x2} 0.78");
Panel(container, $"{name}.Head", name, "0.06 0.055 0.085 1", "0.02 0.80", "0.98 0.98");
Label(container, $"{name}.Head.Label", $"{name}.Head", label, 9, "0.025 0", "0.45 1", TextAnchor.MiddleLeft);
Label(container, $"{name}.Head.Value", $"{name}.Head", value, 9, "0.48 0", "0.975 1", TextAnchor.MiddleRight);
Panel(container, $"{name}.Plot", name, "0.035 0.03 0.05 1", "0.02 0.06", "0.98 0.75");
Label(container, $"{name}.Max", $"{name}.Plot", GraphMaxLabel(max, label), 6, "0.025 0.82", "0.25 0.98", TextAnchor.MiddleLeft);
Label(container, $"{name}.Mid", $"{name}.Plot", GraphMaxLabel(max / 2f, label), 6, "0.025 0.42", "0.25 0.56", TextAnchor.MiddleLeft);
Label(container, $"{name}.Zero", $"{name}.Plot", label.Contains("CPU") ? "0.00%" : label.Contains("Disk") ? "0GiB" : "0MiB", 6, "0.025 0.00", "0.25 0.14", TextAnchor.MiddleLeft);
Panel(container, $"{name}.GridTop", $"{name}.Plot", "0.28 0.30 0.36 0.55", "0.08 0.86", "0.98 0.865");
Panel(container, $"{name}.GridMid", $"{name}.Plot", "0.28 0.30 0.36 0.55", "0.08 0.48", "0.98 0.485");
Panel(container, $"{name}.GridBase", $"{name}.Plot", "0.28 0.30 0.36 0.55", "0.08 0.10", "0.98 0.105");
var recent = values.Skip(Math.Max(0, values.Count - 28)).ToList();
var count = Math.Max(1, recent.Count);
for (var i = 0; i < recent.Count; i++)
{
var height = Math.Max(0.01, Math.Min(1.0, recent[i] / Math.Max(1f, max)));
var barX1 = 0.09 + i * (0.88 / count);
var barX2 = barX1 + (0.80 / count);
Panel(container, $"{name}.Area.{i}", $"{name}.Plot", "0.06 0.36 0.48 0.70", $"{barX1} 0.105", $"{barX2} {0.105 + height * 0.62}");
Panel(container, $"{name}.Line.{i}", $"{name}.Plot", config.AccentColor, $"{barX1} {0.105 + height * 0.62}", $"{barX2} {0.115 + height * 0.62}");
}
}
private string GraphMaxLabel(float value, string label)
{
if (label.Contains("CPU")) return $"{value:0.00}%";
if (label.Contains("Disk")) return $"{value:0.##}GiB";
return $"{value:0}MiB";
}
private void DrawAutoWipePanel(CuiElementContainer container, BasePlayer player)
{
EnsureNextAutoWipe();
var canEdit = CanManageWipe(player);
var canForce = CanForceWipe(player);
var nextText = string.IsNullOrEmpty(storedData.NextAutoWipeUtc) ? "Not calculated" : storedData.NextAutoWipeUtc;
var nextUtc = ParseStoredWipeUtc(storedData.NextAutoWipeUtc);
var nextMode = nextUtc.HasValue ? ScheduledWipeMode(nextUtc.Value) : "Unknown";
var nextForce = FindNextScheduledForceWipe(DateTime.UtcNow);
var pendingText = string.IsNullOrEmpty(storedData.PendingWipeMode)
? "No wipe awaiting verification"
: $"Pending {storedData.PendingWipeMode} since {storedData.PendingWipeStartedUtc}";
var cadence = NormalizeWipeCadence(config.AutoWipeCadence, config.AutoWipeUseWeeklySchedule);
var scheduleText = cadence == "Interval"
? $"Every {config.AutoWipeIntervalDays} days at {config.AutoWipeTimeUtc} UTC"
: cadence == "TwiceWeekly"
? $"Twice weekly: {GetConfiguredSecondWipeWeekday()} + {GetConfiguredWipeWeekday()} at {config.AutoWipeTimeUtc} UTC"
: $"Weekly {GetConfiguredWipeWeekday()} at {config.AutoWipeTimeUtc} UTC";
Panel(container, $"{UiRoot}.Body.Wipe", $"{UiRoot}.Body", "0.055 0.058 0.052 0.94", "0.04 0.055", "0.96 0.875");
Label(container, $"{UiRoot}.Body.Wipe.Title", $"{UiRoot}.Body.Wipe", "Wipe Scheduler", 17, "0.035 0.92", "0.35 0.99", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Wipe.Next", $"{UiRoot}.Body.Wipe", $"Next: {nextText} UTC [{nextMode}]", 10, "0.38 0.925", "0.96 0.985", TextAnchor.MiddleRight);

Panel(container, $"{UiRoot}.Body.Wipe.Status", $"{UiRoot}.Body.Wipe", "0.075 0.080 0.072 0.94", "0.035 0.755", "0.965 0.90");
Label(container, $"{UiRoot}.Body.Wipe.Status.Title", $"{UiRoot}.Body.Wipe.Status", "Schedule", 13, "0.025 0.70", "0.30 0.96", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Wipe.Status.Plan", $"{UiRoot}.Body.Wipe.Status", $"{scheduleText} | Random seed | Size {config.AutoWipeMapSize}", 9, "0.025 0.43", "0.56 0.68", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Wipe.Status.Force", $"{UiRoot}.Body.Wipe.Status", nextForce.HasValue ? $"Monthly force {nextForce.Value:yyyy-MM-dd HH:mm} UTC | {pendingText}" : pendingText, 8, "0.025 0.14", "0.56 0.40", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.Wipe.Enabled", $"{UiRoot}.Body.Wipe.Status", config.AutoWipeEnabled ? "Scheduler ON" : "Scheduler OFF", canEdit ? "liveadmin.ui act wipetoggle" : string.Empty, config.AutoWipeEnabled ? config.SuccessColor : config.DangerColor, "0.59 0.58", "0.72 0.84", 8);
Button(container, $"{UiRoot}.Body.Wipe.ScheduleMode", $"{UiRoot}.Body.Wipe.Status", cadence == "TwiceWeekly" ? "Twice Weekly" : cadence, canEdit ? "liveadmin.ui act wipeschedule" : string.Empty, cadence == "TwiceWeekly" ? config.WarningColor : config.AccentColor, "0.735 0.58", "0.855 0.84", 8);
Button(container, $"{UiRoot}.Body.Wipe.FirstForce", $"{UiRoot}.Body.Wipe.Status", config.AutoWipeForceFirstWeekday ? "Monthly Force ON" : "Monthly Force OFF", canForce ? "liveadmin.ui act wipefirstforce" : string.Empty, config.AutoWipeForceFirstWeekday ? config.WarningColor : "0.14 0.15 0.14 1", "0.87 0.58", "0.985 0.84", 7);
Button(container, $"{UiRoot}.Body.Wipe.NormalBp", $"{UiRoot}.Body.Wipe.Status", config.AutoWipeBlueprints ? "Normal: Wipe BP" : "Normal: Keep BP", canEdit ? "liveadmin.ui act wipebp" : string.Empty, config.AutoWipeBlueprints ? config.WarningColor : "0.14 0.15 0.14 1", "0.59 0.16", "0.72 0.42", 7);
Button(container, $"{UiRoot}.Body.Wipe.ForceBp", $"{UiRoot}.Body.Wipe.Status", config.ForceWipeBlueprints ? "Force: Wipe BP" : "Force: Keep BP", canForce ? "liveadmin.ui act wipeforcebp" : string.Empty, config.ForceWipeBlueprints ? config.DangerColor : "0.14 0.15 0.14 1", "0.735 0.16", "0.855 0.42", 7);
Button(container, $"{UiRoot}.Body.Wipe.Offline", $"{UiRoot}.Body.Wipe.Status", config.ForceWipeOfflineCleanup ? "Offline Ready" : "Offline OFF", canForce ? "liveadmin.ui act wipedelete" : string.Empty, config.ForceWipeOfflineCleanup ? config.SuccessColor : config.DangerColor, "0.87 0.16", "0.985 0.42", 7);

Panel(container, $"{UiRoot}.Body.Wipe.Month", $"{UiRoot}.Body.Wipe", "0.075 0.080 0.072 0.94", "0.035 0.405", "0.49 0.735");
Label(container, $"{UiRoot}.Body.Wipe.Month.Title", $"{UiRoot}.Body.Wipe.Month", "Upcoming Wipes", 13, "0.04 0.84", "0.96 0.98", TextAnchor.MiddleLeft);
var previews = PreviewScheduledWipes(5);
var y = 0.66;
for (var i = 0; i < previews.Count; i++)
{
DrawWipeScheduleRow(container, previews[i], i, y);
y -= 0.16;
}

Panel(container, $"{UiRoot}.Body.Wipe.World", $"{UiRoot}.Body.Wipe", "0.075 0.080 0.072 0.94", "0.515 0.405", "0.965 0.735");
Label(container, $"{UiRoot}.Body.Wipe.World.Title", $"{UiRoot}.Body.Wipe.World", "Schedule Settings", 13, "0.04 0.84", "0.96 0.98", TextAnchor.MiddleLeft);
DrawWipeField(container, $"{UiRoot}.Body.Wipe.World", "Primary Day", "weekday", GetConfiguredWipeWeekday().ToString(), 0.55, 0.05, 0.30);
DrawWipeField(container, $"{UiRoot}.Body.Wipe.World", "UTC Time", "time", config.AutoWipeTimeUtc, 0.55, 0.37, 0.62);
if (cadence == "TwiceWeekly") DrawWipeField(container, $"{UiRoot}.Body.Wipe.World", "Second Day", "secondweekday", GetConfiguredSecondWipeWeekday().ToString(), 0.55, 0.69, 0.94);
else if (cadence == "Interval") DrawWipeField(container, $"{UiRoot}.Body.Wipe.World", "Interval Days", "interval", config.AutoWipeIntervalDays.ToString(), 0.55, 0.69, 0.94);
else Label(container, $"{UiRoot}.Body.Wipe.World.Single", $"{UiRoot}.Body.Wipe.World", "One scheduled wipe per week", 8, "0.69 0.55", "0.94 0.72", TextAnchor.MiddleLeft);
DrawWipeField(container, $"{UiRoot}.Body.Wipe.World", "Seed", "seed", config.AutoWipeSeed, 0.15, 0.05, 0.30);
DrawWipeField(container, $"{UiRoot}.Body.Wipe.World", "Map Size", "size", config.AutoWipeMapSize.ToString(), 0.15, 0.37, 0.62);
Button(container, $"{UiRoot}.Body.Wipe.Reset", $"{UiRoot}.Body.Wipe.World", "Recalculate Next", canEdit ? "liveadmin.ui act wipereset" : string.Empty, "0.14 0.15 0.14 1", "0.69 0.15", "0.82 0.33", 7);
Button(container, $"{UiRoot}.Body.Wipe.Force", $"{UiRoot}.Body.Wipe.World", "Force Wipe Now", canForce ? "liveadmin.ui act forcewipe" : string.Empty, config.DangerColor, "0.83 0.15", "0.96 0.33", 7);

Panel(container, $"{UiRoot}.Body.Wipe.Sequences", $"{UiRoot}.Body.Wipe", "0.075 0.080 0.072 0.94", "0.035 0.075", "0.965 0.375");
Label(container, $"{UiRoot}.Body.Wipe.Sequences.Title", $"{UiRoot}.Body.Wipe.Sequences", "Restart Sequences", 13, "0.025 0.82", "0.45 0.98", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Wipe.Sequences.Help", $"{UiRoot}.Body.Wipe.Sequences", "Use || between commands. Offline map/BP deletion is handled by runds.sh.", 8, "0.48 0.84", "0.975 0.98", TextAnchor.MiddleRight);
DrawWipeField(container, $"{UiRoot}.Body.Wipe.Sequences", "Normal Map Wipe", "autocommands", config.AutoWipeCommandText, 0.48, 0.025, 0.975);
DrawWipeField(container, $"{UiRoot}.Body.Wipe.Sequences", "Monthly Force Wipe", "forcecommands", config.ForceWipeCommandText, 0.14, 0.025, 0.975);
}
private void DrawWipeScheduleRow(CuiElementContainer container, DateTime wipeUtc, int index, double y)
{
var mode = ScheduledWipeMode(wipeUtc);
var force = mode == "ForceWipe";
var name = $"{UiRoot}.Body.Wipe.Month.Row.{index}";
Panel(container, name, $"{UiRoot}.Body.Wipe.Month", force ? "0.22 0.12 0.09 0.88" : "0.08 0.10 0.09 0.88", $"0.04 {y}", $"0.96 {y + 0.11}");
Label(container, $"{name}.Summary", name, $"{wipeUtc:yyyy-MM-dd HH:mm} UTC   |   {(force ? "FORCE" : "NORMAL")}   |   {(force ? (config.ForceWipeBlueprints ? "Map + blueprints" : "Map only") : (config.AutoWipeBlueprints ? "Map + blueprints" : "Map only"))}", 8, "0.025 0.12", "0.975 0.88", TextAnchor.MiddleLeft);
}
private void DrawWipeField(CuiElementContainer container, string parent, string label, string key, string value, double y, double x1, double x2)
{
Label(container, $"{parent}.{key}.Label", parent, label, 7, $"{x1} {y + 0.10}", $"{x2} {y + 0.18}", TextAnchor.MiddleLeft);
Panel(container, $"{parent}.{key}.Box", parent, "0.02 0.025 0.03 0.98", $"{x1} {y}", $"{x2} {y + 0.09}");
Input(container, $"{parent}.{key}.Input", $"{parent}.{key}.Box", value, $"liveadmin.wipefield {key}", 8, "0.03 0", "0.97 1");
}
private void DrawServerField(CuiElementContainer container, string label, string key, string value, double y)
{
Label(container, $"{UiRoot}.Body.ServerInfo.{key}.Label", $"{UiRoot}.Body.ServerInfo", label, 9, "0.035 " + y, "0.20 " + (y + 0.10), TextAnchor.MiddleLeft);
Panel(container, $"{UiRoot}.Body.ServerInfo.{key}.Box", $"{UiRoot}.Body.ServerInfo", "0.02 0.025 0.03 0.98", "0.22 " + y, "0.96 " + (y + 0.10));
Input(container, $"{UiRoot}.Body.ServerInfo.{key}.Input", $"{UiRoot}.Body.ServerInfo.{key}.Box", value, $"liveadmin.serverfield {key}", 9, "0.025 0", "0.975 1");
}
private void DrawConVarField(CuiElementContainer container, string label, string key, string value, double y)
{
Label(container, $"{UiRoot}.Body.ConVars.{key}.Label", $"{UiRoot}.Body.ConVars.Main", label, 10, $"0.04 {y}", $"0.22 {y + 0.08}", TextAnchor.MiddleLeft);
Panel(container, $"{UiRoot}.Body.ConVars.{key}.Box", $"{UiRoot}.Body.ConVars.Main", "0.02 0.025 0.03 0.98", $"0.24 {y}", $"0.94 {y + 0.08}");
Input(container, $"{UiRoot}.Body.ConVars.{key}.Input", $"{UiRoot}.Body.ConVars.{key}.Box", value, $"liveadmin.convarfield {key}", 9, "0.025 0", "0.975 1");
}
private void DrawSwatch(CuiElementContainer container, string name, string parent, string label, string color, double x)
{
Panel(container, $"{name}.Color", parent, color, $"{x} 0.24", $"{x + 0.06} 0.55");
Label(container, $"{name}.Label", parent, label, 9, $"{x + 0.07} 0.22", $"{x + 0.18} 0.58", TextAnchor.MiddleLeft);
}
private void DrawSettingsToggle(CuiElementContainer container, BasePlayer player, string label, string help, bool enabled, string action, double y)
{
Panel(container, $"{UiRoot}.Body.Settings.{action}", $"{UiRoot}.Body.Settings.Main", "0.055 0.06 0.07 0.96", $"0.04 {y}", $"0.96 {y + 0.12}");
Label(container, $"{UiRoot}.Body.Settings.{action}.Label", $"{UiRoot}.Body.Settings.{action}", label, 11, "0.03 0.48", "0.58 0.92", TextAnchor.MiddleLeft);
Label(container, $"{UiRoot}.Body.Settings.{action}.Help", $"{UiRoot}.Body.Settings.{action}", help, 8, "0.03 0.08", "0.66 0.46", TextAnchor.MiddleLeft);
Button(container, $"{UiRoot}.Body.Settings.{action}.Toggle", $"{UiRoot}.Body.Settings.{action}", enabled ? "Enabled" : "Disabled", Can(player, PermOwner) ? $"liveadmin.ui act {action}" : string.Empty, enabled ? config.SuccessColor : "0.12 0.13 0.14 1", "0.72 0.22", "0.94 0.78", 9);
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
private IEnumerable<UiSkin> GetSkins()
{
yield return new UiSkin
{
Key = "default",
Name = "Rust",
Overlay = "0.018 0.019 0.020 0.97",
Top = "0.070 0.068 0.061 0.99",
Nav = "0.045 0.044 0.041 0.99",
Body = "0.027 0.030 0.031 0.97",
Panel = "0.075 0.073 0.067 0.97",
PanelAlt = "0.105 0.094 0.076 0.98",
Slot = "0.29 0.27 0.22 0.34",
Input = "0.018 0.018 0.017 0.99",
Button = "0.105 0.102 0.094 0.98",
ButtonActive = "0.72 0.39 0.14 0.98",
ButtonDisabled = "0.10 0.10 0.095 0.58",
Accent = "0.91 0.45 0.12 1",
AccentAlt = "0.48 0.28 0.92 1",
Warning = "0.86 0.52 0.18 0.96",
Danger = "0.56 0.15 0.12 0.96",
Success = "0.34 0.50 0.30 0.96",
Text = "0.88 0.86 0.80 1",
MutedText = "0.60 0.58 0.53 1",
HeaderText = "0.98 0.96 0.90 1"
};
yield return new UiSkin
{
Key = "minimal",
Name = "Minimal",
Overlay = "0.020 0.023 0.025 0.96",
Top = "0.060 0.066 0.069 0.99",
Nav = "0.038 0.043 0.046 0.99",
Body = "0.028 0.032 0.035 0.97",
Panel = "0.085 0.090 0.095 0.94",
PanelAlt = "0.105 0.112 0.118 0.94",
Slot = "0.25 0.27 0.26 0.34",
Input = "0.018 0.020 0.026 0.96",
Button = "0.130 0.140 0.145 0.94",
ButtonActive = "0.22 0.44 0.42 0.94",
ButtonDisabled = "0.10 0.11 0.12 0.54",
Accent = "0.18 0.62 0.58 1",
AccentAlt = "0.48 0.30 0.94 1",
Warning = "0.74 0.50 0.22 0.94",
Danger = "0.60 0.20 0.18 0.94",
Success = "0.28 0.54 0.34 0.94",
Text = "0.90 0.91 0.88 1",
MutedText = "0.66 0.68 0.66 1",
HeaderText = "0.98 0.98 0.94 1"
};
yield return new UiSkin
{
Key = "contrast",
Name = "High",
Overlay = "0.000 0.000 0.000 0.98",
Top = "0.025 0.025 0.025 0.99",
Nav = "0.018 0.020 0.022 0.99",
Body = "0.012 0.014 0.016 0.98",
Panel = "0.030 0.035 0.038 0.98",
PanelAlt = "0.050 0.055 0.058 0.98",
Slot = "0.12 0.14 0.13 0.50",
Input = "0.000 0.000 0.000 0.99",
Button = "0.085 0.090 0.095 0.98",
ButtonActive = "0.78 0.42 0.12 0.99",
ButtonDisabled = "0.06 0.06 0.06 0.64",
Accent = "0.95 0.55 0.18 1",
AccentAlt = "0.52 0.30 1.00 1",
Warning = "1.00 0.72 0.22 1",
Danger = "0.92 0.16 0.12 1",
Success = "0.35 0.78 0.40 1",
Text = "1.00 0.98 0.90 1",
MutedText = "0.78 0.78 0.72 1",
HeaderText = "1.00 1.00 0.94 1"
};
}
private UiSkin GetSkin(string key)
{
return GetSkins().FirstOrDefault(s => s.Key.Equals(key ?? string.Empty, StringComparison.OrdinalIgnoreCase)) ?? GetSkins().First();
}
private UiSkin GetPlayerSkin(BasePlayer player)
{
EnsureDataDefaults();
if (player == null) return GetSkin("default");
return GetSkin(storedData.UserSkins.TryGetValue(player.UserIDString, out var skin) ? skin : "default");
}
private void SetPlayerSkin(BasePlayer player, string key)
{
if (player == null) return;
var skin = GetSkin(key);
storedData.UserSkins[player.UserIDString] = skin.Key;
SaveData();
}
private UiSkin Skin()
{
return activeSkin ?? GetSkin("default");
}
private string ThemedColor(string color)
{
var skin = Skin();
if (string.IsNullOrEmpty(color)) return skin.Panel;
if (color == "Overlay") return skin.Overlay;
if (NearlyColor(color, config.AccentColor) || NearlyColor(color, "0.12 0.58 0.55 1")) return skin.Accent;
if (NearlyColor(color, config.WarningColor) || NearlyColor(color, "0.96 0.62 0.18 1")) return skin.Warning;
if (NearlyColor(color, config.DangerColor) || NearlyColor(color, "0.82 0.20 0.18 1")) return skin.Danger;
if (NearlyColor(color, config.SuccessColor) || NearlyColor(color, "0.24 0.68 0.36 1")) return skin.Success;
if (color.StartsWith("0.03 0.035") || color.StartsWith("0.04 0.045")) return skin.Body;
if (color.StartsWith("0.08 0.09")) return skin.Top;
if (color.StartsWith("0.06 0.065")) return skin.Nav;
if (color.StartsWith("0.075 0.08") || color.StartsWith("0.055 0.06") || color.StartsWith("0.045 0.05")) return skin.Panel;
if (color.StartsWith("0.10 0.11") || color.StartsWith("0.12 0.13") || color.StartsWith("0.15 0.16") || color.StartsWith("0.18 0.19")) return skin.Button;
if (color.StartsWith("0.12 0.12") || color.StartsWith("0.10 0.10")) return skin.ButtonDisabled;
if (color.StartsWith("0.02 0.025") || color.StartsWith("0.010")) return skin.Input;
if (color.StartsWith("0.30 0.32") || color.StartsWith("0.36 0.38") || color.StartsWith("0.34 0.35")) return skin.Slot;
return color;
}
private bool NearlyColor(string a, string b)
{
return string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
}
private void Panel(CuiElementContainer container, string name, string parent, string color, string min, string max)
{
container.Add(new CuiPanel { Image = { Color = ThemedColor(color) }, RectTransform = { AnchorMin = min, AnchorMax = max }, CursorEnabled = true }, parent, name);
}
private void Label(CuiElementContainer container, string name, string parent, string text, int size, string min, string max, TextAnchor align)
{
container.Add(new CuiLabel { Text = { Text = text, FontSize = size, Align = align, Color = size >= 14 ? Skin().HeaderText : Skin().Text }, RectTransform = { AnchorMin = min, AnchorMax = max } }, parent, name);
}
private void Button(CuiElementContainer container, string name, string parent, string text, string command, string color, string min, string max, int size, TextAnchor align = TextAnchor.MiddleCenter)
{
var buttonColor = string.IsNullOrEmpty(command) ? Skin().ButtonDisabled : ThemedColor(color);
container.Add(new CuiButton { Button = { Command = command, Color = buttonColor }, Text = { Text = text, FontSize = size, Align = align, Color = Skin().HeaderText }, RectTransform = { AnchorMin = min, AnchorMax = max } }, parent, name);
}
private void Input(CuiElementContainer container, string name, string parent, string text, string command, int size, string min, string max)
{
container.Add(new CuiElement
{
Name = name,
Parent = parent,
Components =
{
new CuiInputFieldComponent { Text = text, FontSize = size, Align = TextAnchor.MiddleLeft, Color = Skin().Text, Command = command, CharsLimit = 256 },
new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max }
}
});
}
private void SpriteImage(CuiElementContainer container, string name, string parent, string sprite, string color, string min, string max)
{
container.Add(new CuiElement
{
Name = name,
Parent = parent,
Components =
{
new CuiImageComponent { Sprite = sprite, Color = color },
new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max }
}
});
}
private void ItemImage(CuiElementContainer container, string name, string parent, Item item, string min, string max)
{
if (item == null || item.info == null) return;
container.Add(new CuiElement
{
Name = name,
Parent = parent,
Components =
{
new CuiImageComponent { ItemId = item.info.itemid, SkinId = item.skin, Color = "1 1 1 0.95" },
new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max }
}
});
}
private bool Can(BasePlayer player, string perm)
{
if (player == null) return false;
if (string.IsNullOrEmpty(perm)) return true;
return player != null && (player.IsAdmin || permission.UserHasPermission(player.UserIDString, perm) || permission.UserHasPermission(player.UserIDString, PermOwner));
}
private bool CanAny(BasePlayer player, params string[] perms)
{
return perms != null && perms.Any(perm => Can(player, perm));
}
private bool CanLegacyOr(BasePlayer player, string legacyPerm, string newPerm)
{
return Can(player, PermOwner) || Can(player, newPerm) || Can(player, legacyPerm);
}
private bool CanKick(BasePlayer player) => CanLegacyOr(player, PermPlayersModerate, PermPlayersKick);
private bool CanBan(BasePlayer player) => Can(player, PermOwner) || Can(player, PermPlayersBan);
private bool CanMute(BasePlayer player) => CanLegacyOr(player, PermPlayersModerate, PermPlayersMute);
private bool CanEditServerInfo(BasePlayer player) => CanLegacyOr(player, PermServerActions, PermServerInfo);
private bool CanUseQuickActions(BasePlayer player) => CanLegacyOr(player, PermServerActions, PermServerQuickActions);
private bool CanViewWipe(BasePlayer player) => CanAny(player, PermOwner, PermServerWipeView, PermServerWipeManage, PermServerWipeForce);
private bool CanManageWipe(BasePlayer player) => Can(player, PermOwner) || Can(player, PermServerWipeManage);
private bool CanForceWipe(BasePlayer player) => Can(player, PermOwner) || Can(player, PermServerWipeForce);
private bool CanReloadPlugins(BasePlayer player) => CanLegacyOr(player, PermPluginsManage, PermPluginsReload);
private bool CanViewPluginConfig(BasePlayer player) => CanLegacyOr(player, PermPluginsView, PermPluginsConfigView);
private bool CanEditPluginConfig(BasePlayer player) => Can(player, PermOwner) || Can(player, PermPluginsConfigEdit);
private bool CanRestorePluginConfig(BasePlayer player) => Can(player, PermOwner) || Can(player, PermPluginsConfigRestore);
private bool CanViewInventory(BasePlayer player) => CanAny(player, PermOwner, PermPlayersInventoryView, PermStaffToolsInventory);
private bool CanModifyInventory(BasePlayer player) => CanAny(player, PermOwner, PermPlayersInventoryModify);
private bool CanManageStaffGroups(BasePlayer player) => CanAny(player, PermOwner, PermGroupsManage, PermStaffToolsGroups);
private bool CanManageTickets(BasePlayer player) => CanAny(player, PermOwner, PermReportsManage, PermStaffToolsTickets);
private bool CanViewChat(BasePlayer player) => CanAny(player, PermOwner, PermChatView, PermChatManage);
private bool CanManageChat(BasePlayer player) => CanAny(player, PermOwner, PermChatManage);
private bool IsRateLimited(BasePlayer player, string actionKey, double seconds = 0.75)
{
if (player == null || string.IsNullOrEmpty(actionKey)) return false;
var key = player.UserIDString + ":" + actionKey;
var now = DateTime.UtcNow;
if (rateLimits.TryGetValue(key, out var last) && (now - last).TotalSeconds < seconds)
{
Reply(player, "Please wait before repeating that action.");
LogBlocked(player, "Security", "RateLimited", actionKey, string.Empty, $"{seconds:0.##}s");
return true;
}
rateLimits[key] = now;
return false;
}
private bool BlockedBySafeMode(BasePlayer player, string action, string category)
{
if (!config.SafeMode) return false;
Reply(player, "Safe Mode is enabled. That high-risk action is blocked.");
LogBlocked(player, category, action, string.Empty, string.Empty, "Safe Mode");
return true;
}
private bool CanTargetPlayer(BasePlayer actor, string targetId, string action, bool disruptive = true)
{
var target = BasePlayer.FindByID(ToUlong(targetId)) ?? BasePlayer.FindSleeping(ToUlong(targetId));
var targetName = target?.displayName ?? targetId;
if (actor == null || string.IsNullOrEmpty(targetId)) return true;
if ((action == "kick" || action == "ban" || action == "kill") && actor.UserIDString == targetId)
{
Reply(actor, "LiveAdmin will not run that action on your own active session.");
LogBlocked(actor, "Security", action, targetId, targetName, "Self-target blocked");
return false;
}
if (!disruptive || Can(actor, PermOwner)) return true;
var groups = permission.GetUserGroups(targetId) ?? new string[0];
var protectedGroups = new HashSet<string>(config.ProtectedGroups ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
if (groups.Any(g => protectedGroups.Contains(g)))
{
Reply(actor, "That player is protected.");
LogBlocked(actor, "Security", action, targetId, targetName, "Protected target");
return false;
}
return true;
}
private bool CanSeeTab(BasePlayer player, string tab)
{
if (tab == "console") return CanUseQuickActions(player);
if (tab == "players") return Can(player, PermPlayersView);
if (tab == "staff") return CanAny(player, PermStaffToolsView, PermStaffToolsModerate, PermStaffToolsActions, PermStaffToolsInventory, PermStaffToolsGroups, PermStaffToolsTickets, PermStaffToolsFun, PermStaffToolsDangerous, PermPlayersInventoryView, PermPlayersNotes);
if (tab == "chat") return CanViewChat(player);
if (tab == "reports") return Can(player, PermReportsView);
if (tab == "manage") return Can(player, PermPluginsView) || Can(player, PermGroupsView) || Can(player, PermPermissionsView);
if (tab == "convars") return CanEditServerInfo(player);
if (tab == "wipe") return CanViewWipe(player);
if (tab == "appearance") return Can(player, PermUse);
if (tab == "settings") return Can(player, PermOwner);
if (tab == "logs") return CanAny(player, PermOwner, PermAuditView);
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
if (target == "staffplayer") target = "staffplayers";
if (target == "staffgroup") target = "staffgroups";
if (target == "report" || target == "ticket") target = "reports";
if (target == "chatmessage" || target == "chats") target = "chat";
if (target == "console") target = "console";
if (target == "convar") target = "convars";
if (target == "plugin") target = "plugins";
if (target == "config" || target == "configvalue") target = "configvalues";
if (target == "permplugin" || target == "permissionplugins") target = "permplugins";
if (target == "group") target = "groups";
if (target == "permission" || target == "perm" || target == "perms") target = "permissions";
if (target == "players")
{
state.PlayerFilter = value;
state.PlayerPage = 0;
state.Tab = "players";
return true;
}
if (target == "staffplayers")
{
state.PlayerFilter = value;
state.PlayerPage = 0;
state.Tab = "staff";
return true;
}
if (target == "staffgroups")
{
state.GroupFilter = value;
state.GroupPage = 0;
state.Tab = "staff";
state.StaffSubTab = "groups";
return true;
}
if (target == "reports")
{
state.ReportFilter = value;
state.TicketPage = 0;
state.Tab = "reports";
return true;
}
if (target == "chat")
{
state.ChatFilter = value;
state.ChatPage = 0;
state.Tab = "chat";
return true;
}
if (target == "console")
{
state.ConsoleFilter = value;
state.Tab = "console";
return true;
}
if (target == "convars")
{
state.ConVarFilter = value;
state.Tab = "convars";
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
if (target == "configvalues")
{
state.ConfigFilter = value;
state.ConfigPage = 0;
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
return false;
}
private void ClearFilter(PanelState state, string target)
{
if (target == "player") target = "players";
if (target == "staffplayer") target = "staffplayers";
if (target == "staffgroup") target = "staffgroups";
if (target == "report" || target == "ticket") target = "reports";
if (target == "chatmessage" || target == "chats") target = "chat";
if (target == "console") target = "console";
if (target == "convar") target = "convars";
if (target == "plugin") target = "plugins";
if (target == "config" || target == "configvalue") target = "configvalues";
if (target == "permplugin" || target == "permissionplugins") target = "permplugins";
if (target == "group") target = "groups";
if (target == "permission" || target == "perm" || target == "perms") target = "permissions";
if (target == "all")
{
state.PlayerFilter = string.Empty;
state.ReportFilter = string.Empty;
state.ChatFilter = string.Empty;
state.ConsoleFilter = string.Empty;
state.ConVarFilter = string.Empty;
state.PluginFilter = string.Empty;
state.ConfigFilter = string.Empty;
state.GroupFilter = string.Empty;
state.GroupDropdownFilter = string.Empty;
state.PermissionFilter = string.Empty;
ResetPages(state);
return;
}
if (target == "players" || target == "staffplayers" || (target == "current" && (state.Tab == "players" || state.Tab == "staff")))
{
state.PlayerFilter = string.Empty;
state.PlayerPage = 0;
}
else if (target == "plugins" || (target == "current" && state.Tab == "manage" && state.SubTab == "plugins"))
{
state.PluginFilter = string.Empty;
state.PluginPage = 0;
}
else if (target == "configvalues")
{
state.ConfigFilter = string.Empty;
state.ConfigPage = 0;
}
else if (target == "permplugins")
{
state.PluginFilter = string.Empty;
state.PermissionPluginPage = 0;
}
else if (target == "staffgroups")
{
state.GroupFilter = string.Empty;
state.GroupPage = 0;
}
else if (target == "reports" || (target == "current" && state.Tab == "reports"))
{
state.ReportFilter = string.Empty;
state.TicketPage = 0;
}
else if (target == "chat" || (target == "current" && state.Tab == "chat"))
{
state.ChatFilter = string.Empty;
state.ChatPage = 0;
}
else if (target == "console" || (target == "current" && state.Tab == "console"))
{
state.ConsoleFilter = string.Empty;
}
else if (target == "convars" || (target == "current" && state.Tab == "convars"))
{
state.ConVarFilter = string.Empty;
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
}
private void ResetPages(PanelState state)
{
state.PlayerPage = 0;
state.PluginPage = 0;
state.ConfigPage = 0;
state.GroupPage = 0;
state.PermissionGroupPage = 0;
state.PermissionPluginPage = 0;
state.PermissionPage = 0;
state.GroupDropdownPage = 0;
state.TicketPage = 0;
state.ChatPage = 0;
}
private void ChangePage(PanelState state, string pageKey, int delta)
{
if (pageKey == "players") state.PlayerPage = Math.Max(0, state.PlayerPage + delta);
else if (pageKey == "staffgroups") state.GroupPage = Math.Max(0, state.GroupPage + delta);
else if (pageKey == "tickets") state.TicketPage = Math.Max(0, state.TicketPage + delta);
else if (pageKey == "chat") state.ChatPage = Math.Max(0, state.ChatPage + delta);
else if (pageKey == "plugins") state.PluginPage = Math.Max(0, state.PluginPage + delta);
else if (pageKey == "config") state.ConfigPage = Math.Max(0, state.ConfigPage + delta);
else if (pageKey == "groups") state.GroupPage = Math.Max(0, state.GroupPage + delta);
else if (pageKey == "permissiongroups") state.PermissionGroupPage = Math.Max(0, state.PermissionGroupPage + delta);
else if (pageKey == "permissionplugins") state.PermissionPluginPage = Math.Max(0, state.PermissionPluginPage + delta);
else if (pageKey == "permissions") state.PermissionPage = Math.Max(0, state.PermissionPage + delta);
else if (state.Tab == "players") state.PlayerPage = Math.Max(0, state.PlayerPage + delta);
else if (state.Tab == "chat") state.ChatPage = Math.Max(0, state.ChatPage + delta);
else if (state.Tab == "manage" && state.SubTab == "plugins") state.PluginPage = Math.Max(0, state.PluginPage + delta);
else if (state.Tab == "manage" && state.SubTab == "groups") state.GroupPage = Math.Max(0, state.GroupPage + delta);
else if (state.Tab == "manage" && state.SubTab == "permissions") state.PermissionPage = Math.Max(0, state.PermissionPage + delta);
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
openPanels.Remove(player.userID);
CuiHelper.DestroyUi(player, UiRoot);
UnregisterUiBridge(player);
}
private void RegisterUiBridge(BasePlayer player)
{
if (player == null) return;
RaidlandsUiEscapeBridge?.Call("RegisterUi", player, this, UiRoot, nameof(OnRaidlandsUiBridgeClosed));
}
private void UnregisterUiBridge(BasePlayer player)
{
if (player == null) return;
RaidlandsUiEscapeBridge?.Call("UnregisterUi", player, this, UiRoot);
}
private void OnRaidlandsUiBridgeClosed(BasePlayer player, string reason)
{
if (player == null) return;
openPanels.Remove(player.userID);
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
if (!IsCommandAllowed(player, command, CategorizeAction(label))) return;
ConsoleSystem.Run(ConsoleSystem.Option.Server, command);
Log(player, "ServerCommand", label, command);
}
private bool RunPluginCommand(BasePlayer actor, BasePlayer target, string commandTemplate, string logAction)
{
if (actor == null || string.IsNullOrEmpty(commandTemplate)) return false;
var command = FormatPlayerCommand(commandTemplate, target ?? actor);
if (string.IsNullOrEmpty(command)) return false;
if (!IsCommandAllowed(actor, command, CategorizeAction(logAction))) return false;
try
{
ConsoleSystem.Run(ConsoleSystem.Option.Server, command);
Log(actor, logAction, (target ?? actor).UserIDString, command);
return true;
}
catch
{
return false;
}
}
private string FormatPlayerCommand(string commandTemplate, BasePlayer target)
{
if (target == null) return CleanCommandText(commandTemplate ?? string.Empty);
var name = target.displayName ?? target.UserIDString;
return CleanCommandText(commandTemplate ?? string.Empty)
.Replace("{id}", target.UserIDString)
.Replace("{steamid}", target.UserIDString)
.Replace("{userID}", target.UserIDString)
.Replace("{userid}", target.UserIDString)
.Replace("{name:q}", ConsoleArg(name))
.Replace("{target:q}", ConsoleArg(target.UserIDString))
.Replace("{name}", name)
.Replace("{username}", name);
}
private bool IsCommandAllowed(BasePlayer actor, string command, string category)
{
command = (command ?? string.Empty).Trim();
if (!config.EnableCommandSafety) return true;
if (string.IsNullOrEmpty(command))
{
LogBlocked(actor, "Security", "CommandSafety", string.Empty, string.Empty, "Empty command");
return false;
}
if (command.Contains("\n") || command.Contains("\r") || command.Contains(";"))
{
Reply(actor, "Command blocked by LiveAdmin safety rules.");
LogBlocked(actor, "Security", "CommandSafety", command, string.Empty, "Newline or semicolon");
return false;
}
var lower = command.ToLowerInvariant();
if (IsAuthLevel2(actor) && IsGroupPermissionCommand(lower)) return true;
var ownerBypass = actor != null && Can(actor, PermOwner) && config.AllowOwnerCommandSafetyBypass;
foreach (var prefix in config.BlockedCommandPrefixes ?? new List<string>())
{
if (string.IsNullOrEmpty(prefix)) continue;
if (lower.StartsWith(prefix.ToLowerInvariant()) && !ownerBypass)
{
Reply(actor, "Command blocked by LiveAdmin safety rules.");
LogBlocked(actor, "Security", "CommandSafety", command, string.Empty, "Blocked prefix: " + prefix);
return false;
}
}
foreach (var prefix in config.OwnerOnlyCommandPrefixes ?? new List<string>())
{
if (string.IsNullOrEmpty(prefix)) continue;
if (!lower.StartsWith(prefix.ToLowerInvariant())) continue;
if (Can(actor, PermOwner)) return true;
if (lower.StartsWith("oxide.reload") && CanReloadPlugins(actor)) return true;
if (lower.StartsWith("oxide.load") && CanReloadPlugins(actor)) return true;
if (lower.StartsWith("oxide.unload") && CanReloadPlugins(actor)) return true;
if ((lower.StartsWith("oxide.grant") || lower.StartsWith("oxide.revoke")) && Can(actor, PermPermissionsManage)) return true;
Reply(actor, "Command requires owner or a matching granular permission.");
LogBlocked(actor, "Security", "CommandSafety", command, string.Empty, "Owner-only prefix: " + prefix);
return false;
}
return true;
}
private bool IsAuthLevel2(BasePlayer player)
{
return player != null && player.net?.connection != null && player.net.connection.authLevel >= 2;
}
private bool IsGroupPermissionCommand(string lowerCommand)
{
if (string.IsNullOrEmpty(lowerCommand)) return false;
return lowerCommand.StartsWith("oxide.grant group ") || lowerCommand.StartsWith("oxide.revoke group ");
}
private void AddChatEntry(BasePlayer player, string message, string channel, bool blocked, string matchedWord)
{
EnsureDataDefaults();
storedData.Chat.Add(new ChatEntry
{
Id = DateTime.UtcNow.Ticks + ":" + player.UserIDString,
Time = Now(),
PlayerId = player.UserIDString,
PlayerName = player.displayName,
Message = message,
Channel = channel,
Blocked = blocked,
MatchedWord = matchedWord
});
var max = Math.Max(20, config.ChatMessagesStored);
if (storedData.Chat.Count > max)
{
storedData.Chat.RemoveRange(0, storedData.Chat.Count - max);
}
SaveData();
}
private string MatchBlacklistedWord(string message)
{
if (string.IsNullOrEmpty(message) || config.ChatWordBlacklist == null) return string.Empty;
var lower = message.ToLowerInvariant();
return config.ChatWordBlacklist.FirstOrDefault(word => !string.IsNullOrEmpty(word) && lower.Contains(word.ToLowerInvariant())) ?? string.Empty;
}
private void RefreshOpenPanels(string tab)
{
foreach (var player in BasePlayer.activePlayerList.ToArray())
{
if (player == null || !openPanels.Contains(player.userID)) continue;
var state = GetState(player);
if (state.Tab == tab) Draw(player);
}
}
private void RefreshConsoleViews()
{
foreach (var player in BasePlayer.activePlayerList.ToArray())
{
if (player == null || !openPanels.Contains(player.userID)) continue;
var state = GetState(player);
if (state.Tab == "console" || state.ConsoleMiniOpen) Draw(player);
}
}
private void RefreshWipeViews(BasePlayer actor = null)
{
if (actor != null && openPanels.Contains(actor.userID)) Draw(actor);
RefreshOpenPanels("wipe");
RefreshOpenPanels("dashboard");
RefreshConsoleViews();
}
private ChatEntry FindChatEntry(string id)
{
return storedData.Chat?.FirstOrDefault(c => c != null && c.Id == id);
}
private BasePlayer FindChatPlayer(ChatEntry entry)
{
return entry == null ? null : BasePlayer.FindByID(ToUlong(entry.PlayerId)) ?? BasePlayer.FindSleeping(ToUlong(entry.PlayerId));
}
private void DeleteChatEntry(BasePlayer actor, string id)
{
var entry = FindChatEntry(id);
if (entry == null) return;
entry.Deleted = true;
entry.DeletedBy = actor?.displayName ?? "server";
entry.DeletedAt = Now();
RunChatDeleteCommand(actor, entry);
Log(actor, "ChatDelete", entry.PlayerId, entry.Message);
SaveData();
RefreshOpenPanels("chat");
RefreshConsoleViews();
}
private void RestoreChatEntry(BasePlayer actor, string id)
{
var entry = FindChatEntry(id);
if (entry == null || !entry.Deleted) return;
entry.Deleted = false;
entry.DeletedBy = string.Empty;
entry.DeletedAt = string.Empty;
Log(actor, "ChatRestore", entry.PlayerId, entry.Message);
SaveData();
RefreshOpenPanels("chat");
RefreshConsoleViews();
}
private void RunChatDeleteCommand(BasePlayer actor, ChatEntry entry)
{
if (entry == null || string.IsNullOrEmpty(config.ChatDeleteCommand)) return;
var command = FormatChatCommand(config.ChatDeleteCommand, entry);
if (string.IsNullOrEmpty(command) || !IsCommandAllowed(actor, command, "Chat")) return;
ConsoleSystem.Run(ConsoleSystem.Option.Server, command);
}
private string FormatChatCommand(string template, ChatEntry entry)
{
if (entry == null) return CleanCommandText(template ?? string.Empty);
var message = entry.Message ?? string.Empty;
var name = entry.PlayerName ?? entry.PlayerId;
return CleanCommandText(template ?? string.Empty)
.Replace("{id}", entry.PlayerId ?? string.Empty)
.Replace("{steamid}", entry.PlayerId ?? string.Empty)
.Replace("{userid}", entry.PlayerId ?? string.Empty)
.Replace("{name:q}", ConsoleArg(name))
.Replace("{name}", name)
.Replace("{message:q}", ConsoleArg(message))
.Replace("{message}", message)
.Replace("{chatid:q}", ConsoleArg(entry.Id ?? string.Empty))
.Replace("{chatid}", entry.Id ?? string.Empty)
.Replace("{time:q}", ConsoleArg(entry.Time ?? string.Empty))
.Replace("{time}", entry.Time ?? string.Empty);
}
private void WarnChatPlayer(BasePlayer actor, string id, string message)
{
var entry = FindChatEntry(id);
var target = FindChatPlayer(entry);
if (entry == null || target == null) return;
var warning = string.IsNullOrEmpty(message) ? "Please keep chat respectful." : message;
target.ChatMessage($"<color=#d14a3c>Staff warning:</color> {warning}");
RecordWarning(actor, target, warning);
Log(actor, "ChatWarn", entry.PlayerId, warning);
RefreshOpenPanels("chat");
RefreshConsoleViews();
}
private void TimeoutChatPlayer(BasePlayer actor, string id)
{
var entry = FindChatEntry(id);
if (entry == null) return;
var target = FindChatPlayer(entry);
if (target != null && !CanTargetPlayer(actor, target.UserIDString, "chattimeout")) return;
var state = GetState(actor);
var oldDuration = state.MuteDuration;
var oldReason = state.MuteReason;
var duration = string.IsNullOrEmpty(state.ChatTimeoutText) ? config.ChatTimeoutDuration : state.ChatTimeoutText;
if (!IsValidDuration(duration))
{
Reply(actor, "Timeout duration must look like 30s, 10m, 2h, or 1d.");
LogFailed(actor, "Chat", "ChatTimeout", entry.PlayerId, entry.PlayerName, duration);
return;
}
state.MuteDuration = duration;
state.MuteReason = config.ChatTimeoutReason;
RunMuteAction(actor, entry.PlayerId);
state.MuteDuration = oldDuration;
state.MuteReason = oldReason;
Log(actor, "ChatTimeout", entry.PlayerId, $"{duration} {config.ChatTimeoutReason}");
RefreshOpenPanels("chat");
RefreshConsoleViews();
}
private void ReplyToChatPlayer(BasePlayer actor, string id, string message)
{
var entry = FindChatEntry(id);
var target = FindChatPlayer(entry);
if (entry == null || target == null) return;
var reply = string.IsNullOrEmpty(message) ? GetState(actor).ChatReplyText : message;
if (string.IsNullOrEmpty(reply)) return;
target.ChatMessage($"<color=#1faaa4>Staff reply:</color> {reply}");
Log(actor, "ChatReply", entry.PlayerId, reply);
RefreshOpenPanels("chat");
RefreshConsoleViews();
}
private void AddChatBlacklistWord(BasePlayer actor, string word)
{
word = CleanCommandText(word ?? string.Empty).Trim();
if (string.IsNullOrEmpty(word) || word.Length > 40) return;
if (config.ChatWordBlacklist == null) config.ChatWordBlacklist = new List<string>();
if (!config.ChatWordBlacklist.Any(w => w.Equals(word, StringComparison.OrdinalIgnoreCase)))
{
config.ChatWordBlacklist.Add(word);
config.ChatWordBlacklist = UniqueList(config.ChatWordBlacklist);
SaveConfig();
Log(actor, "ChatBlacklistAdd", word, string.Empty);
RefreshOpenPanels("chat");
RefreshConsoleViews();
}
}
private void RemoveChatBlacklistWord(BasePlayer actor, string word)
{
if (string.IsNullOrEmpty(word) || config.ChatWordBlacklist == null) return;
config.ChatWordBlacklist.RemoveAll(w => w.Equals(word, StringComparison.OrdinalIgnoreCase));
SaveConfig();
Log(actor, "ChatBlacklistRemove", word, string.Empty);
RefreshOpenPanels("chat");
RefreshConsoleViews();
}
private void ApplyRolePresetToGroup(BasePlayer actor, string group, string presetName)
{
if (!IsValidGroupName(group))
{
Reply(actor, "Invalid group name.");
return;
}
var preset = (config.RolePresets ?? new List<RolePreset>()).FirstOrDefault(p => p != null && p.Name.Equals(presetName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
if (preset == null)
{
Reply(actor, "Preset not found.");
return;
}
var granted = 0;
foreach (var perm in UniqueList(preset.Permissions ?? new List<string>()))
{
if (!IsValidPermissionName(perm)) continue;
if (IsDangerousPermission(perm) && !Can(actor, PermOwner) && !IsAuthLevel2(actor)) continue;
RunServerCommand(actor, $"oxide.grant group {ConsoleArg(group)} {ConsoleArg(perm)}", $"grant preset {preset.Name}");
granted++;
}
Reply(actor, $"Applied {granted} permissions from {preset.Name} to {group}.");
Log(actor, "ApplyRolePreset", group, $"{preset.Name} {granted}");
RefreshOpenPanels("manage");
}
private void ToggleVanish(BasePlayer actor)
{
if (actor == null) return;
if (Vanish == null)
{
RunPluginCommand(actor, actor, config.VanishCommand, "StaffVanishCommand");
return;
}
try
{
var invisible = Vanish.Call("IsInvisible", actor);
if (invisible is bool && (bool)invisible)
{
Vanish.Call("Reappear", actor);
Log(actor, "StaffVanishOff", actor.UserIDString, actor.displayName);
}
else
{
Vanish.Call("Disappear", actor);
Log(actor, "StaffVanishOn", actor.UserIDString, actor.displayName);
}
}
catch
{
RunPluginCommand(actor, actor, config.VanishCommand, "StaffVanishCommand");
}
}
private void SeedServerFields(PanelState state)
{
if (string.IsNullOrEmpty(state.ServerName)) state.ServerName = ConVar.Server.hostname ?? string.Empty;
if (string.IsNullOrEmpty(state.ServerDescription)) state.ServerDescription = ConVar.Server.description ?? string.Empty;
if (string.IsNullOrEmpty(state.ServerUrl)) state.ServerUrl = ConVar.Server.url ?? string.Empty;
if (string.IsNullOrEmpty(state.ServerHeaderImage)) state.ServerHeaderImage = ConVar.Server.headerimage ?? string.Empty;
}
private void SeedConVarFields(PanelState state)
{
if (string.IsNullOrEmpty(state.ConVarHostname)) state.ConVarHostname = ConVar.Server.hostname ?? string.Empty;
if (string.IsNullOrEmpty(state.ConVarDescription)) state.ConVarDescription = ConVar.Server.description ?? string.Empty;
if (string.IsNullOrEmpty(state.ConVarUrl)) state.ConVarUrl = ConVar.Server.url ?? string.Empty;
if (string.IsNullOrEmpty(state.ConVarMaxPlayers)) state.ConVarMaxPlayers = ConVar.Server.maxplayers.ToString();
if (string.IsNullOrEmpty(state.ConVarSaveInterval)) state.ConVarSaveInterval = "300";
}
private void ApplyServerInfo(BasePlayer player)
{
var state = GetState(player);
SeedServerFields(state);
if (!IsHttpUrlOrEmpty(state.ServerUrl) || !IsHttpUrlOrEmpty(state.ServerHeaderImage))
{
Reply(player, "Server URL and header image must start with http:// or https://.");
LogFailed(player, "Config", "ServerInfo", "server", string.Empty, "Invalid URL");
return;
}
RunServerCommand(player, $"server.hostname {ConsoleArg(state.ServerName)}", "set server name");
RunServerCommand(player, $"server.description {ConsoleArg(state.ServerDescription)}", "set server description");
RunServerCommand(player, $"server.url {ConsoleArg(state.ServerUrl)}", "set server url");
RunServerCommand(player, $"server.headerimage {ConsoleArg(state.ServerHeaderImage)}", "set server header image");
RunServerCommand(player, "server.writecfg", "write server config");
Reply(player, "Server details applied.");
}
private void ApplyConVars(BasePlayer player)
{
var state = GetState(player);
SeedConVarFields(state);
if (!IsHttpUrlOrEmpty(state.ConVarUrl))
{
Reply(player, "Server URL must be blank or start with http:// or https://");
LogFailed(player, "Config", "ConVars", "server", string.Empty, "Invalid URL");
return;
}
if (!int.TryParse(state.ConVarMaxPlayers, out var maxPlayers) || maxPlayers < 1 || maxPlayers > 1000)
{
Reply(player, "Max players must be between 1 and 1000.");
return;
}
if (!int.TryParse(state.ConVarSaveInterval, out var saveInterval) || saveInterval < 30 || saveInterval > 3600)
{
Reply(player, "Save interval must be between 30 and 3600 seconds.");
return;
}
RunServerCommand(player, $"server.hostname {ConsoleArg(state.ConVarHostname)}", "set convar server.hostname");
RunServerCommand(player, $"server.description {ConsoleArg(state.ConVarDescription)}", "set convar server.description");
RunServerCommand(player, $"server.url {ConsoleArg(state.ConVarUrl)}", "set convar server.url");
RunServerCommand(player, $"server.maxplayers {maxPlayers}", "set convar server.maxplayers");
RunServerCommand(player, $"server.saveinterval {saveInterval}", "set convar server.saveinterval");
RunServerCommand(player, "server.writecfg", "write server config");
Log(player, "ConVarsApply", "server", $"maxplayers {maxPlayers}, saveinterval {saveInterval}");
Reply(player, "ConVars applied.");
}
private void SampleResources(bool refreshDashboard = true)
{
try
{
if (currentProcess == null) currentProcess = Process.GetCurrentProcess();
currentProcess.Refresh();
var now = DateTime.UtcNow;
var cpu = 0f;
if (lastResourceSampleAt != DateTime.MinValue)
{
var elapsed = (now - lastResourceSampleAt).TotalMilliseconds;
var cpuMs = (currentProcess.TotalProcessorTime - lastCpuTime).TotalMilliseconds;
if (elapsed > 0) cpu = (float)Math.Max(0, Math.Min(GetCpuLimitPercent(), cpuMs / elapsed * 100));
}
lastResourceSampleAt = now;
lastCpuTime = currentProcess.TotalProcessorTime;
var memory = currentProcess.WorkingSet64 / 1024f / 1024f;
var disk = GetDiskUsedGiB();
resourceHistory.Add(new ResourceSnapshot { Cpu = cpu, MemoryMb = memory, StorageUsed = disk, DiskGiB = disk, Time = Now() });
if (resourceHistory.Count > 60) resourceHistory.RemoveRange(0, resourceHistory.Count - 60);
if (refreshDashboard && (now - lastDashboardRefreshAt).TotalSeconds >= 30)
{
lastDashboardRefreshAt = now;
RefreshOpenPanels("dashboard");
}
}
catch
{
// Resource sampling is best-effort and should never interrupt the admin panel.
}
}
private float GetCpuLimitPercent()
{
var cgroupLimit = ReadCgroupCpuLimitPercent();
if (cgroupLimit > 0) return cgroupLimit;
return Math.Max(100f, Environment.ProcessorCount * 100f);
}
private float GetMemoryLimitMiB()
{
var cgroupLimit = ReadCgroupMemoryLimitMiB();
if (cgroupLimit > 0) return cgroupLimit;
try
{
return Math.Max(1024f, SystemInfo.systemMemorySize);
}
catch
{
return Math.Max(1024f, resourceHistory.Count == 0 ? 1024f : resourceHistory.Max(s => s.MemoryMb));
}
}
private float ReadCgroupCpuLimitPercent()
{
try
{
var cpuMax = "/sys/fs/cgroup/cpu.max";
if (File.Exists(cpuMax))
{
var parts = File.ReadAllText(cpuMax).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
if (parts.Length >= 2 && parts[0] != "max")
{
var quota = ToFloat(parts[0]);
var period = ToFloat(parts[1]);
if (quota > 0 && period > 0) return quota / period * 100f;
}
}
var quotaPath = "/sys/fs/cgroup/cpu/cpu.cfs_quota_us";
var periodPath = "/sys/fs/cgroup/cpu/cpu.cfs_period_us";
if (File.Exists(quotaPath) && File.Exists(periodPath))
{
var quota = ToFloat(File.ReadAllText(quotaPath).Trim());
var period = ToFloat(File.ReadAllText(periodPath).Trim());
if (quota > 0 && period > 0) return quota / period * 100f;
}
}
catch
{
}
return 0f;
}
private float ReadCgroupMemoryLimitMiB()
{
try
{
var memoryMax = "/sys/fs/cgroup/memory.max";
if (File.Exists(memoryMax))
{
var value = File.ReadAllText(memoryMax).Trim();
if (value != "max")
{
var bytes = ToFloat(value);
if (bytes > 0) return bytes / 1024f / 1024f;
}
}
var limitPath = "/sys/fs/cgroup/memory/memory.limit_in_bytes";
if (File.Exists(limitPath))
{
var bytes = ToFloat(File.ReadAllText(limitPath).Trim());
if (bytes > 0 && bytes < 9000000000000000000f) return bytes / 1024f / 1024f;
}
}
catch
{
}
return 0f;
}
private float GetDiskLimitGiB()
{
try
{
var root = Path.GetPathRoot(Interface.Oxide.RootDirectory);
var drive = new DriveInfo(root);
if (drive.TotalSize <= 0) return Math.Max(1f, resourceHistory.Count == 0 ? 1f : resourceHistory.Max(s => s.DiskGiB));
var totalGiB = drive.TotalSize / 1024f / 1024f / 1024f;
return totalGiB;
}
catch
{
return Math.Max(1f, resourceHistory.Count == 0 ? 1f : resourceHistory.Max(s => s.DiskGiB));
}
}
private float GetDiskUsedGiB()
{
try
{
if ((DateTime.UtcNow - lastDiskSampleAt).TotalSeconds < 5)
{
return cachedDiskGiB;
}
var root = Path.GetFullPath(Interface.Oxide.RootDirectory);
var driveRoot = Path.GetPathRoot(root);
var drive = new DriveInfo(driveRoot);
var usedBytes = Math.Max(0L, drive.TotalSize - drive.AvailableFreeSpace);
cachedDiskGiB = usedBytes / 1024f / 1024f / 1024f;
lastDiskSampleAt = DateTime.UtcNow;
return cachedDiskGiB;
}
catch
{
return cachedDiskGiB > 0 ? cachedDiskGiB : resourceHistory.Count == 0 ? 0f : resourceHistory.Last().DiskGiB;
}
}
private void EnsureNextAutoWipe()
{
if (!ParseStoredWipeUtc(storedData.NextAutoWipeUtc).HasValue)
{
storedData.NextAutoWipeUtc = CalculateNextAutoWipe(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss");
SaveData();
}
}
private DateTime? ParseStoredWipeUtc(string value)
{
if (!DateTime.TryParse(value, out var parsed)) return null;
return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
}
private DateTime CalculateNextAutoWipe(DateTime fromUtc)
{
var time = ParseTimeOfDay(config.AutoWipeTimeUtc);
var cadence = NormalizeWipeCadence(config.AutoWipeCadence, config.AutoWipeUseWeeklySchedule);
if (cadence == "TwiceWeekly")
{
var primary = NextWeekdayOccurrence(fromUtc, GetConfiguredWipeWeekday(), time);
var secondary = NextWeekdayOccurrence(fromUtc, GetConfiguredSecondWipeWeekday(), time);
return primary <= secondary ? primary : secondary;
}
if (cadence == "Weekly")
{
var weekday = GetConfiguredWipeWeekday();
var month = new DateTime(fromUtc.Year, fromUtc.Month, 1, time.Hours, time.Minutes, 0, DateTimeKind.Utc);
for (var monthOffset = 0; monthOffset < 24; monthOffset++)
{
var first = month.AddMonths(monthOffset);
while (first.DayOfWeek != weekday) first = first.AddDays(1);
for (var candidate = first; candidate.Month == first.Month; candidate = candidate.AddDays(7))
{
if (candidate > fromUtc) return candidate;
}
}
}
var intervalNext = new DateTime(fromUtc.Year, fromUtc.Month, fromUtc.Day, time.Hours, time.Minutes, 0, DateTimeKind.Utc);
while (intervalNext <= fromUtc)
{
intervalNext = intervalNext.AddDays(Math.Max(1, config.AutoWipeIntervalDays));
}
return intervalNext;
}
private DateTime NextWeekdayOccurrence(DateTime fromUtc, DayOfWeek weekday, TimeSpan time)
{
var candidate = new DateTime(fromUtc.Year, fromUtc.Month, fromUtc.Day, time.Hours, time.Minutes, 0, DateTimeKind.Utc);
while (candidate <= fromUtc || candidate.DayOfWeek != weekday) candidate = candidate.AddDays(1);
return candidate;
}
private string NormalizeWipeCadence(string value, bool legacyWeekly)
{
var clean = (value ?? string.Empty).Trim();
if (clean.Equals("Weekly", StringComparison.OrdinalIgnoreCase)) return "Weekly";
if (clean.Equals("TwiceWeekly", StringComparison.OrdinalIgnoreCase) || clean.Equals("Twice Weekly", StringComparison.OrdinalIgnoreCase) || clean.Equals("Biweekly", StringComparison.OrdinalIgnoreCase) || clean.Equals("Bi-weekly", StringComparison.OrdinalIgnoreCase)) return "TwiceWeekly";
if (clean.Equals("Interval", StringComparison.OrdinalIgnoreCase) || clean.Equals("Custom", StringComparison.OrdinalIgnoreCase)) return "Interval";
return legacyWeekly ? "Weekly" : "Interval";
}
private string NextWipeCadence(string current)
{
var cadence = NormalizeWipeCadence(current, config.AutoWipeUseWeeklySchedule);
if (cadence == "Weekly") return "TwiceWeekly";
if (cadence == "TwiceWeekly") return "Interval";
return "Weekly";
}
private DayOfWeek GetConfiguredWipeWeekday()
{
return TryParseWeekday(config.AutoWipeWeekday, out var weekday) ? weekday : DayOfWeek.Thursday;
}
private DayOfWeek GetConfiguredSecondWipeWeekday()
{
var primary = GetConfiguredWipeWeekday();
var second = TryParseWeekday(config.AutoWipeSecondWeekday, out var weekday) ? weekday : DayOfWeek.Monday;
return second == primary ? (primary == DayOfWeek.Monday ? DayOfWeek.Thursday : DayOfWeek.Monday) : second;
}
private bool TryParseWeekday(string value, out DayOfWeek weekday)
{
if (Enum.TryParse(value ?? string.Empty, true, out weekday)) return true;
var clean = (value ?? string.Empty).Trim().ToLowerInvariant();
if (clean == "mon") { weekday = DayOfWeek.Monday; return true; }
if (clean == "tue" || clean == "tues") { weekday = DayOfWeek.Tuesday; return true; }
if (clean == "wed") { weekday = DayOfWeek.Wednesday; return true; }
if (clean == "thu" || clean == "thur" || clean == "thurs") { weekday = DayOfWeek.Thursday; return true; }
if (clean == "fri") { weekday = DayOfWeek.Friday; return true; }
if (clean == "sat") { weekday = DayOfWeek.Saturday; return true; }
if (clean == "sun") { weekday = DayOfWeek.Sunday; return true; }
weekday = DayOfWeek.Thursday;
return false;
}
private bool IsFirstConfiguredWeekdayOfMonth(DateTime wipeUtc)
{
var weekday = GetConfiguredWipeWeekday();
if (wipeUtc.DayOfWeek != weekday) return false;
var first = new DateTime(wipeUtc.Year, wipeUtc.Month, 1, wipeUtc.Hour, wipeUtc.Minute, 0, DateTimeKind.Utc);
while (first.DayOfWeek != weekday) first = first.AddDays(1);
return wipeUtc.Date == first.Date;
}
private string ScheduledWipeMode(DateTime wipeUtc)
{
return config.AutoWipeUseWeeklySchedule && config.AutoWipeForceFirstWeekday && IsFirstConfiguredWeekdayOfMonth(wipeUtc) ? "ForceWipe" : "AutoWipe";
}
private List<DateTime> PreviewScheduledWipes(int count)
{
var wipes = new List<DateTime>();
var cursor = DateTime.UtcNow;
for (var i = 0; i < Math.Max(1, count); i++)
{
var next = CalculateNextAutoWipe(cursor);
wipes.Add(next);
cursor = next.AddSeconds(1);
}
return wipes;
}
private DateTime? FindNextScheduledForceWipe(DateTime fromUtc)
{
if (!config.AutoWipeEnabled || !config.AutoWipeUseWeeklySchedule || !config.AutoWipeForceFirstWeekday) return null;
var cursor = fromUtc;
for (var i = 0; i < 60; i++)
{
var candidate = CalculateNextAutoWipe(cursor);
if (ScheduledWipeMode(candidate) == "ForceWipe") return candidate;
cursor = candidate.AddSeconds(1);
}
return null;
}
private void ProcessAutoWipe()
{
if (!config.AutoWipeEnabled) return;
EnsureNextAutoWipe();
var parsedNext = ParseStoredWipeUtc(storedData.NextAutoWipeUtc);
if (!parsedNext.HasValue) return;
var next = parsedNext.Value;
if (DateTime.UtcNow < next) return;
var mode = ScheduledWipeMode(next);
if (!RunWipeCommands(null, mode, mode == "ForceWipe" ? config.ForceWipeCommands : config.AutoWipeCommands)) return;
storedData.NextAutoWipeUtc = CalculateNextAutoWipe(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss");
SaveData();
RefreshWipeViews();
}
private bool RunWipeCommands(BasePlayer actor, string action, List<string> commands)
{
var force = action == "ForceWipe";
var dryRun = force && config.ForceWipeDryRunDefault;
activeWipeSeed = ResolveWipeSeed();
var wipeSeed = activeWipeSeed;
if (dryRun)
{
var preview = string.Join(" | ", PreviewWipeCommands(action).Concat(PreviewWipeFiles(config.ForceWipeBlueprints)).Take(10).ToArray());
Reply(actor, "Force wipe dry-run only. No commands or files were changed.");
LogDryRun(actor, "Wipe", action, "server", string.Empty, preview);
activeWipeSeed = null;
return false;
}
if (config.AllowGlobalChatAnnouncements && !string.IsNullOrEmpty(config.AutoWipeAnnouncement))
{
PrintToChat(FormatWipeText(config.AutoWipeAnnouncement));
}
activeWipeSeed = wipeSeed;
RunWipeSetupCommands(action == "ForceWipe" ? config.ForceWipeDiscordCommands : config.AutoWipeDiscordCommands);
if (!PrepareNextWorld(action, wipeSeed))
{
activeWipeSeed = null;
return false;
}
if (force && config.ForceWipeOfflineCleanup && !WriteForceWipeMarker(wipeSeed))
{
LogFailed(actor, "Wipe", action, "server", string.Empty, "Offline force-wipe marker could not be written; shutdown was cancelled.");
activeWipeSeed = null;
return false;
}
foreach (var command in (commands ?? new List<string>()).Where(c => !string.IsNullOrEmpty(c)))
{
var formatted = FormatWipeText(command);
if (!IsCommandAllowed(actor, formatted, "Wipe")) continue;
ConsoleSystem.Run(ConsoleSystem.Option.Server, formatted);
}
WriteAudit(actor, "Pending", "Wipe", action, "server", string.Empty, $"map={config.AutoWipeMap} bp={(force ? config.ForceWipeBlueprints : config.AutoWipeBlueprints)} seed={wipeSeed} size={config.AutoWipeMapSize}; awaiting restart verification");
activeWipeSeed = null;
RefreshConsoleViews();
return true;
}
private bool WriteForceWipeMarker(string wipeSeed)
{
try
{
if (!int.TryParse(wipeSeed, out var seed) || seed <= 0) return false;
var root = GetWipeRoot();
Directory.CreateDirectory(root);
var marker = Path.Combine(root, ".liveadmin-force-wipe.pending");
var lines = new[]
{
"version=1",
"mode=force",
"seed=" + seed,
"worldsize=" + Math.Max(1000, Math.Min(6000, config.AutoWipeMapSize)),
"wipe_map=" + (config.AutoWipeMap ? "1" : "0"),
"wipe_blueprints=" + (config.ForceWipeBlueprints ? "1" : "0")
};
File.WriteAllLines(marker, lines);
Puts("Offline force-wipe marker written. The startup wrapper will remove map and blueprint files while Rust is stopped.");
return true;
}
catch (Exception ex)
{
Puts("Failed to write offline force-wipe marker: " + ex.Message);
return false;
}
}

private bool PrepareNextWorld(string action, string wipeSeed)
{
if (!config.AutoWipeMap) return true;
if (!int.TryParse(wipeSeed, out var seed) || seed <= 0)
{
LogFailed(null, "Wipe", action, "server", string.Empty, "Could not resolve a valid numeric map seed.");
return false;
}

var size = Math.Max(1000, Math.Min(6000, config.AutoWipeMapSize));
var levelUrl = config.AutoWipeCustomMapUrl ?? string.Empty;
ConsoleSystem.Run(ConsoleSystem.Option.Server, "server.seed " + seed);
ConsoleSystem.Run(ConsoleSystem.Option.Server, "server.worldsize " + size);
ConsoleSystem.Run(ConsoleSystem.Option.Server, "server.levelurl " + ConsoleArg(levelUrl));
ConsoleSystem.Run(ConsoleSystem.Option.Server, "server.writecfg");
if (!WriteNextWorldServerConfig(seed, size, levelUrl))
{
LogFailed(null, "Wipe", action, "server", string.Empty, "Could not persist the next world settings to cfg/server.cfg.");
return false;
}

storedData.PendingWipeMode = action;
storedData.PendingWipeStartedUtc = Now();
storedData.PendingWipeSeed = seed;
storedData.PendingWipeMapSize = size;
storedData.PendingWipeLevelUrl = levelUrl;
SaveData();
Puts($"{action} prepared: next world seed={seed}, size={size}. Waiting for restart verification.");
return true;
}

private bool WriteNextWorldServerConfig(int seed, int size, string levelUrl)
{
try
{
var cfgDirectory = Path.Combine(GetWipeRoot(), "cfg");
Directory.CreateDirectory(cfgDirectory);
var cfgPath = Path.Combine(cfgDirectory, "server.cfg");
var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
{ "server.seed", "server.seed " + seed },
{ "server.worldsize", "server.worldsize " + size },
{ "server.levelurl", "server.levelurl " + ConsoleArg(levelUrl ?? string.Empty) }
};
var lines = File.Exists(cfgPath) ? File.ReadAllLines(cfgPath).ToList() : new List<string>();
for (var i = lines.Count - 1; i >= 0; i--)
{
var trimmed = (lines[i] ?? string.Empty).TrimStart();
var key = replacements.Keys.FirstOrDefault(k => trimmed.Equals(k, StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith(k + " ", StringComparison.OrdinalIgnoreCase));
if (key == null) continue;
lines.RemoveAt(i);
}
lines.AddRange(replacements.Values);
File.WriteAllLines(cfgPath, lines.ToArray());
return true;
}
catch (Exception ex)
{
Puts("Failed to persist next-world server.cfg settings: " + ex.Message);
return false;
}
}

private void VerifyPendingWipe()
{
if (storedData == null || string.IsNullOrEmpty(storedData.PendingWipeMode)) return;

var seedMatches = ConVar.Server.seed == storedData.PendingWipeSeed;
var sizeMatches = ConVar.Server.worldsize == storedData.PendingWipeMapSize;
var expectedUrl = storedData.PendingWipeLevelUrl ?? string.Empty;
var urlMatches = string.Equals(ConVar.Server.levelurl ?? string.Empty, expectedUrl, StringComparison.OrdinalIgnoreCase);
var offlineCleanupMatches = !string.Equals(storedData.PendingWipeMode, "ForceWipe", StringComparison.OrdinalIgnoreCase)
|| (seedMatches && sizeMatches && urlMatches && VerifyForceWipeCleanupReceipt());
var details = $"expected seed={storedData.PendingWipeSeed} size={storedData.PendingWipeMapSize}; running seed={ConVar.Server.seed} size={ConVar.Server.worldsize}";

if (seedMatches && sizeMatches && urlMatches && offlineCleanupMatches)
{
storedData.LastWipeUtc = Now();
LogSuccess(null, "Wipe", storedData.PendingWipeMode + "Verified", "server", string.Empty, details);
Puts($"{storedData.PendingWipeMode} completed successfully and was verified after restart: {details}.");
}
else
{
LogFailed(null, "Wipe", storedData.PendingWipeMode + "Verification", "server", string.Empty, details);
Puts($"{storedData.PendingWipeMode} did not complete successfully: {details}.");
}

storedData.PendingWipeMode = null;
storedData.PendingWipeStartedUtc = null;
storedData.PendingWipeSeed = 0;
storedData.PendingWipeMapSize = 0;
storedData.PendingWipeLevelUrl = null;
SaveData();
}

private bool VerifyForceWipeCleanupReceipt()
{
try
{
var receipt = Path.Combine(GetWipeRoot(), ".liveadmin-force-wipe.cleaned");
if (!File.Exists(receipt)) return false;
var values = File.ReadAllLines(receipt)
.Select(line => (line ?? string.Empty).Split(new[] { '=' }, 2))
.Where(parts => parts.Length == 2)
.ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
var valid = values.TryGetValue("seed", out var seedText)
&& int.TryParse(seedText, out var seed)
&& seed == storedData.PendingWipeSeed
&& values.TryGetValue("worldsize", out var sizeText)
&& int.TryParse(sizeText, out var size)
&& size == storedData.PendingWipeMapSize
&& values.TryGetValue("status", out var status)
&& string.Equals(status, "cleaned", StringComparison.OrdinalIgnoreCase);
if (valid) File.Delete(receipt);
return valid;
}
catch (Exception ex)
{
Puts("Failed to verify offline force-wipe receipt: " + ex.Message);
return false;
}
}
private void RunWipeSetupCommands(List<string> commands)
{
foreach (var command in (commands ?? new List<string>()).Where(c => !string.IsNullOrEmpty(c)))
{
var formatted = FormatWipeText(command);
if (!IsCommandAllowed(null, formatted, "Wipe")) continue;
ConsoleSystem.Run(ConsoleSystem.Option.Server, formatted);
}
}
private void DeleteForceWipeFiles(BasePlayer actor)
{
var patterns = new List<string>();
if (config.AutoWipeMap && config.MapWipeDeletePaths != null) patterns.AddRange(config.MapWipeDeletePaths);
if (config.AutoWipeBlueprints && config.BlueprintWipeDeletePaths != null) patterns.AddRange(config.BlueprintWipeDeletePaths);
var deleted = 0;
foreach (var pattern in patterns.Where(p => !string.IsNullOrEmpty(p)))
{
if (!IsSafeWipePattern(pattern))
{
LogBlocked(actor, "Wipe", "RejectWipePattern", "server", string.Empty, pattern);
continue;
}
deleted += DeleteWipePattern(pattern);
}
Log(actor, "ForceWipeDeleteFiles", "server", $"Deleted {deleted} files");
}
private int DeleteWipePattern(string pattern)
{
try
{
var root = GetWipeRoot();
var formatted = FormatWipeText(pattern).Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
if (!IsSafeWipePattern(pattern)) return 0;
var combined = Path.GetFullPath(Path.Combine(root, formatted));
if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return 0;
var directory = Path.GetDirectoryName(combined);
var search = Path.GetFileName(combined);
if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(search) || !Directory.Exists(directory)) return 0;
var deleted = 0;
foreach (var file in Directory.GetFiles(directory, search, SearchOption.TopDirectoryOnly))
{
var full = Path.GetFullPath(file);
if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
if (Directory.Exists(full)) continue;
File.Delete(full);
LogSuccess(null, "Wipe", "DeleteWipeFile", full, string.Empty, pattern);
deleted++;
}
return deleted;
}
catch (Exception ex)
{
Puts($"Force wipe file deletion skipped for '{pattern}': {ex.Message}");
return 0;
}
}
private string GetWipeRoot()
{
var root = string.IsNullOrEmpty(config.WipeFileRoot) ? Interface.Oxide.RootDirectory : FormatWipeText(config.WipeFileRoot);
root = Path.GetFullPath(root);
return root.EndsWith(Path.DirectorySeparatorChar.ToString()) ? root : root + Path.DirectorySeparatorChar;
}
private string FormatWipeText(string value)
{
if (string.IsNullOrEmpty(value)) return string.Empty;
var seed = activeWipeSeed ?? ResolveWipeSeed();
var identity = ConVar.Server.identity ?? string.Empty;
return value
.Replace("{map}", config.AutoWipeMap ? "true" : "false")
.Replace("{blueprints}", config.AutoWipeBlueprints ? "true" : "false")
.Replace("{bp}", config.AutoWipeBlueprints ? "true" : "false")
.Replace("{seed}", seed)
.Replace("{identity}", identity)
.Replace("{identity:q}", ConsoleArg(identity))
.Replace("{worldsize}", config.AutoWipeMapSize.ToString())
.Replace("{size}", config.AutoWipeMapSize.ToString())
.Replace("{levelurl}", config.AutoWipeCustomMapUrl ?? string.Empty)
.Replace("{mapurl:q}", ConsoleArg(config.AutoWipeCustomMapUrl ?? string.Empty))
.Replace("{mapurl}", config.AutoWipeCustomMapUrl ?? string.Empty);
}
private bool IsSafeWipePattern(string pattern)
{
if (string.IsNullOrWhiteSpace(pattern)) return false;
var formatted = FormatWipeText(pattern).Replace('\\', '/');
if (Path.IsPathRooted(formatted)) return false;
if (formatted.Contains("..")) return false;
return formatted.StartsWith("server/", StringComparison.OrdinalIgnoreCase) || pattern.StartsWith("server/{identity}/", StringComparison.OrdinalIgnoreCase);
}
private List<string> PreviewWipeCommands(string mode)
{
activeWipeSeed = activeWipeSeed ?? ResolveWipeSeed();
var list = mode == "ForceWipe" ? config.ForceWipeCommands : config.AutoWipeCommands;
return (list ?? new List<string>()).Where(c => !string.IsNullOrEmpty(c)).Select(FormatWipeText).ToList();
}
private List<string> PreviewWipeFiles(bool includeBlueprints)
{
var patterns = new List<string>();
if (config.AutoWipeMap && config.MapWipeDeletePaths != null) patterns.AddRange(config.MapWipeDeletePaths);
if (includeBlueprints && config.BlueprintWipeDeletePaths != null) patterns.AddRange(config.BlueprintWipeDeletePaths);
return patterns.Where(IsSafeWipePattern).Select(FormatWipeText).ToList();
}
private void PreviewWipe(BasePlayer player, string mode)
{
var commands = PreviewWipeCommands(mode);
var files = PreviewWipeFiles(mode == "ForceWipe" && config.ForceWipeBlueprints);
Reply(player, $"{mode} preview: {commands.Count} commands, {files.Count} file patterns. See logs.");
LogDryRun(player, "Wipe", "Preview" + mode, "server", string.Empty, string.Join(" | ", commands.Concat(files).Take(12).ToArray()));
}
private void PreviewWipeFilesOnly(BasePlayer player)
{
var files = PreviewWipeFiles(config.ForceWipeBlueprints);
Reply(player, $"File deletion preview: {files.Count} safe patterns. See logs.");
LogDryRun(player, "Wipe", "PreviewWipeFiles", "server", string.Empty, string.Join(" | ", files.Take(12).ToArray()));
}
private string ResolveWipeSeed()
{
return string.Equals(config.AutoWipeSeed, "random", StringComparison.OrdinalIgnoreCase)
? UnityEngine.Random.Range(1, int.MaxValue).ToString()
: config.AutoWipeSeed;
}
private List<PlayerActionCommand> GetPlayerActions(BasePlayer player)
{
var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
return config.PlayerActions == null
? new List<PlayerActionCommand>()
: config.PlayerActions
.Where(a => a != null && !string.IsNullOrEmpty(a.Label) && !string.IsNullOrEmpty(a.Command) && (string.IsNullOrEmpty(a.Permission) || Can(player, a.Permission)))
.Where(a => seen.Add(a.Label))
.ToList();
}
private PlayerActionCommand FindPlayerAction(BasePlayer player, string key)
{
if (string.IsNullOrEmpty(key)) return null;
var actions = GetPlayerActions(player);
var index = ToInt(key, -1);
if (index >= 0 && index < actions.Count) return actions[index];
return actions.FirstOrDefault(a => a.Label.Equals(key, StringComparison.OrdinalIgnoreCase));
}
private bool CanUseStaffAction(BasePlayer actor, string action)
{
if (string.IsNullOrEmpty(action)) return false;
if (action == "save" || action == "broadcast") return CanUseQuickActions(actor);
if (action == "vanishself" || action == "godself" || action == "noclipself") return CanAny(actor, PermOwner, PermStaffToolsModerate, PermStaffToolsActions);
if (action == "launch" || action == "spin" || action == "randomtp" || action == "passive" || action == "timeout" || action == "fake") return Can(actor, PermStaffToolsFun);
if (action == "openinv" || action.StartsWith("openinv:", StringComparison.OrdinalIgnoreCase)) return CanViewInventory(actor);
if (action == "give" || action == "givemain" || action == "givebackpack" || action == "givebelt" || action == "givewear" || action.StartsWith("removeinv:", StringComparison.OrdinalIgnoreCase) || action.StartsWith("setstack:", StringComparison.OrdinalIgnoreCase)) return CanModifyInventory(actor);
if (action == "kick") return CanKick(actor);
if (action == "ban") return Can(actor, PermStaffToolsDangerous) && CanBan(actor);
if (action == "kill" || action == "strip") return Can(actor, PermStaffToolsDangerous);
return CanAny(actor, PermPlayersModerate, PermStaffToolsModerate, PermStaffToolsActions);
}
private bool RequiresStaffConfirm(string action)
{
return action == "kill" || action == "strip" || action == "kick" || action == "ban" || action == "randomtp" || action == "timeout";
}
private bool IsSelfBlockingStaffAction(string action)
{
return action == "kick" || action == "ban" || action == "kill";
}
private bool IsDisruptiveStaffAction(string action)
{
if (string.IsNullOrEmpty(action)) return false;
return action == "kick" || action == "ban" || action == "mute" || action == "freeze" || action == "kill" || action == "strip" || action == "randomtp" || action == "timeout" || action == "fake" || action == "launch" || action.StartsWith("removeinv:", StringComparison.OrdinalIgnoreCase) || action.StartsWith("setstack:", StringComparison.OrdinalIgnoreCase) || action.StartsWith("give", StringComparison.OrdinalIgnoreCase);
}
private void RunStaffAction(BasePlayer actor, string targetId, string action)
{
var target = BasePlayer.FindByID(ToUlong(targetId)) ?? BasePlayer.FindSleeping(ToUlong(targetId));
if (actor == null) return;
if (target == null)
{
Reply(actor, "Target player is not available.");
return;
}
if (!CanUseStaffAction(actor, action)) return;
if (!CanTargetPlayer(actor, target.UserIDString, action, IsDisruptiveStaffAction(action))) return;
if (actor.userID == target.userID && IsSelfBlockingStaffAction(action))
{
Reply(actor, "LiveAdmin will not run that action on your own active session.");
return;
}
var state = GetState(actor);
var reason = CleanCommandText(string.IsNullOrEmpty(state.StaffReason) ? "Staff action" : state.StaffReason);
var message = CleanCommandText(string.IsNullOrEmpty(state.StaffMessage) ? "Please follow the server rules." : state.StaffMessage);
if (action == "heal")
{
target.Heal(1000f);
target.SendNetworkUpdate();
Log(actor, "StaffHeal", target.UserIDString, reason);
}
else if (action == "kill")
{
if (BlockedBySafeMode(actor, "kill", "Moderation")) return;
RunServerCommand(actor, $"killplayer {target.UserIDString}", $"kill {target.displayName}");
Log(actor, "StaffKill", target.UserIDString, reason);
}
else if (action == "freeze")
{
ToggleFreeze(actor, target);
}
else if (action == "spectate")
{
actor.SendConsoleCommand("spectate", target.UserIDString);
Log(actor, "StaffSpectate", target.UserIDString, target.displayName);
}
else if (action == "vanishself")
{
ToggleVanish(actor);
}
else if (action == "godself")
{
if (Godmode != null && RunPluginCommand(actor, actor, config.GodmodeCommand, "StaffGodmodeSelf")) return;
Reply(actor, "Godmode plugin is not loaded or command failed.");
}
else if (action == "noclipself")
{
actor.SendConsoleCommand("noclip");
Log(actor, "StaffNoclipToggle", actor.UserIDString, actor.displayName);
}
else if (action == "warn")
{
target.ChatMessage($"<color=#1faaa4>Staff warning:</color> {message}");
RecordWarning(actor, target, message);
Log(actor, "StaffWarn", target.UserIDString, message);
}
else if (action == "strip")
{
if (BlockedBySafeMode(actor, "strip", "Inventory")) return;
target.inventory.Strip();
target.inventory.SendSnapshot();
Log(actor, "StaffStripInventory", target.UserIDString, reason);
}
else if (action == "give")
{
if (BlockedBySafeMode(actor, action, "Inventory") || IsRateLimited(actor, action + ":" + target.UserIDString, 0.75)) return;
GiveStaffItem(actor, target, state.StaffItemShortname, ToInt(state.StaffItemAmount, 1));
}
else if (action == "openinv" || action.StartsWith("openinv:", StringComparison.OrdinalIgnoreCase))
{
var containerKey = action.StartsWith("openinv:", StringComparison.OrdinalIgnoreCase) ? CleanInventoryTab(action.Substring("openinv:".Length)) : string.Empty;
if (!TryOpenPlayerInventoryLoot(actor, target, containerKey))
{
Reply(actor, "Could not open Rust loot view for that player on this build. Use the Inventory rows as fallback.");
}
Log(actor, "StaffOpenInventory", target.UserIDString, string.IsNullOrEmpty(containerKey) ? target.displayName : $"{target.displayName} {containerKey}");
}
else if (action == "givemain" || action == "givebackpack" || action == "givebelt" || action == "givewear")
{
if (BlockedBySafeMode(actor, action, "Inventory") || IsRateLimited(actor, action + ":" + target.UserIDString, 0.75)) return;
var containerKey = action == "givebackpack" ? "backpack" : action.Substring("give".Length);
GiveStaffItem(actor, target, state.StaffItemShortname, ToInt(state.StaffItemAmount, 1), containerKey);
}
else if (action.StartsWith("removeinv:", StringComparison.OrdinalIgnoreCase))
{
if (BlockedBySafeMode(actor, action, "Inventory") || IsRateLimited(actor, action + ":" + target.UserIDString, 0.75)) return;
RemoveInventoryItem(actor, target, action);
}
else if (action.StartsWith("setstack:", StringComparison.OrdinalIgnoreCase))
{
if (BlockedBySafeMode(actor, action, "Inventory") || IsRateLimited(actor, action + ":" + target.UserIDString, 0.75)) return;
SetInventoryItemStack(actor, target, action, ToInt(state.StaffItemAmount, 1));
}
else if (action == "launch")
{
LaunchPlayer(target, 18f);
Log(actor, "StaffLaunch", target.UserIDString, reason);
}
else if (action == "spin")
{
if (spinningPlayers.Contains(target.userID))
{
spinningPlayers.Remove(target.userID);
Log(actor, "StaffSpinOff", target.UserIDString, reason);
}
else
{
spinningPlayers.Add(target.userID);
Log(actor, "StaffSpinOn", target.UserIDString, reason);
}
}
else if (action == "randomtp")
{
RandomTeleportPlayer(target);
Log(actor, "StaffRandomTeleport", target.UserIDString, reason);
}
else if (action == "passive")
{
if (config.UseGodmodeForPassive && Godmode != null && RunPluginCommand(actor, target, config.GodmodeCommand, "StaffPassiveGodmode"))
{
return;
}
if (passivePlayers.Contains(target.userID))
{
passivePlayers.Remove(target.userID);
Log(actor, "StaffPassiveOff", target.UserIDString, reason);
}
else
{
passivePlayers.Add(target.userID);
Log(actor, "StaffPassiveOn", target.UserIDString, reason);
}
}
else if (action == "timeout")
{
target.Teleport(actor.transform.position + actor.transform.forward * 2f);
frozenPositions[target.userID] = target.transform.position;
Log(actor, "StaffTimeout", target.UserIDString, reason);
}
else if (action == "fake")
{
target.ChatMessage(message);
Log(actor, "StaffFakeMessage", target.UserIDString, message);
}
else if (action == "broadcast")
{
if (BlockedBySafeMode(actor, "broadcast", "Server")) return;
PrintToChat(message);
Log(actor, "StaffBroadcast", "global", message);
}
else if (action == "save")
{
if (BlockedBySafeMode(actor, "save", "Server")) return;
RunServerCommand(actor, "server.save", "save server");
}
else if (action == "kick")
{
target.Kick(reason);
Log(actor, "StaffKick", target.UserIDString, reason);
}
else if (action == "ban")
{
if (BlockedBySafeMode(actor, "ban", "Moderation")) return;
ServerUsers.Set(target.userID, ServerUsers.UserGroup.Banned, target.displayName, reason);
target.Kick(reason);
Log(actor, "StaffBan", target.UserIDString, reason);
}
SaveData();
RefreshOpenPanels("staff");
RefreshOpenPanels("dashboard");
}
private void GiveStaffItem(BasePlayer actor, BasePlayer target, string shortname, int amount)
{
shortname = CleanCommandText(string.IsNullOrEmpty(shortname) ? "syringe.medical" : shortname);
if (!IsValidItemShortname(shortname))
{
Reply(actor, "Invalid item shortname.");
LogFailed(actor, "Inventory", "StaffGiveItem", target?.UserIDString, target?.displayName, shortname);
return;
}
amount = ClampItemAmount(amount);
var item = ItemManager.CreateByName(shortname, amount);
if (item == null)
{
Reply(actor, $"Item not found: {shortname}");
return;
}
target.GiveItem(item);
Log(actor, "StaffGiveItem", target.UserIDString, $"{shortname} x{amount}");
RefreshOpenPanels("staff");
}
private void GiveStaffItem(BasePlayer actor, BasePlayer target, string shortname, int amount, string containerKey)
{
shortname = CleanCommandText(string.IsNullOrEmpty(shortname) ? "syringe.medical" : shortname);
if (!IsValidItemShortname(shortname))
{
Reply(actor, "Invalid item shortname.");
LogFailed(actor, "Inventory", "StaffGiveItem", target?.UserIDString, target?.displayName, shortname);
return;
}
amount = ClampItemAmount(amount);
var item = ItemManager.CreateByName(shortname, amount);
if (item == null)
{
Reply(actor, $"Item not found: {shortname}");
return;
}
var itemContainer = GetInventoryContainer(target, containerKey);
if (itemContainer != null && item.MoveToContainer(itemContainer))
{
target.inventory.SendSnapshot();
Log(actor, "StaffGiveItem", target.UserIDString, $"{shortname} x{amount} -> {containerKey}");
RefreshOpenPanels("staff");
return;
}
target.GiveItem(item);
Log(actor, "StaffGiveItem", target.UserIDString, $"{shortname} x{amount} -> fallback");
RefreshOpenPanels("staff");
}
private ItemContainer GetInventoryContainer(BasePlayer target, string key)
{
if (target == null || target.inventory == null) return null;
key = (key ?? "main").ToLowerInvariant();
if (key == "main") return target.inventory.containerMain;
if (key == "belt") return target.inventory.containerBelt;
if (key == "wear") return target.inventory.containerWear;
if (key == "backpack") return GetBackpackContainer(target);
return target.inventory.containerMain;
}
private ItemContainer GetBackpackContainer(BasePlayer target)
{
if (target == null || Backpacks == null) return null;
var args = new object[] { target };
var container = TryBackpackCall("API_GetBackpackContainer", args) as ItemContainer
?? TryBackpackCall("GetBackpackContainer", args) as ItemContainer
?? TryBackpackCall("GetBackpackInventory", args) as ItemContainer
?? TryBackpackCall("API_GetBackpack", args) as ItemContainer
?? TryBackpackCall("GetBackpack", args) as ItemContainer;
if (container != null) return container;
var idArgs = new object[] { target.userID };
container = TryBackpackCall("API_GetBackpackContainer", idArgs) as ItemContainer
?? TryBackpackCall("GetBackpackContainer", idArgs) as ItemContainer
?? TryBackpackCall("GetBackpackInventory", idArgs) as ItemContainer
?? TryBackpackCall("API_GetBackpack", idArgs) as ItemContainer
?? TryBackpackCall("GetBackpack", idArgs) as ItemContainer;
if (container != null) return container;
var stringArgs = new object[] { target.UserIDString };
container = TryBackpackCall("API_GetBackpackContainer", stringArgs) as ItemContainer
?? TryBackpackCall("GetBackpackContainer", stringArgs) as ItemContainer
?? TryBackpackCall("GetBackpackInventory", stringArgs) as ItemContainer
?? TryBackpackCall("API_GetBackpack", stringArgs) as ItemContainer
?? TryBackpackCall("GetBackpack", stringArgs) as ItemContainer;
return container ?? ExtractItemContainer(TryBackpackCall("API_GetBackpackContainer", args) ?? TryBackpackCall("GetBackpackContainer", args) ?? TryBackpackCall("GetBackpack", args));
}
private object TryBackpackCall(string hook, object[] args)
{
try
{
return Backpacks?.Call(hook, args);
}
catch
{
return null;
}
}
private ItemContainer ExtractItemContainer(object result)
{
if (result == null) return null;
var direct = result as ItemContainer;
if (direct != null) return direct;
var type = result.GetType();
var property = type.GetProperty("inventory") ?? type.GetProperty("Inventory") ?? type.GetProperty("itemContainer");
return property == null ? null : property.GetValue(result, null) as ItemContainer;
}
private void RemoveInventoryItem(BasePlayer actor, BasePlayer target, string action)
{
var parts = action.Split(':');
if (parts.Length < 4) return;
var itemContainer = GetInventoryContainer(target, parts[1]);
var index = ToInt(parts[2], -1);
var item = GetInventoryItemAt(itemContainer, index);
if (itemContainer == null || index < 0 || item == null)
{
Reply(actor, "That inventory item is no longer available.");
return;
}
var amount = parts[3] == "stack" ? item.amount : Math.Max(1, ToInt(parts[3], 1));
var removed = Math.Min(amount, item.amount);
if (removed >= item.amount)
{
item.RemoveFromContainer();
item.Remove();
}
else
{
item.amount -= removed;
item.MarkDirty();
}
target.inventory.SendSnapshot();
Log(actor, "StaffRemoveInventoryItem", target.UserIDString, $"{parts[1]} {item.info?.shortname} x{removed}");
RefreshOpenPanels("staff");
}
private void SetInventoryItemStack(BasePlayer actor, BasePlayer target, string action, int newAmount)
{
var parts = action.Split(':');
if (parts.Length < 3) return;
var itemContainer = GetInventoryContainer(target, parts[1]);
var index = ToInt(parts[2], -1);
var item = GetInventoryItemAt(itemContainer, index);
if (itemContainer == null || index < 0 || item == null)
{
Reply(actor, "That inventory item is no longer available.");
return;
}
newAmount = ClampItemAmount(newAmount);
var oldAmount = item.amount;
item.amount = newAmount;
item.MarkDirty();
target.inventory.SendSnapshot();
Log(actor, "StaffSetInventoryStack", target.UserIDString, $"{parts[1]} {item.info?.shortname} {oldAmount}->{newAmount}");
RefreshOpenPanels("staff");
}
private bool TryOpenPlayerInventoryLoot(BasePlayer actor, BasePlayer target, string containerKey = "")
{
try
{
if (actor == null || target == null || actor.inventory == null || target.inventory == null) return false;
containerKey = string.IsNullOrEmpty(containerKey) ? string.Empty : CleanInventoryTab(containerKey);
if (!string.IsNullOrEmpty(containerKey) && !string.IsNullOrEmpty(config.InventoryViewerCommand) && config.InventoryViewerCommand.Contains("{container}"))
{
var command = FormatPlayerCommand(config.InventoryViewerCommand, target).Replace("{container}", containerKey);
if (!string.IsNullOrEmpty(command) && IsCommandAllowed(actor, command, "Inventory"))
{
ConsoleSystem.Run(ConsoleSystem.Option.Server, command);
Log(actor, "StaffInventoryViewerCommand", target.UserIDString, command);
return true;
}
}
if (!string.IsNullOrEmpty(containerKey) && !string.IsNullOrEmpty(config.InventoryViewerCommand) && !config.InventoryViewerCommand.Contains("{container}"))
{
return RunPluginCommand(actor, target, config.InventoryViewerCommand, "StaffInventoryViewerCommand");
}
if (config.UseInventoryViewerPlugin && InventoryViewer != null)
{
try
{
InventoryViewer.Call("_ViewInventory", actor, target);
return true;
}
catch
{
return RunPluginCommand(actor, target, config.InventoryViewerCommand, "StaffInventoryViewerCommand");
}
}
if (!string.IsNullOrEmpty(containerKey)) return false;
var loot = actor.inventory.loot;
if (loot == null) return false;
loot.Clear();
if (target.inventory.containerMain != null) loot.AddContainer(target.inventory.containerMain);
if (target.inventory.containerBelt != null) loot.AddContainer(target.inventory.containerBelt);
if (target.inventory.containerWear != null) loot.AddContainer(target.inventory.containerWear);
var backpack = GetBackpackContainer(target);
if (backpack != null) loot.AddContainer(backpack);
loot.SendImmediate();
OpenLootPanel(actor);
return true;
}
catch
{
return false;
}
}
private void OpenLootPanel(BasePlayer actor)
{
try
{
var method = typeof(BasePlayer).GetMethods().FirstOrDefault(m => m.Name == "ClientRPCPlayer" && m.GetParameters().Length == 4);
if (method != null)
{
method.Invoke(actor, new object[] { null, actor, "RPC_OpenLootPanel", "generic" });
return;
}
}
catch
{
}
try
{
actor.SendConsoleCommand("inventory.openlootpanel", "generic");
}
catch
{
}
}
private void LaunchPlayer(BasePlayer target, float strength)
{
if (target == null) return;
target.EnsureDismounted();
target.Teleport(target.transform.position + Vector3.up * 1.5f);
try
{
var method = target.GetType().GetMethod("SetVelocity", new[] { typeof(Vector3) });
if (method != null) method.Invoke(target, new object[] { Vector3.up * strength });
else target.Teleport(target.transform.position + Vector3.up * Math.Max(5f, strength * 0.5f));
}
catch
{
target.Teleport(target.transform.position + Vector3.up * Math.Max(5f, strength * 0.5f));
}
target.SendNetworkUpdateImmediate();
}
private void RandomTeleportPlayer(BasePlayer target)
{
if (target == null) return;
target.EnsureDismounted();
var offset = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(18f, 42f);
if (offset == Vector2.zero) offset = new Vector2(20f, 0f);
var destination = target.transform.position + new Vector3(offset.x, 1.5f, offset.y);
target.Teleport(destination);
target.SendNetworkUpdateImmediate();
}
private void ProcessSpinPlayers()
{
if (spinningPlayers.Count == 0) return;
foreach (var userId in spinningPlayers.ToList())
{
var target = BasePlayer.FindByID(userId);
if (target == null || !target.IsConnected)
{
spinningPlayers.Remove(userId);
continue;
}
var rotation = target.transform.rotation.eulerAngles;
target.transform.rotation = Quaternion.Euler(rotation.x, rotation.y + 55f, rotation.z);
target.viewAngles = new Vector3(target.viewAngles.x, target.viewAngles.y + 55f, target.viewAngles.z);
target.SendNetworkUpdateImmediate();
}
}
private void ToggleFreeze(BasePlayer actor, BasePlayer target)
{
if (frozenPositions.ContainsKey(target.userID))
{
frozenPositions.Remove(target.userID);
Log(actor, "Unfreeze", target.UserIDString, target.displayName);
Reply(actor, $"{target.displayName} unfrozen.");
}
else
{
frozenPositions[target.userID] = target.transform.position;
Log(actor, "Freeze", target.UserIDString, target.displayName);
Reply(actor, $"{target.displayName} frozen.");
}
}
private bool CanTargetStaffPlayer(BasePlayer actor, BasePlayer target, string action)
{
if (actor == null || target == null) return false;
if (actor.userID == target.userID) return true;
if (!IsProtectedTarget(target)) return true;
return Can(actor, PermOwner);
}
private bool IsProtectedTarget(BasePlayer target)
{
if (target == null || config.ProtectedGroups == null) return false;
var protectedGroups = new HashSet<string>(config.ProtectedGroups, StringComparer.OrdinalIgnoreCase);
return permission.GetUserGroups(target.UserIDString).Any(group => protectedGroups.Contains(group));
}
private string GetPlayerPing(BasePlayer player)
{
try
{
var connection = player?.net?.connection;
if (connection == null) return "n/a";
var method = connection.GetType().GetMethod("GetAveragePing", new Type[0]);
if (method == null) return "n/a";
return $"{method.Invoke(connection, null)}ms";
}
catch
{
return "n/a";
}
}
private string GetConnectionAge(BasePlayer player)
{
try
{
var connection = player?.net?.connection;
if (connection == null) return "n/a";
var property = connection.GetType().GetProperty("connectionTime");
var value = property == null ? null : property.GetValue(connection, null);
if (value != null) return FormatDuration(TimeSpan.FromSeconds(Convert.ToDouble(value)));
}
catch
{
}
return "n/a";
}
private int CountInventoryItems(BasePlayer player)
{
try
{
if (player == null || player.inventory == null) return 0;
var count = 0;
if (player.inventory.containerMain != null) count += player.inventory.containerMain.itemList.Count;
if (player.inventory.containerBelt != null) count += player.inventory.containerBelt.itemList.Count;
if (player.inventory.containerWear != null) count += player.inventory.containerWear.itemList.Count;
var backpack = GetBackpackContainer(player);
if (backpack != null) count += backpack.itemList.Count;
return count;
}
catch
{
return 0;
}
}
private string GetPlayerAddress(BasePlayer player)
{
try
{
var address = player?.net?.connection?.ipaddress;
if (string.IsNullOrEmpty(address)) return "n/a";
var colon = address.LastIndexOf(':');
return colon > 0 ? address.Substring(0, colon) : address;
}
catch
{
return "n/a";
}
}
private static string FormatDuration(TimeSpan duration)
{
if (duration.TotalDays >= 1) return $"{(int)duration.TotalDays}d {duration.Hours}h";
if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours}h {duration.Minutes}m";
if (duration.TotalMinutes >= 1) return $"{(int)duration.TotalMinutes}m";
return $"{Math.Max(0, duration.Seconds)}s";
}
private void RunPlayerAction(BasePlayer actor, string targetId, PlayerActionCommand action)
{
var target = BasePlayer.FindByID(ToUlong(targetId)) ?? BasePlayer.FindSleeping(ToUlong(targetId));
var targetName = target?.displayName ?? targetId;
if (action == null || !CanTargetPlayer(actor, targetId, action.Label)) return;
if (IsRateLimited(actor, "playercmd:" + action.Label + ":" + targetId, action.Confirm ? 2.0 : 0.75)) return;
if (RunBuiltInPlayerAction(actor, targetId, targetName, action))
{
RefreshOpenPanels("staff");
RefreshOpenPanels("dashboard");
return;
}
var command = action.Command
.Replace("{id}", targetId)
.Replace("{steamid}", targetId)
.Replace("{target:q}", ConsoleArg(targetId))
.Replace("{name:q}", ConsoleArg(targetName))
.Replace("{name}", targetName);
RunServerCommand(actor, command, $"{action.Label} {targetName}");
Log(actor, "PlayerAction", targetId, action.Label);
if (IsUnmuteAction(action))
{
ClearTrackedMute(targetId, targetName);
}
RefreshOpenPanels("staff");
RefreshOpenPanels("dashboard");
}
private bool RunBuiltInPlayerAction(BasePlayer actor, string targetId, string targetName, PlayerActionCommand action)
{
if (action == null || actor == null) return false;
if (action.Label.Equals("Spectate", StringComparison.OrdinalIgnoreCase))
{
actor.SendConsoleCommand("spectate", targetId);
Log(actor, "Spectate", targetId, targetName);
Reply(actor, $"Spectating {targetName}.");
return true;
}
if (action.Label.Equals("Freeze", StringComparison.OrdinalIgnoreCase))
{
var target = BasePlayer.FindByID(ToUlong(targetId));
if (target == null) return false;
ToggleFreeze(actor, target);
return true;
}
return false;
}
private object OnPlayerInput(BasePlayer player, InputState input)
{
if (player == null) return null;
if (!frozenPositions.TryGetValue(player.userID, out var position)) return null;
if (Vector3.Distance(player.transform.position, position) > 0.1f)
{
player.Teleport(position);
}
return null;
}
private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
{
var victim = entity as BasePlayer;
var attacker = info?.InitiatorPlayer;
if ((victim != null && passivePlayers.Contains(victim.userID)) || (attacker != null && passivePlayers.Contains(attacker.userID)))
{
return true;
}
return null;
}
private void RunMuteAction(BasePlayer actor, string targetId)
{
var state = GetState(actor);
var target = BasePlayer.FindByID(ToUlong(targetId)) ?? BasePlayer.FindSleeping(ToUlong(targetId));
var targetName = target?.displayName ?? targetId;
var duration = CleanCommandText(string.IsNullOrEmpty(state.MuteDuration) ? "10m" : state.MuteDuration);
var reason = CleanCommandText(string.IsNullOrEmpty(state.MuteReason) ? "Staff action" : state.MuteReason);
if (!IsValidDuration(duration))
{
Reply(actor, "Mute duration must look like 30s, 10m, 2h, or 1d.");
LogFailed(actor, "Moderation", "Mute", targetId, targetName, "Invalid duration: " + duration);
return;
}
var muteLength = ParseDuration(duration);
if (muteLength <= TimeSpan.Zero)
{
muteLength = TimeSpan.FromMinutes(10);
duration = "10m";
}
var command = (string.IsNullOrEmpty(config.MuteCommand) ? "mute {id} {duration} {reason}" : config.MuteCommand)
.Replace("{id}", targetId)
.Replace("{steamid}", targetId)
.Replace("{target:q}", ConsoleArg(targetId))
.Replace("{name:q}", ConsoleArg(targetName))
.Replace("{reason:q}", ConsoleArg(reason))
.Replace("{name}", targetName)
.Replace("{duration}", duration)
.Replace("{time}", duration)
.Replace("{reason}", reason);
RunServerCommand(actor, command, $"mute {targetName}");
Log(actor, "Mute", targetId, $"{duration}: {reason}");
TrackMute(actor, targetId, targetName, duration, reason, DateTime.UtcNow.Add(muteLength));
if (config.AllowGlobalChatAnnouncements && config.AnnounceMutes)
{
var message = (string.IsNullOrEmpty(config.MuteAnnouncement) ? "{name} was muted for {duration}: {reason}" : config.MuteAnnouncement)
.Replace("{id}", targetId)
.Replace("{steamid}", targetId)
.Replace("{name}", targetName)
.Replace("{duration}", duration)
.Replace("{time}", duration)
.Replace("{reason}", reason);
PrintToChat(message);
}
}
private bool IsUnmuteAction(PlayerActionCommand action)
{
if (action == null) return false;
return (!string.IsNullOrEmpty(action.Label) && action.Label.IndexOf("unmute", StringComparison.OrdinalIgnoreCase) >= 0)
|| (!string.IsNullOrEmpty(action.Command) && action.Command.TrimStart().StartsWith("unmute", StringComparison.OrdinalIgnoreCase));
}
private void ClearTrackedMute(string targetId, string targetName)
{
if (storedData.ActiveMutes == null) return;
var removed = storedData.ActiveMutes.RemoveAll(m => m != null && m.TargetId == targetId);
if (removed <= 0) return;
SaveData();
if (config.AllowGlobalChatAnnouncements && config.AnnounceUnmutes)
{
var message = (string.IsNullOrEmpty(config.UnmuteAnnouncement) ? "{name} has been unmuted." : config.UnmuteAnnouncement)
.Replace("{id}", targetId)
.Replace("{steamid}", targetId)
.Replace("{name}", targetName)
.Replace("{duration}", string.Empty)
.Replace("{time}", string.Empty)
.Replace("{reason}", string.Empty);
PrintToChat(message);
}
}
private void TrackMute(BasePlayer actor, string targetId, string targetName, string duration, string reason, DateTime expiresAt)
{
storedData.ActiveMutes.RemoveAll(m => m == null || m.TargetId == targetId);
storedData.ActiveMutes.Add(new TrackedMute
{
TargetId = targetId,
TargetName = targetName,
Duration = duration,
Reason = reason,
StaffName = actor?.displayName ?? "Staff",
ExpiresAtTicks = expiresAt.Ticks
});
SaveData();
}
private void ProcessExpiredMutes()
{
if (storedData == null || storedData.ActiveMutes == null || storedData.ActiveMutes.Count == 0) return;
var now = DateTime.UtcNow.Ticks;
var expired = storedData.ActiveMutes.Where(m => m == null || m.ExpiresAtTicks <= now).ToList();
if (expired.Count == 0) return;
foreach (var mute in expired.Where(m => m != null))
{
RunUnmuteAction(mute);
}
storedData.ActiveMutes.RemoveAll(m => m == null || m.ExpiresAtTicks <= now);
SaveData();
}
private void RunUnmuteAction(TrackedMute mute)
{
var targetName = string.IsNullOrEmpty(mute.TargetName) ? mute.TargetId : mute.TargetName;
var command = (string.IsNullOrEmpty(config.UnmuteCommand) ? "unmute {id}" : config.UnmuteCommand)
.Replace("{id}", mute.TargetId)
.Replace("{steamid}", mute.TargetId)
.Replace("{target:q}", ConsoleArg(mute.TargetId))
.Replace("{name:q}", ConsoleArg(targetName))
.Replace("{reason:q}", ConsoleArg(mute.Reason ?? string.Empty))
.Replace("{name}", targetName)
.Replace("{duration}", mute.Duration ?? string.Empty)
.Replace("{time}", mute.Duration ?? string.Empty)
.Replace("{reason}", mute.Reason ?? string.Empty);
if (!IsCommandAllowed(null, command, "Moderation")) return;
ConsoleSystem.Run(ConsoleSystem.Option.Server, command);
Log(null, "AutoUnmute", mute.TargetId, mute.Reason ?? string.Empty);
if (config.AllowGlobalChatAnnouncements && config.AnnounceUnmutes)
{
var message = (string.IsNullOrEmpty(config.UnmuteAnnouncement) ? "{name} has been unmuted." : config.UnmuteAnnouncement)
.Replace("{id}", mute.TargetId)
.Replace("{steamid}", mute.TargetId)
.Replace("{name}", targetName)
.Replace("{duration}", mute.Duration ?? string.Empty)
.Replace("{time}", mute.Duration ?? string.Empty)
.Replace("{reason}", mute.Reason ?? string.Empty);
PrintToChat(message);
}
}
private void UpdateReport(int id, string status, string staffName)
{
var report = storedData.Reports.FirstOrDefault(r => r.Id == id);
if (report == null) return;
report.Status = status;
if (status == "Claimed" || status == "Investigating") report.ClaimedBy = staffName;
report.UpdatedAt = Now();
SaveData();
RefreshOpenPanels("reports");
RefreshOpenPanels("staff");
}
private void SetReportPriority(BasePlayer staff, int id, string priority)
{
var report = storedData.Reports.FirstOrDefault(r => r.Id == id);
if (report == null) return;
priority = string.IsNullOrEmpty(priority) ? "Normal" : priority;
if (priority != "Low" && priority != "Medium" && priority != "High" && priority != "Normal") priority = "Normal";
report.Priority = priority;
report.UpdatedAt = Now();
Log(staff, "ReportPriority", id.ToString(), priority);
SaveData();
RefreshOpenPanels("reports");
RefreshOpenPanels("staff");
}
private void ApplyReportEscalation(ReportEntry newEntry)
{
if (newEntry == null || !IsCheatingReport(newEntry.Reason)) return;
var related = storedData.Reports
.Where(r => r != null && r.TargetId == newEntry.TargetId && r.Status != "Resolved" && IsCheatingReport(r.Reason))
.ToList();
var uniqueReporters = related
.Where(r => !string.IsNullOrEmpty(r.ReporterId))
.Select(r => r.ReporterId)
.Distinct(StringComparer.OrdinalIgnoreCase)
.Count();
if (uniqueReporters < 2) return;
foreach (var report in related)
{
report.Priority = "High";
report.UpdatedAt = Now();
if (report.Notes == null) report.Notes = new List<TicketNote>();
if (!report.Notes.Any(n => n.Text == "Auto-priority: multiple unique cheating reports for this target."))
{
report.Notes.Add(new TicketNote
{
Time = Now(),
StaffId = "server",
StaffName = "LiveAdmin",
Text = "Auto-priority: multiple unique cheating reports for this target."
});
}
}
Log(null, "ReportAutoPriority", newEntry.TargetId, $"{uniqueReporters} unique cheating reporters");
}
private bool IsCheatingReport(string reason)
{
if (string.IsNullOrEmpty(reason)) return false;
var text = reason.ToLowerInvariant();
return text.Contains("cheat") || text.Contains("hack") || text.Contains("aimbot") || text.Contains("esp") || text.Contains("script") || text.Contains("macro") || text.Contains("recoil");
}
private void AddTicketNote(BasePlayer staff, int id, string text)
{
var report = storedData.Reports.FirstOrDefault(r => r.Id == id);
if (report == null) return;
text = CleanCommandText(text);
if (string.IsNullOrEmpty(text))
{
Reply(staff, "Enter a ticket note first.");
return;
}
if (report.Notes == null) report.Notes = new List<TicketNote>();
report.Notes.Add(new TicketNote
{
Time = Now(),
StaffId = staff.UserIDString,
StaffName = staff.displayName,
Text = text
});
if (report.Notes.Count > 30)
{
report.Notes.RemoveRange(0, report.Notes.Count - 30);
}
report.UpdatedAt = Now();
Log(staff, "TicketNote", id.ToString(), text);
SaveData();
RefreshOpenPanels("reports");
RefreshOpenPanels("staff");
}
private void DeleteTicket(BasePlayer staff, int id)
{
var report = storedData.Reports.FirstOrDefault(r => r.Id == id);
if (report == null) return;
storedData.Reports.Remove(report);
Log(staff, "TicketDeleted", id.ToString(), report.Reason);
SaveData();
RefreshOpenPanels("reports");
RefreshOpenPanels("staff");
}
private void Log(BasePlayer actor, string action, string target, string details)
{
LogSuccess(actor, CategorizeAction(action), action, target, string.Empty, details);
}
private void LogPlayerActivity(BasePlayer player, string action, string details)
{
var id = player?.UserIDString ?? string.Empty;
var name = player?.displayName ?? id;
WriteAudit(player, "Success", "Connection", action, id, name, details);
}
private void LogSuccess(BasePlayer actor, string category, string action, string target, string targetName, string details)
{
WriteAudit(actor, "Success", category, action, target, targetName, details);
}
private void LogBlocked(BasePlayer actor, string category, string action, string target, string targetName, string details)
{
WriteAudit(actor, "Blocked", category, action, target, targetName, details);
}
private void LogFailed(BasePlayer actor, string category, string action, string target, string targetName, string details)
{
WriteAudit(actor, "Failed", category, action, target, targetName, details);
}
private void LogDryRun(BasePlayer actor, string category, string action, string target, string targetName, string details)
{
WriteAudit(actor, "DryRun", category, action, target, targetName, details);
}
private void WriteAudit(BasePlayer actor, string result, string category, string action, string target, string targetName, string details)
{
if (storedData == null) storedData = new StoredData();
if (storedData.Logs == null) storedData.Logs = new List<ActionLog>();
storedData.Logs.Add(new ActionLog
{
Time = Now(),
ActorId = actor?.UserIDString ?? "server",
ActorName = actor?.displayName ?? "Server",
Action = action,
Target = target,
Details = details,
Result = result,
TargetName = targetName,
Category = category
});
if (storedData.Logs.Count > 500)
{
storedData.Logs.RemoveRange(0, storedData.Logs.Count - 500);
}
RefreshConsoleViews();
}
private string CategorizeAction(string action)
{
action = (action ?? string.Empty).ToLowerInvariant();
if (action.Contains("wipe")) return "Wipe";
if (action.Contains("plugin")) return "Plugin";
if (action.Contains("grant") || action.Contains("revoke") || action.Contains("permission")) return "Permission";
if (action.Contains("group")) return "Group";
if (action.Contains("ticket") || action.Contains("report")) return "Report";
if (action.Contains("join") || action.Contains("leave") || action.Contains("connect") || action.Contains("disconnect")) return "Connection";
if (action.Contains("inventory") || action.Contains("item")) return "Inventory";
if (action.Contains("config")) return "Config";
if (action.Contains("server") || action.Contains("command")) return "Server";
if (action.Contains("blocked") || action.Contains("security")) return "Security";
return "Moderation";
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
private void RecordWarning(BasePlayer staff, BasePlayer target, string reason)
{
if (target == null) return;
EnsureDataDefaults();
storedData.Warnings.Add(new WarningEntry
{
Time = Now(),
TargetId = target.UserIDString,
TargetName = target.displayName,
StaffId = staff?.UserIDString ?? "server",
StaffName = staff?.displayName ?? "Server",
Reason = string.IsNullOrWhiteSpace(reason) ? "Staff warning" : reason.Trim()
});
var excess = storedData.Warnings
.Where(w => w != null && w.TargetId == target.UserIDString)
.OrderByDescending(w => w.Time)
.Skip(Math.Max(1, config.WarningsStoredPerPlayer))
.ToList();
foreach (var warning in excess) storedData.Warnings.Remove(warning);
SaveData();
}
private void DeletePlayerNote(BasePlayer staff, string targetId, int index)
{
if (index < 0 || string.IsNullOrEmpty(targetId)) return;
if (!storedData.Notes.TryGetValue(targetId, out var notes) || notes == null) return;
var ordered = notes.OrderByDescending(n => n.Time).ToList();
if (index >= ordered.Count) return;
var note = ordered[index];
notes.Remove(note);
Log(staff, "StaffNoteDeleted", targetId, note.Text);
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
AddConfigEntries(obj, string.Empty, rows, ref index);
return rows;
}
private void AddConfigEntries(JToken token, string path, List<ConfigEntry> rows, ref int index)
{
if (token == null) return;
if (token.Type == JTokenType.Object)
{
if (!token.HasValues && !string.IsNullOrEmpty(path))
{
rows.Add(new ConfigEntry { Index = index++, Key = path, Value = "{}", Kind = "Empty Object", Type = token.Type, Editable = true, Token = token });
return;
}
foreach (var prop in ((JObject)token).Properties())
{
var childPath = string.IsNullOrEmpty(path) ? prop.Name : path + " > " + prop.Name;
AddConfigEntries(prop.Value, childPath, rows, ref index);
}
return;
}
if (token.Type == JTokenType.Array)
{
var array = (JArray)token;
rows.Add(new ConfigEntry
{
Index = index++,
Key = path,
Value = FormatConfigValue(token),
Kind = "List",
Type = token.Type,
Editable = true,
Token = token
});
for (var i = 0; i < array.Count; i++) AddConfigEntries(array[i], path + $" [{i}]", rows, ref index);
return;
}
var editable = token.Type == JTokenType.Boolean || token.Type == JTokenType.Integer || token.Type == JTokenType.Float || token.Type == JTokenType.String || token.Type == JTokenType.Null;
rows.Add(new ConfigEntry
{
Index = index++,
Key = path,
Value = FormatConfigValue(token),
Kind = editable ? token.Type.ToString() : token.Type.ToString(),
Type = token.Type,
Editable = editable,
Token = token
});
}
private string FormatConfigValue(JToken token)
{
if (token == null) return string.Empty;
if (token.Type == JTokenType.Array) return Shorten(token.ToString(Formatting.None), 44);
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
var backupPath = Path.Combine(backupDir, pluginName + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json");
File.Copy(path, backupPath, true);
return true;
}
catch
{
LogFailed(null, "Config", "BackupPluginConfig", pluginName, string.Empty, "Backup failed");
return false;
}
}
private string LatestPluginConfigBackup(string pluginName)
{
try
{
var backupDir = Path.Combine(Interface.Oxide.ConfigDirectory, "LiveAdminBackups");
if (!Directory.Exists(backupDir)) return null;
return Directory.GetFiles(backupDir, pluginName + "-*.json")
.OrderByDescending(File.GetLastWriteTimeUtc)
.FirstOrDefault();
}
catch { return null; }
}
private bool HasPluginConfigBackup(string pluginName) => !string.IsNullOrEmpty(LatestPluginConfigBackup(pluginName));
private void RestoreLatestPluginConfig(BasePlayer player, string pluginName)
{
pluginName = KnownPluginName(pluginName);
if (string.IsNullOrEmpty(pluginName)) return;
var backupPath = LatestPluginConfigBackup(pluginName);
if (string.IsNullOrEmpty(backupPath) || !File.Exists(backupPath))
{
Reply(player, $"No LiveAdmin config backup exists for {pluginName}.");
return;
}
try
{
var restored = JObject.Parse(File.ReadAllText(backupPath));
BackupPluginConfig(pluginName);
File.WriteAllText(PluginConfigPath(pluginName), restored.ToString(Formatting.Indented));
Log(player, "ConfigRestore", pluginName, Path.GetFileName(backupPath));
Reply(player, $"Restored the latest config backup for {pluginName}. Reload the plugin to apply it.");
}
catch (Exception ex)
{
LogFailed(player, "Config", "ConfigRestore", pluginName, string.Empty, ex.Message);
Reply(player, $"Could not restore the config backup for {pluginName}.");
}
}
private void ToggleConfigValue(BasePlayer player, int index)
{
var state = GetState(player);
if (string.IsNullOrEmpty(state.SelectedConfigPlugin) || !CanEditPluginConfig(player) || BlockedBySafeMode(player, "configtoggle", "Config") || IsRateLimited(player, "config:" + state.SelectedConfigPlugin, 2.0)) return;
var obj = ReadPluginConfig(state.SelectedConfigPlugin);
if (obj == null) return;
var rows = new List<ConfigEntry>();
var rowIndex = 0;
AddConfigEntries(obj, string.Empty, rows, ref rowIndex);
var entry = rows.FirstOrDefault(row => row.Index == index);
if (entry == null || entry.Token == null || entry.Token.Type != JTokenType.Boolean) return;
if (!BackupPluginConfig(state.SelectedConfigPlugin)) LogFailed(player, "Config", "ConfigBackup", state.SelectedConfigPlugin, string.Empty, "Backup unavailable");
entry.Token.Replace(new JValue(!(bool)entry.Token));
File.WriteAllText(PluginConfigPath(state.SelectedConfigPlugin), obj.ToString(Formatting.Indented));
Log(player, "ConfigToggle", state.SelectedConfigPlugin, entry.Key);
RefreshOpenPanels("manage");
}
private void SetConfigValue(BasePlayer player, int index, string value)
{
var state = GetState(player);
if (string.IsNullOrEmpty(state.SelectedConfigPlugin) || !CanEditPluginConfig(player) || BlockedBySafeMode(player, "configset", "Config") || IsRateLimited(player, "config:" + state.SelectedConfigPlugin, 2.0)) return;
var obj = ReadPluginConfig(state.SelectedConfigPlugin);
if (obj == null) return;
var rows = new List<ConfigEntry>();
var rowIndex = 0;
AddConfigEntries(obj, string.Empty, rows, ref rowIndex);
var entry = rows.FirstOrDefault(row => row.Index == index);
if (entry == null || entry.Token == null) return;
var token = entry.Token;
if (!(token.Type == JTokenType.Integer || token.Type == JTokenType.Float || token.Type == JTokenType.String || token.Type == JTokenType.Array || token.Type == JTokenType.Object || token.Type == JTokenType.Null)) return;
if (!BackupPluginConfig(state.SelectedConfigPlugin)) LogFailed(player, "Config", "ConfigBackup", state.SelectedConfigPlugin, string.Empty, "Backup unavailable");
if (token.Type == JTokenType.Integer)
{
if (long.TryParse(value, out var parsed)) token.Replace(new JValue(parsed));
else return;
}
else if (token.Type == JTokenType.Float)
{
if (double.TryParse(value, out var parsed)) token.Replace(new JValue(parsed));
else return;
}
else if (token.Type == JTokenType.Array)
{
JArray parsedArray;
try
{
parsedArray = JArray.Parse(value);
}
catch
{
parsedArray = new JArray((value ?? string.Empty).Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim()));
}
token.Replace(parsedArray);
}
else if (token.Type == JTokenType.Object)
{
try { token.Replace(JObject.Parse(value)); }
catch { return; }
}
else
{
token.Replace(new JValue(value));
}
File.WriteAllText(PluginConfigPath(state.SelectedConfigPlugin), obj.ToString(Formatting.Indented));
Log(player, "ConfigSet", state.SelectedConfigPlugin, entry.Key);
RefreshOpenPanels("manage");
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
AddInstalledPluginFileNames(names);
AddPermissionPluginNames(names);
return names.Where(n => !string.IsNullOrEmpty(n)).ToList();
}
private string KnownPluginName(string pluginName)
{
if (string.IsNullOrEmpty(pluginName)) return null;
return GetKnownPlugins().FirstOrDefault(p => p.Equals(pluginName, StringComparison.OrdinalIgnoreCase));
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
private void AddInstalledPluginFileNames(HashSet<string> names)
{
try
{
foreach (var directory in PluginSearchDirectories())
{
if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) continue;
foreach (var file in Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly))
{
var pluginName = Path.GetFileNameWithoutExtension(file);
if (!string.IsNullOrEmpty(pluginName))
{
names.Add(pluginName);
}
}
}
}
catch
{
// Installed plugin files are best-effort; loaded and configured plugin names remain available.
}
}
private IEnumerable<string> PluginSearchDirectories()
{
var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
try
{
var root = Interface.Oxide.RootDirectory;
if (!string.IsNullOrEmpty(root))
{
directories.Add(Path.Combine(root, "plugins"));
directories.Add(Path.Combine(root, "oxide", "plugins"));
directories.Add(Path.Combine(root, "umod", "plugins"));
}
}
catch
{
}
try
{
var configDir = Interface.Oxide.ConfigDirectory;
if (!string.IsNullOrEmpty(configDir))
{
var oxideDir = Directory.GetParent(configDir)?.FullName;
if (!string.IsNullOrEmpty(oxideDir))
{
directories.Add(Path.Combine(oxideDir, "plugins"));
}
}
}
catch
{
}
return directories;
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
private static int ToInt(string value, int fallback)
{
return int.TryParse(value, out var number) ? number : fallback;
}
private static float ToFloat(string value)
{
return float.TryParse(value, out var number) ? number : 0f;
}
private static ulong ToUlong(string value)
{
return ulong.TryParse(value, out var number) ? number : 0;
}
private static string Title(string value)
{
if (value == "staff") return "Staff Tools";
return string.IsNullOrEmpty(value) ? string.Empty : char.ToUpper(value[0]) + value.Substring(1);
}
private static string Shorten(string value, int maxLength)
{
if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
return value.Substring(0, Math.Max(0, maxLength - 3)) + "...";
}
private static string ShortenCompact(string value, int maxLength)
{
if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
return value.Substring(0, Math.Max(0, maxLength - 2)) + "..";
}
private static string SafeName(string value)
{
if (string.IsNullOrEmpty(value)) return "empty";
var chars = value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
return new string(chars);
}
private static string FormatGiB(float value)
{
return $"{value:0.##} GiB";
}
private static List<string> SplitConfigList(string value, string fallback)
{
var parts = (value ?? string.Empty)
.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries)
.Select(v => v.Trim())
.Where(v => !string.IsNullOrEmpty(v))
.ToList();
if (parts.Count == 0 && !string.IsNullOrEmpty(fallback)) parts.Add(fallback);
return parts;
}
private static string ConsoleArg(string value)
{
if (string.IsNullOrEmpty(value)) return "\"\"";
return value.IndexOf(' ') >= 0 ? "\"" + value.Replace("\"", "\\\"") + "\"" : value;
}
private static string CleanCommandText(string value)
{
if (string.IsNullOrEmpty(value)) return string.Empty;
return value.Replace("\r", " ").Replace("\n", " ").Replace(";", " ").Trim();
}
private static TimeSpan ParseDuration(string value)
{
if (string.IsNullOrEmpty(value)) return TimeSpan.Zero;
value = value.Trim().ToLowerInvariant();
if (TimeSpan.TryParse(value, out var parsed)) return parsed;
var suffix = value[value.Length - 1];
var numberText = char.IsLetter(suffix) ? value.Substring(0, value.Length - 1) : value;
if (!double.TryParse(numberText, out var amount)) return TimeSpan.Zero;
switch (suffix)
{
case 's':
return TimeSpan.FromSeconds(amount);
case 'm':
return TimeSpan.FromMinutes(amount);
case 'h':
return TimeSpan.FromHours(amount);
case 'd':
return TimeSpan.FromDays(amount);
default:
return TimeSpan.FromMinutes(amount);
}
}
private static TimeSpan ParseTimeOfDay(string value)
{
if (string.IsNullOrEmpty(value)) return new TimeSpan(17, 0, 0);
if (TimeSpan.TryParse(value, out var parsed)) return parsed;
return new TimeSpan(17, 0, 0);
}
private static string Now()
{
return DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
}
}
}
