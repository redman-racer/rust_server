using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Rust Report Discord", "OpenAI", "0.4.1")]
    [Description("Captures F7 reports with recent PvP combat context and sends an evidence summary to Discord.")]
    public class RustReportDiscord : RustPlugin
    {
        private PluginConfig _config;
        private readonly List<CombatEvent> _combatEvents = new List<CombatEvent>();
        private readonly Dictionary<ulong, ActiveRecording> _activeRecordings = new Dictionary<ulong, ActiveRecording>();
        private Timer _pruneTimer;

        private class PluginConfig
        {
            [JsonProperty("Discord webhook URL")]
            public string DiscordWebhookUrl = "PASTE_WEBHOOK_URL_HERE";

            [JsonProperty("Discord username")]
            public string DiscordUsername = "Rust Reports";

            [JsonProperty("Discord avatar URL")]
            public string DiscordAvatarUrl = "";

            [JsonProperty("Mention text (role/user mention, optional)")]
            public string MentionText = "";

            [JsonProperty("Combat history seconds")]
            public int CombatHistorySeconds = 180;

            [JsonProperty("Maximum combat events per report")]
            public int MaximumCombatEventsPerReport = 20;

            [JsonProperty("Only include combat involving reported player")]
            public bool OnlyIncludeTargetCombat = true;

            [JsonProperty("Save local JSON evidence bundles")]
            public bool SaveLocalBundles = true;

            [JsonProperty("Include grid positions")]
            public bool IncludeGridPositions = true;

            [JsonProperty("Webhook retry attempts")]
            public int RetryAttempts = 3;

            [JsonProperty("Webhook retry delay seconds")]
            public float RetryDelaySeconds = 5f;

            [JsonProperty("Record reported player")]
            public bool RecordReportedPlayer = true;

            [JsonProperty("Recording duration seconds")]
            public int RecordingDurationSeconds = 60;

            [JsonProperty("Only record reports with combat evidence")]
            public bool OnlyRecordWithCombatEvidence = true;

            [JsonProperty("Minimum combat events required to record")]
            public int MinimumCombatEventsToRecord = 1;

            [JsonProperty("Demo file discovery delay seconds")]
            public float DemoDiscoveryDelaySeconds = 3f;

            [JsonProperty("Combat duplicate window milliseconds")]
            public int CombatDuplicateWindowMilliseconds = 250;

            [JsonProperty("Include health estimates in Discord")]
            public bool IncludeHealthEstimates = true;

            [JsonProperty("Include weapon details in Discord")]
            public bool IncludeWeaponDetails = true;

            [JsonProperty("Include team IDs in Discord")]
            public bool IncludeTeamIds = true;

            [JsonProperty("Demo search folders relative to server root")]
            public List<string> DemoSearchFolders = new List<string> { "demos", "server" };

            [JsonProperty("Attach completed demos to Discord")]
            public bool AttachDemosToDiscord = true;

            [JsonProperty("Maximum Discord demo upload size MB")]
            public float MaximumDiscordUploadSizeMb = 8f;
        }

        private class CombatEvent
        {
            public DateTime TimestampUtc;
            public ulong AttackerId;
            public string AttackerName;
            public ulong VictimId;
            public string VictimName;
            public string Weapon;
            public string Ammo;
            public string Bone;
            public float Damage;
            public float Distance;
            public string AttackerPosition;
            public string VictimPosition;
            public string DamageType;
            public string HitLocation;
            public bool Headshot;
            public float VictimHealthBefore;
            public float VictimHealthAfterEstimated;
            public bool LethalEstimated;
            public ulong AttackerTeamId;
            public ulong VictimTeamId;
            public bool SameTeam;
            public float WeaponCondition;
            public float WeaponMaxCondition;
            public ulong WeaponSkinId;
            public int MagazineAmmo;
            public int MagazineCapacity;
            public string Attachments;
        }

        private class EvidenceBundle
        {
            public string IncidentId;
            public DateTime ReportTimeUtc;
            public string ServerName;
            public string ServerAddress;
            public int WorldSeed;
            public int WorldSize;
            public PlayerRecord Reporter;
            public PlayerRecord Target;
            public string Subject;
            public string Message;
            public string ReportType;
            public List<CombatEvent> CombatEvents;
            public string ReportStyle;
            public bool RecordingEligible;
            public string RecordingDecision;
        }

        private class PlayerRecord
        {
            public string Name;
            public string SteamId;
            public string Position;
            public bool Online;
        }

        private class ActiveRecording
        {
            public ulong PlayerId;
            public string PlayerName;
            public DateTime StartedUtc;
            public DateTime StopAtUtc;
            public HashSet<string> ExistingDemoFiles;
            public List<string> IncidentIds = new List<string>();
            public Timer StopTimer;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            PrintWarning("Creating a new configuration file.");
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null) throw new Exception("Configuration deserialized to null.");
            }
            catch (Exception ex)
            {
                PrintError($"Invalid configuration: {ex.Message}");
                LoadDefaultConfig();
            }
            NormalizeConfig();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private void NormalizeConfig()
        {
            // Migrate the previous default recording duration to the new 60-second default.
            if (_config.RecordingDurationSeconds == 300)
                _config.RecordingDurationSeconds = 60;
            _config.CombatHistorySeconds = Mathf.Clamp(_config.CombatHistorySeconds, 30, 3600);
            _config.MaximumCombatEventsPerReport = Mathf.Clamp(_config.MaximumCombatEventsPerReport, 1, 50);
            _config.RetryAttempts = Mathf.Clamp(_config.RetryAttempts, 0, 10);
            _config.RetryDelaySeconds = Mathf.Clamp(_config.RetryDelaySeconds, 1f, 60f);
            _config.RecordingDurationSeconds = Mathf.Clamp(_config.RecordingDurationSeconds, 30, 3600);
            _config.MinimumCombatEventsToRecord = Mathf.Clamp(_config.MinimumCombatEventsToRecord, 1, 50);
            _config.DemoDiscoveryDelaySeconds = Mathf.Clamp(_config.DemoDiscoveryDelaySeconds, 1f, 30f);
            _config.CombatDuplicateWindowMilliseconds = Mathf.Clamp(_config.CombatDuplicateWindowMilliseconds, 0, 2000);
            _config.MaximumDiscordUploadSizeMb = Mathf.Clamp(_config.MaximumDiscordUploadSizeMb, 1f, 100f);
            if (_config.DemoSearchFolders == null || _config.DemoSearchFolders.Count == 0)
                _config.DemoSearchFolders = new List<string> { "demos", "server" };
        }

        private void OnServerInitialized()
        {
            _pruneTimer = timer.Every(30f, PruneCombatEvents);
            if (!IsWebhookConfigured())
                PrintWarning("Discord webhook is not configured. Edit oxide/config/RustReportDiscord.json.");
            Puts($"Loaded. Retaining {_config.CombatHistorySeconds} seconds of PvP combat history.");
        }

        private void Unload()
        {
            _pruneTimer?.Destroy();
            foreach (ActiveRecording recording in _activeRecordings.Values.ToList())
            {
                recording.StopTimer?.Destroy();
                BasePlayer player = FindPlayer(recording.PlayerId);
                if (player != null)
                {
                    try { player.StopServerDemoRecording(); }
                    catch (Exception ex) { PrintWarning($"Could not stop demo for {recording.PlayerId} during unload: {ex.Message}"); }
                }
            }
            _activeRecordings.Clear();
            _combatEvents.Clear();
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var victim = entity as BasePlayer;
            var attacker = info?.InitiatorPlayer;
            if (victim == null || attacker == null) return;
            if (victim.userID == 0 || attacker.userID == 0 || victim.userID == attacker.userID) return;

            float damage = info.damageTypes?.Total() ?? 0f;
            if (damage <= 0f) return;

            Item weaponItem = attacker.GetActiveItem();
            BaseProjectile projectile = weaponItem?.GetHeldEntity() as BaseProjectile;
            string hitLocation = SafeText(info.boneName ?? "unknown", 40);
            float healthBefore = Mathf.Max(0f, victim.health);
            float healthAfter = Mathf.Max(0f, healthBefore - damage);

            var combatEvent = new CombatEvent
            {
                TimestampUtc = DateTime.UtcNow,
                AttackerId = attacker.userID,
                AttackerName = SafeName(attacker.displayName),
                VictimId = victim.userID,
                VictimName = SafeName(victim.displayName),
                Weapon = SafeText(GetWeaponName(info, attacker), 80),
                Ammo = SafeText(info.ProjectilePrefab != null ? info.ProjectilePrefab.name : string.Empty, 80),
                Bone = hitLocation,
                HitLocation = hitLocation,
                Headshot = IsHeadshotLocation(hitLocation),
                DamageType = SafeText(info.damageTypes?.GetMajorityDamageType().ToString() ?? "unknown", 40),
                Damage = Mathf.Round(damage * 10f) / 10f,
                Distance = Mathf.Round(Vector3.Distance(attacker.transform.position, victim.transform.position) * 10f) / 10f,
                AttackerPosition = FormatPosition(attacker.transform.position),
                VictimPosition = FormatPosition(victim.transform.position),
                VictimHealthBefore = Mathf.Round(healthBefore * 10f) / 10f,
                VictimHealthAfterEstimated = Mathf.Round(healthAfter * 10f) / 10f,
                LethalEstimated = healthAfter <= 0f,
                AttackerTeamId = attacker.currentTeam,
                VictimTeamId = victim.currentTeam,
                SameTeam = attacker.currentTeam != 0 && attacker.currentTeam == victim.currentTeam,
                WeaponCondition = weaponItem != null ? Mathf.Round(weaponItem.condition * 10f) / 10f : 0f,
                WeaponMaxCondition = weaponItem != null ? Mathf.Round(weaponItem.maxCondition * 10f) / 10f : 0f,
                WeaponSkinId = weaponItem != null ? weaponItem.skin : 0UL,
                MagazineAmmo = projectile?.primaryMagazine != null ? projectile.primaryMagazine.contents : -1,
                MagazineCapacity = projectile?.primaryMagazine != null ? projectile.primaryMagazine.capacity : -1,
                Attachments = GetWeaponAttachments(weaponItem)
            };

            if (!IsDuplicateCombatEvent(combatEvent))
                _combatEvents.Add(combatEvent);

            if (_combatEvents.Count > 5000) PruneCombatEvents();
        }

        private void OnPlayerReported(BasePlayer reporter, string targetName, string targetId, string subject, string message, string type)
        {
            HandleReport(reporter, targetName, targetId, subject, message, type);
        }

        private void HandleReport(BasePlayer reporter, string targetName, string targetId, string subject, string message, string type)
        {
            DateTime reportTime = DateTime.UtcNow;
            ulong targetSteamId;
            ulong.TryParse(targetId, out targetSteamId);
            BasePlayer target = FindPlayer(targetSteamId);
            string incidentId = $"{reportTime:yyyyMMdd-HHmmss}-{targetId}";

            List<CombatEvent> relevantEvents = _combatEvents
                .Where(x => x.TimestampUtc >= reportTime.AddSeconds(-_config.CombatHistorySeconds))
                .Where(x => !_config.OnlyIncludeTargetCombat || targetSteamId == 0 || x.AttackerId == targetSteamId || x.VictimId == targetSteamId)
                .OrderByDescending(x => x.TimestampUtc)
                .Take(_config.MaximumCombatEventsPerReport)
                .OrderBy(x => x.TimestampUtc)
                .ToList();

            bool hasCombatEvidence = relevantEvents.Count >= _config.MinimumCombatEventsToRecord;
            bool recordingEligible = _config.RecordReportedPlayer &&
                (!_config.OnlyRecordWithCombatEvidence || hasCombatEvidence);

            string reportStyle = hasCombatEvidence ? "Combat-backed" : "General / non-combat";
            string recordingDecision;
            if (!_config.RecordReportedPlayer)
                recordingDecision = "Disabled in configuration";
            else if (_config.OnlyRecordWithCombatEvidence && !hasCombatEvidence)
                recordingDecision = $"Skipped: requires at least {_config.MinimumCombatEventsToRecord} recent combat event(s)";
            else
                recordingDecision = "Eligible: recent combat evidence detected";

            var bundle = new EvidenceBundle
            {
                IncidentId = incidentId,
                ReportTimeUtc = reportTime,
                ServerName = ConVar.Server.hostname,
                ServerAddress = $"{ConVar.Server.ip}:{ConVar.Server.port}",
                WorldSeed = ConVar.Server.seed,
                WorldSize = ConVar.Server.worldsize,
                Reporter = ToPlayerRecord(reporter, reporter?.displayName, reporter?.UserIDString),
                Target = ToPlayerRecord(target, targetName, targetId),
                Subject = SafeText(subject, 250),
                Message = SafeText(message, 1500),
                ReportType = SafeText(type, 100),
                CombatEvents = relevantEvents,
                ReportStyle = reportStyle,
                RecordingEligible = recordingEligible,
                RecordingDecision = recordingDecision
            };

            if (_config.SaveLocalBundles) SaveBundle(bundle);
            SendDiscord(bundle, 0);
            if (recordingEligible)
                StartOrExtendRecording(target, bundle);
            Puts($"Captured {reportStyle.ToLowerInvariant()} report {incidentId}: {bundle.Reporter.Name} reported {bundle.Target.Name}. Recording: {recordingDecision}.");
        }

        private void StartOrExtendRecording(BasePlayer target, EvidenceBundle bundle)
        {
            if (target == null || !target.IsConnected)
            {
                SendRecordingStatus(bundle.IncidentId, bundle.Target, "Recording not started", "The reported player was offline or could not be resolved.", null);
                return;
            }

            DateTime desiredStop = DateTime.UtcNow.AddSeconds(_config.RecordingDurationSeconds);
            ActiveRecording existing;
            if (_activeRecordings.TryGetValue(target.userID, out existing))
            {
                if (!existing.IncidentIds.Contains(bundle.IncidentId))
                    existing.IncidentIds.Add(bundle.IncidentId);
                existing.StopAtUtc = desiredStop;
                ScheduleRecordingStop(existing);
                SendRecordingStatus(bundle.IncidentId, bundle.Target, "Recording extended", $"An existing recording is active until {desiredStop:HH:mm:ss} UTC.", null);
                return;
            }

            var recording = new ActiveRecording
            {
                PlayerId = target.userID,
                PlayerName = SafeName(target.displayName),
                StartedUtc = DateTime.UtcNow,
                StopAtUtc = desiredStop,
                ExistingDemoFiles = SnapshotDemoFiles(),
                IncidentIds = new List<string> { bundle.IncidentId }
            };

            try
            {
                target.StartServerDemoRecording();
                _activeRecordings[target.userID] = recording;
                ScheduleRecordingStop(recording);
                SendRecordingStatus(bundle.IncidentId, bundle.Target, "Recording started", $"Recording for {_config.RecordingDurationSeconds} seconds.", null);
                Puts($"Started server demo recording for {target.displayName} ({target.userID}).");
            }
            catch (Exception ex)
            {
                PrintError($"Could not start demo recording for {target.userID}: {ex}");
                SendRecordingStatus(bundle.IncidentId, bundle.Target, "Recording failed", SafeText(ex.Message, 500), null);
            }
        }

        private void ScheduleRecordingStop(ActiveRecording recording)
        {
            recording.StopTimer?.Destroy();
            float delay = Mathf.Max(1f, (float)(recording.StopAtUtc - DateTime.UtcNow).TotalSeconds);
            recording.StopTimer = timer.Once(delay, () => StopRecording(recording.PlayerId));
        }

        private void StopRecording(ulong playerId)
        {
            ActiveRecording recording;
            if (!_activeRecordings.TryGetValue(playerId, out recording))
                return;

            BasePlayer player = FindPlayer(playerId);
            try
            {
                if (player != null)
                    player.StopServerDemoRecording();
            }
            catch (Exception ex)
            {
                PrintError($"Could not stop demo recording for {playerId}: {ex}");
            }

            _activeRecordings.Remove(playerId);
            timer.Once(_config.DemoDiscoveryDelaySeconds, () => FinalizeRecording(recording));
        }

        private void FinalizeRecording(ActiveRecording recording)
        {
            string demoPath = FindNewDemoFile(recording);
            string details;
            if (string.IsNullOrEmpty(demoPath))
            {
                details = "Recording stopped, but the plugin could not identify the generated .dem file. Check the server demo folders and configure `Demo search folders relative to server root`.";
            }
            else
            {
                var info = new FileInfo(demoPath);
                details = $"Saved `{DiscordEscape(MakeRelativePath(demoPath))}` ({FormatBytes(info.Length)}).";
            }

            foreach (string incidentId in recording.IncidentIds)
            {
                var target = new PlayerRecord { Name = recording.PlayerName, SteamId = recording.PlayerId.ToString(), Position = "unknown", Online = playerIsOnline(recording.PlayerId) };

                if (!string.IsNullOrEmpty(demoPath) && _config.AttachDemosToDiscord)
                {
                    long maxBytes = (long)(_config.MaximumDiscordUploadSizeMb * 1024f * 1024f);
                    long fileBytes = new FileInfo(demoPath).Length;
                    if (fileBytes <= maxBytes)
                    {
                        UploadDemoToDiscord(incidentId, target, demoPath, details);
                        continue;
                    }

                    details += $" Discord attachment skipped because the file exceeds the configured {_config.MaximumDiscordUploadSizeMb:0.##} MB limit.";
                }

                SendRecordingStatus(incidentId, target, string.IsNullOrEmpty(demoPath) ? "Recording completed — file not found" : "Recording completed", details, demoPath);
            }
        }

        private bool playerIsOnline(ulong playerId)
        {
            BasePlayer player = FindPlayer(playerId);
            return player != null && player.IsConnected;
        }

        private HashSet<string> SnapshotDemoFiles()
        {
            return new HashSet<string>(EnumerateDemoFiles(), StringComparer.OrdinalIgnoreCase);
        }

        private string FindNewDemoFile(ActiveRecording recording)
        {
            return EnumerateDemoFiles()
                .Where(path => !recording.ExistingDemoFiles.Contains(path))
                .Select(path => new FileInfo(path))
                .Where(info => info.Exists && info.LastWriteTimeUtc >= recording.StartedUtc.AddSeconds(-5))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Select(info => info.FullName)
                .FirstOrDefault();
        }

        private IEnumerable<string> EnumerateDemoFiles()
        {
            string root = Directory.GetCurrentDirectory();
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string configured in _config.DemoSearchFolders)
            {
                string folder = Path.IsPathRooted(configured) ? configured : Path.Combine(root, configured);
                if (!Directory.Exists(folder))
                    continue;
                try
                {
                    foreach (string file in Directory.EnumerateFiles(folder, "*.dem", SearchOption.AllDirectories))
                        results.Add(Path.GetFullPath(file));
                }
                catch (Exception ex)
                {
                    PrintWarning($"Could not scan demo folder {folder}: {ex.Message}");
                }
            }
            return results;
        }

        private string MakeRelativePath(string path)
        {
            string root = Directory.GetCurrentDirectory().TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length) : path;
        }

        private string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return $"{value:0.##} {units[unit]}";
        }

        private void UploadDemoToDiscord(string incidentId, PlayerRecord target, string demoPath, string details)
        {
            if (!IsWebhookConfigured())
                return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                int statusCode = 0;
                string responseText = string.Empty;
                try
                {
                    string boundary = "----------------RustReportDiscord" + Guid.NewGuid().ToString("N");
                    byte[] fileBytes = File.ReadAllBytes(demoPath);
                    string filename = Path.GetFileName(demoPath);

                    var payload = new
                    {
                        username = _config.DiscordUsername,
                        avatar_url = string.IsNullOrWhiteSpace(_config.DiscordAvatarUrl) ? null : _config.DiscordAvatarUrl,
                        embeds = new[]
                        {
                            new
                            {
                                title = $"Recording completed · {incidentId}",
                                color = 3066993,
                                timestamp = DateTime.UtcNow.ToString("o"),
                                description = $"**{DiscordEscape(target.Name)}** (`{DiscordEscape(target.SteamId)}`)\n{details}\nAttached as `{DiscordEscape(filename)}`.",
                                footer = new { text = "Rust Report Discord v0.4.1" }
                            }
                        }
                    };

                    byte[] body = BuildMultipartBody(boundary, JsonConvert.SerializeObject(payload), filename, fileBytes);
                    var request = (HttpWebRequest)WebRequest.Create(_config.DiscordWebhookUrl);
                    request.Method = "POST";
                    request.ContentType = "multipart/form-data; boundary=" + boundary;
                    request.ContentLength = body.Length;
                    request.Timeout = 60000;
                    request.ReadWriteTimeout = 60000;
                    request.UserAgent = "RustReportDiscord/0.4.1";

                    using (Stream requestStream = request.GetRequestStream())
                        requestStream.Write(body, 0, body.Length);

                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        statusCode = (int)response.StatusCode;
                        using (var reader = new StreamReader(response.GetResponseStream()))
                            responseText = reader.ReadToEnd();
                    }
                }
                catch (WebException ex)
                {
                    if (ex.Response is HttpWebResponse errorResponse)
                    {
                        statusCode = (int)errorResponse.StatusCode;
                        try
                        {
                            using (var reader = new StreamReader(errorResponse.GetResponseStream()))
                                responseText = reader.ReadToEnd();
                        }
                        catch { }
                    }
                    else
                    {
                        responseText = ex.Message;
                    }
                }
                catch (Exception ex)
                {
                    responseText = ex.Message;
                }

                NextTick(() =>
                {
                    if (statusCode == 200 || statusCode == 204)
                    {
                        Puts($"Uploaded demo attachment for incident {incidentId}: {MakeRelativePath(demoPath)}");
                        return;
                    }

                    PrintWarning($"Demo attachment failed for {incidentId}. HTTP {statusCode}: {SafeText(responseText, 500)}");
                    string fallback = details + $" Discord upload failed; the demo remains available locally at `{DiscordEscape(MakeRelativePath(demoPath))}`.";
                    SendRecordingStatus(incidentId, target, "Recording completed — attachment failed", fallback, demoPath);
                });
            });
        }

        private byte[] BuildMultipartBody(string boundary, string payloadJson, string filename, byte[] fileBytes)
        {
            using (var stream = new MemoryStream())
            {
                WriteUtf8(stream, "--" + boundary + "\r\n");
                WriteUtf8(stream, "Content-Disposition: form-data; name=\"payload_json\"\r\n");
                WriteUtf8(stream, "Content-Type: application/json\r\n\r\n");
                WriteUtf8(stream, payloadJson + "\r\n");

                WriteUtf8(stream, "--" + boundary + "\r\n");
                WriteUtf8(stream, "Content-Disposition: form-data; name=\"files[0]\"; filename=\"" + EscapeMultipartFilename(filename) + "\"\r\n");
                WriteUtf8(stream, "Content-Type: application/octet-stream\r\n\r\n");
                stream.Write(fileBytes, 0, fileBytes.Length);
                WriteUtf8(stream, "\r\n--" + boundary + "--\r\n");
                return stream.ToArray();
            }
        }

        private void WriteUtf8(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private string EscapeMultipartFilename(string filename)
        {
            return SafeText(filename, 180).Replace("\\", "_").Replace("\"", "_").Replace("\r", "_").Replace("\n", "_");
        }

        private void SendRecordingStatus(string incidentId, PlayerRecord target, string title, string details, string demoPath)
        {
            if (!IsWebhookConfigured()) return;
            var payload = new
            {
                username = _config.DiscordUsername,
                avatar_url = string.IsNullOrWhiteSpace(_config.DiscordAvatarUrl) ? null : _config.DiscordAvatarUrl,
                embeds = new[]
                {
                    new
                    {
                        title = $"{title} · {incidentId}",
                        color = string.IsNullOrEmpty(demoPath) ? 15844367 : 3066993,
                        timestamp = DateTime.UtcNow.ToString("o"),
                        description = $"**{DiscordEscape(target.Name)}** (`{DiscordEscape(target.SteamId)}`)\n{details}",
                        footer = new { text = "Rust Report Discord v0.4.1" }
                    }
                }
            };
            string json = JsonConvert.SerializeObject(payload);
            webrequest.Enqueue(_config.DiscordWebhookUrl, json, (code, response) =>
            {
                if (code != 200 && code != 204)
                    PrintWarning($"Discord recording-status message failed for {incidentId}. HTTP {code}: {SafeText(response, 300)}");
            }, this, RequestMethod.POST, new Dictionary<string, string> { ["Content-Type"] = "application/json" }, 15f);
        }

        private void SaveBundle(EvidenceBundle bundle)
        {
            try
            {
                Interface.Oxide.DataFileSystem.WriteObject($"RustReportDiscord/incidents/{bundle.IncidentId}", bundle, true);
            }
            catch (Exception ex)
            {
                PrintError($"Could not save evidence bundle {bundle.IncidentId}: {ex.Message}");
            }
        }

        private void SendDiscord(EvidenceBundle bundle, int attempt)
        {
            if (!IsWebhookConfigured())
            {
                PrintWarning($"Report {bundle.IncidentId} was saved locally but not sent: webhook is not configured.");
                return;
            }

            string payload = BuildDiscordPayload(bundle);
            var headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" };

            webrequest.Enqueue(_config.DiscordWebhookUrl, payload, (code, response) =>
            {
                if (code == 200 || code == 204)
                {
                    Puts($"Sent report {bundle.IncidentId} to Discord.");
                    return;
                }

                if (attempt < _config.RetryAttempts)
                {
                    int nextAttempt = attempt + 1;
                    PrintWarning($"Discord returned HTTP {code} for {bundle.IncidentId}; retry {nextAttempt}/{_config.RetryAttempts}.");
                    timer.Once(_config.RetryDelaySeconds * nextAttempt, () => SendDiscord(bundle, nextAttempt));
                    return;
                }

                PrintError($"Discord delivery failed for {bundle.IncidentId}. HTTP {code}: {SafeText(response, 500)}");
            }, this, RequestMethod.POST, headers, 15f);
        }

        private string BuildDiscordPayload(EvidenceBundle bundle)
        {
            var fields = new List<object>
            {
                new { name = "Reported player", value = PlayerDiscordValue(bundle.Target), inline = true },
                new { name = "Reporter", value = PlayerDiscordValue(bundle.Reporter), inline = true },
                new { name = "Report", value = $"**Type:** {DiscordEscape(bundle.ReportType)}\n**Subject:** {DiscordEscape(bundle.Subject)}\n{DiscordEscape(bundle.Message)}", inline = false },
                new { name = "Report classification", value = $"**Style:** {DiscordEscape(bundle.ReportStyle)}\n**Demo:** {DiscordEscape(bundle.RecordingDecision)}", inline = false },
                new { name = $"Recent PvP ({bundle.CombatEvents.Count})", value = BuildCombatSummary(bundle.CombatEvents, bundle.ReportTimeUtc), inline = false },
                new { name = "Server", value = $"{DiscordEscape(bundle.ServerName)}\nSeed `{bundle.WorldSeed}` · Size `{bundle.WorldSize}`", inline = false }
            };

            var payload = new
            {
                username = _config.DiscordUsername,
                avatar_url = string.IsNullOrWhiteSpace(_config.DiscordAvatarUrl) ? null : _config.DiscordAvatarUrl,
                content = string.IsNullOrWhiteSpace(_config.MentionText) ? null : _config.MentionText,
                allowed_mentions = new { parse = new[] { "roles", "users" } },
                embeds = new[]
                {
                    new
                    {
                        title = $"F7 report · {bundle.IncidentId}",
                        color = 15158332,
                        timestamp = bundle.ReportTimeUtc.ToString("o"),
                        fields,
                        footer = new { text = "Rust Report Discord v0.4.0 · combat-prioritized demo recording" }
                    }
                }
            };
            return JsonConvert.SerializeObject(payload);
        }

        private string BuildCombatSummary(List<CombatEvent> events, DateTime reportTime)
        {
            if (events == null || events.Count == 0)
                return "No matching player-v-player damage was recorded in the configured history window.";

            const int maxLength = 1000;
            var selected = new List<string>();
            int used = 0;

            foreach (CombatEvent item in events.OrderByDescending(x => x.TimestampUtc))
            {
                double seconds = Math.Max(0, (reportTime - item.TimestampUtc).TotalSeconds);
                string flags = item.Headshot ? " · **HEADSHOT**" : (item.LethalEstimated ? " · **LETHAL**" : string.Empty);
                string line1 = $"`-{seconds:0}s` **{DiscordEscape(item.AttackerName)}** → **{DiscordEscape(item.VictimName)}** · `{DiscordEscape(item.Weapon)}`{flags}";
                string line2 = $"{item.Damage:0.0} {DiscordEscape(item.DamageType)} dmg · {DiscordEscape(item.HitLocation)} · {item.Distance:0.0}m";

                if (_config.IncludeHealthEstimates)
                    line2 += $" · HP `{item.VictimHealthBefore:0.#}→{item.VictimHealthAfterEstimated:0.#}`";

                if (_config.IncludeWeaponDetails)
                {
                    if (item.MagazineAmmo >= 0 && item.MagazineCapacity >= 0)
                        line2 += $" · mag `{item.MagazineAmmo}/{item.MagazineCapacity}`";
                    if (!string.IsNullOrWhiteSpace(item.Attachments))
                        line2 += $" · att `{DiscordEscape(item.Attachments)}`";
                }

                string line3 = string.Empty;
                if (_config.IncludeGridPositions)
                    line3 = $"`{item.AttackerPosition}` → `{item.VictimPosition}`";

                if (_config.IncludeTeamIds && (item.AttackerTeamId != 0 || item.VictimTeamId != 0))
                {
                    if (!string.IsNullOrEmpty(line3)) line3 += " · ";
                    line3 += $"teams `{item.AttackerTeamId}→{item.VictimTeamId}`{(item.SameTeam ? " **SAME TEAM**" : string.Empty)}";
                }

                string block = line1 + "\n" + line2 + (string.IsNullOrEmpty(line3) ? string.Empty : "\n" + line3);
                int required = block.Length + (selected.Count > 0 ? 2 : 0);
                if (used + required > maxLength)
                    continue;

                selected.Add(block);
                used += required;
            }

            selected.Reverse();
            return selected.Count > 0 ? string.Join("\n\n", selected) : "Combat events were recorded, but the formatted details exceeded Discord's field limit.";
        }

        private bool IsDuplicateCombatEvent(CombatEvent candidate)
        {
            if (_config.CombatDuplicateWindowMilliseconds <= 0)
                return false;

            double windowSeconds = _config.CombatDuplicateWindowMilliseconds / 1000d;
            for (int i = _combatEvents.Count - 1; i >= 0; i--)
            {
                CombatEvent existing = _combatEvents[i];
                double age = (candidate.TimestampUtc - existing.TimestampUtc).TotalSeconds;
                if (age > windowSeconds)
                    break;

                if (existing.AttackerId == candidate.AttackerId
                    && existing.VictimId == candidate.VictimId
                    && string.Equals(existing.Weapon, candidate.Weapon, StringComparison.Ordinal)
                    && string.Equals(existing.HitLocation, candidate.HitLocation, StringComparison.Ordinal)
                    && Math.Abs(existing.Damage - candidate.Damage) < 0.05f)
                    return true;
            }

            return false;
        }

        private bool IsHeadshotLocation(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName))
                return false;

            string value = boneName.ToLowerInvariant();
            return value.Contains("head") || value.Contains("eye");
        }

        private string GetWeaponAttachments(Item weaponItem)
        {
            if (weaponItem?.contents?.itemList == null || weaponItem.contents.itemList.Count == 0)
                return string.Empty;

            return SafeText(string.Join(", ", weaponItem.contents.itemList
                .Where(x => x?.info != null)
                .Select(x => x.info.shortname)
                .ToArray()), 180);
        }

        private PlayerRecord ToPlayerRecord(BasePlayer player, string fallbackName, string fallbackId)
        {
            return new PlayerRecord
            {
                Name = SafeName(player != null ? player.displayName : fallbackName),
                SteamId = player != null ? player.UserIDString : SafeText(fallbackId, 32),
                Position = player != null ? FormatPosition(player.transform.position) : "unknown",
                Online = player != null && player.IsConnected
            };
        }

        private string PlayerDiscordValue(PlayerRecord player)
        {
            string id = string.IsNullOrWhiteSpace(player.SteamId) ? "unknown" : player.SteamId;
            string profile = id.Length >= 17 ? $"[Steam profile](https://steamcommunity.com/profiles/{id})" : "Steam profile unavailable";
            return $"**{DiscordEscape(player.Name)}**\n`{id}` · {profile}\n{(player.Online ? "Online" : "Offline")} · `{player.Position}`";
        }

        private BasePlayer FindPlayer(ulong userId)
        {
            if (userId == 0) return null;
            return BasePlayer.FindByID(userId) ?? BasePlayer.FindSleeping(userId);
        }

        private string GetWeaponName(HitInfo info, BasePlayer attacker)
        {
            if (info?.WeaponPrefab != null && !string.IsNullOrWhiteSpace(info.WeaponPrefab.ShortPrefabName))
                return info.WeaponPrefab.ShortPrefabName;
            Item activeItem = attacker?.GetActiveItem();
            if (activeItem?.info != null) return activeItem.info.shortname;
            return info?.damageTypes?.GetMajorityDamageType().ToString() ?? "unknown";
        }

        private void PruneCombatEvents()
        {
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-_config.CombatHistorySeconds);
            _combatEvents.RemoveAll(x => x.TimestampUtc < cutoff);
        }

        private string FormatPosition(Vector3 position)
        {
            return $"{position.x:0},{position.y:0},{position.z:0}";
        }

        private bool IsWebhookConfigured()
        {
            return !string.IsNullOrWhiteSpace(_config.DiscordWebhookUrl)
                && _config.DiscordWebhookUrl.StartsWith("https://discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase);
        }

        private string DiscordEscape(string value)
        {
            return SafeText(value, 1500).Replace("\\", "\\\\").Replace("`", "'").Replace("@", "@\u200B");
        }

        private string SafeName(string value) => SafeText(string.IsNullOrWhiteSpace(value) ? "Unknown" : value, 80);

        private string SafeText(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            value = value.Replace("\0", string.Empty).Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        [ConsoleCommand("reportdiscord.test")]
        private void TestReportCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && arg.Connection.authLevel < 2) return;
            BasePlayer reporter = arg.Player();
            BasePlayer target = BasePlayer.activePlayerList.FirstOrDefault(x => x != reporter) ?? BasePlayer.sleepingPlayerList.FirstOrDefault();
            HandleReport(reporter, target?.displayName ?? "Test Target", target?.UserIDString ?? "76561190000000000", "Plugin test", "This is a generated test incident from reportdiscord.test.", "Test");
        }
    }
}
