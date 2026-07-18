using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("WebsiteAirstrikeAnimationBridge", "Raidlands", "1.0.6")]
    [Description("Synchronizes published Raidlands website airstrike animation bundles with PortableAirstrikes visual profiles.")]
    public class WebsiteAirstrikeAnimationBridge : RustPlugin
    {
        [PluginReference] private Plugin PortableAirstrikes;
        [PluginReference] private Plugin PortableAirstrikesAnimationEditor;

        private const string AdminPermission = "websiteairstrikeanimationbridge.admin";
        private const string DefaultVisualProfilesDataFile = "PortableAirstrikes/VisualProfiles";
        private const string StateDataFileName = "WebsiteAirstrikeAnimationBridge/State";
        private const string SecretsConfigName = "Secrets.local";
        private const string VipBridgeConfigName = "WebsiteVipBridge";
        private const string UiName = "WebsiteAirstrikeAnimationBridge.UI";
        private const int DefaultMaxBundleBytes = 20 * 1024 * 1024;

        private Configuration config;
        private BridgeState state;
        private Timer startupSyncTimer;
        private Timer recurringSyncTimer;
        private Timer pendingSnapshotTimer;
        private bool operationInFlight;
        private bool snapshotInFlight;
        private string pendingSnapshotJson;
        private readonly HashSet<string> pendingSnapshotChangedProfileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> secrets;
        private JObject vipBridgeConfig;

        private class Configuration
        {
            public string ApiBaseUrl = "https://raidlands.net";
            public string ServerId = "raidlands-main";
            public string SharedSecret = "${RAIDLANDS_BRIDGE_SHARED_SECRET}";
            public string VisualProfilesDataFile = DefaultVisualProfilesDataFile;
            public bool SyncOnServerInitialized = true;
            public int StartupSyncDelaySeconds = 8;
            public bool EnableRecurringSync = false;
            public int RecurringSyncIntervalSeconds = 21600;
            public bool AutoUploadLocalSaves = true;
            public bool BootstrapUploadIfWebsiteEmpty = true;
            public bool ProtectUnsyncedLocalChanges = true;
            public int BackupCount = 20;
            public int RequestTimeoutMilliseconds = 20000;
            public int MaxBundleBytes = DefaultMaxBundleBytes;
            public string OpenPanelCommand = "airanimsync";
            public bool LogSuccessfulNoUpdateChecks = false;
            public bool AllowInsecureHttpForDevelopment = false;
        }

        private class BridgeState
        {
            public long InstalledRevision;
            public string InstalledSha256 = "";
            public string InstalledPhysicalSha256 = "";
            public long LastKnownPublishedRevision;
            public string LastKnownPublishedSha256 = "";
            public string LastCheckAtUtc = "";
            public string LastSyncAtUtc = "";
            public string LastStatus = "not_started";
            public string LastMessage = "";
            public string LastUploadedLocalSha256 = "";
            public string LastUploadedAtUtc = "";
            public bool LocalDirty;
            public Dictionary<string, string> LastInstalledProfileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating default WebsiteAirstrikeAnimationBridge config.");
            config = new Configuration();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<Configuration>() ?? new Configuration();
            }
            catch
            {
                PrintWarning("Configuration is invalid; loading defaults.");
                config = new Configuration();
            }

            NormalizeConfig();
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void Init()
        {
            permission.RegisterPermission(AdminPermission, this);
            LoadState();
            AdoptInstalledMetadataFromLocalFile();
            cmd.AddChatCommand(NormalizeCommand(config.OpenPanelCommand), this, nameof(CmdOpenPanel));
            if (!string.Equals(NormalizeCommand(config.OpenPanelCommand), "airanimsync", StringComparison.OrdinalIgnoreCase))
            {
                cmd.AddChatCommand("airanimsync", this, nameof(CmdOpenPanel));
            }
        }

        private void OnServerInitialized()
        {
            LogBridgeSecretDiagnostics();

            if (config.SyncOnServerInitialized)
            {
                startupSyncTimer = timer.Once(Math.Max(1, config.StartupSyncDelaySeconds), () =>
                {
                    BeginSync(0L, false, false, "startup recovery check", null);
                });
            }

            if (config.EnableRecurringSync)
            {
                recurringSyncTimer = timer.Every(Math.Max(300, config.RecurringSyncIntervalSeconds), () =>
                {
                    BeginSync(0L, false, false, "recurring check", null);
                });
            }
        }

        private void Unload()
        {
            startupSyncTimer?.Destroy();
            recurringSyncTimer?.Destroy();
            pendingSnapshotTimer?.Destroy();
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, UiName);
            }
        }

        private void OnPortableAirstrikesVisualProfilesSaved(string dataFileName, string serializedJson, string[] changedProfileIds)
        {
            if (!config.AutoUploadLocalSaves || !IsConfiguredVisualProfilesDataFile(dataFileName))
            {
                return;
            }

            pendingSnapshotJson = serializedJson ?? "";
            if (changedProfileIds != null)
            {
                foreach (var profileId in changedProfileIds)
                {
                    var normalized = NormalizeProfileKey(profileId);
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        pendingSnapshotChangedProfileIds.Add(normalized);
                    }
                }
            }

            pendingSnapshotTimer?.Destroy();
            pendingSnapshotTimer = timer.Once(2f, () =>
            {
                var json = pendingSnapshotJson;
                var changed = pendingSnapshotChangedProfileIds.ToArray();
                pendingSnapshotJson = "";
                pendingSnapshotChangedProfileIds.Clear();
                UploadSnapshot("local_save", json, changed, true, message => Puts(message));
            });
        }

        private void CmdOpenPanel(BasePlayer player, string command, string[] args)
        {
            if (!CanUse(player))
            {
                Reply(player, "You do not have permission to use airstrike animation sync.");
                return;
            }

            ShowPanel(player);
        }

        [ConsoleCommand("airanimsync")]
        private void CCmdRoot(ConsoleSystem.Arg arg)
        {
            if (!CanUseConsole(arg))
            {
                ReplyCommand(arg, "You must be server console, RCON, auth level 2, or have " + AdminPermission + ".");
                return;
            }

            var sub = arg.GetString(0, "status").ToLowerInvariant();
            if (sub == "status")
            {
                ReplyCommand(arg, BuildStatusLine());
                return;
            }

            if (sub == "check")
            {
                BeginCheck(message => ReplyCommand(arg, message));
                return;
            }

            if (sub == "sync")
            {
                var revision = ParseRevision(arg.GetString(1, ""));
                var force = IsForceArg(arg.GetString(2, ""));
                BeginSync(revision, force, false, "manual command", message => ReplyCommand(arg, message));
                return;
            }

            if (sub == "force")
            {
                BeginSync(ParseRevision(arg.GetString(1, "")), true, false, "manual force pull", message => ReplyCommand(arg, message));
                return;
            }

            if (sub == "upload")
            {
                UploadSnapshot("manual_upload", null, null, false, message => ReplyCommand(arg, message));
                return;
            }

            if (sub == "rollback")
            {
                var revision = ParseRevision(arg.GetString(1, ""));
                if (revision > 0)
                {
                    BeginSync(revision, true, true, "website rollback command", message => ReplyCommand(arg, message));
                }
                else
                {
                    RollbackLatestLocalBackup(message => ReplyCommand(arg, message));
                }
                return;
            }

            ReplyCommand(arg, "Usage: airanimsync status|check|sync [revision] [force]|force [revision]|upload|rollback [revision]");
        }

        [ConsoleCommand("airanimsync.sync")]
        private void CCmdSync(ConsoleSystem.Arg arg)
        {
            if (!CanUseConsole(arg))
            {
                ReplyCommand(arg, "You must be server console, RCON, auth level 2, or have " + AdminPermission + ".");
                return;
            }

            var revision = ParseRevision(arg.GetString(0, ""));
            var force = IsForceArg(arg.GetString(1, ""));
            BeginSync(revision, force, false, "publish notification", message => ReplyCommand(arg, message));
        }

        [ConsoleCommand("airanimsync.status")]
        private void CCmdStatus(ConsoleSystem.Arg arg)
        {
            if (!CanUseConsole(arg))
            {
                ReplyCommand(arg, "You must be server console, RCON, auth level 2, or have " + AdminPermission + ".");
                return;
            }

            ReplyCommand(arg, BuildStatusLine());
        }

        [ConsoleCommand("airanimsync.check")]
        private void CCmdCheck(ConsoleSystem.Arg arg)
        {
            if (!CanUseConsole(arg))
            {
                ReplyCommand(arg, "You must be server console, RCON, auth level 2, or have " + AdminPermission + ".");
                return;
            }

            BeginCheck(message => ReplyCommand(arg, message));
        }

        [ConsoleCommand("airanimsync.upload")]
        private void CCmdUpload(ConsoleSystem.Arg arg)
        {
            if (!CanUseConsole(arg))
            {
                ReplyCommand(arg, "You must be server console, RCON, auth level 2, or have " + AdminPermission + ".");
                return;
            }

            UploadSnapshot("manual_upload", null, null, false, message => ReplyCommand(arg, message));
        }

        [ConsoleCommand("airanimsync.force")]
        private void CCmdForce(ConsoleSystem.Arg arg)
        {
            if (!CanUseConsole(arg))
            {
                ReplyCommand(arg, "You must be server console, RCON, auth level 2, or have " + AdminPermission + ".");
                return;
            }

            BeginSync(ParseRevision(arg.GetString(0, "")), true, false, "manual force pull", message => ReplyCommand(arg, message));
        }

        [ConsoleCommand("airanimsync.rollback")]
        private void CCmdRollback(ConsoleSystem.Arg arg)
        {
            if (!CanUseConsole(arg))
            {
                ReplyCommand(arg, "You must be server console, RCON, auth level 2, or have " + AdminPermission + ".");
                return;
            }

            var revision = ParseRevision(arg.GetString(0, ""));
            if (revision > 0)
            {
                BeginSync(revision, true, true, "website rollback command", message => ReplyCommand(arg, message));
                return;
            }

            RollbackLatestLocalBackup(message => ReplyCommand(arg, message));
        }

        [ConsoleCommand("airanimsync.ui")]
        private void CCmdUi(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var action = arg.GetString(0, "status").ToLowerInvariant();
            if (action == "close")
            {
                CuiHelper.DestroyUi(player, UiName);
                return;
            }

            Action<string> reply = message =>
            {
                Reply(player, message);
                timer.Once(0.5f, () =>
                {
                    if (player != null && player.IsConnected)
                    {
                        ShowPanel(player);
                    }
                });
            };

            if (action == "check")
            {
                BeginCheck(reply);
                return;
            }

            if (action == "sync")
            {
                BeginSync(0L, false, false, "admin panel sync", reply);
                return;
            }

            if (action == "force")
            {
                BeginSync(0L, true, false, "admin panel force pull", reply);
                return;
            }

            if (action == "upload")
            {
                UploadSnapshot("manual_upload", null, null, false, reply);
                return;
            }

            if (action == "rollback")
            {
                RollbackLatestLocalBackup(reply);
                return;
            }

            ShowPanel(player);
        }

        private void BeginCheck(Action<string> reply)
        {
            BeginBundleRequest(0L, false, false, false, "manual check", reply);
        }

        private void BeginSync(long requestedRevision, bool force, bool rollback, string reason, Action<string> reply)
        {
            BeginBundleRequest(requestedRevision, force, true, rollback, reason, reply);
        }

        private void BeginBundleRequest(long requestedRevision, bool force, bool install, bool rollback, string reason, Action<string> reply)
        {
            if (operationInFlight)
            {
                reply?.Invoke("Airstrike animation sync is already in progress.");
                return;
            }

            string configError;
            if (!CanRequest(out configError))
            {
                reply?.Invoke(configError);
                return;
            }

            operationInFlight = true;
            state.LastCheckAtUtc = NowIso();
            SaveState();

            reply?.Invoke("Airstrike animation " + (install ? "sync" : "check") + " request started.");

            var currentLocalSha = GetCurrentLocalSha256();
            var since = requestedRevision > 0 ? 0L : Math.Max(0L, state.InstalledRevision);
            var url = TrimSlash(ResolveApiBaseUrl()) + "/api/server/airstrike-animation-bundle.php?since=" + since.ToString(CultureInfo.InvariantCulture);
            if (requestedRevision > 0)
            {
                url += "&revision=" + requestedRevision.ToString(CultureInfo.InvariantCulture);
            }
            if (!string.IsNullOrWhiteSpace(currentLocalSha))
            {
                url += "&local_hash=" + Uri.EscapeDataString(currentLocalSha);
            }
            var installedSha = NormalizeSha(state.InstalledSha256);
            if (!string.IsNullOrWhiteSpace(installedSha))
            {
                url += "&installed_hash=" + Uri.EscapeDataString(installedSha);
            }

            SendGet(url, (code, response) =>
            {
                try
                {
                    if (!IsSuccess(code, response, out var requestError))
                    {
                        FinishOperation("install_failed", "Bundle request failed: " + requestError, reply, true);
                        return;
                    }

                    var payload = JObject.Parse(response);
                    if (!payload.Value<bool>("ok"))
                    {
                        FinishOperation("install_failed", "Bundle request failed: " + (payload.Value<string>("error") ?? "invalid response"), reply, true);
                        return;
                    }

                    HandleBundlePayload(payload, requestedRevision, force, install, rollback, reason, reply);
                }
                catch (Exception ex)
                {
                    FinishOperation("install_failed", "Bundle request failed: " + ex.GetType().Name + ": " + ex.Message, reply, true);
                }
            });
        }

        private void HandleBundlePayload(JObject payload, long requestedRevision, bool force, bool install, bool rollback, string reason, Action<string> reply)
        {
            var currentRevision = Math.Max(0L, payload.Value<long?>("current_revision") ?? 0L);
            var currentSha = NormalizeSha(payload.Value<string>("sha256"));
            state.LastKnownPublishedRevision = currentRevision;
            state.LastKnownPublishedSha256 = currentSha;

            if (!payload.Value<bool>("has_update"))
            {
                if (payload.Value<bool>("bootstrap_required") && config.BootstrapUploadIfWebsiteEmpty && File.Exists(ResolveVisualProfilesPath()))
                {
                    UploadSnapshotInternal("bootstrap", null, null, true, (ok, message) =>
                    {
                        var status = ok ? "snapshot_uploaded" : "install_failed";
                        FinishOperation(status, ok ? "Website has no bundle; uploaded local profiles for bootstrap. " + message : "Bootstrap snapshot upload failed: " + message, reply, ok);
                    });
                    return;
                }

                var noUpdateMessage = currentRevision > 0
                    ? "Website animation bundle is already current at revision " + currentRevision + "."
                    : "Website has no published airstrike animation bundle.";
                FinishOperation("checked_no_update", noUpdateMessage, reply, config.LogSuccessfulNoUpdateChecks);
                return;
            }

            if (!install)
            {
                var message = "Website animation bundle revision " + currentRevision + " is available" + (string.IsNullOrWhiteSpace(currentSha) ? "." : " (" + ShortSha(currentSha) + ").");
                state.LastStatus = "update_available";
                state.LastMessage = message;
                SaveState();
                operationInFlight = false;
                reply?.Invoke(message);
                return;
            }

            var localSha = GetCurrentLocalSha256();
            if (IsLocalDirty(localSha) && !force)
            {
                UploadSnapshotInternal("sync_conflict", null, null, true, (ok, message) =>
                {
                    var blockMessage = "Blocked website pull because local VisualProfiles.json differs from the last installed bundle. " + (ok ? "Uploaded a conflict snapshot." : "Conflict snapshot upload failed: " + message);
                    FinishOperation("blocked_local_changes", blockMessage, reply, true);
                });
                return;
            }

            if (IsLocalDirty(localSha) && force)
            {
                UploadSnapshotInternal("pre_overwrite_backup", null, null, true, (ok, message) =>
                {
                    if (!ok)
                    {
                        PrintWarning("Pre-overwrite snapshot upload failed before forced pull: " + message);
                    }

                    InstallBundle(payload, currentRevision, currentSha, rollback, reply);
                });
                return;
            }

            InstallBundle(payload, currentRevision, currentSha, rollback, reply);
        }

        private void InstallBundle(JObject payload, long revision, string expectedSha, bool rollback, Action<string> reply)
        {
            string bundleJson;
            byte[] bundleBytes;
            if (!TryExtractBundleBytes(payload, expectedSha, out bundleJson, out bundleBytes, out var bundleError))
            {
                FinishOperation("install_failed", bundleError, reply, true);
                return;
            }

            JObject bundle;
            if (!TryValidateBundleJson(bundleJson, expectedSha, revision, out bundle, out bundleError))
            {
                FinishOperation("install_failed", bundleError, reply, true);
                return;
            }

            if (!RuntimeReloadApiAvailable(out var reloadApiError))
            {
                FinishOperation("install_failed", reloadApiError, reply, true);
                return;
            }

            var targetPath = ResolveVisualProfilesPath();
            var backupPath = "";
            var tempPath = "";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                tempPath = targetPath + ".incoming-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ".tmp";
                File.WriteAllBytes(tempPath, bundleBytes);

                var tempReadBack = File.ReadAllText(tempPath, Encoding.UTF8);
                if (!TryValidateBundleJson(tempReadBack, expectedSha, revision, out bundle, out bundleError))
                {
                    throw new InvalidOperationException("Temporary bundle validation failed: " + bundleError);
                }

                backupPath = CreateBackup("pre-install", GetCurrentLocalSha256(), state.InstalledRevision);
                ReplaceFile(tempPath, targetPath);
                tempPath = "";
            }
            catch (Exception ex)
            {
                TryDelete(tempPath);
                FinishOperation("install_failed", "Could not install VisualProfiles.json: " + ex.Message, reply, true);
                return;
            }

            if (!ReloadConsumers(out var reloadMessage))
            {
                var rollbackMessage = RestoreBackupAfterFailedReload(backupPath, targetPath, out var restoreMessage)
                    ? "Reload failed and previous VisualProfiles.json was restored. " + reloadMessage + " " + restoreMessage
                    : "Reload failed and rollback could not restore the previous VisualProfiles.json. " + reloadMessage + " " + restoreMessage;
                FinishOperation("reload_failed_rolled_back", rollbackMessage, reply, true);
                return;
            }

            state.InstalledRevision = revision;
            state.InstalledSha256 = expectedSha;
            state.InstalledPhysicalSha256 = GetCurrentLocalSha256();
            state.LastSyncAtUtc = NowIso();
            state.LocalDirty = false;
            state.LastInstalledProfileHashes = BuildProfileHashes(bundle);
            SaveState();
            PruneBackups();

            var status = rollback ? "rollback_installed" : "installed";
            var message = (rollback ? "Installed rollback revision " : "Installed website airstrike animation revision ")
                + revision + " with " + CountProfiles(bundle) + " profile(s). " + reloadMessage;
            FinishOperation(status, message, reply, true);
        }

        private void UploadSnapshot(string reason, string jsonOverride, IEnumerable<string> changedProfileIds, bool fromSaveHook, Action<string> reply)
        {
            if (snapshotInFlight)
            {
                reply?.Invoke("Airstrike animation snapshot upload is already in progress.");
                return;
            }

            string configError;
            if (!CanRequest(out configError))
            {
                reply?.Invoke(configError);
                return;
            }

            snapshotInFlight = true;
            UploadSnapshotInternal(reason, jsonOverride, changedProfileIds, fromSaveHook, (ok, message) =>
            {
                snapshotInFlight = false;
                reply?.Invoke(message);
            });
        }

        private void UploadSnapshotInternal(string reason, string jsonOverride, IEnumerable<string> changedProfileIds, bool allowDuringOperation, Action<bool, string> callback)
        {
            try
            {
                string json;
                if (!TryGetSnapshotJson(jsonOverride, out json, out var error))
                {
                    callback(false, error);
                    return;
                }

                var visualProfiles = JObject.Parse(json);
                var basedOnRevision = ExtractPublishedRevision(visualProfiles);
                if (basedOnRevision <= 0)
                {
                    basedOnRevision = Math.Max(0L, state.InstalledRevision);
                }

                var changed = new JArray();
                if (changedProfileIds != null)
                {
                    foreach (var profileId in changedProfileIds)
                    {
                        var normalized = NormalizeProfileKey(profileId);
                        if (!string.IsNullOrWhiteSpace(normalized))
                        {
                            changed.Add(normalized);
                        }
                    }
                }

                var payload = new JObject
                {
                    ["server_id"] = ResolveServerId(),
                    ["based_on_revision"] = basedOnRevision,
                    ["reason"] = reason,
                    ["sha256"] = "",
                    ["changed_profile_keys"] = changed,
                    ["visual_profiles"] = visualProfiles
                };

                var body = payload.ToString(Formatting.None);
                var url = TrimSlash(ResolveApiBaseUrl()) + "/api/server/airstrike-animation-snapshot.php";
                SendPost(url, body, (code, response) =>
                {
                    try
                    {
                        if (!IsSuccess(code, response, out var requestError))
                        {
                            callback(false, requestError);
                            return;
                        }

                        var result = JObject.Parse(response);
                        if (!result.Value<bool>("ok"))
                        {
                            callback(false, result.Value<string>("error") ?? "invalid response");
                            return;
                        }

                        state.LastUploadedLocalSha256 = Sha256(json);
                        state.LastUploadedAtUtc = NowIso();
                        state.LocalDirty = IsLocalDirty(GetCurrentLocalSha256());
                        state.LastStatus = "snapshot_uploaded";
                        state.LastMessage = "Uploaded " + reason + " snapshot " + (result["snapshotId"] == null ? "" : "#" + result.Value<long>("snapshotId")) + ".";
                        SaveState();
                        PostSyncResult("snapshot_uploaded", state.LastMessage);
                        callback(true, state.LastMessage);
                    }
                    catch (Exception ex)
                    {
                        callback(false, "Snapshot response failed: " + ex.GetType().Name + ": " + ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                callback(false, "Snapshot upload failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void RollbackLatestLocalBackup(Action<string> reply)
        {
            if (operationInFlight)
            {
                reply?.Invoke("Airstrike animation sync is already in progress.");
                return;
            }

            var backup = FindLatestBackup();
            if (string.IsNullOrWhiteSpace(backup))
            {
                reply?.Invoke("No local airstrike animation backup is available.");
                return;
            }

            operationInFlight = true;
            var targetPath = ResolveVisualProfilesPath();
            var currentBackup = "";

            try
            {
                currentBackup = CreateBackup("pre-rollback", GetCurrentLocalSha256(), state.InstalledRevision);
                var json = File.ReadAllText(backup, Encoding.UTF8);
                if (!TryValidateBundleJson(json, "", 0L, out var bundle, out var validationError))
                {
                    throw new InvalidOperationException(validationError);
                }

                if (!RuntimeReloadApiAvailable(out var reloadApiError))
                {
                    throw new InvalidOperationException(reloadApiError);
                }

                File.Copy(backup, targetPath, true);

                if (!ReloadConsumers(out var reloadMessage))
                {
                    if (!string.IsNullOrWhiteSpace(currentBackup) && File.Exists(currentBackup))
                    {
                        File.Copy(currentBackup, targetPath, true);
                        ReloadConsumers(out var restoreReloadMessage);
                        throw new InvalidOperationException("Rollback reload failed; previous file was restored. " + reloadMessage + " " + restoreReloadMessage);
                    }

                    throw new InvalidOperationException("Rollback reload failed and no previous backup was available. " + reloadMessage);
                }

                var revision = ExtractPublishedRevision(bundle);
                var sha = NormalizeSha(bundle.Value<string>("PublishedSha256"));
                if (string.IsNullOrWhiteSpace(sha))
                {
                    sha = Sha256(json);
                }

                state.InstalledRevision = revision;
                state.InstalledSha256 = sha;
                state.InstalledPhysicalSha256 = GetCurrentLocalSha256();
                state.LastSyncAtUtc = NowIso();
                state.LocalDirty = false;
                state.LastInstalledProfileHashes = BuildProfileHashes(bundle);
                SaveState();
                FinishOperation("rollback_installed", "Restored local backup " + Path.GetFileName(backup) + ". " + reloadMessage, reply, true);
            }
            catch (Exception ex)
            {
                FinishOperation("install_failed", "Local rollback failed: " + ex.Message, reply, true);
            }
        }

        private bool TryExtractBundleBytes(JObject payload, string expectedSha, out string bundleJson, out byte[] bundleBytes, out string error)
        {
            bundleJson = "";
            bundleBytes = null;
            error = "";

            var encoded = payload.Value<string>("bundle_json_base64");
            if (!string.IsNullOrWhiteSpace(encoded))
            {
                try
                {
                    bundleBytes = Convert.FromBase64String(encoded);
                    if (bundleBytes.Length > Math.Max(1024, config.MaxBundleBytes))
                    {
                        error = "Published bundle exceeds MaxBundleBytes.";
                        return false;
                    }

                    var actualSha = Sha256Bytes(bundleBytes);
                    if (!string.IsNullOrWhiteSpace(expectedSha) && !string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Published bundle SHA mismatch. Expected " + ShortSha(expectedSha) + ", got " + ShortSha(actualSha) + ".";
                        return false;
                    }

                    bundleJson = Encoding.UTF8.GetString(bundleBytes);
                    return true;
                }
                catch (Exception ex)
                {
                    error = "Could not decode bundle_json_base64: " + ex.Message;
                    return false;
                }
            }

            var bundle = payload["bundle"] as JObject;
            if (bundle == null)
            {
                error = "Bundle response did not contain bundle_json_base64 or bundle.";
                return false;
            }

            bundleJson = bundle.ToString(Formatting.None);
            bundleBytes = Encoding.UTF8.GetBytes(bundleJson);
            if (bundleBytes.Length > Math.Max(1024, config.MaxBundleBytes))
            {
                error = "Published bundle exceeds MaxBundleBytes.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(expectedSha) && !string.Equals(Sha256Bytes(bundleBytes), expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                error = "Published bundle SHA mismatch and no canonical base64 bytes were provided.";
                return false;
            }

            return true;
        }

        private bool TryValidateBundleJson(string json, string expectedSha, long expectedRevision, out JObject bundle, out string error)
        {
            bundle = null;
            error = "";

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Bundle JSON is empty.";
                return false;
            }

            if (Encoding.UTF8.GetByteCount(json) > Math.Max(1024, config.MaxBundleBytes))
            {
                error = "Bundle JSON exceeds MaxBundleBytes.";
                return false;
            }

            try
            {
                bundle = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                error = "Bundle JSON parse failed: " + ex.Message;
                return false;
            }

            var schemaVersion = bundle.Value<int?>("SchemaVersion") ?? 0;
            if (schemaVersion != 1 && schemaVersion != 2 && schemaVersion != 3)
            {
                error = "Bundle SchemaVersion must be 1, 2, or 3.";
                return false;
            }

            if (!(bundle["Profiles"] is JObject profiles))
            {
                error = "Bundle Profiles must be an object.";
                return false;
            }

            if (profiles.Count > 200)
            {
                error = "Bundle has too many Profiles.";
                return false;
            }

            foreach (var entry in profiles)
            {
                if (string.IsNullOrWhiteSpace(NormalizeProfileKey(entry.Key)) || !(entry.Value is JObject profile))
                {
                    error = "Bundle contains an invalid profile entry.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(profile.Value<string>("Vehicle")))
                {
                    error = "Profile " + entry.Key + " is missing Vehicle.";
                    return false;
                }

                var waypoints = profile["Waypoints"] as JArray;
                if (waypoints == null || waypoints.Count < 2)
                {
                    error = "Profile " + entry.Key + " must contain at least two Waypoints.";
                    return false;
                }
            }

            var publishedRevision = ExtractPublishedRevision(bundle);
            if (expectedRevision > 0 && publishedRevision > 0 && publishedRevision != expectedRevision)
            {
                error = "Bundle PublishedRevision " + publishedRevision + " does not match response revision " + expectedRevision + ".";
                return false;
            }

            var publishedSha = NormalizeSha(bundle.Value<string>("PublishedSha256"));
            if (!string.IsNullOrWhiteSpace(publishedSha) && !IsValidSha256(publishedSha))
            {
                error = "Bundle PublishedSha256 is not a valid SHA-256 hex value.";
                return false;
            }

            return true;
        }

        private bool ReloadConsumers(out string message)
        {
            var parts = new List<string>();

            if (!RuntimeReloadApiAvailable(out message))
            {
                return false;
            }

            var runtimeResult = PortableAirstrikes.Call("API_ReloadVisualProfiles");
            if (!PluginResultSucceeded(runtimeResult, out var runtimeMessage))
            {
                message = "PortableAirstrikes reload failed: " + runtimeMessage;
                return false;
            }

            parts.Add("PortableAirstrikes: " + runtimeMessage);

            if (PortableAirstrikesAnimationEditor != null && PortableAirstrikesAnimationEditor.IsLoaded)
            {
                var editorResult = PortableAirstrikesAnimationEditor.Call("API_ReloadProfiles");
                if (editorResult is bool && !(bool)editorResult)
                {
                    message = "PortableAirstrikesAnimationEditor reload failed.";
                    return false;
                }

                parts.Add("PortableAirstrikesAnimationEditor reloaded.");
            }
            else
            {
                parts.Add("PortableAirstrikesAnimationEditor is not loaded.");
            }

            message = string.Join(" ", parts.ToArray());
            return true;
        }

        private bool RuntimeReloadApiAvailable(out string message)
        {
            if (PortableAirstrikes == null || !PortableAirstrikes.IsLoaded)
            {
                message = "PortableAirstrikes is not loaded.";
                return false;
            }

            var statusResult = PortableAirstrikes.Call("API_GetVisualProfileStatus");
            if (statusResult == null)
            {
                message = "PortableAirstrikes is loaded but does not expose the visual-profile reload API. Upload and reload PortableAirstrikes.cs v0.1.50 or newer before syncing website animation bundles.";
                return false;
            }

            if (!PluginResultSucceeded(statusResult, out var statusMessage))
            {
                message = "PortableAirstrikes visual-profile status check failed: " + statusMessage;
                return false;
            }

            message = statusMessage;
            return true;
        }

        private bool RestoreBackupAfterFailedReload(string backupPath, string targetPath, out string message)
        {
            message = "";
            if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
            {
                message = "No previous backup was available.";
                return false;
            }

            try
            {
                File.Copy(backupPath, targetPath, true);
                ReloadConsumers(out var reloadMessage);
                message = reloadMessage;
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        private void FinishOperation(string status, string message, Action<string> reply, bool postResult)
        {
            state.LastStatus = status;
            state.LastMessage = message ?? "";
            state.LocalDirty = IsLocalDirty(GetCurrentLocalSha256());
            SaveState();
            operationInFlight = false;

            if (postResult)
            {
                try
                {
                    PostSyncResult(status, message ?? "");
                }
                catch (Exception ex)
                {
                    PrintWarning("Could not queue airstrike animation sync result: " + ex.Message);
                }
            }

            if (reply == null && (status != "checked_no_update" || config.LogSuccessfulNoUpdateChecks))
            {
                Puts(message);
            }

            if (reply != null)
            {
                try
                {
                    reply(message);
                }
                catch (Exception ex)
                {
                    PrintWarning("Could not send airstrike animation command reply: " + ex.Message);
                }
            }
        }

        private void PostSyncResult(string status, string message)
        {
            string configError;
            if (!CanRequest(out configError))
            {
                PrintWarning("Could not post airstrike animation sync result: " + configError);
                return;
            }

            var payload = new JObject
            {
                ["server_id"] = ResolveServerId(),
                ["revision"] = Math.Max(0L, state.InstalledRevision),
                ["status"] = status,
                ["installed_sha256"] = NormalizeSha(state.InstalledSha256),
                ["local_sha256"] = GetCurrentLocalSha256(),
                ["local_dirty"] = state.LocalDirty,
                ["plugin_version"] = Version.ToString(),
                ["runtime_plugin_version"] = GetPluginVersion(PortableAirstrikes),
                ["editor_plugin_version"] = GetPluginVersion(PortableAirstrikesAnimationEditor),
                ["message"] = message ?? ""
            };

            var body = payload.ToString(Formatting.None);
            var url = TrimSlash(ResolveApiBaseUrl()) + "/api/server/airstrike-animation-sync-result.php";
            SendPost(url, body, (code, response) =>
            {
                if (!IsSuccess(code, response, out var requestError))
                {
                    PrintWarning("Airstrike animation sync-result post failed: " + requestError);
                    return;
                }

                try
                {
                    var result = JObject.Parse(response);
                    if (!result.Value<bool>("ok"))
                    {
                        PrintWarning("Airstrike animation sync-result post failed: " + (result.Value<string>("error") ?? "invalid response"));
                    }
                }
                catch (Exception ex)
                {
                    PrintWarning("Airstrike animation sync-result post returned invalid JSON: " + ex.Message);
                }
            });
        }

        private void SendGet(string url, Action<int, string> callback)
        {
            var headers = BuildHeaders("GET", url, "");
            webrequest.Enqueue(url, null, (code, response) => callback(code, response ?? ""), this, RequestMethod.GET, headers, WebRequestTimeoutMilliseconds());
        }

        private void SendPost(string url, string body, Action<int, string> callback)
        {
            var headers = BuildHeaders("POST", url, body ?? "");
            headers["Content-Type"] = "application/json";
            webrequest.Enqueue(url, body ?? "", (code, response) => callback(code, response ?? ""), this, RequestMethod.POST, headers, WebRequestTimeoutMilliseconds());
        }

        private Dictionary<string, string> BuildHeaders(string method, string url, string body)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var pathAndQuery = new Uri(url).PathAndQuery;
            var payload = method.ToUpperInvariant() + "\n" + pathAndQuery + "\n" + timestamp + "\n" + Sha256(body ?? "");
            var signature = HmacSha256(payload, ResolveBridgeSharedSecret());

            return new Dictionary<string, string>
            {
                ["X-Raidlands-Server"] = ResolveServerId(),
                ["X-Raidlands-Timestamp"] = timestamp,
                ["X-Raidlands-Signature"] = signature,
                ["Accept"] = "application/json"
            };
        }

        private bool CanRequest(out string error)
        {
            var apiBase = ResolveApiBaseUrl();
            if (string.IsNullOrWhiteSpace(apiBase) || !Uri.TryCreate(apiBase, UriKind.Absolute, out var uri))
            {
                error = "WebsiteAirstrikeAnimationBridge ApiBaseUrl is not configured as an absolute URL.";
                return false;
            }

            if (!config.AllowInsecureHttpForDevelopment && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                error = "WebsiteAirstrikeAnimationBridge requires HTTPS ApiBaseUrl unless AllowInsecureHttpForDevelopment is true.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ResolveServerId()))
            {
                error = "WebsiteAirstrikeAnimationBridge ServerId is not configured.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ResolveBridgeSharedSecret()))
            {
                error = "WebsiteAirstrikeAnimationBridge SharedSecret is empty after resolving Secrets.local and WebsiteVipBridge fallback.";
                return false;
            }

            error = "";
            return true;
        }

        private void LogBridgeSecretDiagnostics()
        {
            var secret = ResolveBridgeSharedSecret();
            if (string.IsNullOrWhiteSpace(secret))
            {
                PrintWarning("Airstrike animation bridge SharedSecret is empty after resolving secrets.");
                return;
            }

            Puts("Airstrike animation bridge SharedSecret source: " + DescribeBridgeSecretSource() + "; length: " + secret.Length + "; fingerprint: " + SecretFingerprint(secret));
        }

        private string ResolveBridgeSharedSecret()
        {
            var configured = ResolveSecretValue(config.SharedSecret, false);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            var vipSetting = LoadVipBridgeSetting("SharedSecret");
            var vipSecret = ResolveSecretValue(vipSetting, false);
            if (!string.IsNullOrWhiteSpace(vipSecret))
            {
                return vipSecret;
            }

            return ResolveSecretByKey("WEBSITE_VIP_SHARED_SECRET", false);
        }

        private string DescribeBridgeSecretSource()
        {
            if (!string.IsNullOrWhiteSpace(ResolveSecretValue(config.SharedSecret, false)))
            {
                return DescribeSecretSource(config.SharedSecret, "WebsiteAirstrikeAnimationBridge");
            }

            var vipSetting = LoadVipBridgeSetting("SharedSecret");
            if (!string.IsNullOrWhiteSpace(ResolveSecretValue(vipSetting, false)))
            {
                return DescribeSecretSource(vipSetting, "WebsiteVipBridge") + " via oxide/config/WebsiteVipBridge.json";
            }

            if (!string.IsNullOrWhiteSpace(ResolveSecretByKey("WEBSITE_VIP_SHARED_SECRET", false)))
            {
                return "WEBSITE_VIP_SHARED_SECRET in oxide/config/Secrets.local.json";
            }

            return "unresolved";
        }

        private string ResolveApiBaseUrl()
        {
            return FirstNonEmpty((config.ApiBaseUrl ?? "").Trim(), LoadVipBridgeSetting("ApiBaseUrl"), "https://raidlands.net");
        }

        private string ResolveServerId()
        {
            return FirstNonEmpty((config.ServerId ?? "").Trim(), LoadVipBridgeSetting("ServerId"), "raidlands-main");
        }

        private string ResolveSecretValue(string value, bool warnMissing)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            var trimmed = value.Trim();
            if (!trimmed.StartsWith("${", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                return trimmed;
            }

            var key = trimmed.Substring(2, trimmed.Length - 3).Trim();
            return ResolveSecretByKey(key, warnMissing);
        }

        private string ResolveSecretByKey(string key, bool warnMissing)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return "";
            }

            string secret;
            if (LoadSecrets().TryGetValue(key.Trim(), out secret))
            {
                return (secret ?? "").Trim();
            }

            if (warnMissing)
            {
                PrintWarning("Secret variable " + key + " is not configured in oxide/config/" + SecretsConfigName + ".json.");
            }

            return "";
        }

        private string DescribeSecretSource(string value, string configName)
        {
            var trimmed = (value ?? "").Trim();
            if (!trimmed.StartsWith("${", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                return "oxide/config/" + configName + ".json";
            }

            var key = trimmed.Substring(2, trimmed.Length - 3).Trim();
            return key + " in oxide/config/" + SecretsConfigName + ".json";
        }

        private Dictionary<string, string> LoadSecrets()
        {
            if (secrets != null)
            {
                return secrets;
            }

            secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var path = Path.Combine(Interface.Oxide.ConfigDirectory, SecretsConfigName + ".json");
            if (!File.Exists(path))
            {
                return secrets;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (loaded != null)
                {
                    foreach (var entry in loaded)
                    {
                        secrets[entry.Key] = entry.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                PrintWarning("Could not read oxide/config/" + SecretsConfigName + ".json: " + ex.Message);
            }

            return secrets;
        }

        private string LoadVipBridgeSetting(string key)
        {
            if (vipBridgeConfig == null)
            {
                vipBridgeConfig = new JObject();
                var path = Path.Combine(Interface.Oxide.ConfigDirectory, VipBridgeConfigName + ".json");
                if (File.Exists(path))
                {
                    try
                    {
                        vipBridgeConfig = JObject.Parse(File.ReadAllText(path));
                    }
                    catch (Exception ex)
                    {
                        PrintWarning("Could not read oxide/config/" + VipBridgeConfigName + ".json: " + ex.Message);
                    }
                }
            }

            return (vipBridgeConfig.Value<string>(key) ?? "").Trim();
        }

        private void LoadState()
        {
            try
            {
                state = Interface.Oxide.DataFileSystem.ReadObject<BridgeState>(StateDataFileName) ?? new BridgeState();
            }
            catch (Exception ex)
            {
                PrintWarning("Could not read airstrike animation bridge state; starting fresh. " + ex.Message);
                state = new BridgeState();
            }

            if (state.LastInstalledProfileHashes == null)
            {
                state.LastInstalledProfileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void SaveState()
        {
            Interface.Oxide.DataFileSystem.WriteObject(StateDataFileName, state ?? new BridgeState(), true);
        }

        private void AdoptInstalledMetadataFromLocalFile()
        {
            if (state == null || state.InstalledRevision > 0 || !File.Exists(ResolveVisualProfilesPath()))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(ResolveVisualProfilesPath(), Encoding.UTF8);
                var parsed = JObject.Parse(json);
                var revision = ExtractPublishedRevision(parsed);
                var sha = NormalizeSha(parsed.Value<string>("PublishedSha256"));
                if (revision > 0 && IsValidSha256(sha))
                {
                    state.InstalledRevision = revision;
                    state.InstalledSha256 = sha;
                    state.InstalledPhysicalSha256 = GetCurrentLocalSha256();
                    state.LastStatus = "adopted_local_metadata";
                    state.LastMessage = "Adopted PublishedRevision/PublishedSha256 from the existing VisualProfiles.json.";
                    SaveState();
                }
            }
            catch
            {
            }
        }

        private string ResolveVisualProfilesPath()
        {
            var dataFile = string.IsNullOrWhiteSpace(config.VisualProfilesDataFile)
                ? DefaultVisualProfilesDataFile
                : config.VisualProfilesDataFile.Trim();

            dataFile = dataFile.Replace('\\', '/').Trim('/');
            if (dataFile.Contains(".."))
            {
                dataFile = DefaultVisualProfilesDataFile;
            }

            var relative = (dataFile + ".json").Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(Interface.Oxide.DataDirectory, relative);
        }

        private bool TryGetSnapshotJson(string jsonOverride, out string json, out string error)
        {
            json = "";
            error = "";

            if (!string.IsNullOrWhiteSpace(jsonOverride))
            {
                json = jsonOverride.Trim();
            }
            else
            {
                var path = ResolveVisualProfilesPath();
                if (!File.Exists(path))
                {
                    error = "VisualProfiles.json does not exist at " + path + ".";
                    return false;
                }

                json = File.ReadAllText(path, Encoding.UTF8);
            }

            try
            {
                var parsed = JObject.Parse(json);
                if (!(parsed["Profiles"] is JObject))
                {
                    error = "VisualProfiles.json must contain a Profiles object.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = "VisualProfiles.json parse failed: " + ex.Message;
                return false;
            }

            return true;
        }

        private string CreateBackup(string label, string localSha, long revision)
        {
            var source = ResolveVisualProfilesPath();
            if (!File.Exists(source))
            {
                return "";
            }

            var backupDir = BackupDirectory();
            Directory.CreateDirectory(backupDir);
            var safeSha = string.IsNullOrWhiteSpace(localSha) ? "nosha" : ShortSha(localSha);
            var safeLabel = SanitizeFilePart(label);
            var fileName = "VisualProfiles.rev-" + Math.Max(0L, revision).ToString(CultureInfo.InvariantCulture)
                + "." + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)
                + "." + safeSha + "." + safeLabel + ".json";
            var backup = Path.Combine(backupDir, fileName);
            File.Copy(source, backup, false);

            try
            {
                File.WriteAllText(backup + ".state.json", JsonConvert.SerializeObject(state ?? new BridgeState(), Formatting.Indented), Encoding.UTF8);
            }
            catch
            {
            }

            return backup;
        }

        private string FindLatestBackup()
        {
            var backupDir = BackupDirectory();
            if (!Directory.Exists(backupDir))
            {
                return "";
            }

            return Directory.GetFiles(backupDir, "VisualProfiles.rev-*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault() ?? "";
        }

        private void PruneBackups()
        {
            var backupDir = BackupDirectory();
            if (!Directory.Exists(backupDir))
            {
                return;
            }

            var keep = Math.Max(1, config.BackupCount);
            var backups = Directory.GetFiles(backupDir, "VisualProfiles.rev-*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            foreach (var backup in backups.Skip(keep))
            {
                TryDelete(backup);
                TryDelete(backup + ".state.json");
            }
        }

        private string BackupDirectory()
        {
            return Path.Combine(Interface.Oxide.DataDirectory, "WebsiteAirstrikeAnimationBridge", "backups");
        }

        private void ReplaceFile(string tempPath, string targetPath)
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(tempPath, targetPath);
        }

        private Dictionary<string, string> BuildProfileHashes(JObject bundle)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var profiles = bundle?["Profiles"] as JObject;
            if (profiles == null)
            {
                return result;
            }

            foreach (var profile in profiles)
            {
                result[profile.Key] = Sha256(profile.Value.ToString(Formatting.None));
            }

            return result;
        }

        private int CountProfiles(JObject bundle)
        {
            return (bundle?["Profiles"] as JObject)?.Count ?? 0;
        }

        private bool IsLocalDirty(string localSha)
        {
            if (!config.ProtectUnsyncedLocalChanges || string.IsNullOrWhiteSpace(localSha) || !File.Exists(ResolveVisualProfilesPath()))
            {
                state.LocalDirty = false;
                return false;
            }

            var installedPhysicalSha = NormalizeSha(state.InstalledPhysicalSha256);
            var installedSha = !string.IsNullOrWhiteSpace(installedPhysicalSha)
                ? installedPhysicalSha
                : NormalizeSha(state.InstalledSha256);
            var dirty = !string.IsNullOrWhiteSpace(installedSha) && !string.Equals(localSha, installedSha, StringComparison.OrdinalIgnoreCase);
            state.LocalDirty = dirty;
            return dirty;
        }

        private string GetCurrentLocalSha256()
        {
            var path = ResolveVisualProfilesPath();
            if (!File.Exists(path))
            {
                return "";
            }

            try
            {
                return Sha256Bytes(File.ReadAllBytes(path));
            }
            catch
            {
                return "";
            }
        }

        private bool PluginResultSucceeded(object result, out string message)
        {
            message = "";
            if (result == null)
            {
                message = "plugin returned null";
                return false;
            }

            if (result is bool)
            {
                var ok = (bool)result;
                message = ok ? "ok" : "false";
                return ok;
            }

            var dict = result as IDictionary<string, object>;
            if (dict != null)
            {
                object successValue;
                var ok = dict.TryGetValue("success", out successValue) && Convert.ToBoolean(successValue, CultureInfo.InvariantCulture);
                object messageValue;
                message = dict.TryGetValue("message", out messageValue) ? Convert.ToString(messageValue, CultureInfo.InvariantCulture) : (ok ? "ok" : "failed");
                return ok;
            }

            var jObject = result as JObject;
            if (jObject != null)
            {
                var ok = jObject.Value<bool>("success");
                message = jObject.Value<string>("message") ?? (ok ? "ok" : "failed");
                return ok;
            }

            message = result.ToString();
            return true;
        }

        private void ShowPanel(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
            {
                return;
            }

            var localSha = GetCurrentLocalSha256();
            IsLocalDirty(localSha);

            CuiHelper.DestroyUi(player, UiName);
            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel
            {
                Image = { Color = "0.055 0.070 0.085 0.96" },
                RectTransform = { AnchorMin = "0.285 0.185", AnchorMax = "0.715 0.815" },
                CursorEnabled = true
            }, "Overlay", UiName);

            AddLabel(container, root, "Airstrike Animation Sync", 18, TextAnchor.MiddleLeft, "0.045 0.900", "0.760 0.970", "1 0.86 0.58 1");
            AddButton(container, root, "X", "airanimsync.ui close", "0.925 0.915", "0.970 0.965", "0.35 0.12 0.10 0.95", 12);

            var y = 0.815f;
            AddStatusRow(container, root, "Website revision", state.LastKnownPublishedRevision > 0 ? state.LastKnownPublishedRevision.ToString(CultureInfo.InvariantCulture) : "unknown", ref y);
            AddStatusRow(container, root, "Installed revision", state.InstalledRevision > 0 ? state.InstalledRevision.ToString(CultureInfo.InvariantCulture) : "none", ref y);
            AddStatusRow(container, root, "Installed SHA", ShortSha(state.InstalledSha256), ref y);
            AddStatusRow(container, root, "Local SHA", ShortSha(localSha), ref y);
            AddStatusRow(container, root, "Local dirty", state.LocalDirty ? "yes" : "no", ref y);
            AddStatusRow(container, root, "Last check", ShortDate(state.LastCheckAtUtc), ref y);
            AddStatusRow(container, root, "Last sync", ShortDate(state.LastSyncAtUtc), ref y);
            AddStatusRow(container, root, "Last status", state.LastStatus ?? "", ref y);
            AddStatusRow(container, root, "Runtime", GetPluginVersion(PortableAirstrikes), ref y);
            AddStatusRow(container, root, "Editor", GetPluginVersion(PortableAirstrikesAnimationEditor), ref y);

            AddLabel(container, root, Shorten(state.LastMessage ?? "", 150), 9, TextAnchor.UpperLeft, "0.055 0.230", "0.945 0.315", "0.72 0.80 0.86 1");

            AddButton(container, root, "CHECK", "airanimsync.ui check", "0.055 0.140", "0.205 0.198", "0.14 0.20 0.27 0.96", 10);
            AddButton(container, root, "SYNC NOW", "airanimsync.ui sync", "0.225 0.140", "0.395 0.198", "0.12 0.31 0.24 0.96", 10);
            AddButton(container, root, "UPLOAD LOCAL", "airanimsync.ui upload", "0.415 0.140", "0.610 0.198", "0.13 0.27 0.34 0.96", 10);
            AddButton(container, root, "FORCE PULL", "airanimsync.ui force", "0.630 0.140", "0.790 0.198", "0.42 0.20 0.08 0.96", 10);
            AddButton(container, root, "ROLLBACK", "airanimsync.ui rollback", "0.810 0.140", "0.945 0.198", "0.37 0.12 0.10 0.96", 10);

            AddLabel(container, root, operationInFlight ? "Operation in progress..." : "Permission: " + AdminPermission, 8, TextAnchor.MiddleLeft, "0.055 0.060", "0.945 0.105", operationInFlight ? "1 0.72 0.42 1" : "0.48 0.58 0.64 1");
            CuiHelper.AddUi(player, container);
        }

        private void AddStatusRow(CuiElementContainer container, string root, string label, string value, ref float y)
        {
            AddLabel(container, root, label, 10, TextAnchor.MiddleLeft, "0.055 " + FormatAnchor(y - 0.038f), "0.330 " + FormatAnchor(y), "0.55 0.64 0.70 1");
            AddLabel(container, root, value, 10, TextAnchor.MiddleLeft, "0.345 " + FormatAnchor(y - 0.038f), "0.945 " + FormatAnchor(y), "0.90 0.95 1 1");
            y -= 0.055f;
        }

        private void AddLabel(CuiElementContainer container, string parent, string text, int size, TextAnchor align, string min, string max, string color)
        {
            container.Add(new CuiLabel
            {
                Text = { Text = text ?? "", FontSize = size, Align = align, Color = color },
                RectTransform = { AnchorMin = min, AnchorMax = max }
            }, parent);
        }

        private void AddButton(CuiElementContainer container, string parent, string text, string command, string min, string max, string color, int size)
        {
            container.Add(new CuiButton
            {
                Button = { Command = command, Color = color },
                Text = { Text = text ?? "", FontSize = size, Align = TextAnchor.MiddleCenter, Color = "0.96 0.98 1 1" },
                RectTransform = { AnchorMin = min, AnchorMax = max }
            }, parent);
        }

        private bool CanUse(BasePlayer player)
        {
            return player != null && (player.IsAdmin || permission.UserHasPermission(player.UserIDString, AdminPermission));
        }

        private bool CanUseConsole(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Connection == null || arg.IsRcon || arg.IsAdmin)
            {
                return true;
            }

            if (arg.Connection.authLevel >= 2)
            {
                return true;
            }

            var player = GetArgPlayer(arg);
            return CanUse(player);
        }

        private BasePlayer GetArgPlayer(ConsoleSystem.Arg arg)
        {
            return arg?.Connection?.player as BasePlayer;
        }

        private void Reply(BasePlayer player, string message)
        {
            if (player != null)
            {
                player.ChatMessage(message);
            }
        }

        private void ReplyCommand(ConsoleSystem.Arg arg, string message)
        {
            Puts(message);
            try
            {
                arg?.ReplyWith(message);
            }
            catch (Exception ex)
            {
                PrintWarning("Could not send airstrike animation console reply: " + ex.Message);
            }
        }

        private string BuildStatusLine()
        {
            var localSha = GetCurrentLocalSha256();
            IsLocalDirty(localSha);
            return "Airstrike animation sync: installed rev "
                + (state.InstalledRevision > 0 ? state.InstalledRevision.ToString(CultureInfo.InvariantCulture) : "none")
                + ", website rev "
                + (state.LastKnownPublishedRevision > 0 ? state.LastKnownPublishedRevision.ToString(CultureInfo.InvariantCulture) : "unknown")
                + ", local " + ShortSha(localSha)
                + ", dirty=" + state.LocalDirty
                + ", status=" + state.LastStatus
                + ", message=" + state.LastMessage;
        }

        private void NormalizeConfig()
        {
            var defaults = new Configuration();
            config.ApiBaseUrl = FirstNonEmpty(config.ApiBaseUrl, defaults.ApiBaseUrl).Trim();
            config.ServerId = FirstNonEmpty(config.ServerId, defaults.ServerId).Trim();
            config.SharedSecret = FirstNonEmpty(config.SharedSecret, defaults.SharedSecret).Trim();
            config.VisualProfilesDataFile = FirstNonEmpty(config.VisualProfilesDataFile, defaults.VisualProfilesDataFile).Trim();
            config.OpenPanelCommand = NormalizeCommand(FirstNonEmpty(config.OpenPanelCommand, defaults.OpenPanelCommand));
            config.StartupSyncDelaySeconds = Clamp(config.StartupSyncDelaySeconds, 1, 300);
            config.RecurringSyncIntervalSeconds = Clamp(config.RecurringSyncIntervalSeconds, 300, 86400);
            config.BackupCount = Clamp(config.BackupCount, 1, 100);
            config.RequestTimeoutMilliseconds = Clamp(config.RequestTimeoutMilliseconds, 5000, 120000);
            config.MaxBundleBytes = Clamp(config.MaxBundleBytes, 65536, 64 * 1024 * 1024);
        }

        private float WebRequestTimeoutMilliseconds()
        {
            return (float)Math.Max(5000, config.RequestTimeoutMilliseconds);
        }

        private bool IsSuccess(int code, string response, out string error)
        {
            if (code < 200 || code >= 300)
            {
                error = "HTTP " + code + ": " + Shorten(response ?? "", 300);
                return false;
            }

            if (response == null)
            {
                error = "empty response";
                return false;
            }

            error = "";
            return true;
        }

        private bool IsConfiguredVisualProfilesDataFile(string dataFileName)
        {
            return string.Equals(
                (dataFileName ?? "").Trim().Replace('\\', '/').Trim('/'),
                (config.VisualProfilesDataFile ?? DefaultVisualProfilesDataFile).Trim().Replace('\\', '/').Trim('/'),
                StringComparison.OrdinalIgnoreCase);
        }

        private long ExtractPublishedRevision(JObject bundle)
        {
            return Math.Max(0L, bundle?.Value<long?>("PublishedRevision") ?? 0L);
        }

        private static string Sha256(string value)
        {
            return Sha256Bytes(Encoding.UTF8.GetBytes(value ?? ""));
        }

        private static string Sha256Bytes(byte[] value)
        {
            using (var sha = SHA256.Create())
            {
                return Hex(sha.ComputeHash(value ?? new byte[0]));
            }
        }

        private static string HmacSha256(string value, string secret)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? "")))
            {
                return Hex(hmac.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")));
            }
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        private static bool IsValidSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                {
                    return false;
                }
            }

            return true;
        }

        private static string NormalizeSha(string value)
        {
            value = (value ?? "").Trim().ToLowerInvariant();
            return IsValidSha256(value) ? value : "";
        }

        private static string ShortSha(string value)
        {
            value = NormalizeSha(value);
            return string.IsNullOrWhiteSpace(value) ? "" : value.Substring(0, 10);
        }

        private static string SecretFingerprint(string value)
        {
            var hash = Sha256(value ?? "");
            return hash.Length <= 12 ? hash : hash.Substring(0, 12);
        }

        private static string TrimSlash(string value)
        {
            return (value ?? "").Trim().TrimEnd('/');
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "";
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static long ParseRevision(string value)
        {
            long revision;
            return long.TryParse((value ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out revision) && revision > 0 ? revision : 0L;
        }

        private static bool IsForceArg(string value)
        {
            return string.Equals((value ?? "").Trim(), "force", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCommand(string value)
        {
            value = (value ?? "airanimsync").Trim().TrimStart('/');
            return string.IsNullOrWhiteSpace(value) ? "airanimsync" : value;
        }

        private static string NormalizeProfileKey(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length > 80)
            {
                value = value.Substring(0, 80);
            }

            var sb = new StringBuilder();
            foreach (var c in value)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.')
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static string SanitizeFilePart(string value)
        {
            value = (value ?? "backup").Trim();
            var sb = new StringBuilder();
            foreach (var c in value)
            {
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-');
            }

            return sb.Length == 0 ? "backup" : sb.ToString();
        }

        private static string Shorten(string value, int max)
        {
            value = (value ?? "").Trim();
            return value.Length <= max ? value : value.Substring(0, Math.Max(0, max - 3)) + "...";
        }

        private static string ShortDate(string iso)
        {
            DateTime parsed;
            return DateTime.TryParse(iso ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed)
                ? parsed.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
                : "";
        }

        private static string FormatAnchor(float value)
        {
            return Mathf.Clamp01(value).ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string NowIso()
        {
            return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private string GetPluginVersion(Plugin plugin)
        {
            if (plugin == null || !plugin.IsLoaded)
            {
                return "unloaded";
            }

            try
            {
                return plugin.Version.ToString();
            }
            catch
            {
                return "loaded";
            }
        }
    }
}
