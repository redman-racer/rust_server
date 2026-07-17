using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.UI;

#pragma warning disable 0649

namespace Oxide.Plugins
{
    [Info("PortableAirstrikesAnimationEditor", "Raidlands", "0.2.9")]
    [Description("Admin CUI editor for PortableAirstrikes flight paths and manual or repeated-pattern payload releases, with schema-2 preservation and save notifications for website sync.")]
    public class PortableAirstrikesAnimationEditor : RustPlugin
    {
        private const string AdminPermission = "portableairstrikesanimationeditor.admin";
        private const string DataFileName = "PortableAirstrikes/VisualProfiles";
        private const string UiName = "PortableAirstrikesAnimationEditor.UI";
        private const string WaypointUiName = "PortableAirstrikesAnimationEditor.WaypointUI";
        private const string ValueEditUiName = "PortableAirstrikesAnimationEditor.ValueEdit";
        private const string ConfirmUiName = "PortableAirstrikesAnimationEditor.Confirm";
        private const string InsertUiName = "PortableAirstrikesAnimationEditor.InsertWaypoint";
        private const string TimelineUiName = "PortableAirstrikesAnimationEditor.Timeline";
        private const string ReleaseUiName = "PortableAirstrikesAnimationEditor.ReleaseUI";
        private const string PreviewUiName = "PortableAirstrikesAnimationEditor.PreviewBar";
        private const string AlignUiName = "PortableAirstrikesAnimationEditor.Align";
        private const int DefaultSchemaVersion = 1;
        private const int ReleaseRowsPerPage = 8;
        private const int WaypointRowsPerPage = 8;
        private const int MaxGeneratedReleaseGroups = 1000;
        private const int CommandRowsPerPage = 6;
        private const float ReleasePatternDetectionToleranceSeconds = 0.02f;

        private const string GenericRadiusMapMarkerPrefab = "assets/prefabs/tools/map/genericradiusmarker.prefab";
        private const string DroneVisualPrefab = "assets/prefabs/deployable/drone/drone.deployed.prefab";
        private const string PatrolHelicopterVisualPrefab = "assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab";
        private const string CargoPlaneVisualPrefab = "assets/prefabs/npc/cargo plane/cargo_plane.prefab";
        private const string F15VisualPrefab = "assets/scripts/entity/misc/f15/f15e.prefab";

        private const string DroneDeployEffect = "assets/prefabs/deployable/drone/effects/drone-deploy.prefab";
        private const string VehicleFlybySoundEffect = "assets/content/sound/templates/dangerous-vehicle-engine.prefab";
        private const string ProjectileFlightSoundEffect = "assets/content/sound/templates/projectile-flight.prefab";
        private const string LargeFastFalloffSoundEffect = "assets/content/sound/templates/large-sound-fast-falloff.prefab";
        private const string BulletFlybySoundEffect = "assets/content/sound/templates/bullet-flyby.prefab";
        private const string BulletImpactEffect = "assets/bundled/prefabs/fx/impacts/bullet/generic/generic1.prefab";
        private const string RocketLaunchEffect = "assets/prefabs/weapons/rocketlauncher/effects/rocket_launch_fx.prefab";
        private const string MlrsBackfireEffect = "assets/content/vehicles/mlrs/effects/pfx_mlrs_backfire.prefab";
        private const string PayloadReleaseFlareEffect = "assets/content/vehicles/attackhelicopter/effects/pfx_flares_attackhelicopter.prefab";

        private const float DefaultPreviewMoveIntervalSeconds = 0.04f;
        private const float MinimumPreviewMoveIntervalSeconds = 0.025f;
        private const float MaximumPreviewMoveIntervalSeconds = 0.10f;
        private const float PreviewRideDistance = 5f;
        private const float PreviewRideHeight = 2.5f;
        private const float TangentSampleSeconds = 0.18f;
        private const float MarkerRefreshSeconds = 0.95f;
        private const float MarkerNativeRadius = 0.035f;
        private const float SelectedMarkerNativeRadius = 0.060f;
        private const float TargetMarkerNativeRadius = 0.080f;
        private const float TargetSmokeLift = 0.18f;
        private const float TargetSmokeDebugHeight = 5.5f;
        private const float MarkerDebugDrawDurationSeconds = 1.20f;
        private const float MarkerBubbleRadius = 1.75f;
        private const float SelectedMarkerBubbleRadius = 2.45f;
        private const float MarkerArrowLength = 2.35f;
        private const float SelectedMarkerArrowLength = 3.10f;
        private const float MarkerArrowHeadSize = 0.30f;
        private const float SelectedMarkerArrowHeadSize = 0.42f;
        private const float MarkerAttitudeTickScale = 0.42f;
        private const float WaypointPopupOpenDistance = 8f;
        private const float WaypointPopupClickCooldownSeconds = 0.25f;
        private const float TypedInputCommitDelaySeconds = 0.65f;
        private const float WaypointRotationStepDegrees = 5f;
        private const float WaypointRotationLargeStepDegrees = 15f;
        private const float InsertWaypointDefaultSegmentSeconds = 0.5f;
        private const float TimelineMinimumSegmentSeconds = 0.10f;
        private const float TimelineSmallStepSeconds = 0.25f;
        private const float TimelineLargeStepSeconds = 1.0f;
        private const float TimelineMinimumContentWidth = 980f;
        private const float TimelinePixelsPerSecond = 46f;
        private const float TimelineMinimumNodeWidth = 86f;
        private const float TimelineNodeGap = 8f;
        private const float TimelineViewportWidthPixels = 900f;
        private const float TimelineScrollStepPixels = 360f;
        private const float TimelineEndPaddingPixels = 140f;
        private const float DefaultDroneClearance = 12f;
        private const float DefaultAircraftClearance = 55f;
        private const int MaxChatRows = 12;
        private const int MaxProfilesInUi = 200;
        private const int MaxWaypointsInUi = 120;
        private const int MaxPayloadEventsInProfile = 80;
        private const float DefaultPayloadReleaseIntervalSeconds = 0.5f;

        private static readonly Color WaypointBubbleColor = new Color(0.15f, 0.70f, 1f, 0.82f);
        private static readonly Color WaypointArrowColor = new Color(0.90f, 0.98f, 1f, 1f);
        private static readonly Color SelectedWaypointBubbleColor = new Color(1f, 0.55f, 0.12f, 0.95f);
        private static readonly Color SelectedWaypointArrowColor = new Color(1f, 0.92f, 0.32f, 1f);
        private static readonly Color WaypointRightAxisColor = new Color(1f, 0.22f, 0.18f, 0.90f);
        private static readonly Color WaypointUpAxisColor = new Color(0.20f, 1f, 0.36f, 0.90f);
        private static readonly Color WaypointObjectBodyColor = new Color(0.96f, 0.96f, 0.90f, 0.82f);
        private static readonly Color WaypointObjectAccentColor = new Color(0.24f, 0.78f, 1f, 0.72f);
        private static readonly Color TargetSmokeColor = new Color(0.78f, 0.86f, 0.86f, 0.52f);
        private static readonly Color TargetSmokeCoreColor = new Color(0.10f, 0.80f, 1f, 0.88f);

        private static readonly string[] VehicleValues =
        {
            "drone",
            "cargo_plane",
            "f15",
            "a10",
            "attack_heli"
        };

        private static readonly string[] PayloadValues =
        {
            "bee_grenade",
            "bee_catapult_bomb",
            "beancan",
            "f1_grenade",
            "smoke",
            "flashbang",
            "he_40mm",
            "molotov",
            "firebomb",
            "propane_bomb",
            "hv_rocket",
            "rocket",
            "incendiary_rocket",
            "mortar_he_payload",
            "mortar_frag_payload",
            "bradley_longbarrel_burst",
            "homing_missile",
            "mlrs_rocket"
        };

        private static readonly int TargetRaycastLayer = LayerMask.GetMask(
            "Terrain",
            "World",
            "Construction",
            "Deployed",
            "Default",
            "Vehicle Large",
            "Player (Server)");

        private static readonly int FlightTerrainRaycastLayer = LayerMask.GetMask(
            "Terrain",
            "World",
            "Default");

        private VisualProfileFile profileFile;
        private string lastPersistedProfileJson = "";
        private readonly Dictionary<ulong, EditorSession> sessions = new Dictionary<ulong, EditorSession>();

        [PluginReference]
        private Plugin RaidlandsUiEscapeBridge;

        /*
         * Standalone integration notes:
         * 1. Profiles are saved to oxide/data/PortableAirstrikes/VisualProfiles.json via DataFileName "PortableAirstrikes/VisualProfiles".
         * 2. Admins use /airanim create <id> <vehicle>, /airanim edit <id>, /airanim target, /airanim wp add/set/time/remove,
         *    /airanim preview, and /airanim save. The CUI mirrors these commands and adds quick buttons for common edits.
         * 3. This plugin previews and authors target-relative timed waypoint profiles independently. PortableAirstrikes v0.1.40+ loads
         *    the same DataFileName and maps compatible profile IDs into runtime delivery flight plans.
         * 4. Runtime profile matching prefers exact strike IDs and then falls back to vehicle defaults such as jet_mlrs_run,
         *    a10_strafe_run, cargo_heavy_drop, attack_heli_rocket_run, and drone_grenade_drop.
         */

        private class VisualProfileFile
        {
            [JsonProperty("SchemaVersion")]
            public int SchemaVersion = DefaultSchemaVersion;

            [JsonProperty("CompilerVersion", NullValueHandling = NullValueHandling.Ignore)]
            public string CompilerVersion;

            [JsonProperty("PublishedRevision", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public long PublishedRevision;

            [JsonProperty("PublishedSha256", NullValueHandling = NullValueHandling.Ignore)]
            public string PublishedSha256;

            [JsonProperty("AllowDangerousPayloadPreview")]
            public bool AllowDangerousPayloadPreview;

            [JsonProperty("Profiles")]
            public Dictionary<string, VisualProfileConfig> Profiles = new Dictionary<string, VisualProfileConfig>(StringComparer.OrdinalIgnoreCase);
        }

        private class VisualProfileConfig
        {
            [JsonProperty("Vehicle")]
            public string Vehicle = "f15";

            [JsonProperty("DurationSeconds")]
            public float DurationSeconds = 8f;

            [JsonProperty("FirstPayloadDelaySeconds")]
            public float FirstPayloadDelaySeconds = 3.5f;

            [JsonProperty("PayloadReleaseMode")]
            public string PayloadReleaseMode = "manual";

            [JsonProperty("MaxPayloadCount")]
            public int MaxPayloadCount;

            [JsonProperty("PayloadReleaseIntervalSeconds")]
            public float PayloadReleaseIntervalSeconds = DefaultPayloadReleaseIntervalSeconds;

            [JsonProperty("ReleaseTemplate")]
            public VisualPayloadEvent ReleaseTemplate = new VisualPayloadEvent();

            [JsonProperty("RotationSmoothTimeSeconds")]
            public float RotationSmoothTimeSeconds = 0.12f;

            [JsonProperty("StopAtWaypoints")]
            public bool StopAtWaypoints = true;

            [JsonProperty("MinimumTerrainClearance")]
            public float MinimumTerrainClearance = 55f;

            [JsonProperty("Waypoints")]
            public List<VisualProfileWaypoint> Waypoints = new List<VisualProfileWaypoint>();

            [JsonProperty("PayloadEvents")]
            public List<VisualPayloadEvent> PayloadEvents = new List<VisualPayloadEvent>();

            [JsonProperty("CompiledTrack", NullValueHandling = NullValueHandling.Ignore)]
            public object CompiledTrack;

            [JsonProperty("CompiledReleaseEvents", NullValueHandling = NullValueHandling.Ignore)]
            public object CompiledReleaseEvents;
        }

        private class VisualProfileWaypoint
        {
            [JsonProperty("Time")]
            public float Time;

            [JsonProperty("X")]
            public float X;

            [JsonProperty("Y")]
            public float Y;

            [JsonProperty("Z")]
            public float Z;

            [JsonProperty("RotationX")]
            public float RotationX;

            [JsonProperty("RotationY")]
            public float RotationY;

            [JsonProperty("RotationZ")]
            public float RotationZ;
        }

        private class VisualPayloadEvent
        {
            [JsonProperty("Time")]
            public float Time;

            [JsonProperty("Payload")]
            public string Payload = "";

            [JsonProperty("Index")]
            public int Index;

            [JsonProperty("Count")]
            public int Count = 1;

            [JsonProperty("CarrierOffsetX")]
            public float CarrierOffsetX;

            [JsonProperty("CarrierOffsetY")]
            public float CarrierOffsetY;

            [JsonProperty("CarrierOffsetZ")]
            public float CarrierOffsetZ;

            [JsonProperty("TargetOffsetX")]
            public float TargetOffsetX;

            [JsonProperty("TargetOffsetY")]
            public float TargetOffsetY;

            [JsonProperty("TargetOffsetZ")]
            public float TargetOffsetZ;

            [JsonProperty("SpreadRadius")]
            public float SpreadRadius = -1f;

            [JsonProperty("LaunchSpeed")]
            public float LaunchSpeed = -1f;

            [JsonProperty("FuseSeconds")]
            public float FuseSeconds = -1f;

            [JsonProperty("DamageScale")]
            public float DamageScale = 1f;

            [JsonProperty("VehicleDamageScale")]
            public float VehicleDamageScale = -1f;

            [JsonProperty("SplashRadius")]
            public float SplashRadius = -1f;

            [JsonProperty("ImpactRadius")]
            public float ImpactRadius = -1f;

            [JsonProperty("MaxTrackingSeconds")]
            public float MaxTrackingSeconds = -1f;

            [JsonProperty("MaxTrackingDistance")]
            public float MaxTrackingDistance = -1f;

            [JsonProperty("DamageScales")]
            public Dictionary<string, float> DamageScales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        }

        private class TimelineNodeLayout
        {
            public float Left;
            public float Width;
        }

        private class RepeatedPatternDetection
        {
            public float StartTime;
            public float IntervalSeconds;
            public int UnitsPerRelease;
            public int TotalUnits;
            public int ReleaseGroups;
            public VisualPayloadEvent Template;
        }

        private class CommandHelpEntry
        {
            public string Category;
            public string Syntax;
            public string Description;

            public CommandHelpEntry(string category, string syntax, string description)
            {
                Category = category;
                Syntax = syntax;
                Description = description;
            }
        }

        private class EditorSession
        {
            public ulong UserId;
            public string ProfileId = "";
            public int SelectedWaypointIndex = -1;
            public Vector3 Target;
            public Vector3 Approach = Vector3.forward;
            public bool HasTarget;
            public string LastStatus = "Ready.";
            public string LastWarning = "";
            public BaseEntity PreviewVehicle;
            public Timer PreviewMoveTimer;
            public Timer MarkerTicker;
            public readonly List<Timer> PreviewTimers = new List<Timer>();
            public readonly List<BaseEntity> MarkerEntities = new List<BaseEntity>();
            public readonly HashSet<int> FiredPayloadEvents = new HashSet<int>();
            public readonly List<VisualPayloadEvent> PreviewPayloadSchedule = new List<VisualPayloadEvent>();
            public int NextPreviewPayloadIndex;
            public double PreviewStartedAt;
            public float PreviewPausedElapsed;
            public bool PreviewActive;
            public bool PreviewPaused;
            public bool PreviewUsesNativeCargoPlane;
            public int LastPreviewUiSecond = -1;
            public readonly Dictionary<ulong, PreviewRider> PreviewRiders = new Dictionary<ulong, PreviewRider>();
            public PreviewRider QueuedPreviewRider;
            public bool QueuedPreviewRiderStaged;
            public bool UiOpen;
            public bool WaypointUiOpen;
            public bool InsertUiOpen;
            public bool ObjectMarkersEnabled = true;
            public bool TimelineOpen;
            public float TimelineScrollOffset;
            public int SelectedPayloadEventIndex = -1;
            public VisualPayloadEvent SelectedPayloadEvent;
            public int SelectedGeneratedReleaseIndex;
            public bool ReleaseUiOpen;
            public bool PatternTemplateUiOpen;
            public string ActiveTab = "releases";
            public string ReleaseTimelineView = "releases";
            public string CommandSource = "chat";
            public string CommandCategory = "session";
            public int CommandPage;
            public bool AlignPositionX;
            public bool AlignPositionY = true;
            public bool AlignPositionZ;
            public bool AlignRotationX;
            public bool AlignRotationY;
            public bool AlignRotationZ;
            public bool ReleaseAdvancedOpen;
            public bool ToolsOpen;
            public string ProfileFilter = "";
            public int ReleasePage;
            public int WaypointPage;
            public float WaypointNudgeStep = 1f;
            public readonly List<string> UndoHistory = new List<string>();
            public readonly List<string> RedoHistory = new List<string>();
            public string LastObservedProfileJson = "";
            public bool SuppressHistoryCapture;
            public double LastWaypointClickAt;
            public string NormalizeAxis = "y";
            public readonly HashSet<VisualProfileWaypoint> NormalizeWaypoints = new HashSet<VisualProfileWaypoint>();
            public PendingWaypointCapture PendingWaypoint;
            public PendingAxisInput PendingAxisInput;
            public Timer PendingAxisInputTimer;
            public PendingValueEdit PendingValueEdit;
            public bool ValueEditUiOpen;
        }

        private class PendingWaypointCapture
        {
            public VisualProfileWaypoint Waypoint;
            public Vector3 WorldPosition;
            public Vector3 DesiredForward;
        }

        private class PreviewRider
        {
            public ulong UserId;
            public Vector3 ReturnPosition;
            public Vector3 ReturnViewAngles;
        }

        private class PendingAxisInput
        {
            public string ProfileId;
            public VisualProfileWaypoint Waypoint;
            public string Axis;
            public string Value;
            public bool Rotation;
            public bool FromPopup;
        }

        private class PendingValueEdit
        {
            public string ProfileId;
            public VisualProfileWaypoint Waypoint;
            public string Axis;
            public string ReleaseField;
            public bool Rotation;
            public bool Duration;
            public bool ReleaseEvent;
            public bool FromPopup;
            public VisualPayloadEvent PayloadEvent;
            public string GenericScope = "";
            public string GenericField = "";
            public string DraftValue = "";
            public bool HasDraft;
        }

        private class WorldWaypoint
        {
            public float Time;
            public Vector3 Position;
            public VisualProfileWaypoint Local;
        }

        private void Init()
        {
            permission.RegisterPermission(AdminPermission, this);
            cmd.AddChatCommand("airanim", this, nameof(CmdAirAnim));
            LoadProfiles();
        }

        private void OnServerInitialized()
        {
            Puts("Loaded " + CountProfiles() + " visual profile(s). Admin editor command: /airanim.");
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyUi(player);
            }

            var ids = new List<ulong>(sessions.Keys);
            foreach (var id in ids)
            {
                CloseSession(id, false);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null)
            {
                return;
            }

            StopPreviewRide(player, false);
            CloseSession(player.userID, false);
        }

        [HookMethod(nameof(API_ListProfiles))]
        public object API_ListProfiles()
        {
            NormalizeProfileFile();
            var profiles = new List<object>();
            if (profileFile?.Profiles != null)
            {
                foreach (var entry in profileFile.Profiles)
                {
                    if (entry.Value == null)
                    {
                        continue;
                    }

                    profiles.Add(new Dictionary<string, object>
                    {
                        ["id"] = entry.Key,
                        ["vehicle"] = entry.Value.Vehicle ?? "",
                        ["durationSeconds"] = entry.Value.DurationSeconds,
                        ["firstPayloadDelaySeconds"] = entry.Value.FirstPayloadDelaySeconds,
                        ["payloadReleaseMode"] = entry.Value.PayloadReleaseMode,
                        ["releaseGroupCount"] = BuildEffectiveReleaseSchedule(entry.Value).Count,
                        ["totalPayloadUnits"] = GetTotalPayloadUnits(entry.Value),
                        ["stopAtWaypoints"] = entry.Value.StopAtWaypoints,
                        ["waypointCount"] = entry.Value.Waypoints == null ? 0 : entry.Value.Waypoints.Count
                    });
                }
            }

            return profiles;
        }

        [HookMethod(nameof(API_OpenProfile))]
        public bool API_OpenProfile(ulong playerId, string profileId)
        {
            var player = FindPlayerById(playerId);
            if (player == null || !CanUse(player))
            {
                return false;
            }

            profileId = FindProfileId(profileId);
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return false;
            }

            CmdEdit(player, new[] { "edit", profileId });
            ShowEditorUi(player);
            return true;
        }

        [HookMethod(nameof(API_CreateOrOpenProfile))]
        public bool API_CreateOrOpenProfile(ulong playerId, string profileId, string vehicle)
        {
            var player = FindPlayerById(playerId);
            if (player == null || !CanUse(player))
            {
                return false;
            }

            profileId = NormalizeProfileId(profileId);
            vehicle = NormalizeVehicle(vehicle);
            if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(vehicle))
            {
                return false;
            }

            NormalizeProfileFile();
            if (!profileFile.Profiles.ContainsKey(profileId))
            {
                var profile = CreateStarterProfileForVehicle(vehicle);
                profile.Vehicle = vehicle;
                profileFile.Profiles[profileId] = profile;
                NormalizeProfile(profileId, profile);
                SaveProfiles(new[] { profileId });
            }

            CmdEdit(player, new[] { "edit", profileId });
            ShowEditorUi(player);
            return true;
        }

        [HookMethod(nameof(API_SaveProfiles))]
        public bool API_SaveProfiles()
        {
            SaveProfiles();
            return true;
        }

        [HookMethod(nameof(API_ReloadProfiles))]
        public bool API_ReloadProfiles()
        {
            LoadProfiles();
            return true;
        }

        private object OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null || !input.WasJustPressed(BUTTON.FIRE_PRIMARY) || !CanUse(player))
            {
                return null;
            }

            EditorSession session;
            if (!sessions.TryGetValue(player.userID, out session) || session == null || string.IsNullOrWhiteSpace(session.ProfileId))
            {
                return null;
            }

            if (session.UiOpen || session.WaypointUiOpen || session.ReleaseUiOpen || session.InsertUiOpen || session.ValueEditUiOpen)
            {
                return null;
            }

            var now = GetPreciseNow();
            if (now - session.LastWaypointClickAt < WaypointPopupClickCooldownSeconds)
            {
                return null;
            }

            VisualProfileConfig profile;
            if (!profileFile.Profiles.TryGetValue(session.ProfileId, out profile) || profile == null || profile.Waypoints == null || profile.Waypoints.Count == 0)
            {
                return null;
            }

            int index;
            if (!TryFindLookedAtWaypoint(player, session, profile, out index))
            {
                return null;
            }

            session.LastWaypointClickAt = now;
            session.SelectedWaypointIndex = index;
            SetStatus(session, "Selected waypoint #" + DisplayIndex(index) + ".", "Opened waypoint popup from marker click.");
            RebuildMarkers(player, session);
            ShowWaypointPopupUi(player);
            return true;
        }

        private void CmdAirAnim(BasePlayer player, string command, string[] args)
        {
            if (player == null)
            {
                return;
            }

            if (!CanUse(player))
            {
                if (args != null
                    && args.Length >= 2
                    && IsRideToken(args[0])
                    && IsStopToken(args[1])
                    && IsPreviewRideParticipant(player))
                {
                    StopPreviewRide(player, true);
                    return;
                }

                Reply(player, "You do not have permission to use the animation editor.");
                return;
            }

            if (args == null || args.Length == 0)
            {
                var session = GetOrCreateSession(player);
                EnsureSessionTarget(player, session, false);
                RebuildMarkers(player, session);
                ShowEditorUi(player);
                Reply(player, "Opened the animation editor CUI. Use /airanim hide to hide only the panel, or /airanim close to clean up the session.");
                return;
            }

            var sub = args[0].ToLowerInvariant();
            if (sub == "ui" || sub == "panel" || sub == "open" || sub == "cui" || sub == "show")
            {
                var session = GetOrCreateSession(player);
                EnsureSessionTarget(player, session, false);
                RebuildMarkers(player, session);
                ShowEditorUi(player);
                Reply(player, "Opened the animation editor CUI. Use /airanim close or /airanim hide to hide only the panel; use /airanim end to clean up the session.");
                return;
            }

            if (sub == "hide" || sub == "hideui" || sub == "closeui" || sub == "close")
            {
                HideEditorUi(player, true);
                return;
            }

            if (sub == "end" || sub == "cleanup" || sub == "endsession")
            {
                CloseSession(player.userID, true);
                Reply(player, "Editor session ended. Preview vehicles, marker entities, and CUI were cleaned up.");
                return;
            }

            if (sub == "stop" || sub == "stoppreview")
            {
                var session = GetOrCreateSession(player);
                DestroyPreview(session);
                RebuildMarkers(player, session);
                SetStatus(session, "Preview stopped.", "Markers and the editor session are still active.");
                Reply(player, "Preview stopped. Markers and the editor session are still active.");
                RefreshEditorUiIfOpen(player);
                return;
            }

            if (sub == "pause" || sub == "pausepreview")
            {
                PausePreview(player);
                return;
            }

            if (sub == "resume" || sub == "resumepreview" || sub == "play")
            {
                ResumePreview(player);
                return;
            }

            if (IsRideToken(sub))
            {
                CmdRide(player, args);
                return;
            }

            if (sub == "markers" || sub == "refreshmarkers")
            {
                var session = GetOrCreateSession(player);
                RebuildMarkers(player, session);
                SetStatus(session, "Refreshed waypoint markers.", "Target column refreshed; silent object outlines are " + (session.ObjectMarkersEnabled ? "on" : "off") + ".");
                Reply(player, "Waypoint markers and target column refreshed.");
                RefreshEditorUiIfOpen(player);
                return;
            }

            if (sub == "objects" || sub == "objectmarkers")
            {
                CmdObjects(player, args);
                return;
            }

            if (sub == "timeline")
            {
                CmdTimeline(player, args);
                return;
            }

            if (sub == "payload" || sub == "release" || sub == "ordnance" || sub == "ordinance")
            {
                CmdPayload(player, args);
                return;
            }

            if (sub == "help" || sub == "?")
            {
                var helpSession = GetOrCreateSession(player);
                helpSession.ActiveTab = "commands";
                ShowHelp(player);
                RefreshEditorUiIfOpen(player);
                return;
            }

            if (sub == "list")
            {
                ShowProfileList(player);
                RefreshEditorUiIfOpen(player);
                return;
            }

            if (sub == "create")
            {
                CmdCreate(player, args);
                return;
            }

            if (sub == "edit")
            {
                CmdEdit(player, args);
                return;
            }

            if (sub == "target")
            {
                var session = GetOrCreateSession(player);
                SetSessionTarget(player, session, true);
                RebuildMarkers(player, session);
                RefreshEditorUiIfOpen(player);
                return;
            }

            if (sub == "preview")
            {
                var profileId = args.Length >= 2 ? args[1] : null;
                PreviewProfile(player, profileId);
                return;
            }

            if (sub == "save")
            {
                FlushPendingAxisInput(player);
                var session = GetOrCreateSession(player);
                SaveProfiles(string.IsNullOrWhiteSpace(session.ProfileId) ? null : new[] { session.ProfileId });
                SetStatus(session, "Saved VisualProfiles.json.", "");
                Reply(player, "Saved profiles to oxide/data/PortableAirstrikes/VisualProfiles.json.");
                RefreshEditorUiIfOpen(player);
                return;
            }

            if (sub == "reload")
            {
                var confirmed = args.Length >= 2 && string.Equals(args[1], "confirm", StringComparison.OrdinalIgnoreCase);
                if (HasUnsavedChanges() && !confirmed)
                {
                    SetStatus(GetOrCreateSession(player), "Reload blocked: unsaved changes exist.", "Use /airanim reload confirm or the confirmation dialog in the CUI.");
                    Reply(player, "Reload would discard unsaved changes. Use /airanim reload confirm to continue.");
                    RefreshEditorUiIfOpen(player);
                    return;
                }

                PerformReloadForPlayer(player);
                return;
            }

            if (sub == "delete")
            {
                CmdDelete(player, args);
                return;
            }

            if (sub == "wp" || sub == "waypoint")
            {
                CmdWaypoint(player, args);
                return;
            }

            if (sub == "nudge")
            {
                CmdNudge(player, args);
                return;
            }

            if (sub == "duration")
            {
                CmdSetProfileFloat(player, args, "duration");
                return;
            }

            if (sub == "firstpayload")
            {
                CmdSetProfileFloat(player, args, "firstpayload");
                return;
            }

            if (sub == "smooth")
            {
                CmdSetProfileFloat(player, args, "smooth");
                return;
            }

            if (sub == "clearance")
            {
                CmdSetProfileFloat(player, args, "clearance");
                return;
            }

            if (sub == "stopwaypoints" || sub == "stopwp")
            {
                CmdStopWaypoints(player, args);
                return;
            }

            if (sub == "vehicle")
            {
                CmdSetVehicle(player, args);
                return;
            }

            Reply(player, "Unknown /airanim command. Use /airanim help.");
        }

        [ConsoleCommand("airanim")]
        private void CCmdAirAnimRoot(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null)
            {
                Puts("The airanim console command must be run from an in-game F1 console so an editing player is available.");
                return;
            }

            CmdAirAnim(player, "airanim", GetArgStrings(arg));
        }

        private void CmdCreate(BasePlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                Reply(player, "Usage: /airanim create <profileId> <vehicle>. Vehicles: " + string.Join(", ", VehicleValues) + ".");
                return;
            }

            var profileId = NormalizeProfileId(args[1]);
            if (string.IsNullOrWhiteSpace(profileId))
            {
                Reply(player, "Profile IDs may only contain letters, numbers, underscore, dash, and dot.");
                return;
            }

            var vehicle = NormalizeVehicle(args[2]);
            if (string.IsNullOrWhiteSpace(vehicle))
            {
                Reply(player, "Unknown vehicle '" + args[2] + "'. Vehicles: " + string.Join(", ", VehicleValues) + ".");
                return;
            }

            if (profileFile.Profiles.ContainsKey(profileId))
            {
                Reply(player, "Profile '" + profileId + "' already exists. Use /airanim edit " + profileId + " or /airanim delete " + profileId + ".");
                return;
            }

            var profile = CreateStarterProfileForVehicle(vehicle);
            profile.Vehicle = vehicle;
            profileFile.Profiles[profileId] = profile;
            NormalizeProfile(profileId, profile);

            var session = GetOrCreateSession(player);
            session.ProfileId = profileId;
            session.SelectedWaypointIndex = profile.Waypoints.Count > 0 ? 0 : -1;
            session.SelectedPayloadEvent = null;
            session.SelectedPayloadEventIndex = profile.PayloadEvents != null && profile.PayloadEvents.Count > 0 ? 0 : -1;
            if (session.SelectedPayloadEventIndex >= 0)
            {
                session.SelectedPayloadEvent = profile.PayloadEvents[0];
            }
            session.ReleasePage = 0;
            session.WaypointPage = 0;
            ClearNormalizeSelection(session);
            EnsureSessionTarget(player, session, false);
            SetStatus(session, "Created unsaved profile '" + profileId + "'.", "Use /airanim save when ready.");
            RebuildMarkers(player, session);
            Reply(player, "Created profile '" + profileId + "' using vehicle " + vehicle + ". Use /airanim save to persist it.");
            RefreshEditorUiIfOpen(player);
        }

        private void CmdEdit(BasePlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /airanim edit <profileId>.");
                return;
            }

            VisualProfileConfig profile;
            var profileId = FindProfileId(args[1]);
            if (string.IsNullOrWhiteSpace(profileId) || !profileFile.Profiles.TryGetValue(profileId, out profile) || profile == null)
            {
                Reply(player, "Unknown profile '" + args[1] + "'. Use /airanim list.");
                return;
            }

            NormalizeProfile(profileId, profile);
            var session = GetOrCreateSession(player);
            DestroyPreview(session);
            session.ProfileId = profileId;
            session.SelectedWaypointIndex = profile.Waypoints.Count > 0 ? 0 : -1;
            session.SelectedPayloadEvent = null;
            session.SelectedPayloadEventIndex = profile.PayloadEvents != null && profile.PayloadEvents.Count > 0 ? 0 : -1;
            if (session.SelectedPayloadEventIndex >= 0)
            {
                session.SelectedPayloadEvent = profile.PayloadEvents[0];
            }
            session.ReleasePage = 0;
            session.WaypointPage = 0;
            ClearNormalizeSelection(session);
            EnsureSessionTarget(player, session, false);
            SetStatus(session, "Editing '" + profileId + "'.", "");
            RebuildMarkers(player, session);
            Reply(player, "Editing profile '" + profileId + "'.");
            RefreshEditorUiIfOpen(player);
        }

        private void CmdDelete(BasePlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /airanim delete <profileId>.");
                return;
            }

            var profileId = FindProfileId(args[1]);
            if (string.IsNullOrWhiteSpace(profileId) || !profileFile.Profiles.ContainsKey(profileId))
            {
                Reply(player, "Unknown profile '" + args[1] + "'.");
                return;
            }

            profileFile.Profiles.Remove(profileId);
            SaveProfiles(new[] { profileId });

            EditorSession session;
            if (sessions.TryGetValue(player.userID, out session) && string.Equals(session.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            {
                DestroyPreview(session);
                DestroyMarkers(session);
                session.ProfileId = "";
                session.SelectedWaypointIndex = -1;
                session.SelectedPayloadEventIndex = -1;
                session.SelectedPayloadEvent = null;
                session.ReleasePage = 0;
                session.WaypointPage = 0;
                ClearNormalizeSelection(session);
                SetStatus(session, "Deleted '" + profileId + "'.", "");
            }

            Reply(player, "Deleted profile '" + profileId + "' and saved the profile file.");
            RefreshEditorUiIfOpen(player);
        }

        private void CmdWaypoint(BasePlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Waypoint commands: list, add <time> <x> <y> <z>, here, select <index>, go [index], remove <index>, time <index> <seconds>, duration <index> <seconds>, set <index> <x> <y> <z>, rot <index> <x> <y> <z>, norm <x|y|z> <marked|all|clear|indices...>.");
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var sub = args[1].ToLowerInvariant();
            if (sub == "list")
            {
                ShowWaypointList(player, session, profile);
                RefreshEditorUiIfOpen(player);
                return;
            }

            if (sub == "add")
            {
                if (args.Length < 6)
                {
                    Reply(player, "Usage: /airanim wp add <time> <x> <y> <z>.");
                    return;
                }

                float time;
                float x;
                float y;
                float z;
                if (!TryParseFloat(args[2], out time) || !TryParseFloat(args[3], out x) || !TryParseFloat(args[4], out y) || !TryParseFloat(args[5], out z))
                {
                    Reply(player, "Could not parse waypoint values. Example: /airanim wp add 3.5 0 105 -150");
                    return;
                }

                var waypoint = new VisualProfileWaypoint { Time = time, X = x, Y = y, Z = z };
                profile.Waypoints.Add(waypoint);
                NormalizeProfile(session.ProfileId, profile);
                session.SelectedWaypointIndex = profile.Waypoints.IndexOf(waypoint);
                SetStatus(session, "Added waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + ".", "");
                RebuildMarkers(player, session);
                Reply(player, "Added waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + ".");
                RefreshEditorUiIfOpen(player);
                return;
            }

            if (sub == "here" || sub == "addhere")
            {
                BeginInsertWaypointHere(player, true);
                return;
            }

            if (sub == "select")
            {
                if (args.Length < 3)
                {
                    Reply(player, "Usage: /airanim wp select <index>.");
                    return;
                }

                int index;
                if (!TryParseWaypointIndex(args[2], profile, out index))
                {
                    Reply(player, "Invalid waypoint index. Use /airanim wp list.");
                    return;
                }

                session.SelectedWaypointIndex = index;
                SetStatus(session, "Selected waypoint #" + DisplayIndex(index) + ".", "");
                RebuildMarkers(player, session);
                RefreshEditorUiIfOpen(player);
                return;
            }

            if (sub == "go" || sub == "goto" || sub == "teleport" || sub == "tp")
            {
                if (args.Length >= 3)
                {
                    int index;
                    if (!TryParseWaypointIndex(args[2], profile, out index))
                    {
                        Reply(player, "Invalid waypoint index. Use /airanim wp list.");
                        return;
                    }

                    session.SelectedWaypointIndex = index;
                }

                TeleportPlayerToSelectedWaypoint(player);
                return;
            }

            if (sub == "norm" || sub == "normalize")
            {
                ApplyWaypointNormalizeCommand(player, args, session, profile);
                return;
            }

            if (sub == "remove")
            {
                if (args.Length < 3)
                {
                    Reply(player, "Usage: /airanim wp remove <index>.");
                    return;
                }

                int index;
                if (!TryParseWaypointIndex(args[2], profile, out index))
                {
                    Reply(player, "Invalid waypoint index. Use /airanim wp list.");
                    return;
                }

                profile.Waypoints.RemoveAt(index);
                NormalizeProfile(session.ProfileId, profile);
                session.SelectedWaypointIndex = profile.Waypoints.Count == 0 ? -1 : Mathf.Clamp(index, 0, profile.Waypoints.Count - 1);
                SetStatus(session, "Removed waypoint #" + DisplayIndex(index) + ".", "");
                RebuildMarkers(player, session);
                Reply(player, "Removed waypoint #" + DisplayIndex(index) + ".");
                RefreshEditorUiIfOpen(player);
                return;
            }

            if (sub == "time")
            {
                if (args.Length < 4)
                {
                    Reply(player, "Usage: /airanim wp time <index> <seconds>.");
                    return;
                }

                int index;
                float seconds;
                if (!TryParseWaypointIndex(args[2], profile, out index) || !TryParseFloat(args[3], out seconds))
                {
                    Reply(player, "Invalid waypoint index or time value.");
                    return;
                }

                var waypoint = profile.Waypoints[index];
                waypoint.Time = seconds;
                NormalizeProfile(session.ProfileId, profile);
                session.SelectedWaypointIndex = profile.Waypoints.IndexOf(waypoint);
                SetStatus(session, "Updated waypoint time.", "");
                RebuildMarkers(player, session);
                RefreshEditorUiIfOpen(player);
                return;
            }

            if (sub == "set")
            {
                if (args.Length < 6)
                {
                    Reply(player, "Usage: /airanim wp set <index> <x> <y> <z>.");
                    return;
                }

                int index;
                float x;
                float y;
                float z;
                if (!TryParseWaypointIndex(args[2], profile, out index) || !TryParseFloat(args[3], out x) || !TryParseFloat(args[4], out y) || !TryParseFloat(args[5], out z))
                {
                    Reply(player, "Invalid waypoint index or coordinate value.");
                    return;
                }

                var waypoint = profile.Waypoints[index];
                waypoint.X = x;
                waypoint.Y = y;
                waypoint.Z = z;
                NormalizeProfile(session.ProfileId, profile);
                session.SelectedWaypointIndex = profile.Waypoints.IndexOf(waypoint);
                SetStatus(session, "Updated waypoint coordinates.", "");
                RebuildMarkers(player, session);
                RefreshEditorUiIfOpen(player);
                return;
            }

            if (sub == "duration" || sub == "dur" || sub == "segment" || sub == "seg")
            {
                if (args.Length < 4)
                {
                    Reply(player, "Usage: /airanim wp duration <index> <seconds>.");
                    return;
                }

                int index;
                float seconds;
                if (!TryParseWaypointIndex(args[2], profile, out index) || !TryParseFloat(args[3], out seconds))
                {
                    Reply(player, "Invalid waypoint index or duration value.");
                    return;
                }

                NormalizeProfile(session.ProfileId, profile);
                float appliedDuration;
                if (!SetWaypointSegmentDuration(profile, index, seconds, out appliedDuration))
                {
                    Reply(player, "Could not update waypoint duration.");
                    return;
                }

                NormalizeProfile(session.ProfileId, profile);
                session.SelectedWaypointIndex = Mathf.Clamp(index, 0, profile.Waypoints.Count - 1);
                SetStatus(session, "Updated waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + " duration to " + FormatSeconds(appliedDuration) + ".", "Total duration is now " + FormatSeconds(profile.DurationSeconds) + ".");
                RebuildMarkers(player, session);
                Reply(player, "Updated waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + " duration to " + FormatSeconds(appliedDuration) + ".");
                RefreshEditorUiIfOpen(player);
                return;
            }

            if (sub == "rot" || sub == "rotation")
            {
                if (args.Length < 6)
                {
                    Reply(player, "Usage: /airanim wp rot <index> <rotX> <rotY> <rotZ>.");
                    return;
                }

                int index;
                float x;
                float y;
                float z;
                if (!TryParseWaypointIndex(args[2], profile, out index) || !TryParseFloat(args[3], out x) || !TryParseFloat(args[4], out y) || !TryParseFloat(args[5], out z))
                {
                    Reply(player, "Invalid waypoint index or rotation value.");
                    return;
                }

                var waypoint = profile.Waypoints[index];
                waypoint.RotationX = NormalizeDegrees(x);
                waypoint.RotationY = NormalizeDegrees(y);
                waypoint.RotationZ = NormalizeDegrees(z);
                NormalizeProfile(session.ProfileId, profile);
                session.SelectedWaypointIndex = profile.Waypoints.IndexOf(waypoint);
                SetStatus(session, "Updated waypoint rotation.", "");
                RebuildMarkers(player, session);
                RefreshEditorUiIfOpen(player);
                RefreshWaypointPopupUiIfOpen(player);
                return;
            }

            Reply(player, "Unknown waypoint command. Use /airanim wp list.");
        }

        private void CmdNudge(BasePlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                Reply(player, "Usage: /airanim nudge forward|back|left|right|up|down <meters>.");
                return;
            }

            float meters;
            if (!TryParseFloat(args[2], out meters))
            {
                Reply(player, "Could not parse meters value.");
                return;
            }

            NudgeSelectedWaypoint(player, args[1], meters, true);
        }

        private void CmdObjects(BasePlayer player, string[] args)
        {
            var session = GetOrCreateSession(player);
            var requested = args != null && args.Length >= 2 ? args[1].ToLowerInvariant() : "toggle";
            if (requested == "on" || requested == "true" || requested == "1")
            {
                session.ObjectMarkersEnabled = true;
            }
            else if (requested == "off" || requested == "false" || requested == "0")
            {
                session.ObjectMarkersEnabled = false;
            }
            else
            {
                session.ObjectMarkersEnabled = !session.ObjectMarkersEnabled;
            }

            RebuildMarkers(player, session);

            SetStatus(session, "Waypoint object outlines " + (session.ObjectMarkersEnabled ? "enabled." : "disabled."), "Waypoint bubbles remain visible.");
            Reply(player, "Silent waypoint object outlines are now " + (session.ObjectMarkersEnabled ? "on" : "off") + ". Waypoint bubbles stay visible.");
            RefreshEditorUiIfOpen(player);
            RefreshWaypointPopupUiIfOpen(player);
        }

        private void CmdTimeline(BasePlayer player, string[] args)
        {
            var session = GetOrCreateSession(player);
            var requested = args != null && args.Length >= 2 ? args[1].ToLowerInvariant() : "toggle";
            var wasUiOpen = session.UiOpen;
            if (requested == "on" || requested == "show" || requested == "open" || requested == "true" || requested == "1")
            {
                session.TimelineOpen = true;
                session.ActiveTab = "flight";
            }
            else if (requested == "off" || requested == "hide" || requested == "close" || requested == "false" || requested == "0")
            {
                session.TimelineOpen = false;
                session.TimelineScrollOffset = 0f;
            }
            else
            {
                session.TimelineOpen = !session.TimelineOpen;
                if (session.TimelineOpen)
                {
                    session.ActiveTab = "flight";
                }
                else
                {
                    session.TimelineScrollOffset = 0f;
                }
            }

            SetStatus(session, "Timeline " + (session.TimelineOpen ? "opened." : "closed."), "Timeline edits use segment travel times.");
            if (!session.TimelineOpen)
            {
                CuiHelper.DestroyUi(player, TimelineUiName);
            }

            if (wasUiOpen)
            {
                ShowEditorUi(player);
            }
            else
            {
                RefreshOpenEditorSurfaces(player);
            }
        }

        private void CmdPayload(BasePlayer player, string[] args)
        {
            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var sub = args != null && args.Length >= 2 ? (args[1] ?? "").Trim().ToLowerInvariant() : "list";
            if (sub == "list")
            {
                NormalizeProfile(session.ProfileId, profile);
                var events = BuildEffectiveReleaseSchedule(profile);
                Reply(player, "Payload releases for '" + session.ProfileId + "': mode=" + (IsRepeatedPatternMode(profile) ? "repeated" : "manual") + ", groups=" + events.Count + ", totalUnits=" + GetTotalPayloadUnits(profile) + ", interval=" + FormatSeconds(profile.PayloadReleaseIntervalSeconds) + ".");
                for (var i = 0; i < events.Count && i < MaxChatRows; i++)
                {
                    var ev = events[i];
                    Reply(player, "#" + DisplayIndex(i) + " t=" + FormatSeconds(ev.Time) + " payload=" + GetPayloadDisplay(ev.Payload) + " count=" + Math.Max(1, ev.Count) + " spread=" + FormatOptionalFloat(ev.SpreadRadius) + ".");
                }

                if (events.Count > MaxChatRows)
                {
                    Reply(player, "...and " + (events.Count - MaxChatRows) + " more. Open the Releases tab for the full schedule.");
                }

                RefreshOpenEditorSurfaces(player);
                return;
            }

            if (sub == "mode")
            {
                if (args == null || args.Length < 3)
                {
                    Reply(player, "Usage: /airanim payload mode repeated|manual.");
                    return;
                }

                var requestedMode = NormalizePayloadReleaseMode(args[2]);
                var wasRepeated = IsRepeatedPatternMode(profile);
                var hasStoredPattern = profile.MaxPayloadCount > 0
                    && profile.ReleaseTemplate != null
                    && profile.ReleaseTemplate.Count > 0
                    && !string.IsNullOrWhiteSpace(profile.ReleaseTemplate.Payload);
                if (requestedMode == "generated" && !wasRepeated && !hasStoredPattern)
                {
                    RepeatedPatternDetection detection;
                    if (TryDetectRepeatedPattern(profile, out detection))
                    {
                        profile.FirstPayloadDelaySeconds = detection.StartTime;
                        profile.PayloadReleaseIntervalSeconds = detection.IntervalSeconds;
                        profile.ReleaseTemplate = ClonePayloadEvent(detection.Template) ?? new VisualPayloadEvent();
                        profile.MaxPayloadCount = detection.TotalUnits;
                    }
                    else
                    {
                        if (profile.ReleaseTemplate == null)
                        {
                            profile.ReleaseTemplate = new VisualPayloadEvent();
                        }
                        var source = profile.PayloadEvents != null && profile.PayloadEvents.Count > 0 ? profile.PayloadEvents[0] : null;
                        if (source != null)
                        {
                            profile.ReleaseTemplate = ClonePayloadEvent(source) ?? new VisualPayloadEvent();
                            profile.ReleaseTemplate.Time = 0f;
                            profile.ReleaseTemplate.Index = 0;
                            profile.FirstPayloadDelaySeconds = source.Time;
                        }
                        if (string.IsNullOrWhiteSpace(profile.ReleaseTemplate.Payload))
                        {
                            profile.ReleaseTemplate.Payload = GetDefaultPayloadForVehicle(profile.Vehicle);
                        }
                        if (profile.MaxPayloadCount <= 0)
                        {
                            var total = 0;
                            if (profile.PayloadEvents != null)
                            {
                                foreach (var payloadEvent in profile.PayloadEvents)
                                {
                                    total += payloadEvent == null ? 0 : Math.Max(1, payloadEvent.Count);
                                }
                            }
                            profile.MaxPayloadCount = total > 0 ? total : Math.Max(1, profile.ReleaseTemplate.Count);
                        }
                    }
                }
                profile.PayloadReleaseMode = requestedMode;
                NormalizeProfile(session.ProfileId, profile);
                SetStatus(session, "Payload release mode set to " + (requestedMode == "generated" ? "repeated pattern" : "manual events") + ".", "");
                RefreshOpenEditorSurfaces(player);
                return;
            }

            if (sub == "max")
            {
                if (args == null || args.Length < 3)
                {
                    Reply(player, "Usage: /airanim payload max <count>.");
                    return;
                }

                int max;
                if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out max))
                {
                    Reply(player, "Invalid max count.");
                    return;
                }

                profile.MaxPayloadCount = Mathf.Clamp(max, 0, 1000);
                NormalizeProfile(session.ProfileId, profile);
                SetStatus(session, "Payload max count set to " + profile.MaxPayloadCount + ".", "");
                RefreshOpenEditorSurfaces(player);
                return;
            }

            if (sub == "units" || sub == "perrelease" || sub == "groupcount")
            {
                if (args == null || args.Length < 3)
                {
                    Reply(player, "Usage: /airanim payload units <count>.");
                    return;
                }

                int units;
                if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out units))
                {
                    Reply(player, "Invalid units-per-release count.");
                    return;
                }

                if (profile.ReleaseTemplate == null)
                {
                    profile.ReleaseTemplate = new VisualPayloadEvent();
                }
                profile.ReleaseTemplate.Count = Mathf.Clamp(units, 1, 1000);
                profile.PayloadReleaseMode = "generated";
                NormalizeProfile(session.ProfileId, profile);
                SetStatus(session, "Pattern units per release set to " + profile.ReleaseTemplate.Count + ".", "");
                RefreshOpenEditorSurfaces(player);
                return;
            }

            if (sub == "interval")
            {
                if (args == null || args.Length < 3)
                {
                    Reply(player, "Usage: /airanim payload interval <seconds>.");
                    return;
                }

                float interval;
                if (!TryParseFloat(args[2], out interval))
                {
                    Reply(player, "Invalid interval.");
                    return;
                }

                profile.PayloadReleaseIntervalSeconds = Mathf.Clamp(interval, 0.05f, 30f);
                NormalizeProfile(session.ProfileId, profile);
                SetStatus(session, "Payload interval set to " + FormatSeconds(profile.PayloadReleaseIntervalSeconds) + ".", "");
                RefreshOpenEditorSurfaces(player);
                return;
            }

            if (sub == "add")
            {
                var selectedRelease = GetSelectedPayloadEvent(session, profile);
                float time = selectedRelease != null
                    ? selectedRelease.Time + Mathf.Max(0.05f, profile.PayloadReleaseIntervalSeconds)
                    : profile.FirstPayloadDelaySeconds;
                if (args != null && args.Length >= 3)
                {
                    TryParseFloat(args[2], out time);
                }

                AddPayloadReleaseAt(player, Mathf.Clamp(time, 0f, profile.DurationSeconds), true);
                return;
            }

            if (sub == "edit")
            {
                if (args == null || args.Length < 3)
                {
                    Reply(player, "Usage: /airanim payload edit <index>.");
                    return;
                }

                int index;
                if (!TryParsePayloadEventIndex(args[2], profile, out index))
                {
                    Reply(player, "Invalid release index.");
                    return;
                }

                OpenPayloadReleasePopup(player, index);
                return;
            }

            if (sub == "remove" || sub == "delete" || sub == "del")
            {
                if (args == null || args.Length < 3)
                {
                    Reply(player, "Usage: /airanim payload remove <index>.");
                    return;
                }

                int index;
                if (!TryParsePayloadEventIndex(args[2], profile, out index))
                {
                    Reply(player, "Invalid release index.");
                    return;
                }

                DeletePayloadRelease(player, index);
                return;
            }

            if (sub == "clear")
            {
                if (profile.PayloadEvents != null)
                {
                    profile.PayloadEvents.Clear();
                }

                session.SelectedPayloadEventIndex = -1;
                session.SelectedPayloadEvent = null;
                NormalizeProfile(session.ProfileId, profile);
                SetStatus(session, "Cleared manual payload release events.", "Add a new event or switch to Repeated Pattern.");
                RefreshOpenEditorSurfaces(player);
                return;
            }

            if (sub == "set")
            {
                if (args == null || args.Length < 5)
                {
                    Reply(player, "Usage: /airanim payload set <index> <field> <value>.");
                    return;
                }

                int index;
                if (!TryParsePayloadEventIndex(args[2], profile, out index))
                {
                    Reply(player, "Invalid release index.");
                    return;
                }

                ApplyPayloadReleaseField(player, profile, index, args[3], string.Join(" ", args, 4, args.Length - 4), true);
                return;
            }

            Reply(player, "Payload commands: list, add [seconds], edit <index>, remove <index>, clear, mode repeated|manual, max <totalUnits>, units <perRelease>, interval <seconds>, set <index> <field> <value>.");
        }

        private void CmdStopWaypoints(BasePlayer player, string[] args)
        {
            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var requested = args != null && args.Length >= 2 ? args[1].ToLowerInvariant() : "toggle";
            if (requested == "on" || requested == "true" || requested == "1" || requested == "yes")
            {
                profile.StopAtWaypoints = true;
            }
            else if (requested == "off" || requested == "false" || requested == "0" || requested == "no")
            {
                profile.StopAtWaypoints = false;
            }
            else
            {
                profile.StopAtWaypoints = !profile.StopAtWaypoints;
            }

            NormalizeProfile(session.ProfileId, profile);
            SetStatus(session, "Waypoint stop motion " + (profile.StopAtWaypoints ? "enabled." : "disabled."), profile.StopAtWaypoints ? "Preview eases to a stop at each waypoint." : "Preview blends velocity through waypoint timestamps.");
            Reply(player, "Waypoint stop motion for '" + session.ProfileId + "' is now " + (profile.StopAtWaypoints ? "on" : "off") + ".");
            RefreshOpenEditorSurfaces(player);
        }

        private void CmdSetProfileFloat(BasePlayer player, string[] args, string field)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /airanim " + field + " <seconds/meters>.");
                return;
            }

            float value;
            if (!TryParseFloat(args[1], out value))
            {
                Reply(player, "Could not parse number.");
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            SetProfileFloat(profile, field, value);
            NormalizeProfile(session.ProfileId, profile);
            SetStatus(session, "Updated " + field + ".", "");
            RebuildMarkers(player, session);
            Reply(player, "Updated " + field + " for '" + session.ProfileId + "'.");
            RefreshEditorUiIfOpen(player);
        }

        private void CmdSetVehicle(BasePlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /airanim vehicle <vehicle>. Vehicles: " + string.Join(", ", VehicleValues) + ".");
                return;
            }

            var vehicle = NormalizeVehicle(args[1]);
            if (string.IsNullOrWhiteSpace(vehicle))
            {
                Reply(player, "Unknown vehicle '" + args[1] + "'. Vehicles: " + string.Join(", ", VehicleValues) + ".");
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            profile.Vehicle = vehicle;
            NormalizeProfile(session.ProfileId, profile);
            DestroyPreview(session, true);
            RebuildMarkers(player, session);
            SetStatus(session, "Vehicle changed to " + vehicle + ".", "");
            Reply(player, "Vehicle for '" + session.ProfileId + "' set to " + vehicle + ".");
            RefreshEditorUiIfOpen(player);
        }

        private void PreviewProfile(BasePlayer player, string requestedProfileId)
        {
            var session = GetOrCreateSession(player);
            if (!string.IsNullOrWhiteSpace(requestedProfileId))
            {
                var found = FindProfileId(requestedProfileId);
                if (string.IsNullOrWhiteSpace(found))
                {
                    Reply(player, "Unknown profile '" + requestedProfileId + "'. Use /airanim list.");
                    return;
                }

                session.ProfileId = found;
            }

            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            NormalizeProfile(session.ProfileId, profile);
            var effectiveReleaseSchedule = BuildEffectiveReleaseSchedule(profile);
            if (effectiveReleaseSchedule.Count == 0)
            {
                Reply(player, "This profile has no active payload releases. The vehicle path will preview without release cues.");
            }
            else if (effectiveReleaseSchedule[effectiveReleaseSchedule.Count - 1].Time > profile.DurationSeconds + 0.001f)
            {
                Reply(player, "Warning: the active release schedule continues past the profile duration. Only releases at or before " + FormatSeconds(profile.DurationSeconds) + " will appear in this preview.");
            }

            if (profile.Waypoints == null || profile.Waypoints.Count < 2)
            {
                Reply(player, "Profile '" + session.ProfileId + "' needs at least two waypoints before preview.");
                SetStatus(session, "Preview rejected.", "Add at least two waypoints.");
                RefreshEditorUiIfOpen(player);
                return;
            }

            EnsureSessionTarget(player, session, false);
            DestroyPreview(session);
            RebuildMarkers(player, session);

            var plan = BuildWorldWaypoints(session, profile);
            if (plan.Count < 2)
            {
                RebuildMarkers(player, session);
                Reply(player, "Profile '" + session.ProfileId + "' could not build a valid world path.");
                return;
            }

            var start = EvaluatePlanPosition(plan, profile, 0f);
            var direction = GetPlanDirection(plan, profile, 0f, session.Approach);
            if (direction.sqrMagnitude <= 0.01f)
            {
                direction = session.Approach.sqrMagnitude > 0.01f ? session.Approach : Vector3.forward;
            }

            var prefab = GetVehiclePrefab(profile.Vehicle);
            if (string.IsNullOrWhiteSpace(prefab))
            {
                Reply(player, "Vehicle '" + profile.Vehicle + "' does not have a preview prefab mapping.");
                return;
            }

            BaseEntity entity = null;
            try
            {
                entity = GameManager.server.CreateEntity(prefab, start, Quaternion.LookRotation(direction.normalized, Vector3.up), true) as BaseEntity;
                if (entity == null)
                {
                    RebuildMarkers(player, session);
                    Reply(player, "Could not create preview vehicle prefab '" + prefab + "' for vehicle '" + profile.Vehicle + "'.");
                    PrintWarning("Could not create preview vehicle prefab '" + prefab + "' for vehicle '" + profile.Vehicle + "'.");
                    return;
                }

                entity.OwnerID = player.userID;
                entity.EnableSaving(false);
                entity.Spawn();
                TrySetCreatorEntity(entity, player);
                PreparePreviewVehicle(entity, profile.Vehicle, Vector3.zero);

                session.PreviewVehicle = entity;
                session.PreviewStartedAt = GetPreciseNow();
                session.PreviewPausedElapsed = 0f;
                session.PreviewActive = true;
                session.PreviewPaused = false;
                session.PreviewUsesNativeCargoPlane = false;
                session.FiredPayloadEvents.Clear();
                PreparePreviewPayloadSchedule(session, profile);
                SetStatus(session, "Previewing '" + session.ProfileId + "'.", "Payload cues use the visible " + (IsRepeatedPatternMode(profile) ? "repeated pattern" : "manual schedule") + ".");

                MovePreviewVehicle(session, profile, plan, 0f, true);
                StartQueuedPreviewRide(player, session);
                SchedulePreviewSoundCues(player, session, profile, plan);
                SchedulePreviewStep(player, session, profile, plan);
                Reply(player, "Preview started for '" + session.ProfileId + "'. Vehicle=" + profile.Vehicle + ", duration=" + FormatSeconds(profile.DurationSeconds) + ". CUI hidden so you can watch it. Use /airanim to reopen or /airanim stop to end the preview.");
                HideEditorUi(player, false);
                ShowPreviewBarUi(player, session, profile, 0f);
            }
            catch (Exception ex)
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill(BaseNetworkable.DestroyMode.None);
                }

                RebuildMarkers(player, session);
                PrintWarning("Preview spawn failed for '" + session.ProfileId + "' vehicle '" + profile.Vehicle + "' prefab '" + prefab + "': " + ex.Message);
                Reply(player, "Preview spawn failed for prefab '" + prefab + "': " + ex.Message);
            }
        }

        private void StartCargoPlanePreview(BasePlayer player, EditorSession session, VisualProfileConfig profile, List<WorldWaypoint> plan, Vector3 start, Vector3 direction)
        {
            var phase = "create";
            CargoPlane plane = null;

            try
            {
                var safeDuration = Mathf.Max(0.1f, profile.DurationSeconds);
                var end = EvaluatePlanPosition(plan, profile, safeDuration);
                var effectiveSchedule = BuildEffectiveReleaseSchedule(profile);
                var firstReleaseTime = effectiveSchedule.Count > 0 ? effectiveSchedule[0].Time : profile.FirstPayloadDelaySeconds;
                var release = EvaluatePlanPosition(plan, profile, firstReleaseTime);
                var routeDirection = end - start;
                if (routeDirection.sqrMagnitude <= 0.01f)
                {
                    routeDirection = direction.sqrMagnitude <= 0.01f ? session.Approach : direction;
                }

                if (routeDirection.sqrMagnitude <= 0.01f)
                {
                    routeDirection = Vector3.forward;
                }

                phase = "create cargo plane prefab";
                plane = GameManager.server.CreateEntity(CargoPlaneVisualPrefab, Vector3.zero, Quaternion.identity, true) as CargoPlane;
                if (plane == null)
                {
                    RebuildMarkers(player, session);
                    Reply(player, "Could not create preview cargo plane prefab '" + CargoPlaneVisualPrefab + "'.");
                    PrintWarning("Could not create preview cargo plane prefab '" + CargoPlaneVisualPrefab + "'.");
                    return;
                }

                phase = "configure cargo plane";
                plane.OwnerID = player == null ? 0UL : player.userID;
                plane.EnableSaving(false);
                plane.dropped = true;
                plane.InitDropPosition(release);
                plane.dropped = true;

                phase = "configure cargo plane networking";
                var networkable = plane as BaseNetworkable;
                if (networkable.net == null)
                {
                    networkable.net = Network.Net.sv.CreateNetworkable();
                }

                networkable.limitNetworking = true;

                phase = "spawn cargo plane";
                if ((int)plane.creationFrame == 0)
                {
                    plane.Spawn();
                }

                TrySetCreatorEntity(plane, player);

                phase = "configure cargo plane route";
                plane.transform.position = start;
                plane.transform.rotation = Quaternion.LookRotation(routeDirection.normalized, Vector3.up);
                plane.startPos = start;
                plane.endPos = end;
                plane.secondsToTake = safeDuration;
                plane.secondsTaken = 0f;
                networkable.limitNetworking = false;
                plane.SendNetworkUpdateImmediate();

                session.PreviewVehicle = plane;
                session.PreviewStartedAt = GetPreciseNow();
                session.PreviewPausedElapsed = 0f;
                session.PreviewActive = true;
                session.PreviewPaused = false;
                session.PreviewUsesNativeCargoPlane = true;
                session.FiredPayloadEvents.Clear();
                PreparePreviewPayloadSchedule(session, profile);
                SetStatus(session, "Previewing '" + session.ProfileId + "'.", "Cargo plane is using its native route and the visible release schedule.");

                SchedulePreviewSoundCues(player, session, profile, plan);
                SchedulePreviewStep(player, session, profile, plan);
                Reply(player, "Preview started for '" + session.ProfileId + "'. Vehicle=" + profile.Vehicle + ", duration=" + FormatSeconds(profile.DurationSeconds) + ". CUI hidden so you can watch it. Use /airanim to reopen or /airanim stop to end the preview.");
                HideEditorUi(player, false);
                ShowPreviewBarUi(player, session, profile, 0f);
            }
            catch (Exception ex)
            {
                if (plane != null && !plane.IsDestroyed)
                {
                    plane.Kill(BaseNetworkable.DestroyMode.None);
                }

                RebuildMarkers(player, session);
                PrintWarning("Cargo plane preview failed for '" + session.ProfileId + "' at phase '" + phase + "': " + ex.Message);
                Reply(player, "Cargo plane preview failed at " + phase + ": " + ex.Message);
            }
        }

        private void SchedulePreviewStep(BasePlayer player, EditorSession session, VisualProfileConfig profile, List<WorldWaypoint> plan)
        {
            if (session == null || profile == null || plan == null)
            {
                return;
            }

            var interval = GetPreviewMoveIntervalSeconds();
            session.PreviewMoveTimer = timer.Once(interval, () =>
            {
                session.PreviewMoveTimer = null;
                if (!IsSessionActive(player, session) || session.PreviewVehicle == null || session.PreviewVehicle.IsDestroyed)
                {
                    return;
                }

                if (session.PreviewPaused)
                {
                    ShowPreviewBarUi(player, session, profile, session.PreviewPausedElapsed);
                    return;
                }

                var elapsed = (float)(GetPreciseNow() - session.PreviewStartedAt);
                var safeDuration = Mathf.Max(0.1f, profile.DurationSeconds);
                var previewSecond = Mathf.FloorToInt(elapsed);
                if (previewSecond != session.LastPreviewUiSecond)
                {
                    ShowPreviewBarUi(player, session, profile, elapsed);
                }
                MovePreviewVehicle(session, profile, plan, elapsed, false);
                UpdatePreviewRiders(session);
                TriggerPayloadPreviewCues(player, session, profile, plan, elapsed);

                if (elapsed >= safeDuration)
                {
                    CompletePreview(player, session);
                    return;
                }

                SchedulePreviewStep(player, session, profile, plan);
            });
        }

        private void MovePreviewVehicle(EditorSession session, VisualProfileConfig profile, List<WorldWaypoint> plan, float elapsed, bool immediate)
        {
            var vehicle = session == null ? null : session.PreviewVehicle;
            if (vehicle == null || vehicle.IsDestroyed || profile == null || plan == null || plan.Count == 0)
            {
                return;
            }

            var position = EvaluatePlanPosition(plan, profile, elapsed);
            position = EnsurePositionAboveTerrain(position, GetProfileClearance(profile));
            var direction = GetPlanDirection(plan, profile, elapsed, session.Approach);
            if (direction.sqrMagnitude <= 0.01f)
            {
                direction = session.Approach.sqrMagnitude > 0.01f ? session.Approach : Vector3.forward;
            }

            var velocity = GetPlanVelocity(plan, profile, elapsed, direction);
            var targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up) * EvaluatePlanRotationOffset(plan, profile, elapsed);
            var smooth = Mathf.Clamp(profile.RotationSmoothTimeSeconds <= 0f ? 0.12f : profile.RotationSmoothTimeSeconds, 0.02f, 1.5f);
            var blend = immediate ? 1f : Mathf.Clamp01(GetPreviewMoveIntervalSeconds() / smooth);
            var rotation = Quaternion.Slerp(vehicle.transform.rotation, targetRotation, blend);

            MoveEntity(vehicle, position, rotation, velocity, immediate);
        }

        private void TriggerPayloadPreviewCues(BasePlayer player, EditorSession session, VisualProfileConfig profile, List<WorldWaypoint> plan, float elapsed)
        {
            if (player == null || session == null || profile == null || plan == null)
            {
                return;
            }

            var schedule = session.PreviewPayloadSchedule;
            if (schedule == null || schedule.Count == 0)
            {
                return;
            }

            while (session.NextPreviewPayloadIndex < schedule.Count)
            {
                var i = session.NextPreviewPayloadIndex;
                var payloadEvent = schedule[i];
                if (payloadEvent == null)
                {
                    session.NextPreviewPayloadIndex++;
                    continue;
                }

                if (elapsed < payloadEvent.Time)
                {
                    break;
                }

                session.NextPreviewPayloadIndex++;
                session.FiredPayloadEvents.Add(i);
                var release = GetPreviewReleasePosition(session, plan, profile, payloadEvent.Time);
                RunSafeEffect(RocketLaunchEffect, release, "payload event cue");
                RunSafeEffect(BulletImpactEffect, release + Vector3.up * 0.25f, "payload event spark");
                RunPayloadReleaseFlare(release, "payload event flare");
                var payload = string.IsNullOrWhiteSpace(payloadEvent.Payload) ? "payload" : payloadEvent.Payload;
                var announce = schedule.Count <= 24
                    || i < 5
                    || i == schedule.Count - 1
                    || (i + 1) % 25 == 0;
                if (announce)
                {
                    Reply(player, (IsRepeatedPatternMode(profile) ? "Pattern release #" : "Manual release #") + Math.Max(1, payloadEvent.Index) + " " + payload + " x" + Math.Max(1, payloadEvent.Count) + " at " + FormatSeconds(payloadEvent.Time) + " (safe visual only). Dangerous payload preview is " + (profileFile.AllowDangerousPayloadPreview ? "configured true but not implemented in this editor build" : "disabled") + ".");
                }
            }
        }

        private Vector3 GetPreviewReleasePosition(EditorSession session, List<WorldWaypoint> plan, VisualProfileConfig profile, float releaseTime)
        {
            if (session != null && session.PreviewVehicle != null && !session.PreviewVehicle.IsDestroyed)
            {
                return session.PreviewVehicle.transform.position;
            }

            return EvaluatePlanPosition(plan, profile, releaseTime);
        }

        private void RunPayloadReleaseFlare(Vector3 position, string label)
        {
            RunSafeEffect(PayloadReleaseFlareEffect, position, label);
        }

        private void CompletePreview(BasePlayer player, EditorSession session)
        {
            if (session == null)
            {
                return;
            }

            DestroyPreview(session);
            if (player != null)
            {
                CuiHelper.DestroyUi(player, PreviewUiName);
            }
            RebuildMarkers(player, session);
            SetStatus(session, "Preview complete.", "");
            if (player != null && player.IsConnected)
            {
                Reply(player, "Preview complete.");
                RefreshEditorUiIfOpen(player);
            }
        }

        private void PausePreview(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            if (session == null || !session.PreviewActive)
            {
                Reply(player, "No active preview to pause.");
                return;
            }

            if (session.PreviewPaused)
            {
                Reply(player, "Preview is already paused.");
                return;
            }

            session.PreviewPausedElapsed = Mathf.Max(0f, (float)(GetPreciseNow() - session.PreviewStartedAt));
            session.PreviewPaused = true;
            if (session.PreviewMoveTimer != null)
            {
                session.PreviewMoveTimer.Destroy();
                session.PreviewMoveTimer = null;
            }

            SetStatus(session, "Preview paused.", "Use Resume to continue from " + FormatSeconds(session.PreviewPausedElapsed) + ".");
            ShowPreviewBarForSession(player, session);
            RefreshEditorUiIfOpen(player);
            Reply(player, "Preview paused at " + FormatSeconds(session.PreviewPausedElapsed) + ".");
        }

        private void ResumePreview(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            if (session == null || !session.PreviewActive)
            {
                Reply(player, "No paused preview to resume.");
                return;
            }

            if (!session.PreviewPaused)
            {
                Reply(player, "Preview is already running.");
                return;
            }

            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var plan = BuildWorldWaypoints(session, profile);
            session.PreviewStartedAt = GetPreciseNow() - Math.Max(0f, session.PreviewPausedElapsed);
            session.PreviewPaused = false;
            SetStatus(session, "Preview resumed.", "Continuing from " + FormatSeconds(session.PreviewPausedElapsed) + ".");
            ShowPreviewBarUi(player, session, profile, session.PreviewPausedElapsed);
            SchedulePreviewStep(player, session, profile, plan);
            RefreshEditorUiIfOpen(player);
            Reply(player, "Preview resumed.");
        }

        private void TogglePreviewPause(BasePlayer player)
        {
            var session = player == null ? null : GetOrCreateSession(player);
            if (session == null || !session.PreviewActive)
            {
                Reply(player, "No active preview to pause.");
                return;
            }

            if (session.PreviewPaused)
            {
                ResumePreview(player);
            }
            else
            {
                PausePreview(player);
            }
        }

        private void CmdRide(BasePlayer player, string[] args)
        {
            if (player == null)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            if (args != null && args.Length >= 2 && IsStopToken(args[1]))
            {
                var stopTarget = args.Length >= 3 ? FindOnlinePlayer(GetArgTail(args, 2)) : player;
                if (stopTarget == null)
                {
                    Reply(player, "Could not find that online player to stop riding.");
                    return;
                }

                StopPreviewRide(stopTarget, true);
                if (stopTarget.userID != player.userID)
                {
                    Reply(player, "Stopped preview ride for " + stopTarget.displayName + ".");
                }
                return;
            }

            if (args != null && args.Length >= 2 && IsStageToken(args[1]))
            {
                var stageTarget = args.Length >= 3 ? FindOnlinePlayer(GetArgTail(args, 2)) : FindQueuedPreviewRider(session) ?? player;
                if (stageTarget == null)
                {
                    Reply(player, "Could not find that online player to stage for the preview ride.");
                    return;
                }

                QueuePreviewRide(player, session, stageTarget, true);
                return;
            }

            var rider = args != null && args.Length >= 2 ? FindOnlinePlayer(GetArgTail(args, 1)) : player;
            if (rider == null)
            {
                Reply(player, "Could not find that online player. Use /airanim ride <playerNameOrSteamId>.");
                return;
            }

            if (session.PreviewActive && session.PreviewVehicle != null && !session.PreviewVehicle.IsDestroyed)
            {
                StartPreviewRide(player, session, rider);
                return;
            }

            QueuePreviewRide(player, session, rider, false);
        }

        private void QueuePreviewRide(BasePlayer actor, EditorSession session, BasePlayer rider, bool stageAtStart)
        {
            if (actor == null || session == null || rider == null)
            {
                return;
            }

            StopPreviewRide(rider, false);
            if (session.QueuedPreviewRider != null && session.QueuedPreviewRider.UserId != rider.userID)
            {
                var previous = session.QueuedPreviewRider;
                session.QueuedPreviewRider = null;
                session.QueuedPreviewRiderStaged = false;
                ReturnPreviewRider(BasePlayer.FindByID(previous.UserId), previous);
            }

            if (session.QueuedPreviewRider == null || session.QueuedPreviewRider.UserId != rider.userID)
            {
                session.QueuedPreviewRider = new PreviewRider
                {
                    UserId = rider.userID,
                    ReturnPosition = rider.transform.position,
                    ReturnViewAngles = rider.viewAngles
                };
                session.QueuedPreviewRiderStaged = false;
            }

            if (stageAtStart)
            {
                if (!StageQueuedPreviewRider(actor, session, rider))
                {
                    return;
                }
            }

            SetStatus(session, "Queued preview rider " + rider.displayName + ".", stageAtStart ? "Rider staged at the profile start chase point." : "Use /airanim ride stage to preload them at the start point.");
            Reply(actor, "Queued " + rider.displayName + " for the next preview ride" + (stageAtStart ? " and moved them to the start chase point." : ". Use /airanim ride stage to move them to the start chase point before preview.") + ".");
            if (actor.userID != rider.userID)
            {
                Reply(rider, "You were queued for the next AirAnim preview ride" + (stageAtStart ? " and moved to the start chase point." : ".") + " Use /airanim ride stop to cancel.");
            }
        }

        private bool StageQueuedPreviewRider(BasePlayer actor, EditorSession session, BasePlayer rider)
        {
            if (actor == null || session == null || rider == null)
            {
                return false;
            }

            VisualProfileConfig profile;
            if (!TryGetSessionProfile(actor, session, out profile))
            {
                return false;
            }

            if (profile.Waypoints == null || profile.Waypoints.Count < 2)
            {
                Reply(actor, "The active profile needs at least two waypoints before a rider can be staged.");
                return false;
            }

            EnsureSessionTarget(actor, session, false);
            var plan = BuildWorldWaypoints(session, profile);
            if (plan.Count < 2)
            {
                Reply(actor, "Could not build a valid world path for staging.");
                return false;
            }

            Vector3 position;
            Vector3 viewAngles;
            GetPreviewRidePose(session, profile, plan, 0f, out position, out viewAngles);
            DestroyUi(rider);
            rider.SetParent(null, true, true);
            rider.Teleport(position);
            rider.viewAngles = viewAngles;
            rider.SendNetworkUpdateImmediate();
            session.QueuedPreviewRiderStaged = true;
            return true;
        }

        private void StartPreviewRide(BasePlayer actor, EditorSession session, BasePlayer rider)
        {
            if (actor == null || rider == null || session == null)
            {
                return;
            }

            if (!session.PreviewActive || session.PreviewVehicle == null || session.PreviewVehicle.IsDestroyed)
            {
                Reply(actor, "Start a profile preview first, then use /airanim ride" + (rider.userID == actor.userID ? "." : " <player>."));
                return;
            }

            StopPreviewRide(rider, false);
            session.PreviewRiders[rider.userID] = new PreviewRider
            {
                UserId = rider.userID,
                ReturnPosition = rider.transform.position,
                ReturnViewAngles = rider.viewAngles
            };

            DestroyUi(rider);
            MovePreviewRider(rider, session.PreviewVehicle);
            Reply(rider, "Preview ride started. You are following the selected profile vehicle with editor UI hidden for camera control; /airanim ride stop returns you.");
            if (actor.userID != rider.userID)
            {
                Reply(actor, "Started preview ride for " + rider.displayName + ".");
            }
        }

        private void StopPreviewRide(BasePlayer rider, bool reply)
        {
            if (rider == null)
            {
                return;
            }

            foreach (var session in sessions.Values)
            {
                if (session == null)
                {
                    continue;
                }

                PreviewRider state;
                if (!session.PreviewRiders.TryGetValue(rider.userID, out state))
                {
                    continue;
                }

                session.PreviewRiders.Remove(rider.userID);
                ReturnPreviewRider(rider, state);
                if (reply)
                {
                    Reply(rider, "Preview ride stopped.");
                }
                if (CanUse(rider))
                {
                    ShowPreviewBarForSession(rider, session);
                }
                return;
            }

            foreach (var session in sessions.Values)
            {
                if (session == null || session.QueuedPreviewRider == null || session.QueuedPreviewRider.UserId != rider.userID)
                {
                    continue;
                }

                var queued = session.QueuedPreviewRider;
                session.QueuedPreviewRider = null;
                session.QueuedPreviewRiderStaged = false;
                ReturnPreviewRider(rider, queued);
                if (reply)
                {
                    Reply(rider, "Queued preview ride cancelled.");
                }
                return;
            }

            if (reply)
            {
                Reply(rider, "You are not riding a preview vehicle.");
            }
        }

        private bool IsPreviewRideParticipant(BasePlayer rider)
        {
            if (rider == null)
            {
                return false;
            }

            foreach (var session in sessions.Values)
            {
                if (session != null && session.PreviewRiders.ContainsKey(rider.userID))
                {
                    return true;
                }

                if (session != null && session.QueuedPreviewRider != null && session.QueuedPreviewRider.UserId == rider.userID)
                {
                    return true;
                }
            }

            return false;
        }

        private BasePlayer FindQueuedPreviewRider(EditorSession session)
        {
            if (session == null || session.QueuedPreviewRider == null)
            {
                return null;
            }

            return BasePlayer.FindByID(session.QueuedPreviewRider.UserId);
        }

        private void StartQueuedPreviewRide(BasePlayer actor, EditorSession session)
        {
            if (session == null || session.QueuedPreviewRider == null || session.PreviewVehicle == null || session.PreviewVehicle.IsDestroyed)
            {
                return;
            }

            var queued = session.QueuedPreviewRider;
            session.QueuedPreviewRider = null;
            session.QueuedPreviewRiderStaged = false;
            var rider = BasePlayer.FindByID(queued.UserId);
            if (rider == null || !rider.IsConnected)
            {
                return;
            }

            StopPreviewRide(rider, false);
            session.PreviewRiders[rider.userID] = queued;
            DestroyUi(rider);
            MovePreviewRider(rider, session.PreviewVehicle);
            Reply(rider, "Preview ride started. You are following the selected profile vehicle with editor UI hidden for camera control; /airanim ride stop returns you.");
            if (actor != null && actor.userID != rider.userID)
            {
                Reply(actor, "Started queued preview ride for " + rider.displayName + ".");
            }
        }

        private void StopAllPreviewRides(EditorSession session, bool returnPlayers, bool includeQueued)
        {
            if (session == null || (session.PreviewRiders.Count == 0 && (!includeQueued || session.QueuedPreviewRider == null)))
            {
                return;
            }

            foreach (var riderState in new List<PreviewRider>(session.PreviewRiders.Values))
            {
                if (riderState == null)
                {
                    continue;
                }

                var rider = BasePlayer.FindByID(riderState.UserId);
                if (returnPlayers)
                {
                    ReturnPreviewRider(rider, riderState);
                }

                if (rider != null && rider.IsConnected)
                {
                    Reply(rider, "Preview ride ended.");
                }
            }

            session.PreviewRiders.Clear();
            if (includeQueued && session.QueuedPreviewRider != null)
            {
                var queued = session.QueuedPreviewRider;
                session.QueuedPreviewRider = null;
                session.QueuedPreviewRiderStaged = false;
                if (returnPlayers)
                {
                    ReturnPreviewRider(BasePlayer.FindByID(queued.UserId), queued);
                }
            }
        }

        private void ReturnPreviewRider(BasePlayer rider, PreviewRider state)
        {
            if (rider == null || state == null || !rider.IsConnected)
            {
                return;
            }

            rider.SetParent(null, true, true);
            rider.Teleport(state.ReturnPosition);
            rider.viewAngles = state.ReturnViewAngles;
            rider.SendNetworkUpdateImmediate();
        }

        private void UpdatePreviewRiders(EditorSession session)
        {
            if (session == null || session.PreviewRiders.Count == 0)
            {
                return;
            }

            var vehicle = session.PreviewVehicle;
            if (vehicle == null || vehicle.IsDestroyed)
            {
                StopAllPreviewRides(session, true, false);
                return;
            }

            foreach (var riderState in new List<PreviewRider>(session.PreviewRiders.Values))
            {
                if (riderState == null)
                {
                    continue;
                }

                var rider = BasePlayer.FindByID(riderState.UserId);
                if (rider == null || !rider.IsConnected)
                {
                    session.PreviewRiders.Remove(riderState.UserId);
                    continue;
                }

                MovePreviewRider(rider, vehicle);
            }
        }

        private void MovePreviewRider(BasePlayer rider, BaseEntity vehicle)
        {
            if (rider == null || vehicle == null || vehicle.IsDestroyed)
            {
                return;
            }

            var forward = vehicle.transform.forward;
            if (forward.sqrMagnitude <= 0.01f)
            {
                forward = Vector3.forward;
            }

            var position = vehicle.transform.position - forward.normalized * PreviewRideDistance + Vector3.up * PreviewRideHeight;
            rider.MovePosition(position);
            rider.ClientRPC(RpcTarget.Player("ForcePositionTo", rider), position);
            rider.SendNetworkUpdateImmediate();
        }

        private void GetPreviewRidePose(EditorSession session, VisualProfileConfig profile, List<WorldWaypoint> plan, float elapsed, out Vector3 position, out Vector3 viewAngles)
        {
            var vehiclePosition = EvaluatePlanPosition(plan, profile, elapsed);
            vehiclePosition = EnsurePositionAboveTerrain(vehiclePosition, GetProfileClearance(profile));
            var direction = GetPlanDirection(plan, profile, elapsed, session == null ? Vector3.forward : session.Approach);
            if (direction.sqrMagnitude <= 0.01f)
            {
                direction = session != null && session.Approach.sqrMagnitude > 0.01f ? session.Approach : Vector3.forward;
            }

            position = vehiclePosition - direction.normalized * PreviewRideDistance + Vector3.up * PreviewRideHeight;
            var lookDirection = vehiclePosition - position;
            viewAngles = lookDirection.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(lookDirection.normalized, Vector3.up).eulerAngles
                : Vector3.zero;
        }

        private void ShowPreviewBarForSession(BasePlayer player, EditorSession session)
        {
            VisualProfileConfig profile;
            if (player == null || session == null || !TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var elapsed = session.PreviewPaused ? session.PreviewPausedElapsed : (float)Math.Max(0d, GetPreciseNow() - session.PreviewStartedAt);
            ShowPreviewBarUi(player, session, profile, elapsed);
        }

        private void DestroyPreview(EditorSession session, bool preserveQueuedRider = false)
        {
            if (session == null)
            {
                return;
            }

            session.PreviewActive = false;
            session.PreviewPaused = false;
            session.PreviewPausedElapsed = 0f;
            session.LastPreviewUiSecond = -1;
            var previewPlayer = BasePlayer.FindByID(session.UserId);
            if (previewPlayer != null)
            {
                CuiHelper.DestroyUi(previewPlayer, PreviewUiName);
            }
            if (session.PreviewMoveTimer != null)
            {
                session.PreviewMoveTimer.Destroy();
                session.PreviewMoveTimer = null;
            }

            foreach (var previewTimer in new List<Timer>(session.PreviewTimers))
            {
                previewTimer?.Destroy();
            }

            session.PreviewTimers.Clear();
            session.FiredPayloadEvents.Clear();
            session.PreviewPayloadSchedule.Clear();
            session.NextPreviewPayloadIndex = 0;
            StopAllPreviewRides(session, true, !preserveQueuedRider);

            if (session.PreviewVehicle != null && !session.PreviewVehicle.IsDestroyed)
            {
                session.PreviewVehicle.Kill(BaseNetworkable.DestroyMode.None);
            }

            session.PreviewVehicle = null;
            session.PreviewUsesNativeCargoPlane = false;
        }

        private void SchedulePreviewSoundCues(BasePlayer player, EditorSession session, VisualProfileConfig profile, List<WorldWaypoint> plan)
        {
            if (session == null || profile == null || plan == null || plan.Count == 0)
            {
                return;
            }

            var duration = Mathf.Max(0.1f, profile.DurationSeconds);
            var interval = 0.75f;
            var count = Mathf.Clamp(Mathf.CeilToInt(duration / interval) + 1, 2, 28);
            var prefab = GetVehiclePrefab(profile.Vehicle);
            for (var i = 0; i < count; i++)
            {
                var cueIndex = i;
                var progress = count <= 1 ? 0f : cueIndex / (float)(count - 1);
                var delay = Mathf.Clamp(duration * progress, 0.01f, Math.Max(0.01f, duration - 0.05f));
                Timer cueTimer = null;
                cueTimer = timer.Once(delay, () =>
                {
                    if (cueTimer != null)
                    {
                        session.PreviewTimers.Remove(cueTimer);
                    }

                    if (!IsSessionActive(player, session) || session.PreviewPaused)
                    {
                        return;
                    }

                    var position = EvaluatePlanPosition(plan, profile, delay);
                    RunPreviewSoundBurst(prefab, profile.Vehicle, position, cueIndex, count);
                });

                if (cueTimer != null)
                {
                    session.PreviewTimers.Add(cueTimer);
                }
            }
        }

        private void RunPreviewSoundBurst(string prefab, string vehicle, Vector3 position, int cueIndex, int cueCount)
        {
            RunSafeEffect(VehicleFlybySoundEffect, position, "preview flyby engine cue");
            RunSafeEffect(ProjectileFlightSoundEffect, position + Vector3.up * 0.5f, "preview air movement cue");

            if (string.Equals(vehicle, "drone", StringComparison.OrdinalIgnoreCase) && cueIndex <= 0)
            {
                RunSafeEffect(DroneDeployEffect, position, "drone deploy preview cue");
            }

            if (cueIndex >= cueCount - 1 || string.Equals(vehicle, "f15", StringComparison.OrdinalIgnoreCase) || string.Equals(vehicle, "a10", StringComparison.OrdinalIgnoreCase))
            {
                RunSafeEffect(BulletFlybySoundEffect, position, "preview flyby cue");
            }

            if (!string.Equals(prefab, PatrolHelicopterVisualPrefab, StringComparison.OrdinalIgnoreCase) && !string.Equals(vehicle, "drone", StringComparison.OrdinalIgnoreCase))
            {
                RunSafeEffect(LargeFastFalloffSoundEffect, position, "large flyby preview cue");
            }
        }

        private void NudgeSelectedWaypoint(BasePlayer player, string direction, float meters, bool reply)
        {
            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            if (profile.Waypoints.Count == 0)
            {
                Reply(player, "No waypoints exist yet. Use /airanim wp add <time> <x> <y> <z>.");
                return;
            }

            if (session.SelectedWaypointIndex < 0 || session.SelectedWaypointIndex >= profile.Waypoints.Count)
            {
                session.SelectedWaypointIndex = 0;
            }

            var waypoint = profile.Waypoints[session.SelectedWaypointIndex];
            var dir = (direction ?? "").Trim().ToLowerInvariant();
            var amount = Mathf.Abs(meters);
            switch (dir)
            {
                case "forward":
                case "fwd":
                    waypoint.Z += amount;
                    break;
                case "back":
                case "backward":
                    waypoint.Z -= amount;
                    break;
                case "right":
                    waypoint.X += amount;
                    break;
                case "left":
                    waypoint.X -= amount;
                    break;
                case "up":
                    waypoint.Y += amount;
                    break;
                case "down":
                    waypoint.Y -= amount;
                    break;
                default:
                    Reply(player, "Unknown nudge direction. Use forward, back, left, right, up, or down.");
                    return;
            }

            NormalizeProfile(session.ProfileId, profile);
            session.SelectedWaypointIndex = profile.Waypoints.IndexOf(waypoint);
            RebuildMarkers(player, session);
            SetStatus(session, "Nudged waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + " " + dir + " " + FormatMeters(amount) + ".", "");
            if (reply)
            {
                Reply(player, "Waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + " local position is now X=" + FormatFloat(waypoint.X) + ", Y=" + FormatFloat(waypoint.Y) + ", Z=" + FormatFloat(waypoint.Z) + ".");
            }

            RefreshEditorUiIfOpen(player);
            RefreshWaypointPopupUiIfOpen(player);
        }

        private void ShowHelp(BasePlayer player)
        {
            Reply(player, "PortableAirstrikes animation editor commands:");
            Reply(player, "/airanim or /airanim ui - open the CUI dashboard; /airanim close|hide - hide only the CUI; /airanim end - clean up session, markers, and preview.");
            Reply(player, "/airanim list; /airanim create <profileId> <vehicle>; /airanim edit <profileId>; /airanim target sets the target column; /airanim markers; /airanim preview [profileId]; /airanim stop.");
            Reply(player, "/airanim save; /airanim reload (or reload confirm when unsaved); /airanim delete <profileId>.");
            Reply(player, "/airanim objects on|off|toggle; /airanim timeline on|off|toggle; /airanim stopwaypoints on|off|toggle.");
            Reply(player, "/airanim wp list; /airanim wp add <time> <x> <y> <z>; /airanim wp here; /airanim wp select <index>; /airanim wp go [index]; /airanim wp remove <index>; /airanim wp time <index> <seconds>; /airanim wp set <index> <x> <y> <z>; /airanim wp rot <index> <x> <y> <z>; /airanim wp norm <x|y|z> <marked|all|clear|indices...>.");
            Reply(player, "/airanim payload list; payload mode manual|repeated; payload max <totalUnits>; payload units <perRelease>; payload interval <seconds>; payload add/edit/remove/set.");
            Reply(player, "/airanim nudge forward|back|left|right|up|down <meters>; /airanim duration <seconds>; /airanim firstpayload <seconds>; /airanim smooth <seconds>; /airanim clearance <meters>; /airanim vehicle <vehicle>.");
            Reply(player, "The CUI has Flight Path, Releases, Profile, and Commands tabs. The Commands page groups both /chat syntax and equivalent F1-console syntax.");
            Reply(player, "F1 console uses the same command without the leading slash, for example: airanim preview. Server/RCON cannot use it because there is no editing-player context.");
            Reply(player, "All edit commands work with the panel hidden. Vehicles: " + string.Join(", ", VehicleValues) + ". Coordinates are target-relative: X=right/left, Y=height, Z=approach axis; negative Z is inbound.");
        }

        private void ShowProfileList(BasePlayer player)
        {
            if (profileFile?.Profiles == null || profileFile.Profiles.Count == 0)
            {
                Reply(player, "No visual profiles are loaded.");
                return;
            }

            var ids = GetSortedProfileIds();
            Reply(player, "Visual profiles (" + ids.Count + "): " + string.Join(", ", ids.ToArray()) + ".");
            var shown = 0;
            foreach (var id in ids)
            {
                if (shown >= MaxChatRows)
                {
                    Reply(player, "...and " + (ids.Count - shown) + " more. Open /airanim for the scrollable CUI list.");
                    break;
                }

                var profile = profileFile.Profiles[id];
                Reply(player, id + " | vehicle=" + profile.Vehicle + " | duration=" + FormatSeconds(profile.DurationSeconds) + " | waypoints=" + (profile.Waypoints == null ? 0 : profile.Waypoints.Count) + ".");
                shown++;
            }
        }

        private void ShowWaypointList(BasePlayer player, EditorSession session, VisualProfileConfig profile)
        {
            if (profile == null || profile.Waypoints == null || profile.Waypoints.Count == 0)
            {
                Reply(player, "No waypoints in this profile.");
                return;
            }

            Reply(player, "Waypoints for '" + session.ProfileId + "' (indices are 1-based; 0 is also accepted as the first waypoint):");
            for (var i = 0; i < profile.Waypoints.Count && i < MaxChatRows; i++)
            {
                var waypoint = profile.Waypoints[i];
                var selected = i == session.SelectedWaypointIndex ? " <selected>" : "";
                Reply(player, "#" + DisplayIndex(i) + " t=" + FormatSeconds(waypoint.Time) + " X=" + FormatFloat(waypoint.X) + " Y=" + FormatFloat(waypoint.Y) + " Z=" + FormatFloat(waypoint.Z) + selected + ".");
            }

            if (profile.Waypoints.Count > MaxChatRows)
            {
                Reply(player, "...and " + (profile.Waypoints.Count - MaxChatRows) + " more. Open /airanim for the scrollable waypoint panel.");
            }
        }

        private void ShowPreviewBarUi(BasePlayer player, EditorSession session, VisualProfileConfig profile, float elapsed)
        {
            if (player == null || !player.IsConnected || session == null || profile == null || !session.PreviewActive)
            {
                return;
            }

            if (session.UiOpen || session.PreviewRiders.ContainsKey(player.userID))
            {
                CuiHelper.DestroyUi(player, PreviewUiName);
                return;
            }

            session.LastPreviewUiSecond = Mathf.FloorToInt(Mathf.Max(0f, elapsed));
            CuiHelper.DestroyUi(player, PreviewUiName);
            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel
            {
                Image = { Color = "0.030 0.035 0.045 0.94" },
                RectTransform = { AnchorMin = "0.225 0.012", AnchorMax = "0.775 0.090" }
            }, "Overlay", PreviewUiName);

            var duration = Mathf.Max(0.1f, profile.DurationSeconds);
            var progress = Mathf.Clamp01(elapsed / duration);
            AddLabel(container, root, (session.PreviewPaused ? "Paused " : "Previewing ") + session.ProfileId, 9, TextAnchor.MiddleLeft, "0.025 0.35", "0.360 0.88", "0.82 0.90 0.96 1");
            AddLabel(container, root, FormatSeconds(Mathf.Min(elapsed, duration)) + " / " + FormatSeconds(duration), 9, TextAnchor.MiddleCenter, "0.390 0.35", "0.560 0.88", "0.66 0.76 0.82 1");
            var riding = session.PreviewRiders.ContainsKey(player.userID);
            AddLabel(container, root, (riding ? "/airanim ride stop" : "/airanim ride") + "   /airanim " + (session.PreviewPaused ? "resume" : "pause") + "   /airanim stop   /airanim", 8, TextAnchor.MiddleRight, "0.575 0.35", "0.975 0.88", "0.52 0.62 0.70 1");
            AddPanel(container, root, "0.025 0.10", "0.975 0.18", "0.08 0.09 0.11 0.95");
            AddPanel(container, root, "0.025 0.10", FormatAnchor(0.025f + 0.950f * progress, 0.18f), "0.82 0.38 0.12 0.92");
            CuiHelper.AddUi(player, container);
        }

        private void ShowEditorUi(BasePlayer player)
        {
            if (player == null || !player.IsConnected || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile = null;
            if (!string.IsNullOrWhiteSpace(session.ProfileId))
            {
                profileFile.Profiles.TryGetValue(session.ProfileId, out profile);
                if (profile != null)
                {
                    NormalizeProfile(session.ProfileId, profile);
                }
            }
            CaptureHistoryIfChanged(session);

            session.ActiveTab = NormalizeEditorTab(session.ActiveTab);
            CuiHelper.DestroyUi(player, PreviewUiName);
            DestroyMainEditorUi(player);

            var container = new CuiElementContainer();
            var showLegacyTimeline = session.TimelineOpen && session.ActiveTab == "flight";
            var rootAnchorMin = showLegacyTimeline ? "0.045 0.235" : "0.045 0.055";
            var root = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.025 0.030 0.038 0.985" },
                RectTransform = { AnchorMin = rootAnchorMin, AnchorMax = "0.955 0.945" }
            }, "Overlay", UiName);

            AddPanel(container, root, "0.012 0.895", "0.988 0.985", "0.075 0.085 0.105 0.98");
            AddLabel(container, root, "Portable Airstrikes Animation Editor", 19, TextAnchor.MiddleLeft, "0.030 0.938", "0.405 0.975", "1 0.86 0.58 1");
            var activeText = profile == null ? "No profile selected" : session.ProfileId + "  •  " + profile.Vehicle;
            var dirtyText = HasUnsavedChanges() ? "  •  UNSAVED" : "  •  Saved";
            AddLabel(container, root, activeText + dirtyText, 10, TextAnchor.MiddleLeft, "0.030 0.905", "0.560 0.937", HasUnsavedChanges() ? "1 0.66 0.38 1" : "0.62 0.72 0.78 1");
            AddButton(container, root, "UNDO", "airanim.ui.undo", "0.565 0.915", "0.615 0.965", session.UndoHistory.Count > 0 ? "0.16 0.22 0.28 0.96" : "0.08 0.09 0.11 0.80", 7);
            AddButton(container, root, "REDO", "airanim.ui.redo", "0.620 0.915", "0.670 0.965", session.RedoHistory.Count > 0 ? "0.16 0.22 0.28 0.96" : "0.08 0.09 0.11 0.80", 7);
            AddButton(container, root, "TARGET", "airanim.ui.target", "0.675 0.915", "0.735 0.965", "0.12 0.31 0.37 0.96", 8);
            AddButton(container, root, session.PreviewActive ? (session.PreviewPaused ? "RESUME" : "PAUSE") : "PREVIEW", session.PreviewActive ? "airanim.ui.pause" : "airanim.ui.preview", "0.740 0.915", "0.805 0.965", session.PreviewActive ? (session.PreviewPaused ? "0.14 0.32 0.22 0.96" : "0.34 0.21 0.10 0.96") : "0.46 0.18 0.10 0.96", 8);
            AddButton(container, root, "SAVE", "airanim.ui.save", "0.810 0.915", "0.865 0.965", HasUnsavedChanges() ? "0.48 0.23 0.10 0.96" : "0.20 0.27 0.22 0.96", 8);
            AddButton(container, root, "COMMANDS", "airanim.ui.tab commands", "0.870 0.915", "0.925 0.965", session.ActiveTab == "commands" ? "0.30 0.20 0.10 0.96" : "0.16 0.20 0.25 0.96", 6);
            AddButton(container, root, "HIDE", "airanim.ui.hide", "0.932 0.915", "0.975 0.965", "0.35 0.12 0.10 0.96", 7);

            AddWorkspaceProfileBrowser(container, root, player, session);
            AddWorkspaceTabs(container, root, session, profile);

            if (session.ActiveTab == "commands")
            {
                AddCommandsWorkspace(container, root, session);
            }
            else if (profile == null)
            {
                AddPanel(container, root, "0.238 0.085", "0.982 0.815", "0.045 0.052 0.065 0.95");
                AddLabel(container, root, "Choose a profile on the left, or create a new one.", 14, TextAnchor.MiddleCenter, "0.280 0.430", "0.940 0.535", "0.72 0.80 0.86 1");
            }
            else if (session.ActiveTab == "flight")
            {
                AddFlightPathWorkspace(container, root, player, session, profile);
                AddWaypointInspector(container, root, session, profile);
            }
            else if (session.ActiveTab == "profile")
            {
                AddProfileWorkspace(container, root, session, profile);
                AddProfileToolsInspector(container, root, session, profile);
            }
            else
            {
                AddReleaseWorkspace(container, root, session, profile);
                AddReleaseInspector(container, root, session, profile);
            }

            AddWorkspaceStatusBar(container, root, session, profile);
            RegisterUiBridge(player, UiName);
            CuiHelper.AddUi(player, container);
            session.UiOpen = true;
            if (showLegacyTimeline)
            {
                ShowTimelineUi(player);
            }
            else
            {
                CuiHelper.DestroyUi(player, TimelineUiName);
            }
        }


        private string NormalizeEditorTab(string tab)
        {
            var normalized = (tab ?? "").Trim().ToLowerInvariant();
            if (normalized == "flight" || normalized == "path" || normalized == "waypoints")
            {
                return "flight";
            }

            if (normalized == "profile" || normalized == "settings" || normalized == "tools")
            {
                return "profile";
            }

            if (normalized == "commands" || normalized == "help" || normalized == "reference")
            {
                return "commands";
            }

            return "releases";
        }

        private List<string> GetFilteredProfileIds(string filter)
        {
            var ids = GetSortedProfileIds();
            var normalized = (filter ?? "").Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return ids;
            }

            var filtered = new List<string>();
            foreach (var id in ids)
            {
                VisualProfileConfig profile;
                profileFile.Profiles.TryGetValue(id, out profile);
                if (id.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0
                    || (profile != null && (profile.Vehicle ?? "").IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    filtered.Add(id);
                }
            }

            return filtered;
        }

        private void AddWorkspaceProfileBrowser(CuiElementContainer container, string root, BasePlayer player, EditorSession session)
        {
            AddPanel(container, root, "0.012 0.085", "0.225 0.885", "0.045 0.052 0.065 0.96");
            AddLabel(container, root, "PROFILES", 13, TextAnchor.MiddleLeft, "0.028 0.842", "0.130 0.875", "0.92 0.96 1 1");
            AddLabel(container, root, CountProfiles() + " loaded", 9, TextAnchor.MiddleRight, "0.125 0.842", "0.210 0.875", "0.55 0.64 0.70 1");
            AddTextInput(container, root, session.ProfileFilter, "airanim.ui.profilefilter", "0.028 0.790", "0.210 0.835", "0.070 0.082 0.100 0.98", 10, 32, TextAnchor.MiddleLeft);
            AddLabel(container, root, "Search by profile or vehicle; press Enter", 8, TextAnchor.MiddleLeft, "0.030 0.765", "0.210 0.790", "0.46 0.55 0.62 1");
            AddButton(container, root, "+ NEW F15", "airanim.ui.quickcreate f15", "0.028 0.715", "0.120 0.755", "0.14 0.31 0.20 0.96", 8);
            AddButton(container, root, "CLEAR", "airanim.ui.profilefilterclear", "0.128 0.715", "0.210 0.755", "0.16 0.20 0.25 0.96", 8);

            var ids = GetFilteredProfileIds(session.ProfileFilter);
            var contentHeight = Math.Max(440f, 8f + Math.Min(MaxProfilesInUi, ids.Count) * 58f);
            var scroll = AddScrollView(container, root, "0.028 0.105", "0.210 0.700", contentHeight, true);
            var count = Math.Min(MaxProfilesInUi, ids.Count);
            for (var i = 0; i < count; i++)
            {
                var id = ids[i];
                VisualProfileConfig profile;
                profileFile.Profiles.TryGetValue(id, out profile);
                var top = 8f + i * 58f;
                var bottom = top + 52f;
                var selected = string.Equals(session.ProfileId, id, StringComparison.OrdinalIgnoreCase);
                var row = AddOffsetPanel(container, scroll, top, bottom, selected ? "0.27 0.16 0.08 0.96" : "0.085 0.100 0.125 0.92");
                AddLabel(container, row, (selected && HasUnsavedChanges() ? "• " : "") + id, 10, TextAnchor.MiddleLeft, "0.035 0.50", "0.760 0.92", selected ? "1 0.86 0.55 1" : "0.92 0.96 1 1");
                var releaseSummary = profile == null ? "" : (IsRepeatedPatternMode(profile) ? GetGeneratedReleaseGroupCount(profile) + " pattern" : (profile.PayloadEvents == null ? 0 : profile.PayloadEvents.Count) + " releases");
                var meta = profile == null ? "missing" : profile.Vehicle + "  •  " + FormatSeconds(profile.DurationSeconds) + "  •  " + releaseSummary;
                AddLabel(container, row, meta, 8, TextAnchor.MiddleLeft, "0.035 0.08", "0.790 0.46", "0.56 0.65 0.72 1");
                // Add the transparent hit target after labels so label graphics cannot steal pointer events.
                AddButton(container, row, "", "airanim.ui.edit " + id, "0 0", "0.805 1", "0 0 0 0", 1);
                AddButton(container, row, "▶", "airanim.ui.preview " + id, "0.820 0.20", "0.965 0.80", "0.13 0.31 0.36 0.96", 11);
            }

            if (ids.Count == 0)
            {
                AddLabel(container, root, "No profiles match the current search.", 9, TextAnchor.MiddleCenter, "0.035 0.410", "0.205 0.500", "0.60 0.68 0.74 1");
            }
        }

        private void AddWorkspaceTabs(CuiElementContainer container, string root, EditorSession session, VisualProfileConfig profile)
        {
            // Keep the workspace tabs inside the centre column so they never sit underneath the inspector.
            AddPanel(container, root, "0.238 0.825", "0.718 0.885", "0.045 0.052 0.065 0.96");
            AddButton(container, root, "FLIGHT PATH", "airanim.ui.tab flight", "0.250 0.838", "0.355 0.875", session.ActiveTab == "flight" ? "0.34 0.20 0.09 0.98" : "0.13 0.17 0.22 0.96", 7);
            AddButton(container, root, "RELEASES", "airanim.ui.tab releases", "0.365 0.838", "0.470 0.875", session.ActiveTab == "releases" ? "0.34 0.20 0.09 0.98" : "0.13 0.17 0.22 0.96", 7);
            AddButton(container, root, "PROFILE", "airanim.ui.tab profile", "0.480 0.838", "0.585 0.875", session.ActiveTab == "profile" ? "0.34 0.20 0.09 0.98" : "0.13 0.17 0.22 0.96", 7);
            AddButton(container, root, "COMMANDS", "airanim.ui.tab commands", "0.595 0.838", "0.705 0.875", session.ActiveTab == "commands" ? "0.34 0.20 0.09 0.98" : "0.13 0.17 0.22 0.96", 7);
        }

        private string NormalizeCommandSource(string source)
        {
            return string.Equals((source ?? "").Trim(), "console", StringComparison.OrdinalIgnoreCase) ? "console" : "chat";
        }

        private string NormalizeCommandCategory(string category)
        {
            var normalized = (category ?? "").Trim().ToLowerInvariant();
            if (normalized == "profiles" || normalized == "profile") return "profiles";
            if (normalized == "flight" || normalized == "waypoints" || normalized == "path") return "flight";
            if (normalized == "releases" || normalized == "payload" || normalized == "ordnance") return "releases";
            if (normalized == "preview" || normalized == "world") return "preview";
            return "session";
        }

        private List<CommandHelpEntry> GetCommandHelpEntries(string category)
        {
            var normalized = NormalizeCommandCategory(category);
            var entries = new List<CommandHelpEntry>();
            if (normalized == "session")
            {
                entries.Add(new CommandHelpEntry(normalized, "/airanim", "Open or return to the editor UI."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim hide", "Hide editor panels while keeping the session, markers and preview."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim end", "End the editor session and clean up UI, markers and preview entities."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim help", "Print a compact command summary and open this command page when the UI is visible."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim save", "Save all profiles to VisualProfiles.json."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim reload", "Reload from disk when there are no unsaved changes."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim reload confirm", "Discard unsaved in-memory edits and reload from disk."));
            }
            else if (normalized == "profiles")
            {
                entries.Add(new CommandHelpEntry(normalized, "/airanim list", "List loaded visual profile IDs."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim create {id} {vehicle}", "Create and select a profile. Vehicles: drone, cargo_plane, f15, a10, attack_heli."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim edit {id}", "Select an existing profile for editing."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim delete {id}", "Delete a profile and immediately save the file."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim vehicle {vehicle}", "Change the active profile's preview vehicle."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim duration {seconds}", "Set the active profile duration."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim firstpayload {seconds}", "Set pattern start time, or move the first manual release when manual events exist."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim smooth {seconds}", "Set rotation smoothing time."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim clearance {meters}", "Set minimum terrain clearance."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim stopwaypoints on|off|toggle", "Choose stop-at-waypoint easing or continuous velocity blending."));
            }
            else if (normalized == "flight")
            {
                entries.Add(new CommandHelpEntry(normalized, "/airanim wp list", "List waypoint indices, times and target-relative coordinates."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim wp add {time} {x} {y} {z}", "Add a waypoint using exact local values."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim wp here", "Capture a waypoint from your current eye position and view direction."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim wp select {index}", "Select the active reference waypoint."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim wp go [{index}]", "Teleport to the selected or specified waypoint."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim wp remove {index}", "Remove a waypoint."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim wp time {index} {seconds}", "Set an exact waypoint timestamp."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim wp duration {index} {seconds}", "Set travel time from this waypoint to the next."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim wp set {index} {x} {y} {z}", "Set target-relative waypoint position."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim wp rot {index} {x} {y} {z}", "Set waypoint rotation offsets in degrees."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim wp norm {x|y|z} {marked|all|indices...}", "Legacy single-position-axis alignment. The UI popup also supports multiple position and rotation fields."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim nudge {direction} {meters}", "Move the selected waypoint: forward, back, left, right, up or down."));
            }
            else if (normalized == "releases")
            {
                entries.Add(new CommandHelpEntry(normalized, "/airanim payload list", "List release mode, interval, totals and manual events."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim payload mode manual|repeated", "Switch between exact manual events and a repeated pattern. generated and pattern are accepted aliases."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim payload add [{seconds}]", "Add a manual release at a time, or after the selected event."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim payload edit {index}", "Open the advanced editor for one manual release."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim payload remove {index}", "Delete one manual release."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim payload clear", "Remove all manual release events."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim payload max {totalUnits}", "Set total units for repeated-pattern mode."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim payload units {perRelease}", "Set units released in each repeated-pattern group."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim payload interval {seconds}", "Set time between repeated release groups."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim payload set {index} {field} {value}", "Set a manual event field such as time, count, spread, speed or damage."));
            }
            else
            {
                entries.Add(new CommandHelpEntry(normalized, "/airanim target", "Set target and approach direction from your current look ray."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim preview [{profileId}]", "Start a safe visual preview for the active or named profile."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim ride [player]", "Queue a player for the next preview ride, or attach them immediately if preview is already running."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim ride stage [player]", "Move the queued or named rider to the profile start chase point before preview starts."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim ride stop [player]", "Detach or cancel a rider and return them to where they started."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim stop", "Stop the active preview without ending the editor session."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim markers", "Refresh target and waypoint world markers."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim objects on|off|toggle", "Toggle silent vehicle-shaped waypoint outlines."));
                entries.Add(new CommandHelpEntry(normalized, "/airanim timeline on|off|toggle", "Toggle the legacy detailed waypoint timeline."));
            }

            return entries;
        }

        private string GetCommandCategoryTitle(string category)
        {
            switch (NormalizeCommandCategory(category))
            {
                case "profiles": return "PROFILES & SETTINGS";
                case "flight": return "FLIGHT PATH";
                case "releases": return "RELEASES";
                case "preview": return "PREVIEW & WORLD";
                default: return "SESSION & FILES";
            }
        }

        private void AddCommandCategoryButton(CuiElementContainer container, string root, EditorSession session, string text, string category, float xMin, float xMax)
        {
            var active = NormalizeCommandCategory(session.CommandCategory) == category;
            AddButton(container, root, text, "airanim.commands.category " + category, FormatAnchor(xMin, 0.635f), FormatAnchor(xMax, 0.678f), active ? "0.34 0.20 0.09 0.98" : "0.13 0.17 0.22 0.96", 8);
        }

        private void AddCommandsWorkspace(CuiElementContainer container, string root, EditorSession session)
        {
            session.CommandSource = NormalizeCommandSource(session.CommandSource);
            session.CommandCategory = NormalizeCommandCategory(session.CommandCategory);
            AddPanel(container, root, "0.238 0.085", "0.988 0.815", "0.045 0.052 0.065 0.96");
            AddLabel(container, root, "Command Reference", 15, TextAnchor.MiddleLeft, "0.255 0.770", "0.465 0.805", "1 1 1 1");
            AddButton(container, root, "IN-GAME /COMMANDS", "airanim.commands.source chat", "0.535 0.765", "0.700 0.802", session.CommandSource == "chat" ? "0.34 0.20 0.09 0.98" : "0.13 0.17 0.22 0.96", 8);
            AddButton(container, root, "F1 CONSOLE", "airanim.commands.source console", "0.712 0.765", "0.855 0.802", session.CommandSource == "console" ? "0.34 0.20 0.09 0.98" : "0.13 0.17 0.22 0.96", 8);
            AddLabel(container, root, "Requires: server admin OR permission " + AdminPermission, 9, TextAnchor.MiddleLeft, "0.255 0.720", "0.970 0.755", "1 0.72 0.46 1");
            AddLabel(container, root, session.CommandSource == "console"
                ? "F1 player console only. Server/RCON has no editing-player context. Use the same syntax without the leading slash."
                : "No editor commands are available to all players; every /airanim command uses the permission shown above.",
                8, TextAnchor.MiddleLeft, "0.255 0.695", "0.970 0.722", "0.54 0.64 0.70 1");

            AddCommandCategoryButton(container, root, session, "SESSION", "session", 0.255f, 0.365f);
            AddCommandCategoryButton(container, root, session, "PROFILES", "profiles", 0.375f, 0.485f);
            AddCommandCategoryButton(container, root, session, "FLIGHT PATH", "flight", 0.495f, 0.615f);
            AddCommandCategoryButton(container, root, session, "RELEASES", "releases", 0.625f, 0.735f);
            AddCommandCategoryButton(container, root, session, "PREVIEW / WORLD", "preview", 0.745f, 0.875f);

            var entries = GetCommandHelpEntries(session.CommandCategory);
            var pageCount = Math.Max(1, Mathf.CeilToInt(entries.Count / (float)CommandRowsPerPage));
            session.CommandPage = Mathf.Clamp(session.CommandPage, 0, pageCount - 1);
            AddLabel(container, root, GetCommandCategoryTitle(session.CommandCategory), 10, TextAnchor.MiddleLeft, "0.255 0.590", "0.510 0.625", "0.72 0.80 0.86 1");
            AddLabel(container, root, "Page " + (session.CommandPage + 1) + "/" + pageCount, 8, TextAnchor.MiddleRight, "0.820 0.590", "0.925 0.625", "0.52 0.60 0.66 1");
            AddButton(container, root, "<", "airanim.commands.page -1", "0.932 0.588", "0.952 0.623", "0.14 0.18 0.23 0.96", 9);
            AddButton(container, root, ">", "airanim.commands.page 1", "0.957 0.588", "0.977 0.623", "0.14 0.18 0.23 0.96", 9);

            var first = session.CommandPage * CommandRowsPerPage;
            var last = Math.Min(entries.Count, first + CommandRowsPerPage);
            for (var i = first; i < last; i++)
            {
                var local = i - first;
                var yMax = 0.575f - local * 0.081f;
                var yMin = yMax - 0.070f;
                var row = container.Add(new CuiPanel
                {
                    Image = { Color = local % 2 == 0 ? "0.070 0.082 0.100 0.96" : "0.058 0.069 0.085 0.96" },
                    RectTransform = { AnchorMin = FormatAnchor(0.255f, yMin), AnchorMax = FormatAnchor(0.977f, yMax) }
                }, root);
                var syntax = session.CommandSource == "console" ? entries[i].Syntax.TrimStart('/') : entries[i].Syntax;
                AddLabel(container, row, syntax, 9, TextAnchor.MiddleLeft, "0.020 0.08", "0.390 0.92", "1 0.86 0.58 1");
                AddLabel(container, row, entries[i].Description, 8, TextAnchor.MiddleLeft, "0.410 0.08", "0.980 0.92", "0.76 0.84 0.90 1");
            }

            AddLabel(container, root, "Aliases such as release / ordnance / ordinance are accepted for payload commands. Indices shown to admins are 1-based.", 8, TextAnchor.MiddleLeft, "0.255 0.055", "0.977 0.090", "0.50 0.59 0.66 1");
        }

        private void AddFlightPathWorkspace(CuiElementContainer container, string root, BasePlayer player, EditorSession session, VisualProfileConfig profile)
        {
            AddPanel(container, root, "0.238 0.085", "0.718 0.815", "0.045 0.052 0.065 0.96");
            AddLabel(container, root, "Flight Path", 15, TextAnchor.MiddleLeft, "0.255 0.770", "0.390 0.805", "1 1 1 1");
            AddButton(container, root, "PREV", "airanim.ui.prevwp", "0.445 0.765", "0.500 0.802", "0.14 0.18 0.23 0.96", 8);
            AddButton(container, root, "NEXT", "airanim.ui.nextwp", "0.507 0.765", "0.562 0.802", "0.14 0.18 0.23 0.96", 8);
            AddButton(container, root, "+ ADD", "airanim.ui.addwp", "0.569 0.765", "0.625 0.802", "0.14 0.31 0.20 0.96", 8);
            AddButton(container, root, "+ HERE", "airanim.ui.addhere", "0.632 0.765", "0.690 0.802", "0.12 0.29 0.34 0.96", 8);

            AddFlightPathTimeline(container, root, session, profile);

            var count = profile.Waypoints == null ? 0 : profile.Waypoints.Count;
            var pageCount = Math.Max(1, Mathf.CeilToInt(count / (float)WaypointRowsPerPage));
            session.WaypointPage = Mathf.Clamp(session.WaypointPage, 0, pageCount - 1);
            AddLabel(container, root, "WAYPOINTS", 10, TextAnchor.MiddleLeft, "0.255 0.530", "0.355 0.560", "0.72 0.80 0.86 1");
            AddLabel(container, root, "Page " + (session.WaypointPage + 1) + "/" + pageCount, 8, TextAnchor.MiddleRight, "0.570 0.530", "0.635 0.560", "0.52 0.60 0.66 1");
            AddButton(container, root, "<", "airanim.waypoint.page -1", "0.642 0.528", "0.665 0.558", "0.14 0.18 0.23 0.96", 9);
            AddButton(container, root, ">", "airanim.waypoint.page 1", "0.670 0.528", "0.693 0.558", "0.14 0.18 0.23 0.96", 9);

            if (count == 0)
            {
                AddLabel(container, root, "No waypoints. Add one or capture your current view.", 11, TextAnchor.MiddleCenter, "0.260 0.300", "0.695 0.390", "0.66 0.74 0.80 1");
                return;
            }

            var first = session.WaypointPage * WaypointRowsPerPage;
            var last = Math.Min(count, first + WaypointRowsPerPage);
            for (var i = first; i < last; i++)
            {
                var localRow = i - first;
                var yMax = 0.518f - localRow * 0.053f;
                var yMin = yMax - 0.047f;
                var waypoint = profile.Waypoints[i];
                var selected = i == session.SelectedWaypointIndex;
                var marked = IsNormalizeWaypointSelected(session, waypoint);
                var row = container.Add(new CuiPanel
                {
                    Image = { Color = selected ? "0.28 0.16 0.08 0.96" : "0.082 0.096 0.118 0.94" },
                    RectTransform = { AnchorMin = FormatAnchor(0.255f, yMin), AnchorMax = FormatAnchor(0.697f, yMax) }
                }, root);
                AddLabel(container, row, "#" + DisplayIndex(i) + "   " + FormatSeconds(waypoint.Time), 10, TextAnchor.MiddleLeft, "0.025 0.52", "0.315 0.92", selected ? "1 0.86 0.55 1" : "0.92 0.96 1 1");
                AddLabel(container, row, "X " + FormatFloat(waypoint.X) + "   Y " + FormatFloat(waypoint.Y) + "   Z " + FormatFloat(waypoint.Z), 8, TextAnchor.MiddleLeft, "0.025 0.08", "0.710 0.48", "0.55 0.65 0.72 1");
                // Keep the row selector above text, but stop it before the MARK button.
                AddButton(container, row, "", "airanim.ui.selectwp " + DisplayIndex(i), "0 0", "0.750 1", "0 0 0 0", 1);
                AddButton(container, row, marked ? "MARKED" : "MARK", "airanim.ui.normtoggle " + DisplayIndex(i), "0.775 0.20", "0.965 0.80", marked ? "0.14 0.31 0.20 0.96" : "0.14 0.18 0.23 0.96", 7);
            }

            AddButton(container, root, "REMOVE SELECTED", "airanim.ui.removewp", "0.255 0.095", "0.385 0.130", "0.43 0.12 0.09 0.96", 8);
            AddButton(container, root, "MARK ALL", "airanim.ui.normall", "0.395 0.095", "0.485 0.130", "0.14 0.18 0.23 0.96", 8);
            AddButton(container, root, "CLEAR MARKS", "airanim.ui.normclear", "0.492 0.095", "0.590 0.130", "0.14 0.18 0.23 0.96", 8);
            AddButton(container, root, "ALIGN MARKED…", "airanim.align.open", "0.598 0.095", "0.697 0.130", "0.40 0.19 0.10 0.96", 8);
        }

        private int AssignTimelineMarkerLane(float x, float halfWidth, float[] laneEnds, float gap)
        {
            if (laneEnds == null || laneEnds.Length == 0)
            {
                return 0;
            }

            var left = Mathf.Clamp01(x - halfWidth);
            var right = Mathf.Clamp01(x + halfWidth);
            for (var lane = 0; lane < laneEnds.Length; lane++)
            {
                if (left >= laneEnds[lane] + gap)
                {
                    laneEnds[lane] = right;
                    return lane;
                }
            }

            var bestLane = 0;
            for (var lane = 1; lane < laneEnds.Length; lane++)
            {
                if (laneEnds[lane] < laneEnds[bestLane])
                {
                    bestLane = lane;
                }
            }

            laneEnds[bestLane] = right;
            return bestLane;
        }

        private void AddFlightPathTimeline(CuiElementContainer container, string root, EditorSession session, VisualProfileConfig profile)
        {
            var panel = container.Add(new CuiPanel
            {
                Image = { Color = "0.065 0.075 0.090 0.94" },
                RectTransform = { AnchorMin = "0.255 0.585", AnchorMax = "0.697 0.745" }
            }, root);
            var duration = Mathf.Max(0.5f, profile.DurationSeconds);
            for (var i = 0; i <= 4; i++)
            {
                var fraction = i / 4f;
                AddLabel(container, panel, FormatSeconds(duration * fraction), 7, TextAnchor.UpperCenter, FormatAnchor(Mathf.Clamp01(fraction - 0.045f), 0.78f), FormatAnchor(Mathf.Clamp01(fraction + 0.045f), 0.98f), "0.50 0.58 0.65 1");
                AddPanel(container, panel, FormatAnchor(fraction, 0.06f), FormatAnchor(Mathf.Min(1f, fraction + 0.002f), 0.76f), "0.35 0.40 0.46 0.45");
            }

            if (profile.Waypoints == null)
            {
                return;
            }

            // Close timestamps are assigned to separate vertical lanes instead of drawing on top of each other.
            var laneEnds = new[] { -1f, -1f, -1f, -1f };
            var waypointCount = profile.Waypoints.Count;
            var markerHalfWidth = waypointCount > 40 ? 0.009f : waypointCount > 20 ? 0.014f : 0.022f;
            for (var i = 0; i < waypointCount; i++)
            {
                var waypoint = profile.Waypoints[i];
                // Reserve a small edge gutter so the first and last marker keep their full click target.
                var x = 0.030f + Mathf.Clamp01(waypoint.Time / duration) * 0.940f;
                var lane = AssignTimelineMarkerLane(x, markerHalfWidth, laneEnds, 0.006f);
                var yMin = 0.075f + lane * 0.165f;
                var yMax = yMin + 0.140f;
                var selected = i == session.SelectedWaypointIndex;
                var label = waypointCount > 30 && !selected ? "•" : "W" + DisplayIndex(i);
                AddButton(container, panel, label, "airanim.ui.selectwp " + DisplayIndex(i), FormatAnchor(Mathf.Clamp01(x - markerHalfWidth), yMin), FormatAnchor(Mathf.Clamp01(x + markerHalfWidth), yMax), selected ? "0.95 0.48 0.12 0.98" : "0.16 0.48 0.64 0.94", label.Length > 3 ? 5 : 6);
            }
        }

        private bool IsAlignFieldEnabled(EditorSession session, string field)
        {
            if (session == null)
            {
                return false;
            }

            switch ((field ?? "").Trim().ToLowerInvariant())
            {
                case "x":
                case "posx": return session.AlignPositionX;
                case "y":
                case "posy": return session.AlignPositionY;
                case "z":
                case "posz": return session.AlignPositionZ;
                case "rx":
                case "rotx": return session.AlignRotationX;
                case "ry":
                case "roty": return session.AlignRotationY;
                case "rz":
                case "rotz": return session.AlignRotationZ;
                default: return false;
            }
        }

        private void ToggleAlignField(EditorSession session, string field)
        {
            if (session == null)
            {
                return;
            }

            switch ((field ?? "").Trim().ToLowerInvariant())
            {
                case "x":
                case "posx": session.AlignPositionX = !session.AlignPositionX; break;
                case "y":
                case "posy": session.AlignPositionY = !session.AlignPositionY; break;
                case "z":
                case "posz": session.AlignPositionZ = !session.AlignPositionZ; break;
                case "rx":
                case "rotx": session.AlignRotationX = !session.AlignRotationX; break;
                case "ry":
                case "roty": session.AlignRotationY = !session.AlignRotationY; break;
                case "rz":
                case "rotz": session.AlignRotationZ = !session.AlignRotationZ; break;
            }
        }

        private void SetAlignPreset(EditorSession session, string preset)
        {
            if (session == null)
            {
                return;
            }

            var normalized = (preset ?? "").Trim().ToLowerInvariant();
            var position = normalized == "position" || normalized == "pos" || normalized == "all";
            var rotation = normalized == "rotation" || normalized == "rot" || normalized == "all";
            session.AlignPositionX = position;
            session.AlignPositionY = position;
            session.AlignPositionZ = position;
            session.AlignRotationX = rotation;
            session.AlignRotationY = rotation;
            session.AlignRotationZ = rotation;
            if (normalized == "clear" || normalized == "none")
            {
                session.AlignPositionX = false;
                session.AlignPositionY = false;
                session.AlignPositionZ = false;
                session.AlignRotationX = false;
                session.AlignRotationY = false;
                session.AlignRotationZ = false;
            }
        }

        private bool HasAlignSelection(EditorSession session)
        {
            return session != null && (session.AlignPositionX || session.AlignPositionY || session.AlignPositionZ
                || session.AlignRotationX || session.AlignRotationY || session.AlignRotationZ);
        }

        private void AddAlignFieldButton(CuiElementContainer container, string root, EditorSession session, string text, string field, float x, float y)
        {
            var enabled = IsAlignFieldEnabled(session, field);
            AddButton(container, root, (enabled ? "[ON] " : "[ ] ") + text, "airanim.align.toggle " + field, FormatAnchor(x, y), FormatAnchor(x + 0.145f, y + 0.080f), enabled ? "0.14 0.34 0.22 1" : "0.14 0.18 0.23 1", 8);
        }

        private void ShowAlignUi(BasePlayer player)
        {
            if (player == null || !player.IsConnected || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var active = GetSelectedWaypoint(session, profile);
            if (active == null)
            {
                SetStatus(session, "No active waypoint selected.", "Select the reference waypoint before aligning marked waypoints.");
                ShowEditorUi(player);
                return;
            }

            CuiHelper.DestroyUi(player, AlignUiName);
            var markedCount = CountNormalizeWaypoints(session, profile);
            var container = new CuiElementContainer();
            var overlay = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0 0 0 0.86" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, "Overlay", AlignUiName);
            var root = container.Add(new CuiPanel
            {
                Image = { Color = "0.020 0.024 0.030 1" },
                RectTransform = { AnchorMin = "0.325 0.245", AnchorMax = "0.675 0.755" }
            }, overlay);

            AddPanel(container, root, "0.035 0.850", "0.965 0.965", "0.075 0.085 0.105 1");
            AddLabel(container, root, "Align Marked Waypoints", 16, TextAnchor.MiddleLeft, "0.065 0.895", "0.770 0.945", "1 0.86 0.58 1");
            AddButton(container, root, "X", "airanim.align.close", "0.865 0.890", "0.935 0.945", "0.55 0.12 0.10 1", 14);
            AddLabel(container, root, "Reference: active waypoint #" + DisplayIndex(session.SelectedWaypointIndex), 11, TextAnchor.MiddleLeft, "0.065 0.790", "0.935 0.835", "0.82 0.90 0.96 1");
            AddLabel(container, root, "Applies to " + markedCount + " marked waypoint(s). Multiple fields may be selected.", 9, TextAnchor.MiddleLeft, "0.065 0.745", "0.935 0.785", markedCount > 0 ? "0.60 0.72 0.79 1" : "1 0.64 0.44 1");

            AddLabel(container, root, "POSITION", 10, TextAnchor.MiddleLeft, "0.065 0.660", "0.280 0.705", "0.90 0.94 0.98 1");
            AddAlignFieldButton(container, root, session, "X", "posx", 0.065f, 0.565f);
            AddAlignFieldButton(container, root, session, "Y", "posy", 0.245f, 0.565f);
            AddAlignFieldButton(container, root, session, "Z", "posz", 0.425f, 0.565f);

            AddLabel(container, root, "ROTATION", 10, TextAnchor.MiddleLeft, "0.065 0.475", "0.280 0.520", "0.90 0.94 0.98 1");
            AddAlignFieldButton(container, root, session, "ROT X", "rotx", 0.065f, 0.380f);
            AddAlignFieldButton(container, root, session, "ROT Y", "roty", 0.245f, 0.380f);
            AddAlignFieldButton(container, root, session, "ROT Z", "rotz", 0.425f, 0.380f);

            AddButton(container, root, "POSITION", "airanim.align.preset position", "0.065 0.275", "0.235 0.335", "0.14 0.18 0.23 1", 8);
            AddButton(container, root, "ROTATION", "airanim.align.preset rotation", "0.250 0.275", "0.420 0.335", "0.14 0.18 0.23 1", 8);
            AddButton(container, root, "ALL", "airanim.align.preset all", "0.435 0.275", "0.600 0.335", "0.14 0.24 0.30 1", 8);
            AddButton(container, root, "CLEAR", "airanim.align.preset clear", "0.615 0.275", "0.780 0.335", "0.22 0.15 0.14 1", 8);

            AddButton(container, root, "CANCEL", "airanim.align.close", "0.065 0.105", "0.435 0.190", "0.18 0.22 0.28 1", 11);
            var canApply = markedCount > 0 && HasAlignSelection(session);
            AddButton(container, root, "APPLY ALIGNMENT", "airanim.align.apply", "0.565 0.105", "0.935 0.190", canApply ? "0.42 0.20 0.10 1" : "0.10 0.11 0.13 1", 11);
            AddLabel(container, root, "The selected waypoint remains the reference even when it is also marked.", 8, TextAnchor.MiddleCenter, "0.065 0.035", "0.935 0.085", "0.50 0.59 0.66 1");

            RegisterUiBridge(player, AlignUiName);
            CuiHelper.AddUi(player, container);
        }

        private void AddWaypointInspector(CuiElementContainer container, string root, EditorSession session, VisualProfileConfig profile)
        {
            AddPanel(container, root, "0.728 0.085", "0.988 0.885", "0.045 0.052 0.065 0.96");
            var waypoint = GetSelectedWaypoint(session, profile);
            if (waypoint == null)
            {
                AddLabel(container, root, "WAYPOINT INSPECTOR", 13, TextAnchor.MiddleLeft, "0.745 0.840", "0.965 0.875", "0.92 0.96 1 1");
                AddLabel(container, root, "Select or create a waypoint.", 11, TextAnchor.MiddleCenter, "0.755 0.430", "0.965 0.520", "0.62 0.70 0.76 1");
                return;
            }

            AddLabel(container, root, "WAYPOINT #" + DisplayIndex(session.SelectedWaypointIndex), 13, TextAnchor.MiddleLeft, "0.745 0.840", "0.925 0.875", "1 0.86 0.58 1");
            AddButton(container, root, "GO", "airanim.ui.gotowp", "0.930 0.838", "0.970 0.875", "0.12 0.31 0.37 0.96", 8);
            AddLabel(container, root, "Click a value to open the reliable keypad editor", 8, TextAnchor.MiddleLeft, "0.745 0.806", "0.970 0.832", "0.49 0.58 0.64 1");

            AddLabel(container, root, "TIME", 8, TextAnchor.MiddleLeft, "0.745 0.760", "0.800 0.790", "0.70 0.78 0.84 1");
            AddButton(container, root, FormatFloat(waypoint.Time), "airanim.valueedit.open waypointtime selected full", "0.805 0.755", "0.875 0.795", "0.070 0.082 0.100 0.98", 10);
            AddLabel(container, root, "SEGMENT", 8, TextAnchor.MiddleLeft, "0.882 0.760", "0.930 0.790", "0.70 0.78 0.84 1");
            AddButton(container, root, FormatFloat(GetTimelineSegmentDuration(profile, session.SelectedWaypointIndex)), "airanim.valueedit.open duration selected full", "0.930 0.755", "0.972 0.795", "0.070 0.082 0.100 0.98", 9);

            AddLabel(container, root, "POSITION", 9, TextAnchor.MiddleLeft, "0.745 0.710", "0.850 0.742", "0.88 0.92 0.96 1");
            AddInspectorInput(container, root, "X", FormatFloat(waypoint.X), "airanim.valueedit.open pos x full", 0.745f, 0.660f);
            AddInspectorInput(container, root, "Y", FormatFloat(waypoint.Y), "airanim.valueedit.open pos y full", 0.823f, 0.660f);
            AddInspectorInput(container, root, "Z", FormatFloat(waypoint.Z), "airanim.valueedit.open pos z full", 0.901f, 0.660f);

            AddLabel(container, root, "ROTATION", 9, TextAnchor.MiddleLeft, "0.745 0.610", "0.850 0.642", "0.88 0.92 0.96 1");
            AddInspectorInput(container, root, "X", FormatFloat(waypoint.RotationX), "airanim.valueedit.open rot x full", 0.745f, 0.560f);
            AddInspectorInput(container, root, "Y", FormatFloat(waypoint.RotationY), "airanim.valueedit.open rot y full", 0.823f, 0.560f);
            AddInspectorInput(container, root, "Z", FormatFloat(waypoint.RotationZ), "airanim.valueedit.open rot z full", 0.901f, 0.560f);

            AddLabel(container, root, "MOVE STEP", 9, TextAnchor.MiddleLeft, "0.745 0.510", "0.850 0.542", "0.88 0.92 0.96 1");
            AddStepButton(container, root, session, 0.1f, 0.745f, 0.462f);
            AddStepButton(container, root, session, 1f, 0.803f, 0.462f);
            AddStepButton(container, root, session, 5f, 0.861f, 0.462f);
            AddStepButton(container, root, session, 10f, 0.919f, 0.462f);

            var step = FormatFloat(session.WaypointNudgeStep);
            AddButton(container, root, "FWD", "airanim.ui.nudge forward " + step, "0.812 0.395", "0.905 0.438", "0.14 0.25 0.32 0.96", 8);
            AddButton(container, root, "BACK", "airanim.ui.nudge back " + step, "0.812 0.300", "0.905 0.343", "0.14 0.25 0.32 0.96", 8);
            AddButton(container, root, "LEFT", "airanim.ui.nudge left " + step, "0.745 0.347", "0.825 0.390", "0.14 0.25 0.32 0.96", 8);
            AddButton(container, root, "RIGHT", "airanim.ui.nudge right " + step, "0.892 0.347", "0.972 0.390", "0.14 0.25 0.32 0.96", 8);
            AddButton(container, root, "UP", "airanim.ui.nudge up " + step, "0.745 0.252", "0.825 0.295", "0.14 0.31 0.20 0.96", 8);
            AddButton(container, root, "DOWN", "airanim.ui.nudge down " + step, "0.892 0.252", "0.972 0.295", "0.40 0.16 0.11 0.96", 8);

            AddLabel(container, root, "ALIGNMENT", 8, TextAnchor.MiddleLeft, "0.745 0.190", "0.850 0.220", "0.65 0.73 0.79 1");
            AddLabel(container, root, CountNormalizeWaypoints(session, profile) + " marked • reference is waypoint #" + DisplayIndex(session.SelectedWaypointIndex), 7, TextAnchor.MiddleRight, "0.840 0.190", "0.972 0.220", "0.50 0.59 0.66 1");
            AddButton(container, root, "ALIGN MARKED…", "airanim.align.open", "0.745 0.135", "0.972 0.180", "0.42 0.19 0.10 0.96", 9);
        }

        private void AddInspectorInput(CuiElementContainer container, string root, string label, string value, string command, float x, float y)
        {
            AddLabel(container, root, label, 8, TextAnchor.MiddleCenter, FormatAnchor(x, y), FormatAnchor(x + 0.020f, y + 0.043f), "0.65 0.73 0.79 1");
            AddButton(container, root, value, command, FormatAnchor(x + 0.023f, y), FormatAnchor(x + 0.073f, y + 0.043f), "0.070 0.082 0.100 0.98", 9);
        }

        private void AddStepButton(CuiElementContainer container, string root, EditorSession session, float step, float x, float y)
        {
            var active = Mathf.Abs(session.WaypointNudgeStep - step) < 0.001f;
            AddButton(container, root, FormatFloat(step) + "m", "airanim.waypoint.step " + FormatFloat(step), FormatAnchor(x, y), FormatAnchor(x + 0.050f, y + 0.038f), active ? "0.34 0.20 0.09 0.98" : "0.14 0.18 0.23 0.96", 7);
        }

        private void AddReleaseWorkspace(CuiElementContainer container, string root, EditorSession session, VisualProfileConfig profile)
        {
            if (IsRepeatedPatternMode(profile))
            {
                var generatedCount = GetGeneratedReleaseGroupCount(profile);
                session.SelectedGeneratedReleaseIndex = generatedCount <= 0
                    ? 0
                    : Mathf.Clamp(session.SelectedGeneratedReleaseIndex, 0, generatedCount - 1);
            }

            AddPanel(container, root, "0.238 0.085", "0.718 0.815", "0.045 0.052 0.065 0.96");
            AddLabel(container, root, "Release Timing", 15, TextAnchor.MiddleLeft, "0.255 0.770", "0.390 0.805", "1 1 1 1");
            AddButton(container, root, "MANUAL EVENTS", "airanim.release.mode manual", "0.400 0.765", "0.535 0.802", !IsRepeatedPatternMode(profile) ? "0.34 0.20 0.09 0.98" : "0.13 0.17 0.22 0.96", 8);
            AddButton(container, root, "REPEATED PATTERN", "airanim.release.mode generated", "0.545 0.765", "0.697 0.802", IsRepeatedPatternMode(profile) ? "0.34 0.20 0.09 0.98" : "0.13 0.17 0.22 0.96", 8);

            var validation = GetReleaseValidationMessage(profile);
            AddLabel(container, root, validation, 9, TextAnchor.MiddleLeft, "0.255 0.725", "0.697 0.758", validation.IndexOf("after the profile", StringComparison.OrdinalIgnoreCase) >= 0 || validation.StartsWith("No ", StringComparison.OrdinalIgnoreCase) ? "1 0.65 0.43 1" : "0.66 0.78 0.84 1");

            AddButton(container, root, "FIT PROFILE", "airanim.release.view profile", "0.255 0.680", "0.335 0.713", session.ReleaseTimelineView == "profile" ? "0.28 0.20 0.10 0.98" : "0.13 0.17 0.22 0.96", 7);
            AddButton(container, root, "FIT RELEASES", "airanim.release.view releases", "0.342 0.680", "0.430 0.713", session.ReleaseTimelineView == "releases" ? "0.28 0.20 0.10 0.98" : "0.13 0.17 0.22 0.96", 7);
            AddButton(container, root, "SELECTED ±5s", "airanim.release.view selected", "0.437 0.680", "0.530 0.713", session.ReleaseTimelineView == "selected" ? "0.28 0.20 0.10 0.98" : "0.13 0.17 0.22 0.96", 7);
            if (!IsRepeatedPatternMode(profile))
            {
                AddButton(container, root, "+ RELEASE", "airanim.release.add", "0.540 0.680", "0.610 0.713", "0.14 0.31 0.20 0.96", 7);
                AddButton(container, root, "AT WP", "airanim.release.atwp", "0.618 0.680", "0.697 0.713", "0.12 0.29 0.34 0.96", 7);
            }
            else
            {
                AddButton(container, root, "TO MANUAL", "airanim.pattern.convertmanual", "0.550 0.680", "0.697 0.713", "0.14 0.31 0.20 0.96", 7);
            }

            AddReleaseTimelineLanes(container, root, session, profile);
            AddReleaseTable(container, root, session, profile);
        }

        private void GetReleaseTimelineRange(EditorSession session, VisualProfileConfig profile, List<VisualPayloadEvent> schedule, out float start, out float end)
        {
            start = 0f;
            end = Mathf.Max(0.5f, profile == null ? 0.5f : profile.DurationSeconds);
            var view = session == null ? "releases" : (session.ReleaseTimelineView ?? "releases").Trim().ToLowerInvariant();
            if (view == "profile" || schedule == null || schedule.Count == 0)
            {
                return;
            }

            if (view == "selected")
            {
                var selectedTime = IsRepeatedPatternMode(profile)
                    ? (schedule.Count == 0 ? profile.FirstPayloadDelaySeconds : schedule[Mathf.Clamp(session.SelectedGeneratedReleaseIndex, 0, schedule.Count - 1)].Time)
                    : (GetSelectedPayloadEvent(session, profile) == null ? profile.FirstPayloadDelaySeconds : GetSelectedPayloadEvent(session, profile).Time);
                start = Mathf.Max(0f, selectedTime - 5f);
                end = Mathf.Min(Mathf.Max(profile.DurationSeconds, selectedTime + 5f), selectedTime + 5f);
                if (end - start < 1f)
                {
                    end = start + 1f;
                }
                return;
            }

            var minimum = float.MaxValue;
            var maximum = float.MinValue;
            foreach (var payloadEvent in schedule)
            {
                if (payloadEvent == null)
                {
                    continue;
                }

                minimum = Mathf.Min(minimum, payloadEvent.Time);
                maximum = Mathf.Max(maximum, payloadEvent.Time);
            }

            if (minimum == float.MaxValue)
            {
                return;
            }

            var span = Mathf.Max(0.5f, maximum - minimum);
            var padding = Mathf.Max(0.35f, span * 0.08f);
            start = Mathf.Max(0f, minimum - padding);
            end = maximum + padding;
            if (end - start < 1f)
            {
                end = start + 1f;
            }
        }

        private void AddReleaseTimelineLanes(CuiElementContainer container, string root, EditorSession session, VisualProfileConfig profile)
        {
            var panel = container.Add(new CuiPanel
            {
                Image = { Color = "0.065 0.075 0.090 0.95" },
                RectTransform = { AnchorMin = "0.255 0.475", AnchorMax = "0.697 0.665" }
            }, root);
            var schedule = BuildEffectiveReleaseSchedule(profile);
            float start;
            float end;
            GetReleaseTimelineRange(session, profile, schedule, out start, out end);
            var span = Mathf.Max(0.1f, end - start);

            for (var i = 0; i <= 4; i++)
            {
                var fraction = i / 4f;
                var time = start + span * fraction;
                AddLabel(container, panel, FormatSeconds(time), 7, TextAnchor.UpperCenter, FormatAnchor(Mathf.Clamp01(fraction - 0.055f), 0.82f), FormatAnchor(Mathf.Clamp01(fraction + 0.055f), 0.99f), "0.52 0.61 0.68 1");
                AddPanel(container, panel, FormatAnchor(fraction, 0.03f), FormatAnchor(Mathf.Min(1f, fraction + 0.002f), 0.80f), "0.35 0.40 0.46 0.45");
            }

            AddLabel(container, panel, "WP", 7, TextAnchor.MiddleLeft, "0.005 0.52", "0.050 0.70", "0.52 0.65 0.74 1");
            AddLabel(container, panel, "REL", 7, TextAnchor.MiddleLeft, "0.005 0.12", "0.050 0.30", "0.82 0.60 0.38 1");
            var waypointLaneEnds = new[] { -1f, -1f };
            const float waypointHalfWidth = 0.018f;
            if (profile.Waypoints != null)
            {
                for (var i = 0; i < profile.Waypoints.Count; i++)
                {
                    var time = profile.Waypoints[i].Time;
                    if (time < start - 0.001f || time > end + 0.001f)
                    {
                        continue;
                    }

                    var x = 0.055f + Mathf.Clamp01((time - start) / span) * 0.925f;
                    var lane = AssignTimelineMarkerLane(x, waypointHalfWidth, waypointLaneEnds, 0.004f);
                    var yMin = 0.485f + lane * 0.145f;
                    var yMax = yMin + 0.115f;
                    AddButton(container, panel, "W" + DisplayIndex(i), "airanim.ui.selectwp " + DisplayIndex(i), FormatAnchor(Mathf.Clamp01(x - waypointHalfWidth), yMin), FormatAnchor(Mathf.Clamp01(x + waypointHalfWidth), yMax), i == session.SelectedWaypointIndex ? "0.95 0.48 0.12 0.96" : "0.15 0.46 0.62 0.88", 5);
                }
            }

            var visible = new List<int>();
            for (var i = 0; i < schedule.Count; i++)
            {
                var ev = schedule[i];
                if (ev != null && ev.Time >= start - 0.001f && ev.Time <= end + 0.001f)
                {
                    visible.Add(i);
                }
            }

            var stride = visible.Count > 60 ? Mathf.CeilToInt(visible.Count / 60f) : 1;
            var markerHalfWidth = visible.Count > 40 ? 0.009f : visible.Count > 24 ? 0.012f : visible.Count > 12 ? 0.016f : 0.022f;
            var releaseLaneEnds = new[] { -1f, -1f, -1f };
            for (var n = 0; n < visible.Count; n += stride)
            {
                var i = visible[n];
                var ev = schedule[i];
                var x = 0.055f + Mathf.Clamp01((ev.Time - start) / span) * 0.925f;
                var lane = AssignTimelineMarkerLane(x, markerHalfWidth, releaseLaneEnds, 0.003f);
                var selected = IsRepeatedPatternMode(profile) ? i == session.SelectedGeneratedReleaseIndex : GetSelectedPayloadEvent(session, profile) == ev;
                var command = IsRepeatedPatternMode(profile) ? "airanim.pattern.focus " + i : "airanim.release.select " + profile.PayloadEvents.IndexOf(ev);
                var label = (IsRepeatedPatternMode(profile) ? "R" : "P") + DisplayIndex(i);
                var displayLabel = visible.Count > 24 && !selected ? "•" : label;
                var yMin = 0.045f + lane * 0.125f;
                var yMax = yMin + 0.100f;
                AddButton(container, panel, displayLabel, command, FormatAnchor(Mathf.Clamp01(x - markerHalfWidth), yMin), FormatAnchor(Mathf.Clamp01(x + markerHalfWidth), yMax), selected ? "1.00 0.60 0.16 0.98" : "0.82 0.30 0.10 0.92", displayLabel.Length > 3 ? 5 : 6);
            }

            if (visible.Count > 60)
            {
                AddLabel(container, panel, "Showing every " + stride + "th marker; table retains exact values.", 7, TextAnchor.LowerRight, "0.52 0.005", "0.995 0.040", "0.56 0.64 0.70 1");
            }
        }

        private void AddReleaseTable(CuiElementContainer container, string root, EditorSession session, VisualProfileConfig profile)
        {
            var schedule = IsRepeatedPatternMode(profile) ? BuildEffectiveReleaseSchedule(profile) : (profile.PayloadEvents ?? new List<VisualPayloadEvent>());
            var count = schedule.Count;
            var pageCount = Math.Max(1, Mathf.CeilToInt(count / (float)ReleaseRowsPerPage));
            session.ReleasePage = Mathf.Clamp(session.ReleasePage, 0, pageCount - 1);
            AddLabel(container, root, IsRepeatedPatternMode(profile) ? "GENERATED SCHEDULE" : "MANUAL RELEASE EVENTS", 9, TextAnchor.MiddleLeft, "0.255 0.430", "0.445 0.462", "0.72 0.80 0.86 1");
            AddLabel(container, root, "Page " + (session.ReleasePage + 1) + "/" + pageCount, 8, TextAnchor.MiddleRight, "0.570 0.430", "0.635 0.462", "0.52 0.60 0.66 1");
            AddButton(container, root, "<", "airanim.release.page -1", "0.642 0.428", "0.665 0.458", "0.14 0.18 0.23 0.96", 9);
            AddButton(container, root, ">", "airanim.release.page 1", "0.670 0.428", "0.693 0.458", "0.14 0.18 0.23 0.96", 9);
            AddLabel(container, root, "#     TIME       Δ PREV       ORDNANCE                         UNITS", 7, TextAnchor.MiddleLeft, "0.260 0.402", "0.695 0.427", "0.46 0.55 0.62 1");

            if (count == 0)
            {
                AddLabel(container, root, IsRepeatedPatternMode(profile) ? "Set a total, interval, payload and units per release in the inspector." : "Add a release, or create one at the selected waypoint.", 10, TextAnchor.MiddleCenter, "0.270 0.245", "0.685 0.330", "0.64 0.72 0.78 1");
                if (!IsRepeatedPatternMode(profile))
                {
                    AddButton(container, root, "CREATE FIRST RELEASE", "airanim.release.add", "0.380 0.185", "0.575 0.225", "0.14 0.31 0.20 0.96", 9);
                }
                return;
            }

            var first = session.ReleasePage * ReleaseRowsPerPage;
            var last = Math.Min(count, first + ReleaseRowsPerPage);
            for (var i = first; i < last; i++)
            {
                var localRow = i - first;
                var yMax = 0.397f - localRow * 0.0375f;
                var yMin = yMax - 0.032f;
                var ev = schedule[i];
                var delta = i <= 0 ? "—" : FormatSeconds(ev.Time - schedule[i - 1].Time);
                var selected = IsRepeatedPatternMode(profile) ? i == session.SelectedGeneratedReleaseIndex : GetSelectedPayloadEvent(session, profile) == ev;
                var row = container.Add(new CuiPanel
                {
                    Image = { Color = selected ? "0.27 0.16 0.08 0.96" : "0.082 0.096 0.118 0.94" },
                    RectTransform = { AnchorMin = FormatAnchor(0.255f, yMin), AnchorMax = FormatAnchor(0.697f, yMax) }
                }, root);
                var command = IsRepeatedPatternMode(profile) ? "airanim.pattern.focus " + i : "airanim.release.select " + i;
                AddLabel(container, row, DisplayIndex(i).ToString(CultureInfo.InvariantCulture), 8, TextAnchor.MiddleLeft, "0.015 0.05", "0.080 0.95", selected ? "1 0.86 0.55 1" : "0.90 0.94 0.98 1");
                AddLabel(container, row, FormatSeconds(ev.Time), 8, TextAnchor.MiddleLeft, "0.090 0.05", "0.230 0.95", "0.78 0.86 0.92 1");
                AddLabel(container, row, delta, 8, TextAnchor.MiddleLeft, "0.235 0.05", "0.380 0.95", "0.55 0.64 0.70 1");
                AddLabel(container, row, ShortenText(GetPayloadDisplay(ev.Payload), 27), 8, TextAnchor.MiddleLeft, "0.390 0.05", "0.835 0.95", "0.78 0.86 0.92 1");
                AddLabel(container, row, "x" + Math.Max(1, ev.Count), 8, TextAnchor.MiddleRight, "0.840 0.05", "0.975 0.95", "0.92 0.82 0.58 1");
                // The row hit target must be the last sibling so all visible text remains clickable.
                AddButton(container, row, "", command, "0 0", "1 1", "0 0 0 0", 1);
            }

            if (!IsRepeatedPatternMode(profile))
            {
                RepeatedPatternDetection detection;
                if (TryDetectRepeatedPattern(profile, out detection))
                {
                    AddButton(container, root, "CONVERT DETECTED PATTERN", "airanim.pattern.detect", "0.255 0.095", "0.450 0.130", "0.34 0.20 0.09 0.98", 8);
                    AddLabel(container, root, detection.ReleaseGroups + " groups × " + detection.UnitsPerRelease + " every " + FormatSeconds(detection.IntervalSeconds), 8, TextAnchor.MiddleLeft, "0.462 0.095", "0.697 0.130", "0.62 0.72 0.78 1");
                }
            }
        }

        private void AddReleaseInspector(CuiElementContainer container, string root, EditorSession session, VisualProfileConfig profile)
        {
            AddPanel(container, root, "0.728 0.085", "0.988 0.885", "0.045 0.052 0.065 0.96");
            if (IsRepeatedPatternMode(profile))
            {
                AddPatternInspector(container, root, session, profile);
                return;
            }

            var ev = GetSelectedPayloadEvent(session, profile);
            AddLabel(container, root, ev == null ? "MANUAL RELEASE" : "RELEASE #" + DisplayIndex(session.SelectedPayloadEventIndex), 13, TextAnchor.MiddleLeft, "0.745 0.840", "0.930 0.875", ev == null ? "0.92 0.96 1 1" : "1 0.86 0.58 1");
            if (ev == null)
            {
                AddLabel(container, root, "No release is selected.", 11, TextAnchor.MiddleCenter, "0.755 0.520", "0.970 0.585", "0.62 0.70 0.76 1");
                AddButton(container, root, "ADD RELEASE", "airanim.release.add", "0.790 0.445", "0.930 0.495", "0.14 0.31 0.20 0.96", 9);
                AddButton(container, root, "AT SELECTED WAYPOINT", "airanim.release.atwp", "0.770 0.380", "0.950 0.430", "0.12 0.29 0.34 0.96", 8);
                return;
            }

            AddLabel(container, root, "Click the value to edit with the keypad", 8, TextAnchor.MiddleLeft, "0.745 0.803", "0.970 0.832", "0.49 0.58 0.64 1");
            AddButton(container, root, FormatFloat(ev.Time), "airanim.valueedit.open release time full", "0.745 0.750", "0.970 0.795", "0.070 0.082 0.100 0.98", 13);
            AddButton(container, root, "-1.00", "airanim.release.nudge -1", "0.745 0.700", "0.797 0.738", "0.32 0.13 0.10 0.96", 8);
            AddButton(container, root, "-0.25", "airanim.release.nudge -0.25", "0.803 0.700", "0.855 0.738", "0.32 0.13 0.10 0.96", 8);
            AddButton(container, root, "+0.25", "airanim.release.nudge 0.25", "0.861 0.700", "0.913 0.738", "0.14 0.31 0.20 0.96", 8);
            AddButton(container, root, "+1.00", "airanim.release.nudge 1", "0.919 0.700", "0.970 0.738", "0.14 0.31 0.20 0.96", 8);

            AddLabel(container, root, "SNAP TO WAYPOINT", 8, TextAnchor.MiddleLeft, "0.745 0.655", "0.900 0.684", "0.65 0.73 0.79 1");
            AddButton(container, root, "PREV", "airanim.release.snap prev", "0.745 0.610", "0.815 0.646", "0.14 0.18 0.23 0.96", 8);
            AddButton(container, root, "NEAREST", "airanim.release.snap nearest", "0.822 0.610", "0.895 0.646", "0.14 0.25 0.32 0.96", 7);
            AddButton(container, root, "NEXT", "airanim.release.snap next", "0.902 0.610", "0.970 0.646", "0.14 0.18 0.23 0.96", 8);

            AddLabel(container, root, "ORDNANCE", 8, TextAnchor.MiddleLeft, "0.745 0.565", "0.850 0.594", "0.65 0.73 0.79 1");
            AddButton(container, root, ShortenText(GetPayloadDisplay(ev.Payload), 26), "airanim.release.payload", "0.745 0.515", "0.970 0.558", "0.070 0.082 0.100 0.98", 9);

            AddLabel(container, root, "UNITS THIS RELEASE", 8, TextAnchor.MiddleLeft, "0.745 0.472", "0.875 0.502", "0.65 0.73 0.79 1");
            AddButton(container, root, "-", "airanim.release.countdelta -1", "0.745 0.425", "0.792 0.465", "0.32 0.13 0.10 0.96", 11);
            AddButton(container, root, Math.Max(1, ev.Count).ToString(CultureInfo.InvariantCulture), "airanim.valueedit.open release count full", "0.800 0.425", "0.915 0.465", "0.070 0.082 0.100 0.98", 11);
            AddButton(container, root, "+", "airanim.release.countdelta 1", "0.923 0.425", "0.970 0.465", "0.14 0.31 0.20 0.96", 11);

            AddButton(container, root, "PREV", "airanim.release.prev", "0.745 0.370", "0.800 0.410", "0.14 0.18 0.23 0.96", 8);
            AddButton(container, root, "NEXT", "airanim.release.next", "0.808 0.370", "0.863 0.410", "0.14 0.18 0.23 0.96", 8);
            AddButton(container, root, "DUP", "airanim.release.dup", "0.871 0.370", "0.918 0.410", "0.14 0.31 0.20 0.96", 8);
            AddButton(container, root, "DEL", "airanim.release.delete", "0.926 0.370", "0.970 0.410", "0.45 0.11 0.08 0.96", 8);

            AddButton(container, root, session.ReleaseAdvancedOpen ? "ADVANCED ▴" : "ADVANCED ▾", "airanim.release.advanced", "0.745 0.315", "0.970 0.352", session.ReleaseAdvancedOpen ? "0.28 0.20 0.10 0.98" : "0.14 0.18 0.23 0.96", 8);
            if (session.ReleaseAdvancedOpen)
            {
                AddCompactReleaseField(container, root, "SPREAD", FormatOptionalFloat(ev.SpreadRadius), "spread", 0.745f, 0.265f);
                AddCompactReleaseField(container, root, "SPEED", FormatOptionalFloat(ev.LaunchSpeed), "speed", 0.860f, 0.265f);
                AddCompactReleaseField(container, root, "FUSE", FormatOptionalFloat(ev.FuseSeconds), "fuse", 0.745f, 0.215f);
                AddCompactReleaseField(container, root, "DAMAGE", FormatFloat(ev.DamageScale), "damage", 0.860f, 0.215f);
                AddCompactReleaseField(container, root, "SPLASH", FormatOptionalFloat(ev.SplashRadius), "splash", 0.745f, 0.165f);
                AddCompactReleaseField(container, root, "IMPACT", FormatOptionalFloat(ev.ImpactRadius), "impact", 0.860f, 0.165f);
                AddButton(container, root, "FULL ADVANCED POPUP", "airanim.release.openpopup", "0.745 0.110", "0.970 0.148", "0.16 0.20 0.25 0.96", 7);
            }
            else
            {
                AddLabel(container, root, "Offsets, spread, speed, fuse, damage and tracking are hidden until needed.", 8, TextAnchor.UpperLeft, "0.745 0.220", "0.970 0.295", "0.48 0.57 0.64 1");
            }
        }

        private void AddCompactReleaseField(CuiElementContainer container, string root, string label, string value, string field, float x, float y)
        {
            AddLabel(container, root, label, 7, TextAnchor.MiddleLeft, FormatAnchor(x, y + 0.028f), FormatAnchor(x + 0.100f, y + 0.050f), "0.56 0.65 0.72 1");
            AddButton(container, root, value, "airanim.valueedit.open release " + field + " full", FormatAnchor(x, y), FormatAnchor(x + 0.105f, y + 0.028f), "0.070 0.082 0.100 0.98", 8);
        }

        private void AddPatternInspector(CuiElementContainer container, string root, EditorSession session, VisualProfileConfig profile)
        {
            if (profile.ReleaseTemplate == null)
            {
                profile.ReleaseTemplate = new VisualPayloadEvent();
            }

            var template = profile.ReleaseTemplate;
            var groups = GetGeneratedReleaseGroupCount(profile);
            AddLabel(container, root, "REPEATED PATTERN", 13, TextAnchor.MiddleLeft, "0.745 0.840", "0.970 0.875", "1 0.86 0.58 1");
            AddLabel(container, root, "Click any value to edit with the keypad", 8, TextAnchor.MiddleLeft, "0.745 0.806", "0.970 0.832", "0.49 0.58 0.64 1");

            AddPatternInputRow(container, root, "START TIME", FormatFloat(profile.FirstPayloadDelaySeconds), "start", 0.755f);
            AddPatternInputRow(container, root, "INTERVAL", FormatFloat(profile.PayloadReleaseIntervalSeconds), "interval", 0.675f);
            AddLabel(container, root, "ORDNANCE", 8, TextAnchor.MiddleLeft, "0.745 0.625", "0.850 0.654", "0.65 0.73 0.79 1");
            AddButton(container, root, ShortenText(GetPayloadDisplay(template.Payload), 26), "airanim.pattern.payload", "0.745 0.580", "0.970 0.620", "0.070 0.082 0.100 0.98", 9);
            AddPatternInputRow(container, root, "UNITS / RELEASE", Math.Max(1, template.Count).ToString(CultureInfo.InvariantCulture), "units", 0.505f);
            AddPatternInputRow(container, root, "TOTAL UNITS", Math.Max(0, profile.MaxPayloadCount).ToString(CultureInfo.InvariantCulture), "total", 0.425f);
            AddPatternInputRow(container, root, "RELEASE GROUPS", groups.ToString(CultureInfo.InvariantCulture), "groups", 0.345f);

            var generatedLast = GetGeneratedLastReleaseTime(profile);
            AddLabel(container, root, groups + " group(s)  •  Last at " + FormatSeconds(generatedLast), 9, TextAnchor.MiddleLeft, "0.745 0.300", generatedLast > profile.DurationSeconds ? "0.875 0.335" : "0.970 0.335", generatedLast > profile.DurationSeconds ? "1 0.62 0.40 1" : "0.66 0.78 0.84 1");
            if (generatedLast > profile.DurationSeconds)
            {
                AddButton(container, root, "EXTEND", "airanim.pattern.extend", "0.885 0.298", "0.970 0.338", generatedLast <= 120f ? "0.40 0.19 0.10 0.96" : "0.18 0.12 0.10 0.80", 7);
            }
            AddButton(container, root, "-0.25 START", "airanim.pattern.delta start -0.25", "0.745 0.250", "0.850 0.287", "0.32 0.13 0.10 0.96", 7);
            AddButton(container, root, "+0.25 START", "airanim.pattern.delta start 0.25", "0.860 0.250", "0.970 0.287", "0.14 0.31 0.20 0.96", 7);
            AddButton(container, root, "-0.05 INTERVAL", "airanim.pattern.delta interval -0.05", "0.745 0.205", "0.850 0.242", "0.32 0.13 0.10 0.96", 7);
            AddButton(container, root, "+0.05 INTERVAL", "airanim.pattern.delta interval 0.05", "0.860 0.205", "0.970 0.242", "0.14 0.31 0.20 0.96", 7);

            AddButton(container, root, session.ReleaseAdvancedOpen ? "TEMPLATE ADVANCED ▴" : "TEMPLATE ADVANCED ▾", "airanim.release.advanced", "0.745 0.155", "0.970 0.192", session.ReleaseAdvancedOpen ? "0.28 0.20 0.10 0.98" : "0.14 0.18 0.23 0.96", 8);
            if (session.ReleaseAdvancedOpen)
            {
                AddButton(container, root, "OPEN FULL TEMPLATE SETTINGS", "airanim.pattern.openpopup", "0.745 0.100", "0.970 0.140", "0.16 0.24 0.34 0.96", 8);
            }
        }

        private void AddPatternInputRow(CuiElementContainer container, string root, string label, string value, string field, float y)
        {
            AddLabel(container, root, label, 8, TextAnchor.MiddleLeft, FormatAnchor(0.745f, y), FormatAnchor(0.850f, y + 0.038f), "0.65 0.73 0.79 1");
            AddButton(container, root, value, "airanim.valueedit.open pattern " + field + " full", FormatAnchor(0.855f, y), FormatAnchor(0.970f, y + 0.040f), "0.070 0.082 0.100 0.98", 10);
        }

        private void AddCompactPatternField(CuiElementContainer container, string root, string label, string value, string field, float x, float y)
        {
            AddLabel(container, root, label, 7, TextAnchor.MiddleLeft, FormatAnchor(x, y + 0.026f), FormatAnchor(x + 0.100f, y + 0.046f), "0.56 0.65 0.72 1");
            AddButton(container, root, value, "airanim.valueedit.open pattern " + field + " full", FormatAnchor(x, y), FormatAnchor(x + 0.105f, y + 0.027f), "0.070 0.082 0.100 0.98", 8);
        }

        private void AddProfileWorkspace(CuiElementContainer container, string root, EditorSession session, VisualProfileConfig profile)
        {
            AddPanel(container, root, "0.238 0.085", "0.718 0.815", "0.045 0.052 0.065 0.96");
            AddLabel(container, root, "Profile Settings", 15, TextAnchor.MiddleLeft, "0.255 0.770", "0.430 0.805", "1 1 1 1");
            AddLabel(container, root, "Click numeric values to use the keypad; quick nudges remain beside them.", 8, TextAnchor.MiddleRight, "0.430 0.770", "0.697 0.805", "0.50 0.58 0.65 1");

            AddProfileSettingRow(container, root, "VEHICLE", profile.Vehicle, "airanim.ui.vehicle next", 0.675f, false, "");
            AddProfileSettingRow(container, root, "DURATION", FormatFloat(profile.DurationSeconds), "", 0.585f, true, "duration");
            AddButton(container, root, "-1s", "airanim.ui.profiledelta duration -1", "0.590 0.592", "0.638 0.627", "0.32 0.13 0.10 0.96", 8);
            AddButton(container, root, "+1s", "airanim.ui.profiledelta duration 1", "0.645 0.592", "0.695 0.627", "0.14 0.31 0.20 0.96", 8);
            AddProfileSettingRow(container, root, "ROTATION SMOOTH", FormatFloat(profile.RotationSmoothTimeSeconds), "", 0.495f, true, "smooth");
            AddButton(container, root, "-.05", "airanim.ui.profiledelta smooth -0.05", "0.590 0.502", "0.638 0.537", "0.32 0.13 0.10 0.96", 8);
            AddButton(container, root, "+.05", "airanim.ui.profiledelta smooth 0.05", "0.645 0.502", "0.695 0.537", "0.14 0.31 0.20 0.96", 8);
            AddProfileSettingRow(container, root, "TERRAIN CLEARANCE", FormatFloat(profile.MinimumTerrainClearance), "", 0.405f, true, "clearance");
            AddButton(container, root, "-5m", "airanim.ui.profiledelta clearance -5", "0.590 0.412", "0.638 0.447", "0.32 0.13 0.10 0.96", 8);
            AddButton(container, root, "+5m", "airanim.ui.profiledelta clearance 5", "0.645 0.412", "0.695 0.447", "0.14 0.31 0.20 0.96", 8);

            AddLabel(container, root, "WAYPOINT MOTION", 9, TextAnchor.MiddleLeft, "0.255 0.330", "0.430 0.365", "0.72 0.80 0.86 1");
            AddButton(container, root, profile.StopAtWaypoints ? "STOP AT WAYPOINTS: ON" : "STOP AT WAYPOINTS: OFF", "airanim.ui.stopwaypoints", "0.455 0.325", "0.695 0.368", profile.StopAtWaypoints ? "0.30 0.20 0.10 0.98" : "0.13 0.25 0.31 0.96", 9);

            AddPanel(container, root, "0.255 0.170", "0.695 0.285", "0.065 0.075 0.090 0.94");
            AddLabel(container, root, "RELEASE SUMMARY", 8, TextAnchor.MiddleLeft, "0.270 0.250", "0.410 0.278", "0.58 0.67 0.73 1");
            AddLabel(container, root, IsRepeatedPatternMode(profile) ? "Repeated pattern" : "Manual events", 11, TextAnchor.MiddleLeft, "0.270 0.205", "0.430 0.245", "1 0.86 0.58 1");
            AddLabel(container, root, (IsRepeatedPatternMode(profile) ? GetGeneratedReleaseGroupCount(profile) : (profile.PayloadEvents == null ? 0 : profile.PayloadEvents.Count)) + " release group(s)  •  " + GetTotalPayloadUnits(profile) + " total unit(s)", 9, TextAnchor.MiddleLeft, "0.445 0.205", "0.680 0.245", "0.66 0.76 0.82 1");
            AddButton(container, root, "OPEN RELEASES", "airanim.ui.tab releases", "0.500 0.178", "0.680 0.208", "0.16 0.24 0.34 0.96", 8);
        }

        private void AddProfileSettingRow(CuiElementContainer container, string root, string label, string value, string buttonCommand, float y, bool input, string inputField)
        {
            AddPanel(container, root, FormatAnchor(0.255f, y), FormatAnchor(0.695f, y + 0.070f), "0.065 0.075 0.090 0.94");
            AddLabel(container, root, label, 9, TextAnchor.MiddleLeft, FormatAnchor(0.270f, y + 0.018f), FormatAnchor(0.450f, y + 0.055f), "0.72 0.80 0.86 1");
            if (input)
            {
                AddButton(container, root, value, "airanim.valueedit.open profile " + inputField + " full", FormatAnchor(0.455f, y + 0.014f), FormatAnchor(0.575f, y + 0.058f), "0.070 0.082 0.100 0.98", 10);
            }
            else
            {
                AddButton(container, root, value, buttonCommand, FormatAnchor(0.455f, y + 0.014f), FormatAnchor(0.695f, y + 0.058f), "0.12 0.24 0.30 0.96", 10);
            }
        }

        private void AddProfileToolsInspector(CuiElementContainer container, string root, EditorSession session, VisualProfileConfig profile)
        {
            AddPanel(container, root, "0.728 0.085", "0.988 0.885", "0.045 0.052 0.065 0.96");
            AddLabel(container, root, "TOOLS & MAINTENANCE", 13, TextAnchor.MiddleLeft, "0.745 0.840", "0.970 0.875", "0.92 0.96 1 1");
            AddLabel(container, root, "Common editing stays in the tabs; destructive and world tools live here.", 8, TextAnchor.UpperLeft, "0.745 0.790", "0.970 0.830", "0.49 0.58 0.64 1");

            AddButton(container, root, "SET TARGET FROM VIEW", "airanim.ui.target", "0.745 0.720", "0.970 0.770", "0.12 0.31 0.37 0.96", 9);
            AddButton(container, root, "REFRESH WORLD MARKERS", "airanim.ui.markers", "0.745 0.655", "0.970 0.705", "0.16 0.22 0.28 0.96", 9);
            AddButton(container, root, session.ObjectMarkersEnabled ? "OBJECT OUTLINES: ON" : "OBJECT OUTLINES: OFF", "airanim.ui.objects", "0.745 0.590", "0.970 0.640", session.ObjectMarkersEnabled ? "0.14 0.31 0.20 0.96" : "0.30 0.12 0.10 0.96", 9);
            AddButton(container, root, session.TimelineOpen ? "LEGACY TIMELINE: ON" : "OPEN LEGACY TIMELINE", "airanim.ui.timeline", "0.745 0.525", "0.970 0.575", session.TimelineOpen ? "0.30 0.20 0.10 0.98" : "0.16 0.22 0.28 0.96", 8);
            AddButton(container, root, "HELP / COMMANDS", "airanim.ui.tab commands", "0.745 0.460", "0.970 0.510", "0.16 0.22 0.28 0.96", 9);

            AddLabel(container, root, "FILE", 9, TextAnchor.MiddleLeft, "0.745 0.390", "0.850 0.425", "0.72 0.80 0.86 1");
            AddButton(container, root, "SAVE NOW", "airanim.ui.save", "0.745 0.335", "0.852 0.380", HasUnsavedChanges() ? "0.48 0.23 0.10 0.96" : "0.20 0.27 0.22 0.96", 8);
            AddButton(container, root, "RELOAD", "airanim.ui.reload", "0.862 0.335", "0.970 0.380", "0.24 0.17 0.12 0.96", 8);
            AddLabel(container, root, HasUnsavedChanges() ? "Reload requires confirmation because unsaved changes exist." : "No unsaved changes.", 8, TextAnchor.UpperLeft, "0.745 0.285", "0.970 0.325", HasUnsavedChanges() ? "1 0.64 0.42 1" : "0.52 0.62 0.68 1");

            AddLabel(container, root, "DANGER ZONE", 9, TextAnchor.MiddleLeft, "0.745 0.220", "0.870 0.255", "1 0.64 0.48 1");
            AddButton(container, root, "DELETE PROFILE", "airanim.ui.deleteprompt", "0.745 0.155", "0.852 0.205", "0.48 0.10 0.08 0.96", 8);
            AddButton(container, root, "END SESSION", "airanim.ui.endsession", "0.862 0.155", "0.970 0.205", "0.36 0.12 0.10 0.96", 8);
        }

        private void AddWorkspaceStatusBar(CuiElementContainer container, string root, EditorSession session, VisualProfileConfig profile)
        {
            AddPanel(container, root, "0.012 0.015", "0.988 0.070", "0.035 0.040 0.050 0.98");
            var status = string.IsNullOrWhiteSpace(session.LastStatus) ? "Ready." : session.LastStatus;
            var warning = string.IsNullOrWhiteSpace(session.LastWarning) ? "" : "  •  " + session.LastWarning;
            AddLabel(container, root, status + warning, 9, TextAnchor.MiddleLeft, "0.028 0.033", "0.710 0.060", string.IsNullOrWhiteSpace(session.LastWarning) ? "0.68 0.78 0.84 1" : "1 0.66 0.46 1");
            AddLabel(container, root, HasUnsavedChanges() ? "UNSAVED CHANGES" : "Saved to VisualProfiles.json", 8, TextAnchor.MiddleRight, "0.710 0.033", "0.970 0.060", HasUnsavedChanges() ? "1 0.64 0.38 1" : "0.50 0.62 0.68 1");
        }

        private void ShowTimelineUi(BasePlayer player)
        {
            if (player == null || !player.IsConnected || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            if (!session.TimelineOpen || NormalizeEditorTab(session.ActiveTab) != "flight")
            {
                CuiHelper.DestroyUi(player, TimelineUiName);
                return;
            }

            VisualProfileConfig profile = null;
            if (!string.IsNullOrWhiteSpace(session.ProfileId))
            {
                profileFile.Profiles.TryGetValue(session.ProfileId, out profile);
            }

            CuiHelper.DestroyUi(player, TimelineUiName);
            var container = new CuiElementContainer();
            AddTimelineUi(container, player, session, profile);
            RegisterUiBridge(player, TimelineUiName);
            CuiHelper.AddUi(player, container);
        }

        private void ShowWaypointPopupUi(BasePlayer player)
        {
            if (player == null || !player.IsConnected || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var selected = GetSelectedWaypoint(session, profile);
            if (selected == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, WaypointUiName);

            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.035 0.040 0.050 0.965" },
                RectTransform = { AnchorMin = "0.625 0.055", AnchorMax = "0.985 0.565" }
            }, "Overlay", WaypointUiName);

            AddPanel(container, root, "0.030 0.875", "0.970 0.970", "0.09 0.10 0.12 0.96");
            AddLabel(container, root, "Waypoint #" + DisplayIndex(session.SelectedWaypointIndex), 16, TextAnchor.MiddleLeft, "0.055 0.915", "0.650 0.960", "1 0.86 0.58 1");
            AddLabel(container, root, session.ProfileId, 10, TextAnchor.MiddleRight, "0.520 0.915", "0.855 0.960", "0.60 0.68 0.74 1");
            AddButton(container, root, "X", "airanim.wpui.close", "0.875 0.910", "0.945 0.960", "0.55 0.12 0.10 0.95", 14);

            AddLabel(container, root, "t " + FormatSeconds(selected.Time) + "   X " + FormatFloat(selected.X) + "   Y " + FormatFloat(selected.Y) + "   Z " + FormatFloat(selected.Z), 10, TextAnchor.MiddleLeft, "0.055 0.845", "0.735 0.880", "0.82 0.90 0.96 1");
            AddWaypointValueButton(container, root, "DUR", FormatSeconds(GetTimelineSegmentDuration(profile, session.SelectedWaypointIndex)), "airanim.valueedit.open duration selected popup", 0.755f, 0.842f, 0.190f, 0.040f, 8);

            AddLabel(container, root, "Position", 12, TextAnchor.MiddleLeft, "0.055 0.775", "0.250 0.815", "1 1 1 1");
            AddWaypointValueButton(container, root, "X", FormatFloat(selected.X), "airanim.valueedit.open pos x popup", 0.205f, 0.725f);
            AddWaypointValueButton(container, root, "Y", FormatFloat(selected.Y), "airanim.valueedit.open pos y popup", 0.420f, 0.725f);
            AddWaypointValueButton(container, root, "Z", FormatFloat(selected.Z), "airanim.valueedit.open pos z popup", 0.635f, 0.725f);

            AddLabel(container, root, "Rotation", 12, TextAnchor.MiddleLeft, "0.055 0.645", "0.250 0.685", "1 1 1 1");
            AddWaypointValueButton(container, root, "X", FormatDegrees(selected.RotationX), "airanim.valueedit.open rot x popup", 0.205f, 0.595f);
            AddWaypointValueButton(container, root, "Y", FormatDegrees(selected.RotationY), "airanim.valueedit.open rot y popup", 0.420f, 0.595f);
            AddWaypointValueButton(container, root, "Z", FormatDegrees(selected.RotationZ), "airanim.valueedit.open rot z popup", 0.635f, 0.595f);

            AddLabel(container, root, "Move", 11, TextAnchor.MiddleLeft, "0.055 0.505", "0.185 0.540", "1 1 1 1");
            AddButton(container, root, "FWD", "airanim.wpui.nudge forward 1", "0.205 0.500", "0.315 0.545", "0.15 0.25 0.32 0.95", 8);
            AddButton(container, root, "BACK", "airanim.wpui.nudge back 1", "0.325 0.500", "0.435 0.545", "0.15 0.25 0.32 0.95", 8);
            AddButton(container, root, "LEFT", "airanim.wpui.nudge left 1", "0.445 0.500", "0.555 0.545", "0.15 0.25 0.32 0.95", 8);
            AddButton(container, root, "RIGHT", "airanim.wpui.nudge right 1", "0.565 0.500", "0.675 0.545", "0.15 0.25 0.32 0.95", 8);
            AddButton(container, root, "UP", "airanim.wpui.nudge up 1", "0.685 0.500", "0.800 0.545", "0.16 0.30 0.20 0.95", 8);
            AddButton(container, root, "DOWN", "airanim.wpui.nudge down 1", "0.810 0.500", "0.945 0.545", "0.42 0.18 0.12 0.95", 8);

            AddLabel(container, root, "Rotate", 11, TextAnchor.MiddleLeft, "0.055 0.400", "0.185 0.435", "1 1 1 1");
            AddRotationAxisUi(container, root, "X", "x", selected.RotationX, 0.360f);
            AddRotationAxisUi(container, root, "Y", "y", selected.RotationY, 0.290f);
            AddRotationAxisUi(container, root, "Z", "z", selected.RotationZ, 0.220f);

            AddButton(container, root, "PREV", "airanim.wpui.prev", "0.055 0.125", "0.180 0.175", "0.14 0.18 0.22 0.95", 9);
            AddButton(container, root, "NEXT", "airanim.wpui.next", "0.190 0.125", "0.315 0.175", "0.14 0.18 0.22 0.95", 9);
            AddButton(container, root, "MARKERS", "airanim.wpui.markers", "0.330 0.125", "0.505 0.175", "0.18 0.24 0.30 0.95", 9);
            AddButton(container, root, "PREVIEW", "airanim.ui.preview", "0.520 0.125", "0.680 0.175", "0.48 0.16 0.10 0.95", 9);
            AddButton(container, root, "SAVE", "airanim.wpui.save", "0.695 0.125", "0.820 0.175", "0.40 0.18 0.12 0.95", 9);
            AddButton(container, root, "FULL", "airanim.wpui.full", "0.835 0.125", "0.945 0.175", "0.18 0.22 0.28 0.95", 8);

            var status = string.IsNullOrWhiteSpace(session.LastStatus) ? "Ready." : session.LastStatus;
            AddLabel(container, root, status, 9, TextAnchor.MiddleLeft, "0.055 0.045", "0.945 0.095", "0.58 0.66 0.72 1");

            RegisterUiBridge(player, WaypointUiName);
            CuiHelper.AddUi(player, container);
            session.WaypointUiOpen = true;
        }

        private void ShowPayloadReleasePopupUi(BasePlayer player)
        {
            if (player == null || !player.IsConnected || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            NormalizeProfile(session.ProfileId, profile);
            if (profile.PayloadEvents == null || profile.PayloadEvents.Count == 0)
            {
                CuiHelper.DestroyUi(player, ReleaseUiName);
                session.ReleaseUiOpen = false;
                return;
            }

            var ev = GetSelectedPayloadEvent(session, profile);
            if (ev == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, ReleaseUiName);

            var container = new CuiElementContainer();
            var overlay = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0 0 0 0.88" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, "Overlay", ReleaseUiName);
            var root = container.Add(new CuiPanel
            {
                Image = { Color = "0.018 0.022 0.028 1" },
                RectTransform = { AnchorMin = "0.205 0.115", AnchorMax = "0.795 0.885" }
            }, overlay);

            AddPanel(container, root, "0.030 0.895", "0.970 0.970", "0.09 0.10 0.12 0.96");
            AddLabel(container, root, "Release #" + DisplayIndex(session.SelectedPayloadEventIndex), 16, TextAnchor.MiddleLeft, "0.055 0.925", "0.405 0.962", "1 0.86 0.58 1");
            AddLabel(container, root, session.ProfileId + " | " + GetPayloadDisplay(ev.Payload), 10, TextAnchor.MiddleRight, "0.405 0.925", "0.855 0.962", "0.60 0.68 0.74 1");
            AddButton(container, root, "X", "airanim.release.close", "0.875 0.920", "0.945 0.962", "0.55 0.12 0.10 0.95", 14);

            AddReleaseValueButton(container, root, "TIME", FormatSeconds(ev.Time), "time", 0.055f, 0.835f);
            AddReleaseValueButton(container, root, "COUNT", Math.Max(1, ev.Count).ToString(CultureInfo.InvariantCulture), "count", 0.275f, 0.835f);
            AddLabel(container, root, "PAYLOAD", 8, TextAnchor.MiddleCenter, "0.495 0.835", "0.555 0.880", "0.72 0.80 0.86 1");
            AddButton(container, root, ShortenText(GetPayloadDisplay(ev.Payload), 18), "airanim.release.payload", "0.560 0.835", "0.945 0.880", "0.055 0.070 0.085 0.96", 8);

            AddLabel(container, root, "Carrier Offset", 11, TextAnchor.MiddleLeft, "0.055 0.780", "0.320 0.815", "1 1 1 1");
            AddReleaseValueButton(container, root, "X", FormatFloat(ev.CarrierOffsetX), "carrierx", 0.055f, 0.730f);
            AddReleaseValueButton(container, root, "Y", FormatFloat(ev.CarrierOffsetY), "carriery", 0.275f, 0.730f);
            AddReleaseValueButton(container, root, "Z", FormatFloat(ev.CarrierOffsetZ), "carrierz", 0.495f, 0.730f);

            AddLabel(container, root, "Target Offset", 11, TextAnchor.MiddleLeft, "0.055 0.675", "0.320 0.710", "1 1 1 1");
            AddReleaseValueButton(container, root, "X", FormatFloat(ev.TargetOffsetX), "targetx", 0.055f, 0.625f);
            AddReleaseValueButton(container, root, "Y", FormatFloat(ev.TargetOffsetY), "targety", 0.275f, 0.625f);
            AddReleaseValueButton(container, root, "Z", FormatFloat(ev.TargetOffsetZ), "targetz", 0.495f, 0.625f);

            AddLabel(container, root, "Flight", 11, TextAnchor.MiddleLeft, "0.055 0.570", "0.320 0.605", "1 1 1 1");
            AddReleaseValueButton(container, root, "SPREAD", FormatOptionalFloat(ev.SpreadRadius), "spread", 0.055f, 0.520f);
            AddReleaseValueButton(container, root, "SPEED", FormatOptionalFloat(ev.LaunchSpeed), "speed", 0.275f, 0.520f);
            AddReleaseValueButton(container, root, "FUSE", FormatOptionalFloat(ev.FuseSeconds), "fuse", 0.495f, 0.520f);

            AddLabel(container, root, "Balance", 11, TextAnchor.MiddleLeft, "0.055 0.465", "0.320 0.500", "1 1 1 1");
            AddReleaseValueButton(container, root, "DMG", FormatFloat(ev.DamageScale), "damage", 0.055f, 0.415f);
            AddReleaseValueButton(container, root, "VEH", FormatOptionalFloat(ev.VehicleDamageScale), "vehiclescale", 0.275f, 0.415f);
            AddReleaseValueButton(container, root, "SPLASH", FormatOptionalFloat(ev.SplashRadius), "splash", 0.495f, 0.415f);
            AddReleaseValueButton(container, root, "IMPACT", FormatOptionalFloat(ev.ImpactRadius), "impact", 0.715f, 0.415f);

            AddLabel(container, root, "Tracking", 11, TextAnchor.MiddleLeft, "0.055 0.360", "0.320 0.395", "1 1 1 1");
            AddReleaseValueButton(container, root, "SECS", FormatOptionalFloat(ev.MaxTrackingSeconds), "trackingseconds", 0.055f, 0.310f);
            AddReleaseValueButton(container, root, "DIST", FormatOptionalFloat(ev.MaxTrackingDistance), "trackingdistance", 0.275f, 0.310f);

            AddLabel(container, root, "Damage Scales", 11, TextAnchor.MiddleLeft, "0.055 0.255", "0.320 0.290", "1 1 1 1");
            AddReleaseValueButton(container, root, "PLY", FormatFloat(GetPayloadDamageScale(ev, "Players")), "d_players", 0.055f, 0.205f);
            AddReleaseValueButton(container, root, "BLD", FormatFloat(GetPayloadDamageScale(ev, "Buildings")), "d_buildings", 0.225f, 0.205f);
            AddReleaseValueButton(container, root, "VEH", FormatFloat(GetPayloadDamageScale(ev, "Vehicles")), "d_vehicles", 0.395f, 0.205f);
            AddReleaseValueButton(container, root, "TUR", FormatFloat(GetPayloadDamageScale(ev, "Turrets")), "d_turrets", 0.565f, 0.205f);
            AddReleaseValueButton(container, root, "DEP", FormatFloat(GetPayloadDamageScale(ev, "Deployables")), "d_deployables", 0.735f, 0.205f);

            AddButton(container, root, "PREV", "airanim.release.prev", "0.055 0.125", "0.170 0.175", "0.14 0.18 0.22 0.95", 9);
            AddButton(container, root, "NEXT", "airanim.release.next", "0.180 0.125", "0.295 0.175", "0.14 0.18 0.22 0.95", 9);
            AddButton(container, root, "DUPLICATE", "airanim.release.dup", "0.310 0.125", "0.455 0.175", "0.16 0.30 0.20 0.95", 8);
            AddButton(container, root, "DELETE", "airanim.release.delete", "0.470 0.125", "0.595 0.175", "0.50 0.12 0.10 0.95", 8);
            AddButton(container, root, "OPEN EDITOR", "airanim.wpui.full", "0.610 0.125", "0.770 0.175", "0.18 0.24 0.30 0.95", 8);
            AddButton(container, root, "DONE", "airanim.release.close", "0.785 0.125", "0.945 0.175", "0.18 0.22 0.28 0.95", 9);

            var status = string.IsNullOrWhiteSpace(session.LastStatus) ? "Ready." : session.LastStatus;
            AddLabel(container, root, status, 9, TextAnchor.MiddleLeft, "0.055 0.045", "0.945 0.095", "0.58 0.66 0.72 1");

            RegisterUiBridge(player, ReleaseUiName);
            CuiHelper.AddUi(player, container);
            session.ReleaseUiOpen = true;
        }


        private void ShowPatternTemplatePopupUi(BasePlayer player)
        {
            if (player == null || !player.IsConnected || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            if (profile.ReleaseTemplate == null)
            {
                profile.ReleaseTemplate = new VisualPayloadEvent();
            }

            var template = profile.ReleaseTemplate;
            CuiHelper.DestroyUi(player, ReleaseUiName);
            var container = new CuiElementContainer();
            var overlay = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0 0 0 0.88" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, "Overlay", ReleaseUiName);
            var root = container.Add(new CuiPanel
            {
                Image = { Color = "0.018 0.022 0.028 1" },
                RectTransform = { AnchorMin = "0.205 0.095", AnchorMax = "0.795 0.905" }
            }, overlay);

            AddPanel(container, root, "0.025 0.905", "0.975 0.975", "0.075 0.085 0.105 0.98");
            AddLabel(container, root, "Repeated Pattern Template", 16, TextAnchor.MiddleLeft, "0.050 0.930", "0.650 0.965", "1 0.86 0.58 1");
            AddLabel(container, root, "Click any value to open the keypad", 8, TextAnchor.MiddleRight, "0.520 0.930", "0.865 0.965", "0.52 0.61 0.68 1");
            AddButton(container, root, "X", "airanim.pattern.closepopup", "0.885 0.925", "0.950 0.965", "0.55 0.12 0.10 0.96", 13);

            AddLabel(container, root, "ORDNANCE", 8, TextAnchor.MiddleLeft, "0.050 0.855", "0.200 0.885", "0.66 0.74 0.80 1");
            AddButton(container, root, ShortenText(GetPayloadDisplay(template.Payload), 30), "airanim.pattern.payload", "0.200 0.845", "0.690 0.890", "0.070 0.082 0.100 0.98", 9);
            AddPatternPopupInput(container, root, "UNITS / RELEASE", Math.Max(1, template.Count).ToString(CultureInfo.InvariantCulture), "units", 0.710f, 0.835f, 0.240f);

            AddLabel(container, root, "CARRIER OFFSET", 10, TextAnchor.MiddleLeft, "0.050 0.785", "0.300 0.818", "0.90 0.94 0.98 1");
            AddPatternPopupInput(container, root, "X", FormatFloat(template.CarrierOffsetX), "carrierx", 0.050f, 0.725f, 0.275f);
            AddPatternPopupInput(container, root, "Y", FormatFloat(template.CarrierOffsetY), "carriery", 0.350f, 0.725f, 0.275f);
            AddPatternPopupInput(container, root, "Z", FormatFloat(template.CarrierOffsetZ), "carrierz", 0.650f, 0.725f, 0.300f);

            AddLabel(container, root, "TARGET OFFSET", 10, TextAnchor.MiddleLeft, "0.050 0.665", "0.300 0.698", "0.90 0.94 0.98 1");
            AddPatternPopupInput(container, root, "X", FormatFloat(template.TargetOffsetX), "targetx", 0.050f, 0.605f, 0.275f);
            AddPatternPopupInput(container, root, "Y", FormatFloat(template.TargetOffsetY), "targety", 0.350f, 0.605f, 0.275f);
            AddPatternPopupInput(container, root, "Z", FormatFloat(template.TargetOffsetZ), "targetz", 0.650f, 0.605f, 0.300f);

            AddLabel(container, root, "FLIGHT / DELIVERY", 10, TextAnchor.MiddleLeft, "0.050 0.545", "0.300 0.578", "0.90 0.94 0.98 1");
            AddPatternPopupInput(container, root, "SPREAD", FormatFloat(template.SpreadRadius), "spread", 0.050f, 0.485f, 0.275f);
            AddPatternPopupInput(container, root, "SPEED", FormatFloat(template.LaunchSpeed), "speed", 0.350f, 0.485f, 0.275f);
            AddPatternPopupInput(container, root, "FUSE", FormatFloat(template.FuseSeconds), "fuse", 0.650f, 0.485f, 0.300f);

            AddLabel(container, root, "BALANCE", 10, TextAnchor.MiddleLeft, "0.050 0.425", "0.300 0.458", "0.90 0.94 0.98 1");
            AddPatternPopupInput(container, root, "DAMAGE", FormatFloat(template.DamageScale), "damage", 0.050f, 0.365f, 0.205f);
            AddPatternPopupInput(container, root, "VEHICLE", FormatFloat(template.VehicleDamageScale), "vehiclescale", 0.275f, 0.365f, 0.205f);
            AddPatternPopupInput(container, root, "SPLASH", FormatFloat(template.SplashRadius), "splash", 0.500f, 0.365f, 0.205f);
            AddPatternPopupInput(container, root, "IMPACT", FormatFloat(template.ImpactRadius), "impact", 0.725f, 0.365f, 0.225f);

            AddLabel(container, root, "TRACKING", 10, TextAnchor.MiddleLeft, "0.050 0.305", "0.300 0.338", "0.90 0.94 0.98 1");
            AddPatternPopupInput(container, root, "SECONDS", FormatFloat(template.MaxTrackingSeconds), "trackingseconds", 0.050f, 0.245f, 0.425f);
            AddPatternPopupInput(container, root, "DISTANCE", FormatFloat(template.MaxTrackingDistance), "trackingdistance", 0.525f, 0.245f, 0.425f);

            AddLabel(container, root, "DAMAGE SCALES", 10, TextAnchor.MiddleLeft, "0.050 0.185", "0.300 0.218", "0.90 0.94 0.98 1");
            AddPatternPopupInput(container, root, "PLAYERS", FormatFloat(GetPayloadDamageScale(template, "Players")), "d_players", 0.050f, 0.125f, 0.170f);
            AddPatternPopupInput(container, root, "BUILDINGS", FormatFloat(GetPayloadDamageScale(template, "Buildings")), "d_buildings", 0.235f, 0.125f, 0.170f);
            AddPatternPopupInput(container, root, "VEHICLES", FormatFloat(GetPayloadDamageScale(template, "Vehicles")), "d_vehicles", 0.420f, 0.125f, 0.170f);
            AddPatternPopupInput(container, root, "TURRETS", FormatFloat(GetPayloadDamageScale(template, "Turrets")), "d_turrets", 0.605f, 0.125f, 0.160f);
            AddPatternPopupInput(container, root, "DEPLOY", FormatFloat(GetPayloadDamageScale(template, "Deployables")), "d_deployables", 0.780f, 0.125f, 0.170f);

            AddLabel(container, root, "Use -1 for automatic/default optional values.", 8, TextAnchor.MiddleLeft, "0.050 0.055", "0.600 0.095", "0.50 0.59 0.66 1");
            AddButton(container, root, "DONE", "airanim.pattern.closepopup", "0.760 0.045", "0.950 0.095", "0.18 0.26 0.31 0.96", 9);

            RegisterUiBridge(player, ReleaseUiName);
            CuiHelper.AddUi(player, container);
            session.ReleaseUiOpen = true;
            session.PatternTemplateUiOpen = true;
        }

        private void AddPatternPopupInput(CuiElementContainer container, string root, string label, string value, string field, float x, float y, float width)
        {
            AddLabel(container, root, label, 7, TextAnchor.MiddleLeft, FormatAnchor(x, y + 0.038f), FormatAnchor(x + width, y + 0.060f), "0.56 0.65 0.72 1");
            AddButton(container, root, value, "airanim.valueedit.open pattern " + field + " popup", FormatAnchor(x, y), FormatAnchor(x + width, y + 0.038f), "0.070 0.082 0.100 0.98", 9);
        }

        private bool IsGenericValueEdit(PendingValueEdit edit)
        {
            return edit != null && !string.IsNullOrWhiteSpace(edit.GenericScope);
        }

        private string GetFriendlyValueEditFieldName(PendingValueEdit edit, VisualProfileConfig profile, int index)
        {
            if (edit == null)
            {
                return "Value";
            }

            if (IsGenericValueEdit(edit))
            {
                var scope = edit.GenericScope.Trim().ToLowerInvariant();
                var field = (edit.GenericField ?? "").Trim().ToLowerInvariant();
                if (scope == "waypointtime") return "Waypoint Time";
                if (scope == "profile")
                {
                    if (field == "duration") return "Profile Duration";
                    if (field == "smooth") return "Rotation Smooth Time";
                    if (field == "clearance") return "Terrain Clearance";
                    if (field == "firstpayload") return "First Payload Time";
                }

                if (scope == "pattern")
                {
                    if (field == "start" || field == "time") return "Pattern Start Time";
                    if (field == "interval") return "Pattern Interval";
                    if (field == "units" || field == "count") return "Units Per Release";
                    if (field == "total") return "Total Units";
                    if (field == "groups") return "Release Groups";
                    return "Template " + field.Replace("d_", "Damage ").Replace("_", " ");
                }
            }

            var axisLabel = string.IsNullOrWhiteSpace(edit.Axis) ? "" : edit.Axis.ToUpperInvariant();
            return edit.ReleaseEvent ? "Release " + edit.ReleaseField
                : edit.Duration ? GetWaypointDurationLabel(profile, index)
                : edit.Rotation ? "Rotation " + axisLabel
                : "Position " + axisLabel;
        }

        private float GetGenericValueEditCurrentValue(PendingValueEdit edit, VisualProfileConfig profile, VisualProfileWaypoint waypoint)
        {
            if (edit == null || profile == null)
            {
                return 0f;
            }

            var scope = (edit.GenericScope ?? "").Trim().ToLowerInvariant();
            var field = (edit.GenericField ?? "").Trim().ToLowerInvariant();
            if (scope == "waypointtime")
            {
                return waypoint == null ? 0f : waypoint.Time;
            }

            if (scope == "profile")
            {
                switch (field)
                {
                    case "duration": return profile.DurationSeconds;
                    case "firstpayload": return profile.FirstPayloadDelaySeconds;
                    case "smooth": return profile.RotationSmoothTimeSeconds;
                    case "clearance": return profile.MinimumTerrainClearance;
                }
            }

            if (scope == "pattern")
            {
                if (profile.ReleaseTemplate == null)
                {
                    profile.ReleaseTemplate = new VisualPayloadEvent();
                }

                switch (field)
                {
                    case "start":
                    case "time": return profile.FirstPayloadDelaySeconds;
                    case "interval": return profile.PayloadReleaseIntervalSeconds;
                    case "units":
                    case "count": return Math.Max(1, profile.ReleaseTemplate.Count);
                    case "total": return Math.Max(0, profile.MaxPayloadCount);
                    case "groups": return GetGeneratedReleaseGroupCount(profile);
                    default: return GetPayloadReleaseNumericField(profile.ReleaseTemplate, field);
                }
            }

            return 0f;
        }

        private string FormatPendingValueEditCurrent(PendingValueEdit edit, VisualProfileConfig profile, VisualProfileWaypoint waypoint, int index)
        {
            if (IsGenericValueEdit(edit))
            {
                var field = (edit.GenericField ?? "").Trim().ToLowerInvariant();
                var value = GetGenericValueEditCurrentValue(edit, profile, waypoint);
                return field == "units" || field == "count" || field == "total" || field == "groups"
                    ? Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture)
                    : FormatFloat(value);
            }

            return edit.ReleaseEvent ? FormatFloat(GetPayloadReleaseNumericField(edit.PayloadEvent, edit.ReleaseField))
                : edit.Duration ? FormatSeconds(GetTimelineSegmentDuration(profile, index))
                : edit.Rotation ? FormatDegrees(GetWaypointRotationAxis(waypoint, edit.Axis))
                : FormatFloat(GetWaypointCoordinate(waypoint, edit.Axis));
        }

        private string GetPendingValueEditSubject(PendingValueEdit edit, EditorSession session, int index)
        {
            if (IsGenericValueEdit(edit))
            {
                var scope = (edit.GenericScope ?? "").Trim().ToLowerInvariant();
                if (scope == "profile") return "Profile: " + session.ProfileId;
                if (scope == "pattern") return "Repeated pattern: " + session.ProfileId;
                if (scope == "waypointtime") return "Waypoint #" + DisplayIndex(index);
            }

            return (edit.ReleaseEvent ? "Release #" : "Waypoint #") + DisplayIndex(index);
        }

        private bool ApplyGenericValueEdit(PendingValueEdit edit, VisualProfileConfig profile, VisualProfileWaypoint waypoint, float value)
        {
            if (!IsGenericValueEdit(edit) || profile == null)
            {
                return false;
            }

            var scope = edit.GenericScope.Trim().ToLowerInvariant();
            var field = (edit.GenericField ?? "").Trim().ToLowerInvariant();
            if (scope == "waypointtime")
            {
                if (waypoint == null) return false;
                waypoint.Time = value;
                return true;
            }

            if (scope == "profile")
            {
                SetProfileFloat(profile, field, value);
                return true;
            }

            if (scope != "pattern")
            {
                return false;
            }

            if (profile.ReleaseTemplate == null)
            {
                profile.ReleaseTemplate = new VisualPayloadEvent();
            }

            switch (field)
            {
                case "start":
                case "time":
                    profile.FirstPayloadDelaySeconds = Mathf.Clamp(value, 0f, Mathf.Max(0.1f, profile.DurationSeconds));
                    break;
                case "interval":
                    profile.PayloadReleaseIntervalSeconds = Mathf.Clamp(value, 0.05f, 30f);
                    break;
                case "units":
                case "count":
                    profile.ReleaseTemplate.Count = Mathf.Clamp(Mathf.RoundToInt(value), 1, 1000);
                    break;
                case "total":
                    profile.MaxPayloadCount = Mathf.Clamp(Mathf.RoundToInt(value), 0, 1000);
                    break;
                case "groups":
                    var groups = Mathf.Clamp(Mathf.RoundToInt(value), 0, MaxGeneratedReleaseGroups);
                    profile.MaxPayloadCount = Mathf.Clamp(groups * Math.Max(1, profile.ReleaseTemplate.Count), 0, 1000);
                    break;
                default:
                    SetPayloadReleaseNumericField(profile.ReleaseTemplate, field, value);
                    break;
            }

            profile.PayloadReleaseMode = "generated";
            return true;
        }

        private void ShowValueEditUi(BasePlayer player)
        {
            if (player == null || !player.IsConnected || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            PendingValueEdit edit;
            VisualProfileConfig profile;
            VisualProfileWaypoint waypoint;
            int index;
            if (!TryGetPendingValueEditContext(player, session, out edit, out profile, out waypoint, out index))
            {
                CuiHelper.DestroyUi(player, ValueEditUiName);
                ClearPendingValueEdit(session);
                return;
            }

            var fieldLabel = GetFriendlyValueEditFieldName(edit, profile, index);
            var currentValue = FormatPendingValueEditCurrent(edit, profile, waypoint, index);
            var draftValue = edit.HasDraft ? edit.DraftValue : "";

            CuiHelper.DestroyUi(player, ValueEditUiName);
            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.025 0.030 0.038 0.995" },
                RectTransform = { AnchorMin = "0.350 0.235", AnchorMax = "0.650 0.735" }
            }, "Overlay", ValueEditUiName);

            AddPanel(container, root, "0.045 0.835", "0.955 0.955", "0.09 0.10 0.12 1");
            AddLabel(container, root, "Edit " + fieldLabel, 16, TextAnchor.MiddleLeft, "0.075 0.890", "0.720 0.940", "1 0.86 0.58 1");
            AddButton(container, root, "X", "airanim.valueedit.cancel", "0.840 0.885", "0.925 0.940", "0.55 0.12 0.10 1", 14);

            AddLabel(container, root, GetPendingValueEditSubject(edit, session, index) + "   Current: " + currentValue, 11, TextAnchor.MiddleLeft, "0.075 0.765", "0.925 0.825", "0.82 0.90 0.96 1");
            AddLabel(container, root, "New value", 10, TextAnchor.MiddleLeft, "0.075 0.670", "0.320 0.725", "0.72 0.80 0.86 1");
            AddPanel(container, root, "0.315 0.660", "0.925 0.735", "0.055 0.070 0.085 1");
            AddLabel(container, root, string.IsNullOrWhiteSpace(draftValue) ? "(empty)" : draftValue, 15, TextAnchor.MiddleLeft, "0.345 0.668", "0.895 0.725", string.IsNullOrWhiteSpace(draftValue) ? "0.50 0.58 0.64 1" : "0.94 0.97 1 1");

            AddValueEditKeyButton(container, root, "7", "7", 0.075f, 0.540f);
            AddValueEditKeyButton(container, root, "8", "8", 0.245f, 0.540f);
            AddValueEditKeyButton(container, root, "9", "9", 0.415f, 0.540f);
            AddValueEditKeyButton(container, root, "DEL", "back", 0.585f, 0.540f);

            AddValueEditKeyButton(container, root, "4", "4", 0.075f, 0.435f);
            AddValueEditKeyButton(container, root, "5", "5", 0.245f, 0.435f);
            AddValueEditKeyButton(container, root, "6", "6", 0.415f, 0.435f);
            AddValueEditKeyButton(container, root, "CLR", "clear", 0.585f, 0.435f);

            AddValueEditKeyButton(container, root, "1", "1", 0.075f, 0.330f);
            AddValueEditKeyButton(container, root, "2", "2", 0.245f, 0.330f);
            AddValueEditKeyButton(container, root, "3", "3", 0.415f, 0.330f);
            AddValueEditKeyButton(container, root, "CUR", "current", 0.585f, 0.330f);

            AddValueEditKeyButton(container, root, "-", "minus", 0.075f, 0.225f);
            AddValueEditKeyButton(container, root, "0", "0", 0.245f, 0.225f);
            AddValueEditKeyButton(container, root, ".", "dot", 0.415f, 0.225f);
            AddButton(container, root, "APPLY", "airanim.valueedit.apply", "0.585 0.225", "0.925 0.305", "0.40 0.18 0.12 1", 12);

            AddButton(container, root, "CANCEL", "airanim.valueedit.cancel", "0.075 0.115", "0.925 0.190", "0.18 0.22 0.28 1", 12);

            var guidance = edit.HasDraft
                ? "Draft is server-built from keypad buttons. Click APPLY to commit the value."
                : "Use the keypad buttons, then click APPLY.";
            var warning = string.IsNullOrWhiteSpace(session.LastWarning) ? guidance : session.LastWarning;
            AddLabel(container, root, warning, 9, TextAnchor.MiddleLeft, "0.075 0.040", "0.925 0.095", string.IsNullOrWhiteSpace(session.LastWarning) ? "0.58 0.66 0.72 1" : "1 0.66 0.48 1");

            RegisterUiBridge(player, ValueEditUiName);
            CuiHelper.AddUi(player, container);
            session.ValueEditUiOpen = true;
        }

        private void AddValueEditKeyButton(CuiElementContainer container, string root, string text, string token, float x, float y)
        {
            var width = token == "current" ? 0.340f : 0.150f;
            AddButton(container, root, text, "airanim.valueedit.key " + token, FormatAnchor(x, y), FormatAnchor(x + width, y + 0.080f), "0.14 0.18 0.22 0.95", 12);
        }

        private void AddRotationAxisUi(CuiElementContainer container, string root, string label, string axis, float value, float y)
        {
            AddLabel(container, root, label + " " + FormatDegrees(value), 10, TextAnchor.MiddleLeft, FormatAnchor(0.055f, y + 0.012f), FormatAnchor(0.235f, y + 0.067f), "0.82 0.90 0.96 1");
            AddButton(container, root, "-15", "airanim.wpui.rotate " + axis + " -" + FormatFloat(WaypointRotationLargeStepDegrees), FormatAnchor(0.245f, y), FormatAnchor(0.360f, y + 0.065f), "0.36 0.12 0.10 0.95", 9);
            AddButton(container, root, "-5", "airanim.wpui.rotate " + axis + " -" + FormatFloat(WaypointRotationStepDegrees), FormatAnchor(0.370f, y), FormatAnchor(0.485f, y + 0.065f), "0.42 0.18 0.12 0.95", 9);
            AddButton(container, root, "+5", "airanim.wpui.rotate " + axis + " " + FormatFloat(WaypointRotationStepDegrees), FormatAnchor(0.500f, y), FormatAnchor(0.615f, y + 0.065f), "0.16 0.30 0.20 0.95", 9);
            AddButton(container, root, "+15", "airanim.wpui.rotate " + axis + " " + FormatFloat(WaypointRotationLargeStepDegrees), FormatAnchor(0.625f, y), FormatAnchor(0.740f, y + 0.065f), "0.12 0.24 0.30 0.95", 9);
            AddButton(container, root, "0", "airanim.wpui.rotate " + axis + " reset", FormatAnchor(0.755f, y), FormatAnchor(0.845f, y + 0.065f), "0.18 0.22 0.28 0.95", 9);
            AddButton(container, root, "180", "airanim.wpui.rotate " + axis + " 180", FormatAnchor(0.855f, y), FormatAnchor(0.945f, y + 0.065f), "0.18 0.22 0.28 0.95", 9);
        }

        private void AddWaypointValueButton(CuiElementContainer container, string root, string label, string value, string command, float x, float y, float width = 0.180f, float height = 0.052f, int size = 9)
        {
            var labelWidth = Mathf.Clamp(width * 0.24f, 0.020f, 0.042f);
            AddLabel(container, root, label, size, TextAnchor.MiddleCenter, FormatAnchor(x, y), FormatAnchor(x + labelWidth, y + height), "0.72 0.80 0.86 1");
            AddButton(container, root, value, command, FormatAnchor(x + labelWidth + 0.006f, y), FormatAnchor(x + width, y + height), "0.055 0.070 0.085 0.96", size);
        }

        private void AddReleaseValueButton(CuiElementContainer container, string root, string label, string value, string field, float x, float y, float width = 0.200f, float height = 0.045f, int size = 8)
        {
            var labelWidth = Mathf.Clamp(width * 0.34f, 0.040f, 0.072f);
            AddLabel(container, root, label, size, TextAnchor.MiddleCenter, FormatAnchor(x, y), FormatAnchor(x + labelWidth, y + height), "0.72 0.80 0.86 1");
            AddButton(container, root, ShortenText(value, 12), "airanim.valueedit.open release " + field + " popup", FormatAnchor(x + labelWidth + 0.006f, y), FormatAnchor(x + width, y + height), "0.055 0.070 0.085 0.96", size);
        }

        private void AddProfileListUi(CuiElementContainer container, string root, BasePlayer player, EditorSession session)
        {
            AddPanel(container, root, "0.018 0.130", "0.315 0.858", "0.055 0.065 0.080 0.94");
            AddLabel(container, root, "Profiles", 15, TextAnchor.MiddleLeft, "0.035 0.815", "0.180 0.852", "1 1 1 1");
            AddLabel(container, root, CountProfiles() + " loaded", 10, TextAnchor.MiddleRight, "0.178 0.815", "0.300 0.852", "0.60 0.68 0.74 1");
            AddButton(container, root, "SAVE", "airanim.ui.save", "0.035 0.770", "0.125 0.807", "0.40 0.18 0.12 0.95", 10);
            AddButton(container, root, "RELOAD", "airanim.ui.reload", "0.135 0.770", "0.225 0.807", "0.18 0.22 0.28 0.95", 10);
            AddButton(container, root, "NEW F15", "airanim.ui.quickcreate f15", "0.235 0.770", "0.300 0.807", "0.16 0.30 0.20 0.95", 9);

            var ids = GetSortedProfileIds();
            var contentHeight = Math.Max(410f, 8f + Math.Min(MaxProfilesInUi, ids.Count) * 52f);
            var scroll = AddScrollView(container, root, "0.035 0.148", "0.300 0.758", contentHeight, true);
            var count = Math.Min(MaxProfilesInUi, ids.Count);
            for (var i = 0; i < count; i++)
            {
                var id = ids[i];
                VisualProfileConfig profile;
                profileFile.Profiles.TryGetValue(id, out profile);
                var top = 8f + i * 52f;
                var bottom = top + 46f;
                var selected = string.Equals(session.ProfileId, id, StringComparison.OrdinalIgnoreCase);
                var row = AddOffsetPanel(container, scroll, top, bottom, selected ? "0.24 0.16 0.10 0.95" : "0.10 0.12 0.145 0.90");
                AddLabel(container, row, id, 10, TextAnchor.MiddleLeft, "0.035 0.50", "0.68 0.93", selected ? "1 0.86 0.55 1" : "0.92 0.96 1 1");
                var meta = profile == null ? "missing" : profile.Vehicle + " • " + FormatSeconds(profile.DurationSeconds) + " • " + (profile.Waypoints == null ? 0 : profile.Waypoints.Count) + " wp";
                AddLabel(container, row, meta, 9, TextAnchor.MiddleLeft, "0.035 0.08", "0.68 0.48", "0.60 0.68 0.74 1");
                AddButton(container, row, "EDIT", "airanim.ui.edit " + id, "0.705 0.54", "0.965 0.92", selected ? "0.50 0.20 0.10 0.95" : "0.18 0.24 0.30 0.95", 8);
                AddButton(container, row, "PREVIEW", "airanim.ui.preview " + id, "0.705 0.08", "0.965 0.46", "0.16 0.34 0.40 0.95", 8);
            }
        }

        private void AddProfileDetailsUi(CuiElementContainer container, string root, BasePlayer player, EditorSession session, VisualProfileConfig profile)
        {
            AddPanel(container, root, "0.333 0.600", "0.982 0.858", "0.055 0.065 0.080 0.94");
            AddLabel(container, root, "Active Profile", 15, TextAnchor.MiddleLeft, "0.350 0.817", "0.520 0.852", "1 1 1 1");

            if (profile == null)
            {
                AddLabel(container, root, "No profile selected. Choose one from the list, or use /airanim create <id> <vehicle>.", 12, TextAnchor.MiddleLeft, "0.350 0.735", "0.945 0.785", "0.72 0.78 0.84 1");
                AddLabel(container, root, "Starter profiles are created automatically on first load.", 11, TextAnchor.MiddleLeft, "0.350 0.685", "0.945 0.725", "0.56 0.64 0.70 1");
                return;
            }

            var selected = GetSelectedWaypoint(session, profile);
            var targetText = session.HasTarget ? FormatPosition(session.Target) : "not set";
            var approachText = FormatVectorShort(session.Approach);
            AddLabel(container, root, session.ProfileId, 13, TextAnchor.MiddleLeft, "0.350 0.775", "0.645 0.810", "1 0.86 0.58 1");
            AddLabel(container, root, "Target: " + targetText + "  •  Approach: " + approachText, 10, TextAnchor.MiddleLeft, "0.350 0.743", "0.945 0.772", "0.62 0.70 0.76 1");

            AddMetricCard(container, root, "VEHICLE", profile.Vehicle, "airanim.ui.vehicle next", "0.350 0.660", "0.472 0.730", "0.14 0.18 0.22 0.95");
            AddMetricCard(container, root, "DURATION", FormatSeconds(profile.DurationSeconds), "airanim.ui.profiledelta duration 1", "0.482 0.660", "0.604 0.730", "0.14 0.18 0.22 0.95");
            AddMetricCard(container, root, "FIRST PAYLOAD", FormatSeconds(profile.FirstPayloadDelaySeconds), "airanim.ui.profiledelta firstpayload 0.25", "0.614 0.660", "0.736 0.730", "0.14 0.18 0.22 0.95");
            AddMetricCard(container, root, "SMOOTH", FormatFloat(profile.RotationSmoothTimeSeconds) + "s", "airanim.ui.profiledelta smooth 0.05", "0.746 0.660", "0.852 0.730", "0.14 0.18 0.22 0.95");
            AddMetricCard(container, root, "CLEARANCE", FormatMeters(profile.MinimumTerrainClearance), "airanim.ui.profiledelta clearance 5", "0.862 0.660", "0.965 0.730", "0.14 0.18 0.22 0.95");

            AddButton(container, root, profile.StopAtWaypoints ? "STOP WP ON" : "STOP WP OFF", "airanim.ui.stopwaypoints", "0.350 0.618", "0.445 0.648", profile.StopAtWaypoints ? "0.32 0.20 0.10 0.95" : "0.12 0.28 0.34 0.95", 8);
            AddButton(container, root, "Dur -1", "airanim.ui.profiledelta duration -1", "0.450 0.618", "0.515 0.648", "0.12 0.15 0.19 0.95", 9);
            AddButton(container, root, "Dur +1", "airanim.ui.profiledelta duration 1", "0.520 0.618", "0.585 0.648", "0.12 0.15 0.19 0.95", 9);
            AddButton(container, root, "Payload -0.25", "airanim.ui.profiledelta firstpayload -0.25", "0.595 0.618", "0.695 0.648", "0.12 0.15 0.19 0.95", 9);
            AddButton(container, root, "Payload +0.25", "airanim.ui.profiledelta firstpayload 0.25", "0.700 0.618", "0.800 0.648", "0.12 0.15 0.19 0.95", 9);
            AddButton(container, root, "Clear -5", "airanim.ui.profiledelta clearance -5", "0.810 0.618", "0.885 0.648", "0.12 0.15 0.19 0.95", 9);
            AddButton(container, root, "Clear +5", "airanim.ui.profiledelta clearance 5", "0.890 0.618", "0.965 0.648", "0.12 0.15 0.19 0.95", 9);

            if (selected != null)
            {
                var world = LocalToWorld(session, selected);
                world = EnsurePositionAboveTerrain(world, GetProfileClearance(profile));
                AddLabel(container, root, "Selected waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + ":  t=" + FormatSeconds(selected.Time) + "  X=" + FormatFloat(selected.X) + "  Y=" + FormatFloat(selected.Y) + "  Z=" + FormatFloat(selected.Z), 11, TextAnchor.MiddleLeft, "0.350 0.585", "0.800 0.612", "0.82 0.90 0.96 1");
                AddLabel(container, root, "World preview: " + FormatPosition(world), 10, TextAnchor.MiddleRight, "0.690 0.585", "0.965 0.612", "0.56 0.64 0.70 1");
            }
        }

        private void AddMetricCard(CuiElementContainer container, string root, string label, string value, string command, string anchorMin, string anchorMax, string color)
        {
            AddPanel(container, root, anchorMin, anchorMax, color);
            var min = SplitAnchor(anchorMin);
            var max = SplitAnchor(anchorMax);
            var xPad = 0.008f;
            AddLabel(container, root, label, 8, TextAnchor.MiddleCenter, FormatAnchor(min.x + xPad, min.y + 0.040f), FormatAnchor(max.x - xPad, max.y - 0.006f), "0.60 0.68 0.74 1");
            AddButton(container, root, value, command, FormatAnchor(min.x + xPad, min.y + 0.006f), FormatAnchor(max.x - xPad, min.y + 0.040f), "0 0 0 0", 11);
        }

        private void AddWaypointListUi(CuiElementContainer container, string root, EditorSession session, VisualProfileConfig profile)
        {
            AddPanel(container, root, "0.333 0.224", "0.675 0.585", "0.055 0.065 0.080 0.94");
            AddLabel(container, root, "Waypoints", 15, TextAnchor.MiddleLeft, "0.350 0.545", "0.470 0.578", "1 1 1 1");

            if (profile == null)
            {
                AddLabel(container, root, "Select a profile to view and edit waypoints.", 11, TextAnchor.MiddleCenter, "0.350 0.385", "0.655 0.435", "0.70 0.76 0.82 1");
                return;
            }

            var count = profile.Waypoints == null ? 0 : profile.Waypoints.Count;
            PruneNormalizeSelection(session, profile);
            var markedCount = CountNormalizeWaypoints(session, profile);
            var normalizedAxis = NormalizeCoordinateAxis(session.NormalizeAxis);
            if (string.IsNullOrWhiteSpace(normalizedAxis))
            {
                normalizedAxis = "y";
                session.NormalizeAxis = normalizedAxis;
            }

            AddLabel(container, root, count + " total | " + markedCount + " marked", 10, TextAnchor.MiddleRight, "0.500 0.545", "0.655 0.578", "0.60 0.68 0.74 1");
            AddButton(container, root, "PREV", "airanim.ui.prevwp", "0.350 0.505", "0.405 0.536", "0.14 0.18 0.22 0.95", 9);
            AddButton(container, root, "NEXT", "airanim.ui.nextwp", "0.410 0.505", "0.465 0.536", "0.14 0.18 0.22 0.95", 9);
            AddButton(container, root, "ADD", "airanim.ui.addwp", "0.470 0.505", "0.525 0.536", "0.16 0.30 0.20 0.95", 9);
            AddButton(container, root, "HERE", "airanim.ui.addhere", "0.530 0.505", "0.585 0.536", "0.12 0.29 0.34 0.95", 9);
            AddButton(container, root, "REMOVE", "airanim.ui.removewp", "0.590 0.505", "0.655 0.536", "0.45 0.16 0.12 0.95", 8);

            AddButton(container, root, "X", "airanim.ui.normaxis x", "0.350 0.466", "0.382 0.497", normalizedAxis == "x" ? "0.32 0.20 0.10 0.95" : "0.14 0.18 0.22 0.95", 9);
            AddButton(container, root, "Y", "airanim.ui.normaxis y", "0.387 0.466", "0.419 0.497", normalizedAxis == "y" ? "0.32 0.20 0.10 0.95" : "0.14 0.18 0.22 0.95", 9);
            AddButton(container, root, "Z", "airanim.ui.normaxis z", "0.424 0.466", "0.456 0.497", normalizedAxis == "z" ? "0.32 0.20 0.10 0.95" : "0.14 0.18 0.22 0.95", 9);
            AddButton(container, root, "ALL", "airanim.ui.normall", "0.465 0.466", "0.515 0.497", "0.12 0.24 0.30 0.95", 8);
            AddButton(container, root, "CLEAR", "airanim.ui.normclear", "0.520 0.466", "0.575 0.497", "0.18 0.22 0.28 0.95", 7);
            AddButton(container, root, "NORM", "airanim.ui.normalize", "0.580 0.466", "0.655 0.497", markedCount == 0 ? "0.10 0.10 0.10 0.80" : "0.40 0.18 0.12 0.95", 8);

            if (count == 0)
            {
                AddLabel(container, root, "No waypoints yet. Click ADD or use /airanim wp add <time> <x> <y> <z>.", 10, TextAnchor.MiddleCenter, "0.350 0.360", "0.655 0.430", "0.70 0.76 0.82 1");
                return;
            }

            var rows = Math.Min(MaxWaypointsInUi, count);
            var contentHeight = Math.Max(250f, 6f + rows * 42f);
            var scroll = AddScrollView(container, root, "0.350 0.242", "0.655 0.456", contentHeight, true);
            for (var i = 0; i < rows; i++)
            {
                var waypoint = profile.Waypoints[i];
                var selected = i == session.SelectedWaypointIndex;
                var markedForNormalize = IsNormalizeWaypointSelected(session, waypoint);
                var top = 6f + i * 42f;
                var bottom = top + 36f;
                var row = AddOffsetPanel(container, scroll, top, bottom, selected ? "0.27 0.15 0.08 0.94" : "0.10 0.12 0.145 0.88");
                AddLabel(container, row, "#" + DisplayIndex(i) + "  t=" + FormatSeconds(waypoint.Time), 10, TextAnchor.MiddleLeft, "0.030 0.51", "0.410 0.94", selected ? "1 0.86 0.55 1" : "0.92 0.96 1 1");
                AddLabel(container, row, "X " + FormatFloat(waypoint.X) + "   Y " + FormatFloat(waypoint.Y) + "   Z " + FormatFloat(waypoint.Z), 9, TextAnchor.MiddleLeft, "0.030 0.08", "0.570 0.48", "0.60 0.68 0.74 1");
                AddButton(container, row, markedForNormalize ? "ON" : "MARK", "airanim.ui.normtoggle " + DisplayIndex(i), "0.585 0.18", "0.765 0.82", markedForNormalize ? "0.15 0.30 0.20 0.95" : "0.14 0.18 0.22 0.95", 8);
                AddButton(container, row, selected ? "ACTIVE" : "SELECT", "airanim.ui.selectwp " + DisplayIndex(i), "0.775 0.18", "0.965 0.82", selected ? "0.50 0.20 0.10 0.95" : "0.18 0.24 0.30 0.95", 8);
            }
        }

        private void AddNudgePadUi(CuiElementContainer container, string root, EditorSession session, VisualProfileConfig profile)
        {
            AddPanel(container, root, "0.695 0.224", "0.982 0.585", "0.055 0.065 0.080 0.94");
            AddLabel(container, root, "Waypoint Controls", 15, TextAnchor.MiddleLeft, "0.715 0.545", "0.880 0.578", "1 1 1 1");
            AddLabel(container, root, "selected waypoint", 10, TextAnchor.MiddleRight, "0.850 0.545", "0.960 0.578", "0.60 0.68 0.74 1");

            if (profile == null || profile.Waypoints == null || profile.Waypoints.Count == 0)
            {
                AddLabel(container, root, "Select or create a profile, then add waypoints to enable controls.", 10, TextAnchor.MiddleCenter, "0.720 0.380", "0.955 0.450", "0.70 0.76 0.82 1");
                return;
            }

            var selected = GetSelectedWaypoint(session, profile);
            if (selected == null)
            {
                AddLabel(container, root, "Select a waypoint row to edit exact values.", 10, TextAnchor.MiddleCenter, "0.720 0.380", "0.955 0.450", "0.70 0.76 0.82 1");
                return;
            }

            AddLabel(container, root, "Position", 10, TextAnchor.MiddleLeft, "0.715 0.492", "0.815 0.525", "0.82 0.90 0.96 1");
            AddWaypointValueButton(container, root, "DUR", FormatSeconds(GetTimelineSegmentDuration(profile, session.SelectedWaypointIndex)), "airanim.valueedit.open duration selected full", 0.830f, 0.489f, 0.125f, 0.033f, 7);
            AddWaypointValueButton(container, root, "X", FormatFloat(selected.X), "airanim.valueedit.open pos x full", 0.715f, 0.445f, 0.074f, 0.038f, 8);
            AddWaypointValueButton(container, root, "Y", FormatFloat(selected.Y), "airanim.valueedit.open pos y full", 0.800f, 0.445f, 0.074f, 0.038f, 8);
            AddWaypointValueButton(container, root, "Z", FormatFloat(selected.Z), "airanim.valueedit.open pos z full", 0.885f, 0.445f, 0.074f, 0.038f, 8);

            AddLabel(container, root, "Rotation", 10, TextAnchor.MiddleLeft, "0.715 0.390", "0.815 0.423", "0.82 0.90 0.96 1");
            AddWaypointValueButton(container, root, "X", FormatDegrees(selected.RotationX), "airanim.valueedit.open rot x full", 0.715f, 0.343f, 0.074f, 0.038f, 8);
            AddWaypointValueButton(container, root, "Y", FormatDegrees(selected.RotationY), "airanim.valueedit.open rot y full", 0.800f, 0.343f, 0.074f, 0.038f, 8);
            AddWaypointValueButton(container, root, "Z", FormatDegrees(selected.RotationZ), "airanim.valueedit.open rot z full", 0.885f, 0.343f, 0.074f, 0.038f, 8);

            AddButton(container, root, "FWD", "airanim.ui.nudge forward 1", "0.790 0.290", "0.875 0.327", "0.15 0.25 0.32 0.95", 8);
            AddButton(container, root, "BACK", "airanim.ui.nudge back 1", "0.880 0.290", "0.955 0.327", "0.15 0.25 0.32 0.95", 8);
            AddButton(container, root, "LEFT", "airanim.ui.nudge left 1", "0.715 0.248", "0.790 0.285", "0.15 0.25 0.32 0.95", 8);
            AddButton(container, root, "RIGHT", "airanim.ui.nudge right 1", "0.795 0.248", "0.875 0.285", "0.15 0.25 0.32 0.95", 8);
            AddButton(container, root, "UP", "airanim.ui.nudge up 1", "0.880 0.248", "0.917 0.285", "0.16 0.30 0.20 0.95", 8);
            AddButton(container, root, "DN", "airanim.ui.nudge down 1", "0.922 0.248", "0.955 0.285", "0.42 0.18 0.12 0.95", 8);

            AddLabel(container, root, "Click a value to open exact edit.", 9, TextAnchor.MiddleCenter, "0.715 0.195", "0.955 0.224", "0.54 0.62 0.68 1");
        }

        private void AddBottomActionBarUi(CuiElementContainer container, string root, EditorSession session, VisualProfileConfig profile)
        {
            var hasSelectedWaypoint = profile != null && profile.Waypoints != null && profile.Waypoints.Count > 0;
            AddPanel(container, root, "0.333 0.130", "0.982 0.208", "0.055 0.065 0.080 0.94");
            AddButton(container, root, "TARGET", "airanim.ui.target", "0.350 0.169", "0.428 0.198", "0.14 0.36 0.42 0.95", 9);
            AddButton(container, root, "ADD HERE", "airanim.ui.addhere", "0.433 0.169", "0.520 0.198", profile == null ? "0.10 0.10 0.10 0.80" : "0.12 0.29 0.34 0.95", 9);
            AddButton(container, root, "MARKERS", "airanim.ui.markers", "0.525 0.169", "0.608 0.198", "0.18 0.24 0.30 0.95", 9);
            AddButton(container, root, session.ObjectMarkersEnabled ? "OBJ ON" : "OBJ OFF", "airanim.ui.objects", "0.613 0.169", "0.690 0.198", session.ObjectMarkersEnabled ? "0.15 0.30 0.20 0.95" : "0.28 0.12 0.11 0.95", 8);
            AddButton(container, root, session.TimelineOpen ? "TIME ON" : "TIMELINE", "airanim.ui.timeline", "0.695 0.169", "0.782 0.198", session.TimelineOpen ? "0.32 0.20 0.10 0.95" : "0.16 0.24 0.34 0.95", 8);
            AddButton(container, root, "PREVIEW", "airanim.ui.preview", "0.787 0.169", "0.855 0.198", profile == null ? "0.10 0.10 0.10 0.80" : "0.48 0.16 0.10 0.95", 8);
            AddButton(container, root, session.PreviewPaused ? "RESUME" : "PAUSE", "airanim.ui.pause", "0.860 0.169", "0.920 0.198", session.PreviewActive ? (session.PreviewPaused ? "0.14 0.32 0.22 0.95" : "0.34 0.21 0.10 0.95") : "0.12 0.15 0.19 0.80", 8);
            AddButton(container, root, "STOP", "airanim.ui.stop", "0.925 0.169", "0.970 0.198", session.PreviewActive ? "0.46 0.13 0.10 0.95" : "0.12 0.15 0.19 0.80", 8);
            AddButton(container, root, "SAVE", "airanim.ui.save", "0.350 0.138", "0.428 0.164", "0.40 0.18 0.12 0.95", 8);
            AddButton(container, root, "HIDE", "airanim.ui.hide", "0.433 0.138", "0.510 0.164", "0.18 0.22 0.28 0.95", 8);
            AddButton(container, root, "END", "airanim.ui.endsession", "0.515 0.138", "0.592 0.164", "0.36 0.12 0.10 0.95", 8);
            if (profile != null)
            {
                AddButton(container, root, "DEL", "airanim.ui.deleteprompt", "0.597 0.138", "0.665 0.164", "0.44 0.10 0.08 0.95", 8);
            }

            AddButton(container, root, "GO WP", "airanim.ui.gotowp", "0.670 0.138", "0.745 0.164", hasSelectedWaypoint ? "0.14 0.36 0.42 0.95" : "0.10 0.10 0.10 0.80", 8);
        }

        private void AddTimelineUi(CuiElementContainer container, BasePlayer player, EditorSession session, VisualProfileConfig profile)
        {
            var root = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.030 0.035 0.045 0.975" },
                RectTransform = { AnchorMin = "0.08 0.025", AnchorMax = "0.92 0.220" }
            }, "Overlay", TimelineUiName);

            AddPanel(container, root, "0.018 0.735", "0.982 0.965", "0.08 0.09 0.11 0.96");
            AddLabel(container, root, "Timeline", 15, TextAnchor.MiddleLeft, "0.035 0.835", "0.145 0.930", "1 0.86 0.58 1");
            var releaseCount = profile == null ? 0 : BuildEffectiveReleaseSchedule(profile).Count;
            AddLabel(container, root, profile == null ? "No active profile" : "Duration " + FormatSeconds(profile.DurationSeconds) + "   Releases " + releaseCount + "   Mode " + (IsRepeatedPatternMode(profile) ? "repeated" : "manual"), 10, TextAnchor.MiddleLeft, "0.145 0.835", "0.405 0.930", "0.70 0.78 0.84 1");
            AddButton(container, root, "ADD REL", "airanim.release.add", "0.410 0.815", "0.475 0.930", profile == null || IsRepeatedPatternMode(profile) ? "0.10 0.10 0.10 0.80" : "0.32 0.20 0.10 0.95", 7);
            AddButton(container, root, "-1", "airanim.timeline.payload -1", "0.480 0.815", "0.515 0.930", "0.28 0.13 0.11 0.95", 7);
            AddButton(container, root, "-.25", "airanim.timeline.payload -" + FormatFloat(TimelineSmallStepSeconds), "0.520 0.815", "0.560 0.930", "0.28 0.13 0.11 0.95", 7);
            AddButton(container, root, "+.25", "airanim.timeline.payload " + FormatFloat(TimelineSmallStepSeconds), "0.565 0.815", "0.610 0.930", "0.14 0.30 0.20 0.95", 7);
            AddButton(container, root, "+1", "airanim.timeline.payload 1", "0.615 0.815", "0.650 0.930", "0.14 0.30 0.20 0.95", 7);
            AddButton(container, root, "ADD WP", "airanim.ui.addhere", "0.655 0.815", "0.724 0.930", profile == null ? "0.10 0.10 0.10 0.80" : "0.12 0.29 0.34 0.95", 7);
            AddButton(container, root, "GO WP", "airanim.ui.gotowp", "0.730 0.815", "0.790 0.930", profile == null || profile.Waypoints == null || profile.Waypoints.Count == 0 ? "0.10 0.10 0.10 0.80" : "0.14 0.36 0.42 0.95", 7);
            AddButton(container, root, session.ObjectMarkersEnabled ? "OBJ ON" : "OBJ OFF", "airanim.ui.objects", "0.796 0.815", "0.840 0.930", session.ObjectMarkersEnabled ? "0.15 0.30 0.20 0.95" : "0.28 0.12 0.11 0.95", 7);
            AddButton(container, root, "|<", "airanim.timeline.scroll home", "0.846 0.815", "0.867 0.930", "0.14 0.18 0.22 0.95", 8);
            AddButton(container, root, "<", "airanim.timeline.scroll -" + FormatFloat(TimelineScrollStepPixels), "0.870 0.815", "0.891 0.930", "0.14 0.18 0.22 0.95", 8);
            AddButton(container, root, ">", "airanim.timeline.scroll " + FormatFloat(TimelineScrollStepPixels), "0.894 0.815", "0.915 0.930", "0.14 0.18 0.22 0.95", 8);
            AddButton(container, root, ">|", "airanim.timeline.scroll end", "0.918 0.815", "0.939 0.930", "0.14 0.18 0.22 0.95", 7);
            AddButton(container, root, "X", "airanim.ui.timeline", "0.944 0.815", "0.965 0.930", "0.55 0.12 0.10 0.95", 12);

            if (profile == null || profile.Waypoints == null || profile.Waypoints.Count == 0)
            {
                AddLabel(container, root, "Select a profile to edit waypoint order and timing.", 11, TextAnchor.MiddleCenter, "0.035 0.230", "0.965 0.560", "0.70 0.76 0.82 1");
                return;
            }

            var count = profile.Waypoints.Count;
            var total = GetTimelineTotalDuration(profile);
            var timeScaleWidth = GetTimelineTimeScaleWidth(total);
            var nodeLayouts = BuildTimelineNodeLayouts(profile, total, timeScaleWidth);
            var contentWidth = GetTimelineContentWidth(profile, total, nodeLayouts);
            var maxOffset = Mathf.Max(0f, contentWidth - TimelineViewportWidthPixels);
            session.TimelineScrollOffset = Mathf.Clamp(session.TimelineScrollOffset, 0f, maxOffset);
            var scrollOffset = session.TimelineScrollOffset;
            var scroll = AddHorizontalScrollView(container, root, "0.035 0.095", "0.965 0.705", contentWidth, false);

            AddTimelinePayloadMarkers(container, scroll, session, profile, total, timeScaleWidth, scrollOffset);

            for (var i = 0; i < count; i++)
            {
                var layout = nodeLayouts[i];
                AddTimelineNode(container, scroll, session, profile, i, layout.Left - scrollOffset, layout.Width);
            }
        }

        private void AddTimelinePayloadMarkers(CuiElementContainer container, string scroll, EditorSession session, VisualProfileConfig profile, float total, float timeScaleWidth, float scrollOffset)
        {
            if (profile == null || total <= 0f)
            {
                return;
            }

            var schedule = BuildEffectiveReleaseSchedule(profile);
            if (schedule.Count == 0)
            {
                AddTimelinePayloadMarker(container, scroll, Mathf.Clamp(profile.FirstPayloadDelaySeconds, 0f, total), total, timeScaleWidth, scrollOffset, "START", IsRepeatedPatternMode(profile) ? "airanim.ui.tab releases" : "airanim.release.add " + FormatFloat(profile.FirstPayloadDelaySeconds), false);
                return;
            }

            for (var i = 0; i < schedule.Count; i++)
            {
                var payloadEvent = schedule[i];
                if (payloadEvent == null || payloadEvent.Time > total + 0.001f)
                {
                    continue;
                }

                var selected = IsRepeatedPatternMode(profile)
                    ? session != null && i == session.SelectedGeneratedReleaseIndex
                    : session != null && GetSelectedPayloadEvent(session, profile) == payloadEvent;
                var command = IsRepeatedPatternMode(profile) ? "airanim.pattern.focus " + i : "airanim.release.edit " + profile.PayloadEvents.IndexOf(payloadEvent);
                AddTimelinePayloadMarker(container, scroll, payloadEvent.Time, total, timeScaleWidth, scrollOffset, (IsRepeatedPatternMode(profile) ? "R" : "P") + DisplayIndex(i), command, selected);
            }
        }

        private void AddTimelinePayloadMarker(CuiElementContainer container, string scroll, float time, float total, float timeScaleWidth, float scrollOffset, string label, string command, bool selected)
        {
            var x = Mathf.Clamp01(time / total) * timeScaleWidth - scrollOffset;
            var marker = AddOffsetRectPanel(container, scroll, x - 10f, x + 20f, 5f, -5f, selected ? "1.00 0.62 0.18 0.94" : "0.95 0.38 0.12 0.86");
            AddButton(container, marker, label, command, "0 0", "1 1", "0 0 0 0", label.Length > 2 ? 7 : 8);
        }

        private void AddTimelineNode(CuiElementContainer container, string scroll, EditorSession session, VisualProfileConfig profile, int index, float left, float width)
        {
            var waypoint = profile.Waypoints[index];
            var selected = index == session.SelectedWaypointIndex;
            var right = left + width;
            var row = AddOffsetRectPanel(container, scroll, left, right, 8f, -8f, selected ? "0.28 0.16 0.08 0.95" : "0.10 0.12 0.145 0.92");
            var segmentText = (index < profile.Waypoints.Count - 1 ? "dur " : "tail ") + FormatSeconds(GetTimelineSegmentDuration(profile, index));

            AddLabel(container, row, "#" + DisplayIndex(index) + "  " + FormatSeconds(waypoint.Time), 10, TextAnchor.MiddleLeft, "0.045 0.68", "0.720 0.94", selected ? "1 0.86 0.55 1" : "0.92 0.96 1 1");
            AddButton(container, row, selected ? "ACTIVE" : "SEL", "airanim.timeline.select " + index, "0.735 0.68", "0.965 0.94", selected ? "0.50 0.20 0.10 0.95" : "0.18 0.24 0.30 0.95", 8);
            AddButton(container, row, segmentText, "airanim.timeline.duration " + index, "0.045 0.43", "0.420 0.66", "0.055 0.070 0.085 0.96", 8);

            AddButton(container, row, "<", "airanim.timeline.move " + index + " -1", "0.045 0.10", "0.165 0.38", index <= 0 ? "0.08 0.09 0.10 0.70" : "0.14 0.18 0.22 0.95", 9);
            AddButton(container, row, ">", "airanim.timeline.move " + index + " 1", "0.175 0.10", "0.295 0.38", index >= profile.Waypoints.Count - 1 ? "0.08 0.09 0.10 0.70" : "0.14 0.18 0.22 0.95", 9);
            AddButton(container, row, "ADD REL", "airanim.timeline.payloadwp " + index, "0.305 0.10", "0.445 0.38", "0.32 0.20 0.10 0.95", 7);

            if (index < profile.Waypoints.Count - 1)
            {
                AddButton(container, row, "-.25", "airanim.timeline.segment " + index + " -" + FormatFloat(TimelineSmallStepSeconds), "0.465 0.10", "0.605 0.38", "0.36 0.12 0.10 0.95", 8);
                AddButton(container, row, "+.25", "airanim.timeline.segment " + index + " " + FormatFloat(TimelineSmallStepSeconds), "0.615 0.10", "0.755 0.38", "0.16 0.30 0.20 0.95", 8);
                AddButton(container, row, "+1", "airanim.timeline.segment " + index + " " + FormatFloat(TimelineLargeStepSeconds), "0.765 0.10", "0.885 0.38", "0.12 0.24 0.30 0.95", 8);
            }
        }

        private void AddStatusBarUi(CuiElementContainer container, string root, EditorSession session)
        {
            AddPanel(container, root, "0.018 0.028", "0.982 0.115", "0.030 0.035 0.045 0.96");
            var status = string.IsNullOrWhiteSpace(session.LastStatus) ? "Ready." : session.LastStatus;
            var warning = string.IsNullOrWhiteSpace(session.LastWarning) ? "" : "  •  " + session.LastWarning;
            AddLabel(container, root, status + warning, 11, TextAnchor.MiddleLeft, "0.035 0.070", "0.780 0.105", string.IsNullOrWhiteSpace(session.LastWarning) ? "0.70 0.80 0.86 1" : "1 0.66 0.48 1");
            AddLabel(container, root, "Data: oxide/data/PortableAirstrikes/VisualProfiles.json", 10, TextAnchor.MiddleRight, "0.650 0.038", "0.965 0.070", "0.46 0.54 0.60 1");
            AddLabel(container, root, "Tip: Preview hides this panel so you can watch. /airanim close hides CUI only; /airanim end cleans up markers/preview.", 10, TextAnchor.MiddleLeft, "0.035 0.038", "0.640 0.070", "0.50 0.58 0.64 1");
        }

        [ConsoleCommand("airanim.ui.close")]
        private void CCmdUiClose(ConsoleSystem.Arg arg)
        {
            HideEditorUiFromArg(arg);
        }

        [ConsoleCommand("airanim.ui.hide")]
        private void CCmdUiHide(ConsoleSystem.Arg arg)
        {
            HideEditorUiFromArg(arg);
        }

        private void HideEditorUiFromArg(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            HideEditorUi(player, true);
        }


        [ConsoleCommand("airanim.previewui.open")]
        private void CCmdPreviewUiOpen(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            CuiHelper.DestroyUi(player, PreviewUiName);
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.undo")]
        private void CCmdUiUndo(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            ApplyHistorySnapshot(player, false);
        }

        [ConsoleCommand("airanim.ui.redo")]
        private void CCmdUiRedo(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            ApplyHistorySnapshot(player, true);
        }

        [ConsoleCommand("airanim.ui.tab")]
        private void CCmdUiTab(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            session.ActiveTab = NormalizeEditorTab(arg.GetString(0));
            if (session.ActiveTab != "flight")
            {
                CuiHelper.DestroyUi(player, TimelineUiName);
            }

            SetStatus(session, session.ActiveTab == "flight" ? "Opened flight path workspace."
                : session.ActiveTab == "profile" ? "Opened profile tools."
                : session.ActiveTab == "commands" ? "Opened command reference."
                : "Opened release timing workspace.", "");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.commands.source")]
        private void CCmdCommandsSource(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            session.CommandSource = NormalizeCommandSource(arg.GetString(0));
            session.CommandPage = 0;
            session.ActiveTab = "commands";
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.commands.category")]
        private void CCmdCommandsCategory(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            session.CommandCategory = NormalizeCommandCategory(arg.GetString(0));
            session.CommandPage = 0;
            session.ActiveTab = "commands";
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.commands.page")]
        private void CCmdCommandsPage(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            int delta;
            if (!int.TryParse(arg.GetString(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out delta))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            var entries = GetCommandHelpEntries(session.CommandCategory);
            var pageCount = Math.Max(1, Mathf.CeilToInt(entries.Count / (float)CommandRowsPerPage));
            session.CommandPage = (session.CommandPage + delta + pageCount) % pageCount;
            session.ActiveTab = "commands";
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.profilefilter")]
        private void CCmdUiProfileFilter(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            session.ProfileFilter = GetArgTail(arg, 0);
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.profilefilterclear")]
        private void CCmdUiProfileFilterClear(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            session.ProfileFilter = "";
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.waypoint.page")]
        private void CCmdWaypointPage(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            int delta;
            if (!int.TryParse(arg.GetString(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out delta))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            session.WaypointPage = Math.Max(0, session.WaypointPage + delta);
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.waypoint.step")]
        private void CCmdWaypointStep(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            float step;
            if (!TryParseFloat(arg.GetString(0), out step))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            session.WaypointNudgeStep = Mathf.Clamp(step, 0.1f, 50f);
            SetStatus(session, "Waypoint move step set to " + FormatMeters(session.WaypointNudgeStep) + ".", "");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.waypoint.inline")]
        private void CCmdWaypointInline(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 2)
            {
                return;
            }

            var field = (arg.GetString(0) ?? "").Trim().ToLowerInvariant();
            float value;
            if (!TryParseInputFloat(GetArgTail(arg, 1), field, out value))
            {
                SetStatus(GetOrCreateSession(player), "Invalid waypoint value.", "Enter a number and press Enter.");
                ShowEditorUi(player);
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var waypoint = GetSelectedWaypoint(session, profile);
            if (waypoint == null)
            {
                return;
            }

            var index = profile.Waypoints.IndexOf(waypoint);
            float applied;
            switch (field)
            {
                case "time":
                    waypoint.Time = value;
                    break;
                case "duration":
                case "segment":
                    if (!SetWaypointSegmentDuration(profile, index, value, out applied))
                    {
                        return;
                    }
                    break;
                case "x":
                    waypoint.X = value;
                    break;
                case "y":
                    waypoint.Y = value;
                    break;
                case "z":
                    waypoint.Z = value;
                    break;
                case "rotx":
                    waypoint.RotationX = NormalizeDegrees(value);
                    break;
                case "roty":
                    waypoint.RotationY = NormalizeDegrees(value);
                    break;
                case "rotz":
                    waypoint.RotationZ = NormalizeDegrees(value);
                    break;
                default:
                    return;
            }

            NormalizeProfile(session.ProfileId, profile);
            session.SelectedWaypointIndex = profile.Waypoints.IndexOf(waypoint);
            if (session.SelectedWaypointIndex < 0 && profile.Waypoints.Count > 0)
            {
                session.SelectedWaypointIndex = 0;
            }
            session.WaypointPage = Math.Max(0, session.SelectedWaypointIndex / WaypointRowsPerPage);
            RebuildMarkers(player, session);
            SetStatus(session, "Updated waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + " " + field + ".", "");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.release.mode")]
        private void CCmdReleaseMode(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var requested = NormalizePayloadReleaseMode(arg.GetString(0));
            var wasRepeated = IsRepeatedPatternMode(profile);
            var hasStoredPattern = profile.MaxPayloadCount > 0
                && profile.ReleaseTemplate != null
                && profile.ReleaseTemplate.Count > 0
                && !string.IsNullOrWhiteSpace(profile.ReleaseTemplate.Payload);
            if (requested == "generated" && !wasRepeated && !hasStoredPattern)
            {
                RepeatedPatternDetection detection;
                if (TryDetectRepeatedPattern(profile, out detection))
                {
                    profile.FirstPayloadDelaySeconds = detection.StartTime;
                    profile.PayloadReleaseIntervalSeconds = detection.IntervalSeconds;
                    profile.ReleaseTemplate = ClonePayloadEvent(detection.Template) ?? new VisualPayloadEvent();
                    profile.MaxPayloadCount = detection.TotalUnits;
                }
                else
                {
                    var selected = GetSelectedPayloadEvent(session, profile);
                    if (selected == null && profile.PayloadEvents != null && profile.PayloadEvents.Count > 0)
                    {
                        selected = profile.PayloadEvents[0];
                    }

                    if (selected != null)
                    {
                        profile.ReleaseTemplate = ClonePayloadEvent(selected) ?? new VisualPayloadEvent();
                        profile.ReleaseTemplate.Time = 0f;
                        profile.ReleaseTemplate.Index = 0;
                        profile.FirstPayloadDelaySeconds = selected.Time;
                    }

                    if (profile.ReleaseTemplate == null)
                    {
                        profile.ReleaseTemplate = new VisualPayloadEvent();
                    }

                    if (string.IsNullOrWhiteSpace(profile.ReleaseTemplate.Payload))
                    {
                        profile.ReleaseTemplate.Payload = GetDefaultPayloadForVehicle(profile.Vehicle);
                    }

                    if (profile.MaxPayloadCount <= 0)
                    {
                        var manualTotal = GetTotalPayloadUnits(profile);
                        profile.MaxPayloadCount = manualTotal > 0 ? manualTotal : Math.Max(1, profile.ReleaseTemplate.Count);
                    }
                }
            }

            profile.PayloadReleaseMode = requested;
            NormalizeProfile(session.ProfileId, profile);
            session.ActiveTab = "releases";
            session.ReleasePage = 0;
            session.SelectedGeneratedReleaseIndex = 0;
            SetStatus(session, requested == "generated" ? "Repeated pattern mode enabled." : "Manual release events enabled.", requested == "generated" ? "Manual events are preserved but inactive." : "First payload time is synchronized to the earliest manual event.");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.release.view")]
        private void CCmdReleaseView(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            var view = (arg.GetString(0) ?? "").Trim().ToLowerInvariant();
            if (view != "profile" && view != "selected")
            {
                view = "releases";
            }

            var session = GetOrCreateSession(player);
            session.ReleaseTimelineView = view;
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.release.page")]
        private void CCmdReleasePage(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            int delta;
            if (!int.TryParse(arg.GetString(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out delta))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            session.ReleasePage = Math.Max(0, session.ReleasePage + delta);
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.release.select")]
        private void CCmdReleaseSelect(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            int index;
            if (!int.TryParse(arg.GetString(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile) || profile.PayloadEvents == null || index < 0 || index >= profile.PayloadEvents.Count)
            {
                return;
            }

            SetSelectedPayloadEvent(session, profile, profile.PayloadEvents[index]);
            session.ActiveTab = "releases";
            SetStatus(session, "Selected release #" + DisplayIndex(session.SelectedPayloadEventIndex) + ".", "");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.release.nudge")]
        private void CCmdReleaseNudge(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            float delta;
            if (!TryParseFloat(arg.GetString(0), out delta))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var ev = GetSelectedPayloadEvent(session, profile);
            if (ev == null)
            {
                return;
            }

            ev.Time = Mathf.Clamp(ev.Time + delta, 0f, Mathf.Max(0.1f, profile.DurationSeconds));
            NormalizeProfileKeepingRelease(session, profile, ev);
            SetStatus(session, "Release #" + DisplayIndex(session.SelectedPayloadEventIndex) + " moved to " + FormatSeconds(ev.Time) + ".", "");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.release.snap")]
        private void CCmdReleaseSnap(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile) || profile.Waypoints == null || profile.Waypoints.Count == 0)
            {
                return;
            }

            var ev = GetSelectedPayloadEvent(session, profile);
            if (ev == null)
            {
                return;
            }

            var mode = (arg.GetString(0) ?? "nearest").Trim().ToLowerInvariant();
            var target = profile.Waypoints[0].Time;
            if (mode == "prev")
            {
                target = profile.Waypoints[0].Time;
                for (var i = 0; i < profile.Waypoints.Count; i++)
                {
                    if (profile.Waypoints[i].Time < ev.Time - 0.001f)
                    {
                        target = profile.Waypoints[i].Time;
                    }
                }
            }
            else if (mode == "next")
            {
                target = profile.Waypoints[profile.Waypoints.Count - 1].Time;
                for (var i = 0; i < profile.Waypoints.Count; i++)
                {
                    if (profile.Waypoints[i].Time > ev.Time + 0.001f)
                    {
                        target = profile.Waypoints[i].Time;
                        break;
                    }
                }
            }
            else
            {
                var bestDistance = float.MaxValue;
                foreach (var waypoint in profile.Waypoints)
                {
                    var distance = Mathf.Abs(waypoint.Time - ev.Time);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        target = waypoint.Time;
                    }
                }
            }

            ev.Time = target;
            NormalizeProfileKeepingRelease(session, profile, ev);
            SetStatus(session, "Snapped release #" + DisplayIndex(session.SelectedPayloadEventIndex) + " to " + FormatSeconds(ev.Time) + ".", "");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.release.countdelta")]
        private void CCmdReleaseCountDelta(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            int delta;
            if (!int.TryParse(arg.GetString(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out delta))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var ev = GetSelectedPayloadEvent(session, profile);
            if (ev == null)
            {
                return;
            }

            ev.Count = Mathf.Clamp(ev.Count + delta, 1, 1000);
            NormalizeProfileKeepingRelease(session, profile, ev);
            SetStatus(session, "Release #" + DisplayIndex(session.SelectedPayloadEventIndex) + " now releases " + ev.Count + " unit(s).", "");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.release.advanced")]
        private void CCmdReleaseAdvanced(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            session.ReleaseAdvancedOpen = !session.ReleaseAdvancedOpen;
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.release.atwp")]
        private void CCmdReleaseAtWaypoint(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var waypoint = GetSelectedWaypoint(session, profile);
            if (waypoint == null)
            {
                SetStatus(session, "No waypoint selected.", "Select a waypoint in Flight Path first.");
                ShowEditorUi(player);
                return;
            }

            AddPayloadReleaseAt(player, waypoint.Time, false);
            session.ActiveTab = "releases";
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.release.openpopup")]
        private void CCmdReleaseOpenPopup(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            ShowPayloadReleasePopupUi(player);
        }

        [ConsoleCommand("airanim.release.inline")]
        private void CCmdReleaseInline(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 2)
            {
                return;
            }

            var field = NormalizePayloadReleaseField(arg.GetString(0));
            float value;
            if (string.IsNullOrWhiteSpace(field) || !TryParseInputFloat(GetArgTail(arg, 1), field, out value))
            {
                SetStatus(GetOrCreateSession(player), "Invalid release value.", "Enter a number and press Enter.");
                ShowEditorUi(player);
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var ev = GetSelectedPayloadEvent(session, profile);
            if (ev == null)
            {
                return;
            }

            SetPayloadReleaseNumericField(ev, field, value);
            NormalizeProfileKeepingRelease(session, profile, ev);
            SetStatus(session, "Updated release #" + DisplayIndex(session.SelectedPayloadEventIndex) + " " + field + ".", "");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.pattern.openpopup")]
        private void CCmdPatternOpenPopup(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            ShowPatternTemplatePopupUi(player);
        }

        [ConsoleCommand("airanim.pattern.closepopup")]
        private void CCmdPatternClosePopup(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, ReleaseUiName);
            var session = GetOrCreateSession(player);
            session.ReleaseUiOpen = false;
            session.PatternTemplateUiOpen = false;
        }

        [ConsoleCommand("airanim.pattern.inline")]
        private void CCmdPatternInline(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 2)
            {
                return;
            }

            var field = (arg.GetString(0) ?? "").Trim().ToLowerInvariant();
            float value;
            if (!TryParseInputFloat(GetArgTail(arg, 1), field, out value))
            {
                SetStatus(GetOrCreateSession(player), "Invalid repeated-pattern value.", "Enter a number and press Enter.");
                ShowEditorUi(player);
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            if (profile.ReleaseTemplate == null)
            {
                profile.ReleaseTemplate = new VisualPayloadEvent();
            }

            switch (field)
            {
                case "start":
                case "time":
                    profile.FirstPayloadDelaySeconds = Mathf.Clamp(value, 0f, Mathf.Max(0.1f, profile.DurationSeconds));
                    break;
                case "interval":
                    profile.PayloadReleaseIntervalSeconds = Mathf.Clamp(value, 0.05f, 30f);
                    break;
                case "units":
                case "count":
                    profile.ReleaseTemplate.Count = Mathf.Clamp(Mathf.RoundToInt(value), 1, 1000);
                    break;
                case "total":
                    profile.MaxPayloadCount = Mathf.Clamp(Mathf.RoundToInt(value), 0, 1000);
                    break;
                case "groups":
                    var groups = Mathf.Clamp(Mathf.RoundToInt(value), 0, MaxGeneratedReleaseGroups);
                    profile.MaxPayloadCount = Mathf.Clamp(groups * Math.Max(1, profile.ReleaseTemplate.Count), 0, 1000);
                    break;
                default:
                    var releaseField = NormalizePayloadReleaseField(field);
                    if (string.IsNullOrWhiteSpace(releaseField) || releaseField == "time" || releaseField == "count" || releaseField == "payload")
                    {
                        return;
                    }
                    SetPayloadReleaseNumericField(profile.ReleaseTemplate, releaseField, value);
                    break;
            }

            var reopenTemplatePopup = session.PatternTemplateUiOpen;
            profile.PayloadReleaseMode = "generated";
            NormalizeProfile(session.ProfileId, profile);
            SetStatus(session, "Updated repeated pattern " + field + ".", GetReleaseValidationMessage(profile));
            ShowEditorUi(player);
            if (reopenTemplatePopup)
            {
                ShowPatternTemplatePopupUi(player);
            }
        }

        [ConsoleCommand("airanim.pattern.delta")]
        private void CCmdPatternDelta(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 2)
            {
                return;
            }

            float delta;
            if (!TryParseFloat(arg.GetString(1), out delta))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var field = (arg.GetString(0) ?? "").Trim().ToLowerInvariant();
            if (field == "start")
            {
                profile.FirstPayloadDelaySeconds = Mathf.Clamp(profile.FirstPayloadDelaySeconds + delta, 0f, Mathf.Max(0.1f, profile.DurationSeconds));
            }
            else if (field == "interval")
            {
                profile.PayloadReleaseIntervalSeconds = Mathf.Clamp(profile.PayloadReleaseIntervalSeconds + delta, 0.05f, 30f);
            }
            else
            {
                return;
            }

            profile.PayloadReleaseMode = "generated";
            NormalizeProfile(session.ProfileId, profile);
            SetStatus(session, "Adjusted repeated pattern " + field + ".", GetReleaseValidationMessage(profile));
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.pattern.extend")]
        private void CCmdPatternExtend(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var last = GetGeneratedLastReleaseTime(profile);
            if (last > 120f)
            {
                SetStatus(session, "Pattern extends beyond the 120-second profile limit.", "Reduce total units or interval before extending.");
                ShowEditorUi(player);
                return;
            }

            profile.DurationSeconds = Mathf.Max(profile.DurationSeconds, last);
            NormalizeProfile(session.ProfileId, profile);
            RebuildMarkers(player, session);
            SetStatus(session, "Extended profile duration to " + FormatSeconds(profile.DurationSeconds) + ".", "Repeated pattern now fits within the profile.");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.pattern.payload")]
        private void CCmdPatternPayload(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            if (profile.ReleaseTemplate == null)
            {
                profile.ReleaseTemplate = new VisualPayloadEvent();
            }

            var reopenTemplatePopup = session.PatternTemplateUiOpen;
            profile.ReleaseTemplate.Payload = GetNextPayload(profile.ReleaseTemplate.Payload);
            profile.PayloadReleaseMode = "generated";
            NormalizeProfile(session.ProfileId, profile);
            SetStatus(session, "Pattern ordnance set to " + GetPayloadDisplay(profile.ReleaseTemplate.Payload) + ".", "");
            ShowEditorUi(player);
            if (reopenTemplatePopup)
            {
                ShowPatternTemplatePopupUi(player);
            }
        }

        [ConsoleCommand("airanim.pattern.focus")]
        private void CCmdPatternFocus(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            int index;
            if (!int.TryParse(arg.GetString(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            session.SelectedGeneratedReleaseIndex = Math.Max(0, index);
            session.ReleasePage = session.SelectedGeneratedReleaseIndex / ReleaseRowsPerPage;
            SetStatus(session, "Focused generated release #" + DisplayIndex(session.SelectedGeneratedReleaseIndex) + ".", "Edit the pattern fields to move the entire sequence.");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.pattern.convertmanual")]
        private void CCmdPatternConvertManual(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var schedule = BuildEffectiveReleaseSchedule(profile);
            if (schedule.Count > 0 && schedule[schedule.Count - 1].Time > profile.DurationSeconds + 0.001f)
            {
                SetStatus(session, "Pattern does not fit inside the profile duration.", "Use EXTEND or adjust the pattern before converting to manual events.");
                ShowEditorUi(player);
                return;
            }

            if (schedule.Count > MaxPayloadEventsInProfile)
            {
                SetStatus(session, "Pattern has " + schedule.Count + " release groups.", "Reduce it to " + MaxPayloadEventsInProfile + " or fewer before converting to manual events.");
                ShowEditorUi(player);
                return;
            }

            profile.PayloadEvents = new List<VisualPayloadEvent>();
            foreach (var generated in schedule)
            {
                profile.PayloadEvents.Add(ClonePayloadEvent(generated) ?? new VisualPayloadEvent());
            }

            profile.PayloadReleaseMode = "manual";
            NormalizeProfile(session.ProfileId, profile);
            SetSelectedPayloadEvent(session, profile, profile.PayloadEvents.Count == 0 ? null : profile.PayloadEvents[0]);
            session.ReleasePage = 0;
            SetStatus(session, "Converted repeated pattern to " + profile.PayloadEvents.Count + " manual release event(s).", "Each event can now be fine-tuned independently.");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.pattern.detect")]
        private void CCmdPatternDetect(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            RepeatedPatternDetection detection;
            if (!TryDetectRepeatedPattern(profile, out detection))
            {
                SetStatus(session, "The manual events do not form a regular pattern.", "Payload settings, counts and intervals must match.");
                ShowEditorUi(player);
                return;
            }

            profile.FirstPayloadDelaySeconds = detection.StartTime;
            profile.PayloadReleaseIntervalSeconds = detection.IntervalSeconds;
            profile.ReleaseTemplate = ClonePayloadEvent(detection.Template) ?? new VisualPayloadEvent();
            profile.MaxPayloadCount = detection.TotalUnits;
            profile.PayloadReleaseMode = "generated";
            NormalizeProfile(session.ProfileId, profile);
            session.SelectedGeneratedReleaseIndex = 0;
            session.ReleasePage = 0;
            SetStatus(session, "Converted detected manual sequence to a repeated pattern.", detection.ReleaseGroups + " group(s) × " + detection.UnitsPerRelease + " every " + FormatSeconds(detection.IntervalSeconds) + ". Manual events remain preserved but inactive.");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.profile.inline")]
        private void CCmdProfileInline(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 2)
            {
                return;
            }

            var field = (arg.GetString(0) ?? "").Trim().ToLowerInvariant();
            float value;
            if (!TryParseInputFloat(GetArgTail(arg, 1), field, out value))
            {
                SetStatus(GetOrCreateSession(player), "Invalid profile value.", "Enter a number and press Enter.");
                ShowEditorUi(player);
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            SetProfileFloat(profile, field, value);
            NormalizeProfile(session.ProfileId, profile);
            RebuildMarkers(player, session);
            SetStatus(session, "Updated profile " + field + ".", "");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.wpui.close")]
        private void CCmdWaypointUiClose(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, WaypointUiName);
            EditorSession session;
            if (sessions.TryGetValue(player.userID, out session) && session != null)
            {
                session.WaypointUiOpen = false;
            }
        }

        [ConsoleCommand("airanim.wpui.full")]
        private void CCmdWaypointUiFull(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.valueedit.open")]
        private void CCmdValueEditOpen(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 2)
            {
                return;
            }

            var fieldType = (arg.GetString(0) ?? "").Trim().ToLowerInvariant();
            if (fieldType == "release" || fieldType == "payload")
            {
                if (arg.Args.Length < 2)
                {
                    return;
                }

                var fromReleasePopup = arg.Args.Length >= 3 && string.Equals(arg.GetString(2), "popup", StringComparison.OrdinalIgnoreCase);
                OpenPayloadReleaseValueEdit(player, arg.GetString(1), fromReleasePopup);
                return;
            }

            var genericFromPopup = arg.Args.Length >= 3 && string.Equals(arg.GetString(2), "popup", StringComparison.OrdinalIgnoreCase);
            if (fieldType == "waypointtime" || fieldType == "wptime")
            {
                OpenWaypointTimeEdit(player, arg.GetString(1), genericFromPopup);
                return;
            }

            if (fieldType == "profile")
            {
                OpenProfileValueEdit(player, arg.GetString(1), genericFromPopup);
                return;
            }

            if (fieldType == "pattern" || fieldType == "template")
            {
                OpenPatternValueEdit(player, arg.GetString(1), genericFromPopup);
                return;
            }

            if (fieldType == "duration" || fieldType == "dur" || fieldType == "segment" || fieldType == "seg")
            {
                var selector = arg.GetString(1);
                OpenWaypointDurationEdit(player, selector, genericFromPopup);
                return;
            }

            var rotation = fieldType == "rot" || fieldType == "rotation" || fieldType == "angle";
            if (!rotation && fieldType != "pos" && fieldType != "position" && fieldType != "coord")
            {
                return;
            }

            var axis = NormalizeCoordinateAxis(arg.GetString(1));
            if (string.IsNullOrWhiteSpace(axis))
            {
                return;
            }

            var fromPopup = arg.Args.Length >= 3 && string.Equals(arg.GetString(2), "popup", StringComparison.OrdinalIgnoreCase);
            OpenWaypointValueEdit(player, axis, rotation, fromPopup);
        }

        [ConsoleCommand("airanim.valueedit.input")]
        private void CCmdValueEditInput(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            CapturePendingValueEditInput(player, GetArgTail(arg, 0));
        }

        [ConsoleCommand("airanim.valueedit.key")]
        private void CCmdValueEditKey(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            ApplyPendingValueEditKey(player, arg.GetString(0));
        }

        [ConsoleCommand("airanim.valueedit.apply")]
        private void CCmdValueEditApply(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            CommitPendingValueEdit(player.userID);
        }

        [ConsoleCommand("airanim.valueedit.cancel")]
        private void CCmdValueEditCancel(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            CancelPendingValueEdit(player, true);
        }

        [ConsoleCommand("airanim.wpui.prev")]
        private void CCmdWaypointUiPrev(ConsoleSystem.Arg arg)
        {
            SelectRelativeWaypointForPopup(arg, -1);
        }

        [ConsoleCommand("airanim.wpui.next")]
        private void CCmdWaypointUiNext(ConsoleSystem.Arg arg)
        {
            SelectRelativeWaypointForPopup(arg, 1);
        }

        [ConsoleCommand("airanim.wpui.nudge")]
        private void CCmdWaypointUiNudge(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 2)
            {
                return;
            }

            float meters;
            if (!TryParseFloat(arg.GetString(1), out meters))
            {
                return;
            }

            NudgeSelectedWaypoint(player, arg.GetString(0), meters, false);
            RefreshWaypointPopupUiIfOpen(player);
        }

        [ConsoleCommand("airanim.wpui.setpos")]
        private void CCmdWaypointUiSetPosition(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            if (!TryGetAxisInput(arg, out var axis, out var value))
            {
                ShowInputSubmitWarning(player, "position");
                return;
            }

            QueueSelectedWaypointInput(player, axis, value, false, true);
        }

        [ConsoleCommand("airanim.wpui.setrot")]
        private void CCmdWaypointUiSetRotation(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            if (!TryGetAxisInput(arg, out var axis, out var value))
            {
                ShowInputSubmitWarning(player, "rotation");
                return;
            }

            QueueSelectedWaypointInput(player, axis, value, true, true);
        }

        [ConsoleCommand("airanim.wpui.setposx")]
        private void CCmdWaypointUiSetPositionX(ConsoleSystem.Arg arg)
        {
            ApplyAxisOnlyInput(arg, "x", false, true);
        }

        [ConsoleCommand("airanim.wpui.setposy")]
        private void CCmdWaypointUiSetPositionY(ConsoleSystem.Arg arg)
        {
            ApplyAxisOnlyInput(arg, "y", false, true);
        }

        [ConsoleCommand("airanim.wpui.setposz")]
        private void CCmdWaypointUiSetPositionZ(ConsoleSystem.Arg arg)
        {
            ApplyAxisOnlyInput(arg, "z", false, true);
        }

        [ConsoleCommand("airanim.wpui.setrotx")]
        private void CCmdWaypointUiSetRotationX(ConsoleSystem.Arg arg)
        {
            ApplyAxisOnlyInput(arg, "x", true, true);
        }

        [ConsoleCommand("airanim.wpui.setroty")]
        private void CCmdWaypointUiSetRotationY(ConsoleSystem.Arg arg)
        {
            ApplyAxisOnlyInput(arg, "y", true, true);
        }

        [ConsoleCommand("airanim.wpui.setrotz")]
        private void CCmdWaypointUiSetRotationZ(ConsoleSystem.Arg arg)
        {
            ApplyAxisOnlyInput(arg, "z", true, true);
        }

        [ConsoleCommand("airanim.wpui.rotate")]
        private void CCmdWaypointUiRotate(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 2)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var waypoint = GetSelectedWaypoint(session, profile);
            if (waypoint == null)
            {
                return;
            }

            var axis = arg.GetString(0);
            var value = arg.GetString(1);
            if (!ApplyWaypointRotationChange(waypoint, axis, value))
            {
                return;
            }

            NormalizeProfile(session.ProfileId, profile);
            RebuildMarkers(player, session);
            SetStatus(session, "Rotated waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + ".", "");
            RefreshEditorUiIfOpen(player);
            ShowWaypointPopupUi(player);
        }

        [ConsoleCommand("airanim.wpui.markers")]
        private void CCmdWaypointUiMarkers(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            FlushPendingAxisInput(player);
            RebuildMarkers(player, session);
            SetStatus(session, "Refreshed waypoint markers.", "");
            ShowWaypointPopupUi(player);
        }

        [ConsoleCommand("airanim.wpui.save")]
        private void CCmdWaypointUiSave(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            FlushPendingAxisInput(player);
            var session = GetOrCreateSession(player);
            SaveProfiles(string.IsNullOrWhiteSpace(session.ProfileId) ? null : new[] { session.ProfileId });
            SetStatus(session, "Saved VisualProfiles.json.", "");
            Reply(player, "Saved profiles to oxide/data/PortableAirstrikes/VisualProfiles.json.");
            ShowWaypointPopupUi(player);
        }

        [ConsoleCommand("airanim.ui.stop")]
        private void CCmdUiStop(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            DestroyPreview(session);
            RebuildMarkers(player, session);
            SetStatus(session, "Preview stopped.", "Markers and the editor session are still active.");
            Reply(player, "Preview stopped. Markers and the editor session are still active.");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.pause")]
        private void CCmdUiPause(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            TogglePreviewPause(player);
        }

        [ConsoleCommand("airanim.ui.ride")]
        private void CCmdUiRide(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            CmdRide(player, new[] { "ride" });
            ShowPreviewBarForSession(player, GetOrCreateSession(player));
        }

        [ConsoleCommand("airanim.ui.ridestop")]
        private void CCmdUiRideStop(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            CmdRide(player, new[] { "ride", "stop" });
            ShowPreviewBarForSession(player, GetOrCreateSession(player));
        }

        [ConsoleCommand("airanim.ui.endsession")]
        private void CCmdUiEndSession(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            CloseSession(player.userID, true);
            Reply(player, "Editor session ended. Preview vehicles, marker entities, and CUI were cleaned up.");
        }


        [ConsoleCommand("airanim.ui.help")]
        private void CCmdUiHelp(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            session.ActiveTab = "commands";
            ShowHelp(player);
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.target")]
        private void CCmdUiTarget(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            SetSessionTarget(player, session, true);
            RebuildMarkers(player, session);
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.gotowp")]
        private void CCmdUiGoToWaypoint(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            TeleportPlayerToSelectedWaypoint(player);
        }

        [ConsoleCommand("airanim.ui.markers")]
        private void CCmdUiMarkers(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            RebuildMarkers(player, session);
            SetStatus(session, "Refreshed waypoint markers.", "Target column refreshed.");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.objects")]
        private void CCmdUiObjects(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            CmdObjects(player, new[] { "objects", "toggle" });
        }

        [ConsoleCommand("airanim.ui.timeline")]
        private void CCmdUiTimeline(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            CmdTimeline(player, new[] { "timeline", "toggle" });
        }

        [ConsoleCommand("airanim.ui.stopwaypoints")]
        private void CCmdUiStopWaypoints(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            CmdStopWaypoints(player, new[] { "stopwaypoints", "toggle" });
        }

        [ConsoleCommand("airanim.ui.save")]
        private void CCmdUiSave(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            FlushPendingAxisInput(player);
            var session = GetOrCreateSession(player);
            SaveProfiles(string.IsNullOrWhiteSpace(session.ProfileId) ? null : new[] { session.ProfileId });
            SetStatus(session, "Saved VisualProfiles.json.", "");
            Reply(player, "Saved profiles to oxide/data/PortableAirstrikes/VisualProfiles.json.");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.reload")]
        private void CCmdUiReload(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            if (HasUnsavedChanges())
            {
                ShowReloadConfirmUi(player);
                return;
            }

            PerformReloadForPlayer(player);
        }

        [ConsoleCommand("airanim.ui.reloadconfirm")]
        private void CCmdUiReloadConfirm(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            CuiHelper.DestroyUi(player, ConfirmUiName);
            PerformReloadForPlayer(player);
        }

        [ConsoleCommand("airanim.ui.reloadcancel")]
        private void CCmdUiReloadCancel(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            CuiHelper.DestroyUi(player, ConfirmUiName);
            SetStatus(GetOrCreateSession(player), "Reload cancelled.", "Unsaved changes were preserved.");
            ShowEditorUi(player);
        }

        private void PerformReloadForPlayer(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            LoadProfiles();
            var session = GetOrCreateSession(player);
            DestroyPreview(session);
            ClearNormalizeSelection(session);
            ClearPendingValueEdit(session);
            session.SelectedPayloadEvent = null;
            session.SelectedPayloadEventIndex = -1;
            session.ReleasePage = 0;
            session.WaypointPage = 0;
            RebuildMarkers(player, session);
            SetStatus(session, "Reloaded profile file.", "In-memory edits were replaced by the saved file.");
            Reply(player, "Reloaded VisualProfiles.json.");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.edit")]
        private void CCmdUiEdit(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            CmdEdit(player, new[] { "edit", arg.GetString(0) });
        }

        [ConsoleCommand("airanim.ui.preview")]
        private void CCmdUiPreview(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var profileId = arg.Args != null && arg.Args.Length >= 1 ? arg.GetString(0) : null;
            PreviewProfile(player, profileId);
        }

        [ConsoleCommand("airanim.ui.selectwp")]
        private void CCmdUiSelectWaypoint(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            int index;
            if (!TryParseWaypointIndex(arg.GetString(0), profile, out index))
            {
                Reply(player, "Invalid waypoint index.");
                return;
            }

            session.SelectedWaypointIndex = index;
            SetStatus(session, "Selected waypoint #" + DisplayIndex(index) + ".", "");
            RebuildMarkers(player, session);
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.normtoggle")]
        private void CCmdUiNormalizeToggle(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            int index;
            if (!TryParseWaypointIndex(arg.GetString(0), profile, out index))
            {
                Reply(player, "Invalid waypoint index.");
                return;
            }

            var marked = ToggleNormalizeWaypointSelection(session, profile, index);
            SetStatus(session, (marked ? "Marked" : "Unmarked") + " waypoint #" + DisplayIndex(index) + " for alignment.", "Use ALIGN MARKED… to choose one or more position and rotation fields.");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.normaxis")]
        private void CCmdUiNormalizeAxis(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            var axis = NormalizeCoordinateAxis(arg.GetString(0));
            if (string.IsNullOrWhiteSpace(axis))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            session.NormalizeAxis = axis;
            SetStatus(session, "Normalize axis set to " + axis.ToUpperInvariant() + ".", "Marked waypoints will match the active waypoint.");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.normall")]
        private void CCmdUiNormalizeAll(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            SelectAllNormalizeWaypoints(session, profile);
            SetStatus(session, "Marked all waypoints for alignment.", "Click ALIGN MARKED… to choose position and rotation fields.");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.normclear")]
        private void CCmdUiNormalizeClear(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            ClearNormalizeSelection(session);
            SetStatus(session, "Cleared waypoint normalization marks.", "");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.align.open")]
        private void CCmdAlignOpen(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            ShowAlignUi(player);
        }

        [ConsoleCommand("airanim.align.close")]
        private void CCmdAlignClose(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player != null)
            {
                CuiHelper.DestroyUi(player, AlignUiName);
            }
        }

        [ConsoleCommand("airanim.align.toggle")]
        private void CCmdAlignToggle(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            ToggleAlignField(GetOrCreateSession(player), arg.GetString(0));
            ShowAlignUi(player);
        }

        [ConsoleCommand("airanim.align.preset")]
        private void CCmdAlignPreset(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            SetAlignPreset(GetOrCreateSession(player), arg.GetString(0));
            ShowAlignUi(player);
        }

        [ConsoleCommand("airanim.align.apply")]
        private void CCmdAlignApply(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var active = GetSelectedWaypoint(session, profile);
            var marked = GetNormalizeWaypoints(session, profile);
            if (active == null || marked.Count == 0 || !HasAlignSelection(session))
            {
                SetStatus(session, "Alignment not applied.", active == null ? "Select an active reference waypoint." : marked.Count == 0 ? "Mark at least one waypoint." : "Choose at least one position or rotation field.");
                ShowAlignUi(player);
                return;
            }

            var changedWaypoints = 0;
            foreach (var waypoint in marked)
            {
                if (waypoint == null)
                {
                    continue;
                }

                var changed = false;
                if (session.AlignPositionX && Mathf.Abs(waypoint.X - active.X) > 0.0001f) { waypoint.X = active.X; changed = true; }
                if (session.AlignPositionY && Mathf.Abs(waypoint.Y - active.Y) > 0.0001f) { waypoint.Y = active.Y; changed = true; }
                if (session.AlignPositionZ && Mathf.Abs(waypoint.Z - active.Z) > 0.0001f) { waypoint.Z = active.Z; changed = true; }
                if (session.AlignRotationX && Mathf.Abs(Mathf.DeltaAngle(waypoint.RotationX, active.RotationX)) > 0.0001f) { waypoint.RotationX = active.RotationX; changed = true; }
                if (session.AlignRotationY && Mathf.Abs(Mathf.DeltaAngle(waypoint.RotationY, active.RotationY)) > 0.0001f) { waypoint.RotationY = active.RotationY; changed = true; }
                if (session.AlignRotationZ && Mathf.Abs(Mathf.DeltaAngle(waypoint.RotationZ, active.RotationZ)) > 0.0001f) { waypoint.RotationZ = active.RotationZ; changed = true; }
                if (changed)
                {
                    changedWaypoints++;
                }
            }

            NormalizeProfile(session.ProfileId, profile);
            session.SelectedWaypointIndex = profile.Waypoints.IndexOf(active);
            if (session.SelectedWaypointIndex < 0 && profile.Waypoints.Count > 0)
            {
                session.SelectedWaypointIndex = 0;
            }

            PruneNormalizeSelection(session, profile);
            RebuildMarkers(player, session);
            CuiHelper.DestroyUi(player, AlignUiName);
            SetStatus(session, "Aligned " + marked.Count + " marked waypoint(s) to active waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + ".", changedWaypoints + " waypoint(s) changed; selected position and rotation fields were copied.");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.normalize")]
        private void CCmdUiNormalizeApply(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            ApplyWaypointNormalization(player);
        }

        [ConsoleCommand("airanim.ui.prevwp")]
        private void CCmdUiPrevWaypoint(ConsoleSystem.Arg arg)
        {
            SelectRelativeWaypoint(arg, -1);
        }

        [ConsoleCommand("airanim.ui.nextwp")]
        private void CCmdUiNextWaypoint(ConsoleSystem.Arg arg)
        {
            SelectRelativeWaypoint(arg, 1);
        }

        [ConsoleCommand("airanim.ui.addwp")]
        private void CCmdUiAddWaypoint(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var newTime = Mathf.Clamp(profile.FirstPayloadDelaySeconds, 0f, Mathf.Max(0.1f, profile.DurationSeconds));
            var x = 0f;
            var y = string.Equals(profile.Vehicle, "drone", StringComparison.OrdinalIgnoreCase) ? 24f : 100f;
            var z = -25f;

            if (profile.Waypoints.Count > 0)
            {
                var source = session.SelectedWaypointIndex >= 0 && session.SelectedWaypointIndex < profile.Waypoints.Count ? profile.Waypoints[session.SelectedWaypointIndex] : profile.Waypoints[profile.Waypoints.Count - 1];
                newTime = Mathf.Clamp(source.Time + 0.5f, 0f, Mathf.Max(0.1f, profile.DurationSeconds));
                x = source.X;
                y = source.Y;
                z = source.Z + 25f;
            }

            var waypoint = new VisualProfileWaypoint { Time = newTime, X = x, Y = y, Z = z };
            profile.Waypoints.Add(waypoint);
            NormalizeProfile(session.ProfileId, profile);
            session.SelectedWaypointIndex = profile.Waypoints.IndexOf(waypoint);
            SetStatus(session, "Added waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + ".", "Use the nudge pad to place it.");
            RebuildMarkers(player, session);
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.addhere")]
        private void CCmdUiAddWaypointHere(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            BeginInsertWaypointHere(player, true);
        }

        [ConsoleCommand("airanim.ui.removewp")]
        private void CCmdUiRemoveWaypoint(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            if (session.SelectedWaypointIndex < 0 || session.SelectedWaypointIndex >= profile.Waypoints.Count)
            {
                Reply(player, "No waypoint is selected.");
                return;
            }

            var removed = session.SelectedWaypointIndex;
            profile.Waypoints.RemoveAt(session.SelectedWaypointIndex);
            NormalizeProfile(session.ProfileId, profile);
            session.SelectedWaypointIndex = profile.Waypoints.Count == 0 ? -1 : Mathf.Clamp(removed, 0, profile.Waypoints.Count - 1);
            SetStatus(session, "Removed waypoint #" + DisplayIndex(removed) + ".", "");
            RebuildMarkers(player, session);
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.nudge")]
        private void CCmdUiNudge(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 2)
            {
                return;
            }

            float meters;
            if (!TryParseFloat(arg.GetString(1), out meters))
            {
                return;
            }

            NudgeSelectedWaypoint(player, arg.GetString(0), meters, false);
        }

        [ConsoleCommand("airanim.ui.setpos")]
        private void CCmdUiSetPosition(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            if (!TryGetAxisInput(arg, out var axis, out var value))
            {
                ShowInputSubmitWarning(player, "position");
                return;
            }

            QueueSelectedWaypointInput(player, axis, value, false, false);
        }

        [ConsoleCommand("airanim.ui.setrot")]
        private void CCmdUiSetRotation(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            if (!TryGetAxisInput(arg, out var axis, out var value))
            {
                ShowInputSubmitWarning(player, "rotation");
                return;
            }

            QueueSelectedWaypointInput(player, axis, value, true, false);
        }

        [ConsoleCommand("airanim.ui.setposx")]
        private void CCmdUiSetPositionX(ConsoleSystem.Arg arg)
        {
            ApplyAxisOnlyInput(arg, "x", false, false);
        }

        [ConsoleCommand("airanim.ui.setposy")]
        private void CCmdUiSetPositionY(ConsoleSystem.Arg arg)
        {
            ApplyAxisOnlyInput(arg, "y", false, false);
        }

        [ConsoleCommand("airanim.ui.setposz")]
        private void CCmdUiSetPositionZ(ConsoleSystem.Arg arg)
        {
            ApplyAxisOnlyInput(arg, "z", false, false);
        }

        [ConsoleCommand("airanim.ui.setrotx")]
        private void CCmdUiSetRotationX(ConsoleSystem.Arg arg)
        {
            ApplyAxisOnlyInput(arg, "x", true, false);
        }

        [ConsoleCommand("airanim.ui.setroty")]
        private void CCmdUiSetRotationY(ConsoleSystem.Arg arg)
        {
            ApplyAxisOnlyInput(arg, "y", true, false);
        }

        [ConsoleCommand("airanim.ui.setrotz")]
        private void CCmdUiSetRotationZ(ConsoleSystem.Arg arg)
        {
            ApplyAxisOnlyInput(arg, "z", true, false);
        }

        [ConsoleCommand("airanim.ui.profiledelta")]
        private void CCmdUiProfileDelta(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 2)
            {
                return;
            }

            float delta;
            if (!TryParseFloat(arg.GetString(1), out delta))
            {
                return;
            }

            var field = arg.GetString(0).ToLowerInvariant();
            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            ApplyProfileDelta(profile, field, delta);
            NormalizeProfile(session.ProfileId, profile);
            RebuildMarkers(player, session);
            SetStatus(session, "Adjusted " + field + ".", "");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.vehicle")]
        private void CCmdUiVehicle(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var requested = arg.Args != null && arg.Args.Length >= 1 ? arg.GetString(0) : "next";
            if (string.Equals(requested, "next", StringComparison.OrdinalIgnoreCase))
            {
                profile.Vehicle = GetNextVehicle(profile.Vehicle);
            }
            else
            {
                var vehicle = NormalizeVehicle(requested);
                if (!string.IsNullOrWhiteSpace(vehicle))
                {
                    profile.Vehicle = vehicle;
                }
            }

            NormalizeProfile(session.ProfileId, profile);
            DestroyPreview(session);
            RebuildMarkers(player, session);
            SetStatus(session, "Vehicle changed to " + profile.Vehicle + ".", "");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.quickcreate")]
        private void CCmdUiQuickCreate(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var vehicle = arg.Args != null && arg.Args.Length >= 1 ? NormalizeVehicle(arg.GetString(0)) : "f15";
            if (string.IsNullOrWhiteSpace(vehicle))
            {
                vehicle = "f15";
            }

            var baseId = vehicle + "_custom_run";
            var id = baseId;
            var suffix = 2;
            while (profileFile.Profiles.ContainsKey(id))
            {
                id = baseId + "_" + suffix;
                suffix++;
            }

            CmdCreate(player, new[] { "create", id, vehicle });
        }

        [ConsoleCommand("airanim.ui.deleteprompt")]
        private void CCmdUiDeletePrompt(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            if (string.IsNullOrWhiteSpace(session.ProfileId))
            {
                return;
            }

            ShowDeleteConfirmUi(player, session.ProfileId);
        }

        [ConsoleCommand("airanim.ui.deleteconfirm")]
        private void CCmdUiDeleteConfirm(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            CuiHelper.DestroyUi(player, ConfirmUiName);
            CmdDelete(player, new[] { "delete", arg.GetString(0) });
        }

        [ConsoleCommand("airanim.ui.deletecancel")]
        private void CCmdUiDeleteCancel(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, ConfirmUiName);
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.insert.cancel")]
        private void CCmdInsertCancel(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, InsertUiName);
            EditorSession session;
            if (sessions.TryGetValue(player.userID, out session) && session != null)
            {
                session.PendingWaypoint = null;
                session.InsertUiOpen = false;
                SetStatus(session, "Insert waypoint canceled.", "");
            }

            RefreshEditorUiIfOpen(player);
        }

        [ConsoleCommand("airanim.insert.commit")]
        private void CCmdInsertCommit(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            int insertIndex;
            if (!int.TryParse(arg.GetString(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out insertIndex))
            {
                return;
            }

            CommitPendingWaypointInsert(player, insertIndex);
        }

        [ConsoleCommand("airanim.timeline.select")]
        private void CCmdTimelineSelect(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            int index;
            if (!int.TryParse(arg.GetString(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out index) || index < 0 || index >= profile.Waypoints.Count)
            {
                return;
            }

            session.SelectedWaypointIndex = index;
            SetStatus(session, "Selected waypoint #" + DisplayIndex(index) + ".", "");
            RebuildMarkers(player, session);
            RefreshOpenEditorSurfaces(player);
        }

        [ConsoleCommand("airanim.timeline.move")]
        private void CCmdTimelineMove(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 2)
            {
                return;
            }

            int index;
            int delta;
            if (!int.TryParse(arg.GetString(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out index) || !int.TryParse(arg.GetString(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out delta))
            {
                return;
            }

            ApplyTimelineMove(player, index, delta);
        }

        [ConsoleCommand("airanim.timeline.segment")]
        private void CCmdTimelineSegment(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 2)
            {
                return;
            }

            int index;
            float delta;
            if (!int.TryParse(arg.GetString(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out index) || !TryParseFloat(arg.GetString(1), out delta))
            {
                return;
            }

            ApplyTimelineSegmentDelta(player, index, delta);
        }

        [ConsoleCommand("airanim.timeline.duration")]
        private void CCmdTimelineDuration(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            int index;
            if (!int.TryParse(arg.GetString(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                return;
            }

            OpenWaypointDurationEdit(player, index, false);
        }

        [ConsoleCommand("airanim.timeline.payload")]
        private void CCmdTimelinePayload(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            float delta;
            if (!TryParseFloat(arg.GetString(0), out delta))
            {
                return;
            }

            ApplyTimelinePayloadDelta(player, delta);
        }

        [ConsoleCommand("airanim.timeline.payloadwp")]
        private void CCmdTimelinePayloadWaypoint(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            int index;
            if (!int.TryParse(arg.GetString(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                return;
            }

            ApplyTimelinePayloadAtWaypoint(player, index);
        }

        [ConsoleCommand("airanim.release.add")]
        private void CCmdReleaseAdd(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var time = profile.FirstPayloadDelaySeconds;
            if (arg.Args != null && arg.Args.Length >= 1)
            {
                TryParseFloat(arg.GetString(0), out time);
            }

            AddPayloadReleaseAt(player, Mathf.Clamp(time, 0f, profile.DurationSeconds), true);
        }

        [ConsoleCommand("airanim.release.edit")]
        private void CCmdReleaseEdit(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            int index;
            if (!int.TryParse(arg.GetString(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                return;
            }

            OpenPayloadReleasePopup(player, index);
        }

        [ConsoleCommand("airanim.release.close")]
        private void CCmdReleaseClose(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, ReleaseUiName);
            var session = GetOrCreateSession(player);
            session.ReleaseUiOpen = false;
            session.PatternTemplateUiOpen = false;
        }

        [ConsoleCommand("airanim.release.prev")]
        private void CCmdReleasePrev(ConsoleSystem.Arg arg)
        {
            SelectRelativePayloadRelease(arg, -1);
        }

        [ConsoleCommand("airanim.release.next")]
        private void CCmdReleaseNext(ConsoleSystem.Arg arg)
        {
            SelectRelativePayloadRelease(arg, 1);
        }

        [ConsoleCommand("airanim.release.dup")]
        private void CCmdReleaseDuplicate(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }
            var selected = GetSelectedPayloadEvent(session, profile);
            DuplicatePayloadRelease(player, selected == null ? -1 : profile.PayloadEvents.IndexOf(selected));
        }

        [ConsoleCommand("airanim.release.delete")]
        private void CCmdReleaseDelete(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }
            var selected = GetSelectedPayloadEvent(session, profile);
            DeletePayloadRelease(player, selected == null ? -1 : profile.PayloadEvents.IndexOf(selected));
        }

        [ConsoleCommand("airanim.release.save")]
        private void CCmdReleaseSave(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            SaveProfiles(string.IsNullOrWhiteSpace(session.ProfileId) ? null : new[] { session.ProfileId });
            SetStatus(session, "Saved VisualProfiles.json.", "Release event changes persisted.");
            RefreshOpenEditorSurfaces(player);
        }

        [ConsoleCommand("airanim.release.payload")]
        private void CCmdReleasePayload(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var ev = GetSelectedPayloadEvent(session, profile);
            if (ev == null)
            {
                return;
            }

            ev.Payload = GetNextPayload(ev.Payload);
            NormalizeProfileKeepingRelease(session, profile, ev);
            SetStatus(session, "Release #" + DisplayIndex(session.SelectedPayloadEventIndex) + " payload set to " + GetPayloadDisplay(ev.Payload) + ".", "");
            RefreshOpenEditorSurfaces(player);
        }

        [ConsoleCommand("airanim.release.value")]
        private void CCmdReleaseValue(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 2)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var selected = GetSelectedPayloadEvent(session, profile);
            ApplyPayloadReleaseField(player, profile, selected == null ? -1 : profile.PayloadEvents.IndexOf(selected), arg.GetString(0), GetArgTail(arg, 1), false);
        }

        [ConsoleCommand("airanim.timeline.scroll")]
        private void CCmdTimelineScroll(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player) || arg.Args == null || arg.Args.Length < 1)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var total = GetTimelineTotalDuration(profile);
            var maxOffset = Mathf.Max(0f, GetTimelineContentWidth(profile, total) - TimelineViewportWidthPixels);
            var requested = arg.GetString(0).Trim().ToLowerInvariant();
            if (requested == "home" || requested == "start" || requested == "first")
            {
                session.TimelineScrollOffset = 0f;
            }
            else if (requested == "end" || requested == "last")
            {
                session.TimelineScrollOffset = maxOffset;
            }
            else
            {
                float delta;
                if (!TryParseFloat(requested, out delta))
                {
                    return;
                }

                session.TimelineScrollOffset = Mathf.Clamp(session.TimelineScrollOffset + delta, 0f, maxOffset);
            }

            RefreshOpenEditorSurfaces(player);
        }

        private void SelectRelativeWaypoint(ConsoleSystem.Arg arg, int delta)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            if (profile.Waypoints.Count == 0)
            {
                Reply(player, "No waypoints exist in this profile.");
                return;
            }

            if (session.SelectedWaypointIndex < 0 || session.SelectedWaypointIndex >= profile.Waypoints.Count)
            {
                session.SelectedWaypointIndex = 0;
            }
            else
            {
                session.SelectedWaypointIndex = (session.SelectedWaypointIndex + delta + profile.Waypoints.Count) % profile.Waypoints.Count;
            }

            SetStatus(session, "Selected waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + ".", "");
            RebuildMarkers(player, session);
            ShowEditorUi(player);
        }

        private void SelectRelativeWaypointForPopup(ConsoleSystem.Arg arg, int delta)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            if (profile.Waypoints.Count == 0)
            {
                Reply(player, "No waypoints exist in this profile.");
                return;
            }

            if (session.SelectedWaypointIndex < 0 || session.SelectedWaypointIndex >= profile.Waypoints.Count)
            {
                session.SelectedWaypointIndex = 0;
            }
            else
            {
                session.SelectedWaypointIndex = (session.SelectedWaypointIndex + delta + profile.Waypoints.Count) % profile.Waypoints.Count;
            }

            SetStatus(session, "Selected waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + ".", "");
            RebuildMarkers(player, session);
            RefreshEditorUiIfOpen(player);
            ShowWaypointPopupUi(player);
        }

        private void BeginInsertWaypointHere(BasePlayer player, bool showMenu)
        {
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            EnsureSessionTarget(player, session, false);
            PendingWaypointCapture capture;
            if (!TryCaptureWaypointFromPlayer(player, session, profile, out capture))
            {
                Reply(player, "Could not capture your current eye position and view direction.");
                return;
            }

            session.PendingWaypoint = capture;
            session.InsertUiOpen = true;
            SetStatus(session, "Captured a new waypoint from your current view.", "Choose its order position.");
            if (showMenu)
            {
                ShowInsertWaypointUi(player);
            }
        }

        private bool TryCaptureWaypointFromPlayer(BasePlayer player, EditorSession session, VisualProfileConfig profile, out PendingWaypointCapture capture)
        {
            capture = null;
            if (player == null || player.eyes == null || session == null)
            {
                return false;
            }

            var ray = player.eyes.HeadRay();
            var world = ray.origin;
            var local = WorldToLocal(session, world);
            var forward = NormalizeMarkerDirection(ray.direction, session.Approach);
            var waypoint = new VisualProfileWaypoint
            {
                Time = GetDefaultInsertedWaypointTime(profile, session),
                X = local.x,
                Y = local.y,
                Z = local.z
            };

            capture = new PendingWaypointCapture
            {
                Waypoint = waypoint,
                WorldPosition = world,
                DesiredForward = forward
            };
            return true;
        }

        private void ShowInsertWaypointUi(BasePlayer player)
        {
            if (player == null || !player.IsConnected || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile) || session.PendingWaypoint == null || session.PendingWaypoint.Waypoint == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, InsertUiName);

            var count = profile.Waypoints == null ? 0 : profile.Waypoints.Count;
            var selected = count == 0 ? 0 : Mathf.Clamp(session.SelectedWaypointIndex, 0, count - 1);
            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.035 0.040 0.050 0.975" },
                RectTransform = { AnchorMin = "0.335 0.335", AnchorMax = "0.665 0.765" }
            }, "Overlay", InsertUiName);

            AddPanel(container, root, "0.035 0.850", "0.965 0.965", "0.09 0.10 0.12 0.96");
            AddLabel(container, root, "Insert Captured Waypoint", 16, TextAnchor.MiddleLeft, "0.065 0.895", "0.760 0.945", "1 0.86 0.58 1");
            AddButton(container, root, "X", "airanim.insert.cancel", "0.865 0.895", "0.935 0.945", "0.55 0.12 0.10 0.95", 14);

            var pending = session.PendingWaypoint.Waypoint;
            AddLabel(container, root, "Captured eye position: " + FormatPosition(session.PendingWaypoint.WorldPosition), 10, TextAnchor.MiddleLeft, "0.065 0.795", "0.935 0.835", "0.78 0.86 0.92 1");
            AddLabel(container, root, "Local X " + FormatFloat(pending.X) + "   Y " + FormatFloat(pending.Y) + "   Z " + FormatFloat(pending.Z), 10, TextAnchor.MiddleLeft, "0.065 0.755", "0.935 0.795", "0.58 0.66 0.72 1");

            if (count == 0)
            {
                AddButton(container, root, "CREATE FIRST WAYPOINT", "airanim.insert.commit 0", "0.065 0.610", "0.935 0.690", "0.16 0.30 0.20 0.95", 12);
            }
            else
            {
                AddButton(container, root, "FIRST", "airanim.insert.commit 0", "0.065 0.665", "0.250 0.725", "0.16 0.30 0.20 0.95", 10);
                AddButton(container, root, "BEFORE #" + DisplayIndex(selected), "airanim.insert.commit " + selected, "0.270 0.665", "0.465 0.725", "0.12 0.24 0.30 0.95", 10);
                AddButton(container, root, "AFTER #" + DisplayIndex(selected), "airanim.insert.commit " + (selected + 1), "0.485 0.665", "0.680 0.725", "0.12 0.24 0.30 0.95", 10);
                AddButton(container, root, "LAST", "airanim.insert.commit " + count, "0.700 0.665", "0.935 0.725", "0.16 0.30 0.20 0.95", 10);

                var rows = count + 1;
                var contentHeight = Math.Max(210f, 6f + rows * 38f);
                var scroll = AddScrollView(container, root, "0.065 0.135", "0.935 0.625", contentHeight, true);
                for (var slot = 0; slot <= count; slot++)
                {
                    var top = 6f + slot * 38f;
                    var bottom = top + 32f;
                    var row = AddOffsetPanel(container, scroll, top, bottom, slot == selected || slot == selected + 1 ? "0.18 0.13 0.09 0.94" : "0.10 0.12 0.145 0.88");
                    var text = slot == 0 ? "Slot 1: before #" + DisplayIndex(0) : slot >= count ? "Slot " + DisplayIndex(slot) + ": after #" + DisplayIndex(count - 1) : "Slot " + DisplayIndex(slot) + ": between #" + DisplayIndex(slot - 1) + " and #" + DisplayIndex(slot);
                    AddLabel(container, row, text, 10, TextAnchor.MiddleLeft, "0.035 0.18", "0.700 0.84", "0.82 0.90 0.96 1");
                    AddButton(container, row, "INSERT", "airanim.insert.commit " + slot, "0.735 0.18", "0.965 0.84", "0.18 0.24 0.30 0.95", 8);
                }
            }

            AddButton(container, root, "CANCEL", "airanim.insert.cancel", "0.065 0.055", "0.935 0.115", "0.18 0.22 0.28 0.95", 10);
            RegisterUiBridge(player, InsertUiName);
            CuiHelper.AddUi(player, container);
            session.InsertUiOpen = true;
        }

        private void CommitPendingWaypointInsert(BasePlayer player, int insertIndex)
        {
            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile) || session.PendingWaypoint == null || session.PendingWaypoint.Waypoint == null)
            {
                return;
            }

            var pending = session.PendingWaypoint;
            var waypoint = pending.Waypoint;
            InsertWaypointAtIndex(profile, waypoint, insertIndex);
            NormalizeProfile(session.ProfileId, profile);
            ApplyCapturedWaypointRotation(session, profile, waypoint, pending.DesiredForward);
            NormalizeProfile(session.ProfileId, profile);

            session.SelectedWaypointIndex = profile.Waypoints.IndexOf(waypoint);
            session.PendingWaypoint = null;
            session.InsertUiOpen = false;
            CuiHelper.DestroyUi(player, InsertUiName);

            SetStatus(session, "Inserted waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + " from your current view.", "Use timeline or nudge controls to fine tune.");
            RebuildMarkers(player, session);
            RefreshOpenEditorSurfaces(player);
        }

        private void InsertWaypointAtIndex(VisualProfileConfig profile, VisualProfileWaypoint waypoint, int insertIndex)
        {
            if (profile == null || waypoint == null)
            {
                return;
            }

            NormalizeProfile("", profile);
            if (profile.Waypoints == null)
            {
                profile.Waypoints = new List<VisualProfileWaypoint>();
            }

            var ordered = new List<VisualProfileWaypoint>(profile.Waypoints);
            insertIndex = Mathf.Clamp(insertIndex, 0, ordered.Count);

            if (ordered.Count == 0)
            {
                waypoint.Time = 0f;
                ordered.Add(waypoint);
                profile.DurationSeconds = Mathf.Max(profile.DurationSeconds, InsertWaypointDefaultSegmentSeconds);
                profile.Waypoints = ordered;
                return;
            }

            if (insertIndex <= 0)
            {
                waypoint.Time = 0f;
                foreach (var existing in ordered)
                {
                    existing.Time += InsertWaypointDefaultSegmentSeconds;
                }

                profile.DurationSeconds += InsertWaypointDefaultSegmentSeconds;
            }
            else if (insertIndex >= ordered.Count)
            {
                waypoint.Time = ordered[ordered.Count - 1].Time + InsertWaypointDefaultSegmentSeconds;
                profile.DurationSeconds = Mathf.Max(profile.DurationSeconds, waypoint.Time);
            }
            else
            {
                var previous = ordered[insertIndex - 1];
                var next = ordered[insertIndex];
                var gap = next.Time - previous.Time;
                if (gap >= InsertWaypointDefaultSegmentSeconds * 1.75f)
                {
                    waypoint.Time = previous.Time + gap * 0.5f;
                }
                else
                {
                    waypoint.Time = previous.Time + InsertWaypointDefaultSegmentSeconds;
                    var requiredNext = waypoint.Time + TimelineMinimumSegmentSeconds;
                    var shift = Mathf.Max(0f, requiredNext - next.Time);
                    if (shift > 0f)
                    {
                        for (var i = insertIndex; i < ordered.Count; i++)
                        {
                            ordered[i].Time += shift;
                        }

                        profile.DurationSeconds += shift;
                    }
                }
            }

            ordered.Insert(insertIndex, waypoint);
            profile.Waypoints = ordered;
            if (profile.Waypoints.Count > 0)
            {
                profile.DurationSeconds = Mathf.Max(profile.DurationSeconds, profile.Waypoints[profile.Waypoints.Count - 1].Time);
            }
        }

        private void ApplyCapturedWaypointRotation(EditorSession session, VisualProfileConfig profile, VisualProfileWaypoint waypoint, Vector3 desiredForward)
        {
            if (session == null || profile == null || waypoint == null)
            {
                return;
            }

            var plan = BuildWorldWaypoints(session, profile);
            var planIndex = -1;
            for (var i = 0; i < plan.Count; i++)
            {
                if (ReferenceEquals(plan[i].Local, waypoint))
                {
                    planIndex = i;
                    break;
                }
            }

            if (planIndex < 0)
            {
                return;
            }

            var tangent = GetWaypointMarkerDirection(plan, profile, planIndex, session.Approach);
            var baseRotation = GetWaypointMarkerRotation(tangent);
            var desiredRotation = GetWaypointMarkerRotation(NormalizeMarkerDirection(desiredForward, tangent));
            var offset = Quaternion.Inverse(baseRotation) * desiredRotation;
            var euler = offset.eulerAngles;
            waypoint.RotationX = NormalizeDegrees(euler.x);
            waypoint.RotationY = NormalizeDegrees(euler.y);
            waypoint.RotationZ = NormalizeDegrees(euler.z);
        }

        private void ApplyTimelineMove(BasePlayer player, int index, int delta)
        {
            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile) || profile.Waypoints == null || profile.Waypoints.Count < 2)
            {
                return;
            }

            NormalizeProfile(session.ProfileId, profile);
            index = Mathf.Clamp(index, 0, profile.Waypoints.Count - 1);
            var nextIndex = Mathf.Clamp(index + delta, 0, profile.Waypoints.Count - 1);
            if (nextIndex == index)
            {
                return;
            }

            var ordered = new List<VisualProfileWaypoint>(profile.Waypoints);
            var segments = GetSegmentDurations(profile);
            var waypoint = ordered[index];
            ordered.RemoveAt(index);
            ordered.Insert(nextIndex, waypoint);
            ApplyOrderedWaypointsWithSegments(profile, ordered, segments, profile.DurationSeconds);
            NormalizeProfile(session.ProfileId, profile);
            session.SelectedWaypointIndex = nextIndex;
            SetStatus(session, "Moved waypoint #" + DisplayIndex(index) + " to position #" + DisplayIndex(nextIndex) + ".", "Timeline order updated.");
            RebuildMarkers(player, session);
            RefreshOpenEditorSurfaces(player);
        }

        private void ApplyTimelineSegmentDelta(BasePlayer player, int index, float delta)
        {
            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile) || profile.Waypoints == null || profile.Waypoints.Count < 2)
            {
                return;
            }

            NormalizeProfile(session.ProfileId, profile);
            if (index < 0 || index >= profile.Waypoints.Count - 1)
            {
                return;
            }

            var current = Mathf.Max(TimelineMinimumSegmentSeconds, profile.Waypoints[index + 1].Time - profile.Waypoints[index].Time);
            float appliedDuration;
            if (!SetWaypointSegmentDuration(profile, index, current + delta, out appliedDuration))
            {
                return;
            }

            NormalizeProfile(session.ProfileId, profile);
            session.SelectedWaypointIndex = Mathf.Clamp(index, 0, profile.Waypoints.Count - 1);
            SetStatus(session, "Adjusted segment after waypoint #" + DisplayIndex(index) + " to " + FormatSeconds(appliedDuration) + ".", "Total duration is now " + FormatSeconds(profile.DurationSeconds) + ".");
            RebuildMarkers(player, session);
            RefreshOpenEditorSurfaces(player);
        }

        private void ApplyTimelinePayloadDelta(BasePlayer player, float delta)
        {
            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            NormalizePayloadEvents(profile);
            if (IsRepeatedPatternMode(profile))
            {
                profile.FirstPayloadDelaySeconds = Mathf.Clamp(profile.FirstPayloadDelaySeconds + delta, 0f, Mathf.Max(0.1f, profile.DurationSeconds));
                NormalizeProfile(session.ProfileId, profile);
                SetStatus(session, "Repeated pattern start moved to " + FormatSeconds(profile.FirstPayloadDelaySeconds) + ".", "");
                RefreshOpenEditorSurfaces(player);
                return;
            }

            if (profile.PayloadEvents != null && profile.PayloadEvents.Count > 0)
            {
                var selected = GetSelectedPayloadEvent(session, profile) ?? profile.PayloadEvents[0];
                selected.Time = Mathf.Clamp(selected.Time + delta, 0f, Mathf.Max(0.1f, profile.DurationSeconds));
                NormalizeProfileKeepingRelease(session, profile, selected);
                SetStatus(session, "Release #" + DisplayIndex(session.SelectedPayloadEventIndex) + " moved to " + FormatSeconds(selected.Time) + ".", "");
                RefreshOpenEditorSurfaces(player);
                return;
            }

            profile.FirstPayloadDelaySeconds = Mathf.Clamp(profile.FirstPayloadDelaySeconds + delta, 0f, Mathf.Max(0.1f, profile.DurationSeconds));
            NormalizeProfile(session.ProfileId, profile);
            SetStatus(session, "Payload start set to " + FormatSeconds(profile.FirstPayloadDelaySeconds) + ".", "");
            RefreshOpenEditorSurfaces(player);
        }

        private void ApplyTimelinePayloadAtWaypoint(BasePlayer player, int index)
        {
            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile) || profile.Waypoints == null || profile.Waypoints.Count == 0)
            {
                return;
            }

            index = Mathf.Clamp(index, 0, profile.Waypoints.Count - 1);
            session.SelectedWaypointIndex = index;
            AddPayloadReleaseAt(player, Mathf.Clamp(profile.Waypoints[index].Time, 0f, Mathf.Max(0.1f, profile.DurationSeconds)), true);
        }

        private void AddPayloadReleaseAt(BasePlayer player, float time, bool openPopup)
        {
            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            if (profile.PayloadEvents == null)
            {
                profile.PayloadEvents = new List<VisualPayloadEvent>();
            }

            if (profile.PayloadEvents.Count >= MaxPayloadEventsInProfile)
            {
                SetStatus(session, "Manual release limit reached.", "A profile can contain up to " + MaxPayloadEventsInProfile + " manual events; use Repeated Pattern for larger schedules.");
                RefreshOpenEditorSurfaces(player);
                return;
            }

            NormalizePayloadEvents(profile);
            var release = CreatePayloadEventFromTemplate(profile, Mathf.Clamp(time, 0f, profile.DurationSeconds), session.SelectedPayloadEventIndex);
            profile.PayloadEvents.Add(release);
            profile.PayloadReleaseMode = "manual";
            NormalizeProfileKeepingRelease(session, profile, release);

            SetStatus(session, "Added release #" + DisplayIndex(session.SelectedPayloadEventIndex) + " at " + FormatSeconds(release.Time) + ".", "");
            RefreshOpenEditorSurfaces(player);
            if (openPopup && !session.UiOpen)
            {
                ShowPayloadReleasePopupUi(player);
            }
        }

        private void OpenPayloadReleasePopup(BasePlayer player, int index)
        {
            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            NormalizeProfile(session.ProfileId, profile);
            if (profile.PayloadEvents == null || profile.PayloadEvents.Count == 0)
            {
                AddPayloadReleaseAt(player, profile.FirstPayloadDelaySeconds, true);
                return;
            }

            index = Mathf.Clamp(index, 0, profile.PayloadEvents.Count - 1);
            SetSelectedPayloadEvent(session, profile, profile.PayloadEvents[index]);
            session.ActiveTab = "releases";
            SetStatus(session, "Editing release #" + DisplayIndex(session.SelectedPayloadEventIndex) + ".", "");
            RefreshTimelineUiIfOpen(player);
            if (session.UiOpen)
            {
                ShowEditorUi(player);
            }
            else
            {
                ShowPayloadReleasePopupUi(player);
            }
        }

        private void SelectRelativePayloadRelease(ConsoleSystem.Arg arg, int delta)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            NormalizeProfile(session.ProfileId, profile);
            if (profile.PayloadEvents == null || profile.PayloadEvents.Count == 0)
            {
                return;
            }

            var current = GetSelectedPayloadEvent(session, profile);
            var currentIndex = current == null ? 0 : profile.PayloadEvents.IndexOf(current);
            var nextIndex = (currentIndex + delta + profile.PayloadEvents.Count) % profile.PayloadEvents.Count;
            SetSelectedPayloadEvent(session, profile, profile.PayloadEvents[nextIndex]);

            SetStatus(session, "Selected release #" + DisplayIndex(session.SelectedPayloadEventIndex) + ".", "");
            RefreshOpenEditorSurfaces(player);
        }

        private void DuplicatePayloadRelease(BasePlayer player, int index)
        {
            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            NormalizeProfile(session.ProfileId, profile);
            if (profile.PayloadEvents == null || profile.PayloadEvents.Count == 0)
            {
                AddPayloadReleaseAt(player, profile.FirstPayloadDelaySeconds, true);
                return;
            }

            index = Mathf.Clamp(index, 0, profile.PayloadEvents.Count - 1);
            var copy = ClonePayloadEvent(profile.PayloadEvents[index]);
            copy.Time = Mathf.Clamp(copy.Time + Mathf.Max(0.05f, profile.PayloadReleaseIntervalSeconds), 0f, profile.DurationSeconds);
            profile.PayloadEvents.Add(copy);
            NormalizeProfileKeepingRelease(session, profile, copy);

            SetStatus(session, "Duplicated release #" + DisplayIndex(index) + ".", "New release is #" + DisplayIndex(session.SelectedPayloadEventIndex) + ".");
            RefreshOpenEditorSurfaces(player);
            if (!session.UiOpen)
            {
                ShowPayloadReleasePopupUi(player);
            }
        }

        private void DeletePayloadRelease(BasePlayer player, int index)
        {
            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile) || profile.PayloadEvents == null || profile.PayloadEvents.Count == 0)
            {
                return;
            }

            if (index < 0 || index >= profile.PayloadEvents.Count)
            {
                index = Mathf.Clamp(session.SelectedPayloadEventIndex, 0, profile.PayloadEvents.Count - 1);
            }

            profile.PayloadEvents.RemoveAt(index);
            NormalizeProfile(session.ProfileId, profile);
            if (profile.PayloadEvents.Count == 0)
            {
                SetSelectedPayloadEvent(session, profile, null);
            }
            else
            {
                SetSelectedPayloadEvent(session, profile, profile.PayloadEvents[Mathf.Clamp(index, 0, profile.PayloadEvents.Count - 1)]);
            }
            SetStatus(session, "Deleted release #" + DisplayIndex(index) + ".", profile.PayloadEvents.Count == 0 ? "Add a new manual release or switch to Repeated Pattern." : "");
            if (profile.PayloadEvents.Count == 0)
            {
                CuiHelper.DestroyUi(player, ReleaseUiName);
                session.ReleaseUiOpen = false;
            }

            RefreshOpenEditorSurfaces(player);
        }

        private bool ApplyPayloadReleaseField(BasePlayer player, VisualProfileConfig profile, int index, string field, string value, bool reply)
        {
            var session = GetOrCreateSession(player);
            if (profile == null || profile.PayloadEvents == null || index < 0 || index >= profile.PayloadEvents.Count)
            {
                return false;
            }

            var ev = profile.PayloadEvents[index];
            var key = NormalizePayloadReleaseField(field);
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (key == "payload")
            {
                ev.Payload = NormalizePayload(value);
            }
            else
            {
                float parsed;
                if (!TryParseInputFloat(value, key, out parsed))
                {
                    if (reply)
                    {
                        Reply(player, "Invalid value for release field '" + field + "'.");
                    }
                    return false;
                }

                SetPayloadReleaseNumericField(ev, key, parsed);
            }

            NormalizeProfileKeepingRelease(session, profile, ev);
            SetStatus(session, "Updated release #" + DisplayIndex(session.SelectedPayloadEventIndex) + " " + key + ".", "");
            RefreshOpenEditorSurfaces(player);
            return true;
        }

        private void OpenPayloadReleaseValueEdit(BasePlayer player, string field, bool fromPopup)
        {
            var key = NormalizePayloadReleaseField(field);
            if (player == null || string.IsNullOrWhiteSpace(key) || key == "payload")
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            NormalizeProfile(session.ProfileId, profile);
            if (profile.PayloadEvents == null || profile.PayloadEvents.Count == 0)
            {
                AddPayloadReleaseAt(player, profile.FirstPayloadDelaySeconds, true);
                return;
            }

            var selectedRelease = GetSelectedPayloadEvent(session, profile);
            if (selectedRelease == null)
            {
                return;
            }
            CancelPendingAxisInput(session);
            session.PendingValueEdit = new PendingValueEdit
            {
                ProfileId = session.ProfileId,
                ReleaseEvent = true,
                PayloadEvent = selectedRelease,
                ReleaseField = key,
                FromPopup = fromPopup
            };
            session.ValueEditUiOpen = true;
            SetStatus(session, "Editing release #" + DisplayIndex(session.SelectedPayloadEventIndex) + " " + key + ".", "Use the keypad buttons, then click APPLY.");
            ShowValueEditUi(player);
        }

        private bool TeleportPlayerToSelectedWaypoint(BasePlayer player)
        {
            if (player == null || !CanUse(player))
            {
                return false;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return false;
            }

            var waypoint = GetSelectedWaypoint(session, profile);
            if (waypoint == null)
            {
                Reply(player, "No waypoint is selected.");
                return false;
            }

            EnsureSessionTarget(player, session, false);
            var destination = GetWaypointWorldPosition(session, profile, waypoint);
            if (player.isMounted)
            {
                player.EnsureDismounted();
            }

            player.Teleport(destination);
            SetStatus(session, "Teleported to waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + ".", "World " + FormatPosition(destination) + ".");
            Reply(player, "Teleported to waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + " at " + FormatPosition(destination) + ".");
            RebuildMarkers(player, session);
            RefreshOpenEditorSurfaces(player);
            return true;
        }

        private Vector3 GetWaypointWorldPosition(EditorSession session, VisualProfileConfig profile, VisualProfileWaypoint waypoint)
        {
            return EnsurePositionAboveTerrain(LocalToWorld(session, waypoint), GetProfileClearance(profile));
        }

        private List<float> GetSegmentDurations(VisualProfileConfig profile)
        {
            var segments = new List<float>();
            if (profile == null || profile.Waypoints == null)
            {
                return segments;
            }

            for (var i = 0; i < profile.Waypoints.Count - 1; i++)
            {
                segments.Add(Mathf.Max(TimelineMinimumSegmentSeconds, profile.Waypoints[i + 1].Time - profile.Waypoints[i].Time));
            }

            return segments;
        }

        private void ApplyOrderedWaypointsWithSegments(VisualProfileConfig profile, List<VisualProfileWaypoint> ordered, List<float> segments, float previousDuration)
        {
            if (profile == null || ordered == null || ordered.Count == 0)
            {
                return;
            }

            ordered[0].Time = 0f;
            var elapsed = 0f;
            for (var i = 1; i < ordered.Count; i++)
            {
                var segment = i - 1 < segments.Count ? segments[i - 1] : InsertWaypointDefaultSegmentSeconds;
                elapsed += Mathf.Max(TimelineMinimumSegmentSeconds, segment);
                ordered[i].Time = elapsed;
            }

            profile.Waypoints = ordered;
            profile.DurationSeconds = Mathf.Clamp(Mathf.Max(previousDuration, elapsed), 0.5f, 120f);
            profile.FirstPayloadDelaySeconds = Mathf.Clamp(profile.FirstPayloadDelaySeconds, 0f, profile.DurationSeconds);
        }

        private void ShowReloadConfirmUi(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, ConfirmUiName);
            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.04 0.04 0.05 0.98" },
                RectTransform = { AnchorMin = "0.34 0.36", AnchorMax = "0.66 0.64" }
            }, "Overlay", ConfirmUiName);

            AddLabel(container, root, "Discard unsaved changes?", 16, TextAnchor.MiddleLeft, "0.08 0.72", "0.92 0.90", "1 0.72 0.58 1");
            AddLabel(container, root, "Reloading replaces all in-memory profile edits with the saved VisualProfiles.json file.", 11, TextAnchor.MiddleLeft, "0.08 0.45", "0.92 0.68", "0.80 0.86 0.90 1");
            AddButton(container, root, "DISCARD & RELOAD", "airanim.ui.reloadconfirm", "0.08 0.15", "0.48 0.34", "0.55 0.10 0.08 0.96", 11);
            AddButton(container, root, "CANCEL", "airanim.ui.reloadcancel", "0.54 0.15", "0.92 0.34", "0.18 0.22 0.28 0.96", 11);
            RegisterUiBridge(player, ConfirmUiName);
            CuiHelper.AddUi(player, container);
        }

        private void ShowDeleteConfirmUi(BasePlayer player, string profileId)
        {
            CuiHelper.DestroyUi(player, ConfirmUiName);
            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.04 0.04 0.05 0.97" },
                RectTransform = { AnchorMin = "0.36 0.38", AnchorMax = "0.64 0.62" }
            }, "Overlay", ConfirmUiName);

            AddLabel(container, root, "Delete visual profile?", 16, TextAnchor.MiddleLeft, "0.08 0.72", "0.92 0.90", "1 0.72 0.58 1");
            AddLabel(container, root, "This will remove '" + profileId + "' and immediately save VisualProfiles.json.", 11, TextAnchor.MiddleLeft, "0.08 0.48", "0.92 0.68", "0.80 0.86 0.90 1");
            AddButton(container, root, "DELETE", "airanim.ui.deleteconfirm " + profileId, "0.08 0.16", "0.46 0.34", "0.55 0.10 0.08 0.95", 12);
            AddButton(container, root, "CANCEL", "airanim.ui.deletecancel", "0.54 0.16", "0.92 0.34", "0.18 0.22 0.28 0.95", 12);
            RegisterUiBridge(player, ConfirmUiName);
            CuiHelper.AddUi(player, container);
        }

        private void RefreshEditorUiIfOpen(BasePlayer player)
        {
            RefreshOpenEditorSurfaces(player);
        }

        private void RefreshTimelineUiIfOpen(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
            {
                return;
            }

            EditorSession session;
            if (!sessions.TryGetValue(player.userID, out session) || session == null || !session.TimelineOpen)
            {
                return;
            }

            ShowTimelineUi(player);
        }

        private void RefreshWaypointPopupUiIfOpen(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
            {
                return;
            }

            EditorSession session;
            if (!sessions.TryGetValue(player.userID, out session) || session == null || !session.WaypointUiOpen)
            {
                return;
            }

            ShowWaypointPopupUi(player);
        }

        private void RefreshOpenEditorSurfaces(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
            {
                return;
            }

            EditorSession session;
            if (!sessions.TryGetValue(player.userID, out session) || session == null)
            {
                return;
            }

            var uiOpen = session.UiOpen;
            var timelineOpen = session.TimelineOpen;
            var waypointUiOpen = session.WaypointUiOpen;
            var releaseUiOpen = session.ReleaseUiOpen;
            var patternTemplateUiOpen = session.PatternTemplateUiOpen;
            var valueEditUiOpen = session.ValueEditUiOpen;

            if (uiOpen)
            {
                ShowEditorUi(player);
                if (valueEditUiOpen)
                {
                    ShowValueEditUi(player);
                }

                if (releaseUiOpen)
                {
                    if (patternTemplateUiOpen)
                    {
                        ShowPatternTemplatePopupUi(player);
                    }
                    else
                    {
                        ShowPayloadReleasePopupUi(player);
                    }
                }

                return;
            }

            if (timelineOpen)
            {
                ShowTimelineUi(player);
            }

            if (waypointUiOpen)
            {
                ShowWaypointPopupUi(player);
            }

            if (releaseUiOpen)
            {
                if (patternTemplateUiOpen)
                {
                    ShowPatternTemplatePopupUi(player);
                }
                else
                {
                    ShowPayloadReleasePopupUi(player);
                }
            }

            if (valueEditUiOpen)
            {
                ShowValueEditUi(player);
            }
        }

        private void HideEditorUi(BasePlayer player, bool reply)
        {
            if (player == null)
            {
                return;
            }

            DestroyMainEditorUi(player);
            CuiHelper.DestroyUi(player, TimelineUiName);
            CancelPendingValueEdit(player, false);
            EditorSession hiddenSession;
            if (sessions.TryGetValue(player.userID, out hiddenSession) && hiddenSession != null && hiddenSession.PreviewActive)
            {
                VisualProfileConfig hiddenProfile;
                if (!string.IsNullOrWhiteSpace(hiddenSession.ProfileId) && profileFile.Profiles.TryGetValue(hiddenSession.ProfileId, out hiddenProfile) && hiddenProfile != null)
                {
                    var elapsed = (float)Math.Max(0d, GetPreciseNow() - hiddenSession.PreviewStartedAt);
                    ShowPreviewBarUi(player, hiddenSession, hiddenProfile, elapsed);
                }
            }
            if (reply)
            {
                Reply(player, "Editor panels hidden. Waypoint markers and any active preview remain; the timeline reopens with the editor. Use /airanim to reopen, /airanim stop to stop preview, or /airanim end to clean everything up.");
            }
        }

        private void DestroyMainEditorUi(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, UiName);
            CuiHelper.DestroyUi(player, WaypointUiName);
            CuiHelper.DestroyUi(player, ReleaseUiName);
            CuiHelper.DestroyUi(player, ConfirmUiName);
            CuiHelper.DestroyUi(player, InsertUiName);
            CuiHelper.DestroyUi(player, AlignUiName);
            UnregisterEditorUiBridge(player, false);

            EditorSession session;
            if (sessions.TryGetValue(player.userID, out session) && session != null)
            {
                session.UiOpen = false;
                session.WaypointUiOpen = false;
                session.ReleaseUiOpen = false;
                session.PatternTemplateUiOpen = false;
                session.InsertUiOpen = false;
            }
        }

        private void DestroyUi(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, UiName);
            CuiHelper.DestroyUi(player, WaypointUiName);
            CuiHelper.DestroyUi(player, ReleaseUiName);
            CuiHelper.DestroyUi(player, ValueEditUiName);
            CuiHelper.DestroyUi(player, ConfirmUiName);
            CuiHelper.DestroyUi(player, InsertUiName);
            CuiHelper.DestroyUi(player, TimelineUiName);
            CuiHelper.DestroyUi(player, PreviewUiName);
            CuiHelper.DestroyUi(player, AlignUiName);
            UnregisterEditorUiBridge(player, true);

            EditorSession session;
            if (sessions.TryGetValue(player.userID, out session) && session != null)
            {
                session.UiOpen = false;
                session.WaypointUiOpen = false;
                session.ReleaseUiOpen = false;
                session.PatternTemplateUiOpen = false;
                session.ValueEditUiOpen = false;
                session.PendingValueEdit = null;
                session.InsertUiOpen = false;
                session.TimelineOpen = false;
                session.TimelineScrollOffset = 0f;
            }
        }

        private void RegisterUiBridge(BasePlayer player, string rootName)
        {
            if (player == null || string.IsNullOrWhiteSpace(rootName))
                return;

            RaidlandsUiEscapeBridge?.Call("RegisterUi", player, this, rootName, nameof(OnRaidlandsUiBridgeClosed));
        }

        private void UnregisterUiBridge(BasePlayer player, string rootName)
        {
            if (player == null || string.IsNullOrWhiteSpace(rootName))
                return;

            RaidlandsUiEscapeBridge?.Call("UnregisterUi", player, this, rootName);
        }

        private void UnregisterEditorUiBridge(BasePlayer player, bool includeTimeline)
        {
            UnregisterUiBridge(player, UiName);
            UnregisterUiBridge(player, WaypointUiName);
            UnregisterUiBridge(player, ReleaseUiName);
            UnregisterUiBridge(player, ValueEditUiName);
            UnregisterUiBridge(player, ConfirmUiName);
            UnregisterUiBridge(player, InsertUiName);
            UnregisterUiBridge(player, AlignUiName);
            if (includeTimeline)
                UnregisterUiBridge(player, TimelineUiName);
        }

        private void OnRaidlandsUiBridgeClosed(BasePlayer player, string reason)
        {
            DestroyUi(player);
        }

        private void RebuildMarkers(BasePlayer player, EditorSession session)
        {
            if (player == null || session == null)
            {
                return;
            }

            DestroyMarkers(session);
            VisualProfileConfig profile;
            if (string.IsNullOrWhiteSpace(session.ProfileId) || !profileFile.Profiles.TryGetValue(session.ProfileId, out profile) || profile == null)
            {
                StartMarkerTicker(player, session);
                return;
            }

            EnsureSessionTarget(player, session, false);
            NormalizeProfile(session.ProfileId, profile);
            for (var i = 0; i < profile.Waypoints.Count; i++)
            {
                var waypoint = profile.Waypoints[i];
                var world = EnsurePositionAboveTerrain(LocalToWorld(session, waypoint), GetProfileClearance(profile));
                var selected = i == session.SelectedWaypointIndex;
                var marker = CreateMapMarker(world, selected ? SelectedMarkerNativeRadius : MarkerNativeRadius, selected, false, player.userID);
                if (marker != null)
                {
                    session.MarkerEntities.Add(marker);
                }
            }

            if (session.HasTarget)
            {
                var targetMarker = CreateMapMarker(EnsurePositionAboveTerrain(session.Target + Vector3.up * 0.2f, 0f), TargetMarkerNativeRadius, false, true, player.userID);
                if (targetMarker != null)
                {
                    session.MarkerEntities.Add(targetMarker);
                }

            }

            StartMarkerTicker(player, session);
            RunMarkerEffects(player, session);
        }

        private bool TryFindLookedAtWaypoint(BasePlayer player, EditorSession session, VisualProfileConfig profile, out int index)
        {
            index = -1;
            if (player == null || player.eyes == null || session == null || profile == null || profile.Waypoints == null || profile.Waypoints.Count == 0)
            {
                return false;
            }

            var plan = BuildWorldWaypoints(session, profile);
            if (plan.Count == 0)
            {
                return false;
            }

            var ray = player.eyes.HeadRay();
            var origin = ray.origin;
            var direction = ray.direction;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            direction.Normalize();
            var bestIndex = -1;
            var bestDistance = float.MaxValue;
            var maxOpenDistanceSqr = WaypointPopupOpenDistance * WaypointPopupOpenDistance;

            for (var i = 0; i < plan.Count; i++)
            {
                var center = plan[i].Position;
                if ((center - player.transform.position).sqrMagnitude > maxOpenDistanceSqr)
                {
                    continue;
                }

                var selected = i == session.SelectedWaypointIndex;
                var radius = (selected ? SelectedMarkerBubbleRadius : MarkerBubbleRadius) + 0.25f;
                var toCenter = center - origin;
                var projection = Vector3.Dot(toCenter, direction);
                if (projection < 0f || projection > WaypointPopupOpenDistance + radius)
                {
                    continue;
                }

                var closestSqr = toCenter.sqrMagnitude - projection * projection;
                if (closestSqr > radius * radius || projection >= bestDistance)
                {
                    continue;
                }

                bestIndex = i;
                bestDistance = projection;
            }

            if (bestIndex < 0)
            {
                return false;
            }

            index = bestIndex;
            return true;
        }

        private MapMarkerGenericRadius CreateMapMarker(Vector3 position, float radius, bool selected, bool target, ulong ownerId)
        {
            BaseEntity entity = null;
            try
            {
                entity = GameManager.server.CreateEntity(GenericRadiusMapMarkerPrefab, position, Quaternion.identity, true);
                var marker = entity as MapMarkerGenericRadius;
                if (marker == null)
                {
                    if (entity != null && !entity.IsDestroyed)
                    {
                        entity.Kill(BaseNetworkable.DestroyMode.None);
                    }

                    PrintWarning("Could not create waypoint marker from prefab '" + GenericRadiusMapMarkerPrefab + "'.");
                    return null;
                }

                marker.OwnerID = ownerId;
                marker.enableSaving = false;
                marker.globalBroadcast = false;
                marker.radius = Mathf.Clamp(radius, 0.015f, 0.16f);
                marker.alpha = target ? 0.70f : selected ? 0.90f : 0.55f;
                if (target)
                {
                    marker.color1 = new Color(0.10f, 0.80f, 1f, marker.alpha);
                    marker.color2 = new Color(1f, 0.80f, 0.20f, marker.alpha);
                }
                else if (selected)
                {
                    marker.color1 = new Color(1f, 0.28f, 0.10f, marker.alpha);
                    marker.color2 = new Color(1f, 0.86f, 0.30f, marker.alpha);
                }
                else
                {
                    marker.color1 = new Color(0.20f, 0.60f, 1f, marker.alpha);
                    marker.color2 = new Color(0.12f, 0.26f, 0.40f, marker.alpha);
                }

                marker.Spawn();
                marker.SendUpdate();
                marker.SendNetworkUpdateImmediate();
                return marker;
            }
            catch (Exception ex)
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill(BaseNetworkable.DestroyMode.None);
                }

                PrintWarning("Could not create waypoint marker: " + ex.Message);
                return null;
            }
        }

        private Vector3 GetTargetSmokePosition(EditorSession session)
        {
            if (session == null)
            {
                return Vector3.zero;
            }

            return EnsurePositionAboveTerrain(session.Target + Vector3.up * TargetSmokeLift, 0f);
        }

        private void DrawTargetSmokeDebugMarker(BasePlayer player, EditorSession session)
        {
            if (player == null || session == null || !session.HasTarget)
            {
                return;
            }

            var basePosition = GetTargetSmokePosition(session);
            DrawSphere(player, MarkerDebugDrawDurationSeconds, TargetSmokeCoreColor, basePosition + Vector3.up * 0.25f, 0.55f);
            DrawLine(player, MarkerDebugDrawDurationSeconds, TargetSmokeCoreColor, basePosition + Vector3.left * 1.4f, basePosition + Vector3.right * 1.4f);
            DrawLine(player, MarkerDebugDrawDurationSeconds, TargetSmokeCoreColor, basePosition + Vector3.back * 1.4f, basePosition + Vector3.forward * 1.4f);

            var heightStep = TargetSmokeDebugHeight / 4f;
            for (var i = 0; i < 4; i++)
            {
                var center = basePosition + Vector3.up * (0.9f + heightStep * i);
                var radius = 0.85f + i * 0.34f;
                DrawSphere(player, MarkerDebugDrawDurationSeconds, TargetSmokeColor, center, radius);
            }
        }

        private void StartMarkerTicker(BasePlayer player, EditorSession session)
        {
            if (session == null || session.MarkerTicker != null)
            {
                return;
            }

            session.MarkerTicker = timer.Every(MarkerRefreshSeconds, () =>
            {
                if (!IsSessionActive(player, session))
                {
                    return;
                }

                RunMarkerEffects(player, session);
            });
        }

        private void RunMarkerEffects(BasePlayer player, EditorSession session)
        {
            if (player == null || session == null || string.IsNullOrWhiteSpace(session.ProfileId))
            {
                return;
            }

            VisualProfileConfig profile;
            if (!profileFile.Profiles.TryGetValue(session.ProfileId, out profile) || profile == null || profile.Waypoints == null)
            {
                return;
            }

            var plan = BuildWorldWaypoints(session, profile);
            DrawWaypointDebugMarkers(player, session, profile, plan);
            DrawWaypointObjectMarkers(player, session, profile, plan);
            DrawTargetSmokeDebugMarker(player, session);
        }

        private void DrawWaypointDebugMarkers(BasePlayer player, EditorSession session, VisualProfileConfig profile, List<WorldWaypoint> plan)
        {
            if (player == null || session == null || plan == null || plan.Count == 0)
            {
                return;
            }

            for (var i = 0; i < plan.Count; i++)
            {
                var waypoint = plan[i];
                var selected = i == session.SelectedWaypointIndex;
                var direction = GetWaypointMarkerDirection(plan, profile, i, session.Approach);
                DrawWaypointDebugMarker(player, waypoint.Position, direction, waypoint.Local, selected);
            }
        }

        private void DrawWaypointDebugMarker(BasePlayer player, Vector3 center, Vector3 direction, VisualProfileWaypoint waypoint, bool selected)
        {
            var forward = NormalizeMarkerDirection(direction, Vector3.forward);
            var rotation = GetWaypointMarkerRotation(forward) * GetWaypointRotationOffset(waypoint);
            forward = (rotation * Vector3.forward).normalized;

            var radius = selected ? SelectedMarkerBubbleRadius : MarkerBubbleRadius;
            var arrowLength = selected ? SelectedMarkerArrowLength : MarkerArrowLength;
            var arrowHead = selected ? SelectedMarkerArrowHeadSize : MarkerArrowHeadSize;
            var bubbleColor = selected ? SelectedWaypointBubbleColor : WaypointBubbleColor;
            var arrowColor = selected ? SelectedWaypointArrowColor : WaypointArrowColor;
            var halfArrow = Mathf.Min(arrowLength * 0.5f, radius * 0.72f);
            var arrowStart = center - forward * halfArrow;
            var arrowEnd = center + forward * halfArrow;

            DrawSphere(player, MarkerDebugDrawDurationSeconds, bubbleColor, center, radius);
            DrawArrow(player, MarkerDebugDrawDurationSeconds, arrowColor, arrowStart, arrowEnd, arrowHead);

            var right = (rotation * Vector3.right).normalized;
            var up = (rotation * Vector3.up).normalized;
            var tickCenter = center + forward * (radius * 0.12f);
            var tickLength = radius * MarkerAttitudeTickScale;

            DrawLine(player, MarkerDebugDrawDurationSeconds, WaypointRightAxisColor, tickCenter - right * tickLength, tickCenter + right * tickLength);
            DrawLine(player, MarkerDebugDrawDurationSeconds, WaypointUpAxisColor, tickCenter - up * (tickLength * 0.65f), tickCenter + up * (tickLength * 0.65f));
        }

        private void DrawWaypointObjectMarkers(BasePlayer player, EditorSession session, VisualProfileConfig profile, List<WorldWaypoint> plan)
        {
            if (player == null || session == null || profile == null || !session.ObjectMarkersEnabled || session.PreviewActive || plan == null || plan.Count == 0)
            {
                return;
            }

            for (var i = 0; i < plan.Count; i++)
            {
                var waypoint = plan[i];
                var selected = i == session.SelectedWaypointIndex;
                var direction = GetWaypointMarkerDirection(plan, profile, i, session.Approach);
                DrawWaypointObjectMarker(player, waypoint.Position, direction, waypoint.Local, profile.Vehicle, selected);
            }
        }

        private void DrawWaypointObjectMarker(BasePlayer player, Vector3 center, Vector3 direction, VisualProfileWaypoint waypoint, string vehicle, bool selected)
        {
            var forward = NormalizeMarkerDirection(direction, Vector3.forward);
            var rotation = GetWaypointMarkerRotation(forward) * GetWaypointRotationOffset(waypoint);
            forward = (rotation * Vector3.forward).normalized;
            var right = (rotation * Vector3.right).normalized;
            var up = (rotation * Vector3.up).normalized;
            var bodyColor = selected ? SelectedWaypointArrowColor : WaypointObjectBodyColor;
            var accentColor = selected ? SelectedWaypointBubbleColor : WaypointObjectAccentColor;
            var normalizedVehicle = (vehicle ?? "").Trim().ToLowerInvariant();

            if (normalizedVehicle == "drone")
            {
                var arm = 1.55f;
                var rotor = 0.42f;
                DrawLine(player, MarkerDebugDrawDurationSeconds, bodyColor, center - right * arm, center + right * arm);
                DrawLine(player, MarkerDebugDrawDurationSeconds, bodyColor, center - forward * arm, center + forward * arm);
                DrawLine(player, MarkerDebugDrawDurationSeconds, accentColor, center + right * arm - forward * rotor, center + right * arm + forward * rotor);
                DrawLine(player, MarkerDebugDrawDurationSeconds, accentColor, center - right * arm - forward * rotor, center - right * arm + forward * rotor);
                DrawLine(player, MarkerDebugDrawDurationSeconds, accentColor, center + forward * arm - right * rotor, center + forward * arm + right * rotor);
                DrawLine(player, MarkerDebugDrawDurationSeconds, accentColor, center - forward * arm - right * rotor, center - forward * arm + right * rotor);
                return;
            }

            if (normalizedVehicle == "attack_heli")
            {
                var body = 2.8f;
                var rotor = 4.3f;
                var tail = 5.0f;
                DrawLine(player, MarkerDebugDrawDurationSeconds, bodyColor, center - forward * body, center + forward * body);
                DrawLine(player, MarkerDebugDrawDurationSeconds, accentColor, center - right * rotor, center + right * rotor);
                DrawLine(player, MarkerDebugDrawDurationSeconds, accentColor, center - forward * rotor, center + forward * rotor);
                DrawLine(player, MarkerDebugDrawDurationSeconds, bodyColor, center - forward * tail, center - forward * body + up * 0.7f);
                DrawLine(player, MarkerDebugDrawDurationSeconds, accentColor, center - forward * tail - right * 0.9f, center - forward * tail + right * 0.9f);
                return;
            }

            var length = normalizedVehicle == "cargo_plane" ? 8.8f : 6.4f;
            var wing = normalizedVehicle == "cargo_plane" ? 7.0f : 4.8f;
            var tailWing = normalizedVehicle == "cargo_plane" ? 3.2f : 2.4f;
            var nose = center + forward * (length * 0.55f);
            var tailCenter = center - forward * (length * 0.45f);
            var wingCenter = center + forward * (length * 0.02f);

            DrawLine(player, MarkerDebugDrawDurationSeconds, bodyColor, tailCenter, nose);
            DrawLine(player, MarkerDebugDrawDurationSeconds, accentColor, wingCenter - right * wing, wingCenter + right * wing);
            DrawLine(player, MarkerDebugDrawDurationSeconds, accentColor, tailCenter - right * tailWing, tailCenter + right * tailWing);
            DrawLine(player, MarkerDebugDrawDurationSeconds, bodyColor, nose - right * 0.75f, nose);
            DrawLine(player, MarkerDebugDrawDurationSeconds, bodyColor, nose + right * 0.75f, nose);
            DrawLine(player, MarkerDebugDrawDurationSeconds, accentColor, tailCenter, tailCenter + up * 1.6f);
        }

        private Vector3 GetWaypointMarkerDirection(List<WorldWaypoint> plan, VisualProfileConfig profile, int index, Vector3 fallback)
        {
            if (plan == null || plan.Count == 0)
            {
                return NormalizeMarkerDirection(Vector3.zero, fallback);
            }

            if (plan.Count == 1)
            {
                return NormalizeMarkerDirection(Vector3.zero, fallback);
            }

            var last = plan.Count - 1;
            if (profile != null && !profile.StopAtWaypoints)
            {
                var blendedVelocity = GetPlanWaypointVelocity(plan, Mathf.Clamp(index, 0, last));
                return NormalizeMarkerDirection(blendedVelocity, fallback);
            }

            if (index <= 0)
            {
                return NormalizeMarkerDirection(plan[1].Position - plan[0].Position, fallback);
            }

            if (index >= last)
            {
                return NormalizeMarkerDirection(plan[last].Position - plan[last - 1].Position, fallback);
            }

            var previousDuration = Mathf.Max(0.05f, plan[index].Time - plan[index - 1].Time);
            var nextDuration = Mathf.Max(0.05f, plan[index + 1].Time - plan[index].Time);
            var previousVelocity = (plan[index].Position - plan[index - 1].Position) / previousDuration;
            var nextVelocity = (plan[index + 1].Position - plan[index].Position) / nextDuration;
            var blended = previousVelocity + nextVelocity;

            if (blended.sqrMagnitude > 0.0001f)
            {
                return blended.normalized;
            }

            return NormalizeMarkerDirection(nextVelocity.sqrMagnitude > previousVelocity.sqrMagnitude ? nextVelocity : previousVelocity, fallback);
        }

        private Vector3 NormalizeMarkerDirection(Vector3 direction, Vector3 fallback)
        {
            if (direction.sqrMagnitude > 0.0001f)
            {
                return direction.normalized;
            }

            if (fallback.sqrMagnitude > 0.0001f)
            {
                return fallback.normalized;
            }

            return Vector3.forward;
        }

        private Quaternion GetWaypointMarkerRotation(Vector3 forward)
        {
            forward = NormalizeMarkerDirection(forward, Vector3.forward);
            var up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.985f)
            {
                up = Vector3.forward;
            }

            return Quaternion.LookRotation(forward, up);
        }

        private void DrawSphere(BasePlayer player, float duration, Color color, Vector3 position, float radius)
        {
            player.SendConsoleCommand("ddraw.sphere", duration, color, position, radius);
        }

        private void DrawArrow(BasePlayer player, float duration, Color color, Vector3 start, Vector3 end, float arrowHeadSize)
        {
            player.SendConsoleCommand("ddraw.arrow", duration, color, start, end, arrowHeadSize);
        }

        private void DrawLine(BasePlayer player, float duration, Color color, Vector3 start, Vector3 end)
        {
            player.SendConsoleCommand("ddraw.line", duration, color, start, end);
        }

        private void DestroyMarkers(EditorSession session)
        {
            if (session == null)
            {
                return;
            }

            foreach (var entity in new List<BaseEntity>(session.MarkerEntities))
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill(BaseNetworkable.DestroyMode.None);
                }
            }

            session.MarkerEntities.Clear();
        }

        private void CloseSession(ulong userId, bool destroyUi)
        {
            EditorSession session;
            if (!sessions.TryGetValue(userId, out session) || session == null)
            {
                if (destroyUi)
                {
                    var existingPlayer = BasePlayer.FindByID(userId);
                    if (existingPlayer != null)
                    {
                        DestroyUi(existingPlayer);
                    }
                }

                return;
            }

            DestroyPreview(session);
            DestroyMarkers(session);
            CancelPendingAxisInput(session);
            ClearPendingValueEdit(session);
            if (session.MarkerTicker != null)
            {
                session.MarkerTicker.Destroy();
                session.MarkerTicker = null;
            }

            sessions.Remove(userId);

            if (destroyUi)
            {
                var player = BasePlayer.FindByID(userId);
                if (player != null)
                {
                    DestroyUi(player);
                }
            }
        }

        private void SetSessionTarget(BasePlayer player, EditorSession session, bool reply)
        {
            Vector3 target;
            string source;
            ResolveEditorTarget(player, out target, out source);
            session.Target = target;
            session.HasTarget = true;
            session.Approach = ResolveApproach(player, target);
            SetStatus(session, "Target set from " + source + ".", "Target column moved to " + FormatPosition(target) + ".");
            if (reply)
            {
                Reply(player, "Editor target set from " + source + " at " + FormatPosition(target) + "; target column moved; approach " + FormatVectorShort(session.Approach) + ".");
            }
        }

        private void EnsureSessionTarget(BasePlayer player, EditorSession session, bool preferExisting)
        {
            if (session == null || (preferExisting && session.HasTarget) || session.HasTarget)
            {
                return;
            }

            SetSessionTarget(player, session, false);
        }

        private void ResolveEditorTarget(BasePlayer player, out Vector3 target, out string source)
        {
            target = Vector3.zero;
            source = "fallback";

            if (player?.eyes != null)
            {
                RaycastHit hit;
                var ray = player.eyes.HeadRay();
                if (Physics.Raycast(ray, out hit, 1000f, TargetRaycastLayer, QueryTriggerInteraction.Ignore))
                {
                    target = ResolveImpactPosition(hit.point);
                    source = "look raycast";
                    return;
                }
            }

            if (TryGetLatestPingPosition(player, out target))
            {
                target = ResolveImpactPosition(target);
                source = "latest ping";
                return;
            }

            if (player?.eyes != null)
            {
                var ray = player.eyes.HeadRay();
                target = ResolveImpactPosition(ray.origin + ray.direction.normalized * 100f);
                source = "100m fallback";
                return;
            }

            target = Vector3.zero;
            source = "origin fallback";
        }

        private bool TryGetLatestPingPosition(BasePlayer player, out Vector3 position)
        {
            position = Vector3.zero;
            var pings = player?.State?.pings;
            if (pings == null || pings.Count == 0)
            {
                return false;
            }

            ProtoBuf.MapNote best = null;
            var bestRemaining = float.MinValue;
            foreach (var note in pings)
            {
                if (note == null || !note.isPing || note.timeRemaining <= 0f)
                {
                    continue;
                }

                if (note.timeRemaining <= bestRemaining)
                {
                    continue;
                }

                best = note;
                bestRemaining = note.timeRemaining;
            }

            if (best == null)
            {
                return false;
            }

            position = best.worldPosition;
            return true;
        }

        private Vector3 ResolveApproach(BasePlayer player, Vector3 target)
        {
            var approach = Vector3.zero;
            if (player != null)
            {
                approach = target - player.transform.position;
            }

            approach.y = 0f;
            if (approach.sqrMagnitude > 0.01f)
            {
                return approach.normalized;
            }

            if (player?.eyes != null)
            {
                approach = player.eyes.HeadRay().direction;
                approach.y = 0f;
                if (approach.sqrMagnitude > 0.01f)
                {
                    return approach.normalized;
                }
            }

            return Vector3.forward;
        }

        private Vector3 LocalToWorld(EditorSession session, VisualProfileWaypoint waypoint)
        {
            if (session == null || waypoint == null)
            {
                return Vector3.zero;
            }

            var approach = session.Approach;
            approach.y = 0f;
            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = Vector3.forward;
            }
            else
            {
                approach.Normalize();
            }

            var right = GetRightVector(approach);
            return session.Target + right * waypoint.X + Vector3.up * waypoint.Y + approach * waypoint.Z;
        }

        private Vector3 WorldToLocal(EditorSession session, Vector3 world)
        {
            if (session == null)
            {
                return Vector3.zero;
            }

            var approach = session.Approach;
            approach.y = 0f;
            if (approach.sqrMagnitude <= 0.01f)
            {
                approach = Vector3.forward;
            }
            else
            {
                approach.Normalize();
            }

            var right = GetRightVector(approach);
            var offset = world - session.Target;
            return new Vector3(Vector3.Dot(offset, right), offset.y, Vector3.Dot(offset, approach));
        }

        private float GetDefaultInsertedWaypointTime(VisualProfileConfig profile, EditorSession session)
        {
            if (profile == null || profile.Waypoints == null || profile.Waypoints.Count == 0)
            {
                return 0f;
            }

            if (session != null && session.SelectedWaypointIndex >= 0 && session.SelectedWaypointIndex < profile.Waypoints.Count)
            {
                return Mathf.Clamp(profile.Waypoints[session.SelectedWaypointIndex].Time + InsertWaypointDefaultSegmentSeconds, 0f, Mathf.Max(0.1f, profile.DurationSeconds));
            }

            return Mathf.Clamp(profile.FirstPayloadDelaySeconds, 0f, Mathf.Max(0.1f, profile.DurationSeconds));
        }

        private float GetTimelineTotalDuration(VisualProfileConfig profile)
        {
            var total = Mathf.Max(0.5f, profile == null ? 0.5f : profile.DurationSeconds);
            if (profile?.Waypoints != null && profile.Waypoints.Count > 0)
            {
                total = Mathf.Max(total, profile.Waypoints[profile.Waypoints.Count - 1].Time);
            }

            return Mathf.Clamp(total, 0.5f, 120f);
        }

        private float GetTimelineTimeScaleWidth(float total)
        {
            return Mathf.Max(TimelineMinimumContentWidth, Mathf.Max(0.5f, total) * TimelinePixelsPerSecond);
        }

        private float GetTimelineContentWidth(VisualProfileConfig profile, float total)
        {
            return GetTimelineContentWidth(profile, total, BuildTimelineNodeLayouts(profile, total, GetTimelineTimeScaleWidth(total)));
        }

        private float GetTimelineContentWidth(VisualProfileConfig profile, float total, List<TimelineNodeLayout> nodeLayouts)
        {
            var timeScaleWidth = GetTimelineTimeScaleWidth(total);
            if (nodeLayouts == null || nodeLayouts.Count == 0)
            {
                return timeScaleWidth;
            }

            var rightMost = timeScaleWidth;
            for (var i = 0; i < nodeLayouts.Count; i++)
            {
                rightMost = Mathf.Max(rightMost, nodeLayouts[i].Left + nodeLayouts[i].Width + TimelineEndPaddingPixels);
            }

            return rightMost;
        }

        private List<TimelineNodeLayout> BuildTimelineNodeLayouts(VisualProfileConfig profile, float total, float timeScaleWidth)
        {
            var layouts = new List<TimelineNodeLayout>();
            if (profile == null || profile.Waypoints == null || profile.Waypoints.Count == 0)
            {
                return layouts;
            }

            var safeTotal = Mathf.Max(0.5f, total);
            var cursor = 0f;
            for (var i = 0; i < profile.Waypoints.Count; i++)
            {
                var waypoint = profile.Waypoints[i];
                var desiredLeft = Mathf.Clamp01(waypoint.Time / safeTotal) * timeScaleWidth;
                var segment = GetTimelineSegmentDuration(profile, i);
                var width = Mathf.Max(TimelineMinimumNodeWidth, segment / safeTotal * timeScaleWidth - TimelineNodeGap);
                var left = Mathf.Max(desiredLeft, cursor);
                layouts.Add(new TimelineNodeLayout
                {
                    Left = left,
                    Width = width
                });
                cursor = left + width + TimelineNodeGap;
            }

            return layouts;
        }

        private float GetTimelineSegmentDuration(VisualProfileConfig profile, int index)
        {
            if (profile == null || profile.Waypoints == null || index < 0 || index >= profile.Waypoints.Count)
            {
                return TimelineMinimumSegmentSeconds;
            }

            var waypoint = profile.Waypoints[index];
            var endTime = index < profile.Waypoints.Count - 1 ? profile.Waypoints[index + 1].Time : profile.DurationSeconds;
            return Mathf.Max(TimelineMinimumSegmentSeconds, endTime - waypoint.Time);
        }

        private string GetWaypointDurationLabel(VisualProfileConfig profile, int index)
        {
            if (profile == null || profile.Waypoints == null || index < 0 || index >= profile.Waypoints.Count)
            {
                return "Duration";
            }

            return index < profile.Waypoints.Count - 1 ? "Segment Duration" : "End Duration";
        }

        private bool SetWaypointSegmentDuration(VisualProfileConfig profile, int index, float requestedDuration, out float appliedDuration)
        {
            appliedDuration = TimelineMinimumSegmentSeconds;
            if (profile == null || profile.Waypoints == null || index < 0 || index >= profile.Waypoints.Count || float.IsNaN(requestedDuration) || float.IsInfinity(requestedDuration))
            {
                return false;
            }

            var desired = Mathf.Max(TimelineMinimumSegmentSeconds, requestedDuration);
            var waypoint = profile.Waypoints[index];
            if (index < profile.Waypoints.Count - 1)
            {
                var current = GetTimelineSegmentDuration(profile, index);
                var durationOutsideSegment = Mathf.Max(0f, profile.DurationSeconds - current);
                var maxDesired = Mathf.Max(TimelineMinimumSegmentSeconds, 120f - durationOutsideSegment);
                desired = Mathf.Clamp(desired, TimelineMinimumSegmentSeconds, maxDesired);
                var delta = desired - current;
                if (Mathf.Abs(delta) > 0.0005f)
                {
                    for (var i = index + 1; i < profile.Waypoints.Count; i++)
                    {
                        profile.Waypoints[i].Time += delta;
                    }

                    profile.DurationSeconds += delta;
                }
            }
            else
            {
                var maxTailDuration = Mathf.Max(TimelineMinimumSegmentSeconds, 120f - waypoint.Time);
                desired = Mathf.Clamp(desired, TimelineMinimumSegmentSeconds, maxTailDuration);
                profile.DurationSeconds = Mathf.Clamp(waypoint.Time + desired, 0.5f, 120f);
            }

            profile.FirstPayloadDelaySeconds = Mathf.Clamp(profile.FirstPayloadDelaySeconds, 0f, Mathf.Max(0.1f, profile.DurationSeconds));
            appliedDuration = desired;
            return true;
        }

        private List<WorldWaypoint> BuildWorldWaypoints(EditorSession session, VisualProfileConfig profile)
        {
            var waypoints = new List<WorldWaypoint>();
            if (session == null || profile == null || profile.Waypoints == null)
            {
                return waypoints;
            }

            foreach (var local in profile.Waypoints)
            {
                if (local == null)
                {
                    continue;
                }

                waypoints.Add(new WorldWaypoint
                {
                    Time = Mathf.Clamp(local.Time, 0f, Mathf.Max(0.1f, profile.DurationSeconds)),
                    Local = local,
                    Position = EnsurePositionAboveTerrain(LocalToWorld(session, local), GetProfileClearance(profile))
                });
            }

            waypoints.Sort((a, b) => a.Time.CompareTo(b.Time));
            return waypoints;
        }

        private Vector3 EvaluatePlanPosition(List<WorldWaypoint> plan, VisualProfileConfig profile, float elapsed)
        {
            if (plan == null || plan.Count == 0)
            {
                return Vector3.zero;
            }

            var safeDuration = Mathf.Max(0.1f, profile == null ? plan[plan.Count - 1].Time : profile.DurationSeconds);
            var time = Mathf.Clamp(elapsed, 0f, safeDuration);
            if (time <= plan[0].Time || plan.Count == 1)
            {
                return plan[0].Position;
            }

            var last = plan.Count - 1;
            if (time >= plan[last].Time)
            {
                return plan[last].Position;
            }

            for (var i = 0; i < last; i++)
            {
                var a = plan[i];
                var b = plan[i + 1];
                if (time < a.Time || time > b.Time)
                {
                    continue;
                }

                var segmentDuration = Mathf.Max(0.05f, b.Time - a.Time);
                var t = Mathf.Clamp01((time - a.Time) / segmentDuration);
                if (profile != null && !profile.StopAtWaypoints)
                {
                    return EvaluateHermitePlanPosition(plan, i, t, segmentDuration);
                }

                var eased = Mathf.SmoothStep(0f, 1f, t);
                return Vector3.Lerp(a.Position, b.Position, eased);
            }

            return plan[last].Position;
        }

        private Vector3 EvaluateHermitePlanPosition(List<WorldWaypoint> plan, int index, float t, float segmentDuration)
        {
            var a = plan[index];
            var b = plan[index + 1];
            var m0 = GetPlanWaypointVelocity(plan, index);
            var m1 = GetPlanWaypointVelocity(plan, index + 1);
            var t2 = t * t;
            var t3 = t2 * t;
            return ((2f * t3 - 3f * t2 + 1f) * a.Position)
                + ((t3 - 2f * t2 + t) * segmentDuration * m0)
                + ((-2f * t3 + 3f * t2) * b.Position)
                + ((t3 - t2) * segmentDuration * m1);
        }

        private Vector3 EvaluateHermitePlanVelocity(List<WorldWaypoint> plan, int index, float t, float segmentDuration)
        {
            var a = plan[index];
            var b = plan[index + 1];
            var m0 = GetPlanWaypointVelocity(plan, index);
            var m1 = GetPlanWaypointVelocity(plan, index + 1);
            var safeDuration = Mathf.Max(0.05f, segmentDuration);
            var t2 = t * t;
            return (((6f * t2 - 6f * t) / safeDuration) * a.Position)
                + ((3f * t2 - 4f * t + 1f) * m0)
                + (((-6f * t2 + 6f * t) / safeDuration) * b.Position)
                + ((3f * t2 - 2f * t) * m1);
        }

        private Vector3 GetPlanWaypointVelocity(List<WorldWaypoint> plan, int index)
        {
            if (plan == null || plan.Count < 2)
            {
                return Vector3.zero;
            }

            var last = plan.Count - 1;
            if (index <= 0)
            {
                return GetPlanSegmentVelocity(plan, 0, 1);
            }

            if (index >= last)
            {
                return GetPlanSegmentVelocity(plan, last - 1, last);
            }

            var previous = GetPlanSegmentVelocity(plan, index - 1, index);
            var next = GetPlanSegmentVelocity(plan, index, index + 1);
            if (previous.sqrMagnitude <= 0.01f)
            {
                return next;
            }

            if (next.sqrMagnitude <= 0.01f)
            {
                return previous;
            }

            return (previous + next) * 0.5f;
        }

        private Vector3 GetPlanSegmentVelocity(List<WorldWaypoint> plan, int fromIndex, int toIndex)
        {
            var a = plan[fromIndex];
            var b = plan[toIndex];
            var duration = Mathf.Max(0.05f, b.Time - a.Time);
            return (b.Position - a.Position) / duration;
        }

        private Vector3 GetPlanDirection(List<WorldWaypoint> plan, VisualProfileConfig profile, float elapsed, Vector3 fallback)
        {
            if (plan == null || plan.Count < 2)
            {
                return fallback;
            }

            var safeDuration = Mathf.Max(0.1f, profile == null ? plan[plan.Count - 1].Time : profile.DurationSeconds);
            if (profile != null && !profile.StopAtWaypoints)
            {
                var time = Mathf.Clamp(elapsed, 0f, safeDuration);
                for (var i = 0; i < plan.Count - 1; i++)
                {
                    var a = plan[i];
                    var b = plan[i + 1];
                    if (time < a.Time || time > b.Time)
                    {
                        continue;
                    }

                    var segmentDuration = Mathf.Max(0.05f, b.Time - a.Time);
                    var t = Mathf.Clamp01((time - a.Time) / segmentDuration);
                    var blendedVelocity = EvaluateHermitePlanVelocity(plan, i, t, segmentDuration);
                    if (blendedVelocity.sqrMagnitude > 0.01f)
                    {
                        return blendedVelocity.normalized;
                    }
                }

                var endpointVelocity = time <= plan[0].Time ? GetPlanWaypointVelocity(plan, 0) : GetPlanWaypointVelocity(plan, plan.Count - 1);
                if (endpointVelocity.sqrMagnitude > 0.01f)
                {
                    return endpointVelocity.normalized;
                }
            }

            var sample = Mathf.Clamp(Mathf.Min(TangentSampleSeconds, safeDuration * 0.08f), 0.035f, 0.35f);
            var before = Mathf.Clamp(elapsed - sample, 0f, safeDuration);
            var after = Mathf.Clamp(elapsed + sample, 0f, safeDuration);
            if (after - before < 0.025f)
            {
                before = Mathf.Clamp(elapsed - sample * 2f, 0f, safeDuration);
                after = Mathf.Clamp(elapsed + sample * 2f, 0f, safeDuration);
            }

            var direction = EvaluatePlanPosition(plan, profile, after) - EvaluatePlanPosition(plan, profile, before);
            if (direction.sqrMagnitude > 0.01f)
            {
                return direction.normalized;
            }

            fallback.y = 0f;
            return fallback.sqrMagnitude > 0.01f ? fallback.normalized : Vector3.forward;
        }

        private Vector3 GetPlanVelocity(List<WorldWaypoint> plan, VisualProfileConfig profile, float elapsed, Vector3 direction)
        {
            if (plan == null || plan.Count < 2)
            {
                return direction.normalized * 1f;
            }

            var time = Mathf.Clamp(elapsed, 0f, Mathf.Max(0.1f, profile.DurationSeconds));
            for (var i = 0; i < plan.Count - 1; i++)
            {
                var a = plan[i];
                var b = plan[i + 1];
                if (time < a.Time || time > b.Time)
                {
                    continue;
                }

                var segmentDuration = Mathf.Max(0.05f, b.Time - a.Time);
                var speed = Vector3.Distance(a.Position, b.Position) / segmentDuration;
                if (profile != null && !profile.StopAtWaypoints)
                {
                    var t = Mathf.Clamp01((time - a.Time) / segmentDuration);
                    var blendedVelocity = EvaluateHermitePlanVelocity(plan, i, t, segmentDuration);
                    if (blendedVelocity.sqrMagnitude > 0.01f)
                    {
                        return blendedVelocity;
                    }
                }

                return direction.normalized * speed;
            }

            return direction.normalized * Math.Max(1f, Vector3.Distance(plan[0].Position, plan[plan.Count - 1].Position) / Mathf.Max(0.1f, profile.DurationSeconds));
        }

        private Quaternion EvaluatePlanRotationOffset(List<WorldWaypoint> plan, VisualProfileConfig profile, float elapsed)
        {
            if (plan == null || plan.Count == 0)
            {
                return Quaternion.identity;
            }

            var safeDuration = Mathf.Max(0.1f, profile == null ? plan[plan.Count - 1].Time : profile.DurationSeconds);
            var time = Mathf.Clamp(elapsed, 0f, safeDuration);
            if (time <= plan[0].Time || plan.Count == 1)
            {
                return GetWaypointRotationOffset(plan[0].Local);
            }

            var last = plan.Count - 1;
            if (time >= plan[last].Time)
            {
                return GetWaypointRotationOffset(plan[last].Local);
            }

            for (var i = 0; i < last; i++)
            {
                var a = plan[i];
                var b = plan[i + 1];
                if (time < a.Time || time > b.Time)
                {
                    continue;
                }

                var segmentDuration = Mathf.Max(0.05f, b.Time - a.Time);
                var t = Mathf.Clamp01((time - a.Time) / segmentDuration);
                var eased = profile != null && !profile.StopAtWaypoints ? t : Mathf.SmoothStep(0f, 1f, t);
                return Quaternion.Slerp(GetWaypointRotationOffset(a.Local), GetWaypointRotationOffset(b.Local), eased);
            }

            return GetWaypointRotationOffset(plan[last].Local);
        }

        private Quaternion GetWaypointRotationOffset(VisualProfileWaypoint waypoint)
        {
            if (waypoint == null)
            {
                return Quaternion.identity;
            }

            return Quaternion.Euler(waypoint.RotationX, waypoint.RotationY, waypoint.RotationZ);
        }

        private void MoveEntity(BaseEntity entity, Vector3 position, Quaternion rotation, Vector3 velocity, bool immediate)
        {
            if (entity == null || entity.IsDestroyed)
            {
                return;
            }

            try
            {
                var rigidbody = entity.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    rigidbody.useGravity = false;
                    rigidbody.velocity = velocity;
                    rigidbody.angularVelocity = Vector3.zero;
                    rigidbody.isKinematic = true;
                }
                else
                {
                    entity.SetVelocity(velocity);
                }

                entity.transform.SetPositionAndRotation(position, rotation);
                entity.UpdateNetworkGroup();
                if (immediate)
                {
                    entity.SendNetworkUpdateImmediate();
                }
                else
                {
                    entity.SendNetworkUpdate();
                }
            }
            catch (Exception ex)
            {
                PrintWarning("Preview movement update failed: " + ex.Message);
            }
        }

        private void PreparePreviewVehicle(BaseEntity entity, string vehicle, Vector3 velocity)
        {
            if (entity == null || entity.IsDestroyed)
            {
                return;
            }

            try
            {
                entity.SetFlagLocal(BaseEntity.Flags.On, true);
                entity.SetVelocity(velocity);

                var rigidbody = entity.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    rigidbody.useGravity = false;
                    rigidbody.velocity = velocity;
                    rigidbody.angularVelocity = Vector3.zero;
                    rigidbody.isKinematic = true;
                }

                var cargoPlane = entity as CargoPlane;
                if (cargoPlane != null)
                {
                    cargoPlane.dropped = true;
                    cargoPlane.startPos = entity.transform.position;
                    cargoPlane.endPos = entity.transform.position;
                    cargoPlane.secondsToTake = float.MaxValue;
                    cargoPlane.secondsTaken = 0f;
                }

                var patrolHeli = entity as PatrolHelicopter;
                if (patrolHeli != null)
                {
                    try
                    {
                        patrolHeli.HasBrain = false;
                        if (patrolHeli.myAI != null)
                        {
                            patrolHeli.myAI.enabled = false;
                        }

                        patrolHeli.servergibs.guid = string.Empty;
                        patrolHeli.fireBall.guid = string.Empty;
                        patrolHeli.mapMarkerEntityPrefab.guid = string.Empty;
                        patrolHeli.fleeMapMarkerEntityPrefab.guid = string.Empty;
                        patrolHeli.DestroyFleeMarker();
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                PrintWarning("Preview vehicle prep failed for " + (vehicle ?? "unknown") + ": " + ex.Message);
            }
        }

        private void TrySetCreatorEntity(BaseEntity entity, BasePlayer player)
        {
            if (entity == null || player == null)
            {
                return;
            }

            try
            {
                entity.SetCreatorEntity(player);
            }
            catch
            {
            }
        }

        private void RunSafeEffect(string prefab, Vector3 position, string label)
        {
            if (string.IsNullOrWhiteSpace(prefab))
            {
                return;
            }

            try
            {
                Effect.server.Run(prefab, position);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(label))
                {
                    PrintWarning("Effect '" + label + "' failed for prefab '" + prefab + "': " + ex.Message);
                }
            }
        }

        private Vector3 ResolveImpactPosition(Vector3 position)
        {
            RaycastHit hit;
            var startY = position.y + 120f;
            try
            {
                if (TerrainMeta.HeightMap != null)
                {
                    startY = Mathf.Max(startY, TerrainMeta.HeightMap.GetHeight(position) + 120f);
                }
            }
            catch
            {
            }

            var start = new Vector3(position.x, startY, position.z);
            var rayDistance = Mathf.Max(260f, startY - position.y + 140f);
            if (Physics.Raycast(start, Vector3.down, out hit, rayDistance, FlightTerrainRaycastLayer, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }

            float surfaceY;
            if (TryGetFlightSurfaceHeight(position, out surfaceY))
            {
                position.y = surfaceY;
                return position;
            }

            try
            {
                if (TerrainMeta.HeightMap != null)
                {
                    position.y = TerrainMeta.HeightMap.GetHeight(position);
                }
            }
            catch
            {
            }

            return position;
        }

        private bool TryGetFlightSurfaceHeight(Vector3 position, out float surfaceY)
        {
            surfaceY = 0f;
            var found = false;

            try
            {
                if (TerrainMeta.HeightMap != null)
                {
                    surfaceY = TerrainMeta.HeightMap.GetHeight(position);
                    found = true;
                }
            }
            catch
            {
            }

            try
            {
                RaycastHit hit;
                var startY = found ? Mathf.Max(position.y + 320f, surfaceY + 320f) : position.y + 320f;
                var start = new Vector3(position.x, startY, position.z);
                if (Physics.Raycast(start, Vector3.down, out hit, 720f, FlightTerrainRaycastLayer, QueryTriggerInteraction.Ignore))
                {
                    surfaceY = found ? Mathf.Max(surfaceY, hit.point.y) : hit.point.y;
                    found = true;
                }
            }
            catch
            {
            }

            return found;
        }

        private Vector3 EnsurePositionAboveTerrain(Vector3 position, float clearance)
        {
            float surfaceY;
            if (!TryGetFlightSurfaceHeight(position, out surfaceY))
            {
                return position;
            }

            var minimumY = surfaceY + Mathf.Max(0f, clearance);
            if (position.y < minimumY)
            {
                position.y = minimumY;
            }

            return position;
        }

        private Vector3 GetRightVector(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.01f)
            {
                forward = Vector3.forward;
            }
            else
            {
                forward.Normalize();
            }

            var right = new Vector3(-forward.z, 0f, forward.x);
            if (right.sqrMagnitude <= 0.01f)
            {
                right = Vector3.right;
            }
            else
            {
                right.Normalize();
            }

            return right;
        }

        private string GetVehiclePrefab(string vehicle)
        {
            switch ((vehicle ?? "").Trim().ToLowerInvariant())
            {
                case "drone":
                    return DroneVisualPrefab;
                case "cargo_plane":
                    return CargoPlaneVisualPrefab;
                case "attack_heli":
                    return PatrolHelicopterVisualPrefab;
                case "a10":
                case "f15":
                    return F15VisualPrefab;
                default:
                    return "";
            }
        }

        private float GetProfileClearance(VisualProfileConfig profile)
        {
            if (profile == null)
            {
                return DefaultAircraftClearance;
            }

            var fallback = string.Equals(profile.Vehicle, "drone", StringComparison.OrdinalIgnoreCase) ? DefaultDroneClearance : DefaultAircraftClearance;
            return Mathf.Clamp(profile.MinimumTerrainClearance <= 0f ? fallback : profile.MinimumTerrainClearance, 0f, 250f);
        }

        private float GetPreviewMoveIntervalSeconds()
        {
            return Mathf.Clamp(DefaultPreviewMoveIntervalSeconds, MinimumPreviewMoveIntervalSeconds, MaximumPreviewMoveIntervalSeconds);
        }

        private bool IsSessionActive(BasePlayer player, EditorSession session)
        {
            if (player == null || !player.IsConnected || session == null)
            {
                return false;
            }

            EditorSession current;
            return sessions.TryGetValue(player.userID, out current) && ReferenceEquals(current, session);
        }

        private EditorSession GetOrCreateSession(BasePlayer player)
        {
            EditorSession session;
            if (sessions.TryGetValue(player.userID, out session) && session != null)
            {
                return session;
            }

            session = new EditorSession { UserId = player.userID };
            sessions[player.userID] = session;
            return session;
        }

        private bool TryGetSessionProfile(BasePlayer player, EditorSession session, out VisualProfileConfig profile)
        {
            profile = null;
            if (session == null || string.IsNullOrWhiteSpace(session.ProfileId))
            {
                Reply(player, "No active profile. Use /airanim list then /airanim edit <profileId>, or create one.");
                return false;
            }

            if (!profileFile.Profiles.TryGetValue(session.ProfileId, out profile) || profile == null)
            {
                Reply(player, "The active profile '" + session.ProfileId + "' no longer exists. Use /airanim list.");
                session.ProfileId = "";
                session.SelectedWaypointIndex = -1;
                return false;
            }

            NormalizeProfile(session.ProfileId, profile);
            return true;
        }

        private VisualProfileWaypoint GetSelectedWaypoint(EditorSession session, VisualProfileConfig profile)
        {
            if (session == null || profile == null || profile.Waypoints == null || profile.Waypoints.Count == 0)
            {
                return null;
            }

            if (session.SelectedWaypointIndex < 0 || session.SelectedWaypointIndex >= profile.Waypoints.Count)
            {
                session.SelectedWaypointIndex = 0;
            }

            return profile.Waypoints[session.SelectedWaypointIndex];
        }

        private void LoadProfiles()
        {
            try
            {
                profileFile = Interface.Oxide.DataFileSystem.ReadObject<VisualProfileFile>(DataFileName);
            }
            catch (Exception ex)
            {
                BackupBadProfileFile();
                PrintWarning("Could not read VisualProfiles.json. A default file will be used. Error: " + ex.Message);
                profileFile = null;
            }

            if (profileFile == null || profileFile.Profiles == null || profileFile.Profiles.Count == 0)
            {
                profileFile = CreateDefaultProfileFile();
                SaveProfiles();
                ResetSessionHistoriesToCurrent();
                return;
            }

            var beforeNormalization = GetProfileComparisonJson();
            NormalizeProfileFile();
            if (!string.Equals(beforeNormalization, GetProfileComparisonJson(), StringComparison.Ordinal))
            {
                BackupProfileFile("pre-v0.2.0");
            }
            SaveProfiles();
            ResetSessionHistoriesToCurrent();
        }

        private void SaveProfiles(IEnumerable<string> changedProfileIds = null)
        {
            NormalizeProfileFile();
            var changed = NormalizeChangedProfileIds(changedProfileIds);
            foreach (var profileId in changed)
            {
                VisualProfileConfig profile;
                if (profileFile.Profiles.TryGetValue(profileId, out profile) && profile != null)
                {
                    profile.CompiledTrack = null;
                    profile.CompiledReleaseEvents = null;
                }
            }

            if (changed.Count > 0 && profileFile.SchemaVersion >= 2)
            {
                profileFile.PublishedRevision = 0;
                profileFile.PublishedSha256 = null;
            }

            Interface.Oxide.DataFileSystem.WriteObject(DataFileName, profileFile ?? CreateDefaultProfileFile(), true);
            lastPersistedProfileJson = GetProfileComparisonJson();

            if (changed.Count > 0)
            {
                Interface.CallHook(
                    "OnPortableAirstrikesVisualProfilesSaved",
                    DataFileName,
                    lastPersistedProfileJson,
                    changed.ToArray());
            }
        }

        private List<string> NormalizeChangedProfileIds(IEnumerable<string> profileIds)
        {
            var result = new List<string>();
            if (profileIds == null)
            {
                return result;
            }

            foreach (var value in profileIds)
            {
                var profileId = NormalizeProfileId(value);
                if (!string.IsNullOrWhiteSpace(profileId) && !result.Contains(profileId))
                {
                    result.Add(profileId);
                }
            }

            return result;
        }

        private string GetProfileComparisonJson()
        {
            try
            {
                return JsonConvert.SerializeObject(profileFile ?? new VisualProfileFile(), Formatting.None);
            }
            catch
            {
                return "";
            }
        }

        private bool HasUnsavedChanges()
        {
            return !string.Equals(lastPersistedProfileJson ?? "", GetProfileComparisonJson(), StringComparison.Ordinal);
        }

        private void CaptureHistoryIfChanged(EditorSession session)
        {
            if (session == null)
            {
                return;
            }

            var current = GetProfileComparisonJson();
            if (string.IsNullOrWhiteSpace(session.LastObservedProfileJson))
            {
                session.LastObservedProfileJson = current;
                session.SuppressHistoryCapture = false;
                return;
            }

            if (string.Equals(session.LastObservedProfileJson, current, StringComparison.Ordinal))
            {
                session.SuppressHistoryCapture = false;
                return;
            }

            if (!session.SuppressHistoryCapture)
            {
                AddBoundedHistorySnapshot(session.UndoHistory, session.LastObservedProfileJson);
                session.RedoHistory.Clear();
            }

            session.LastObservedProfileJson = current;
            session.SuppressHistoryCapture = false;
        }

        private void AddBoundedHistorySnapshot(List<string> history, string snapshot)
        {
            if (history == null || string.IsNullOrWhiteSpace(snapshot))
            {
                return;
            }

            if (history.Count > 0 && string.Equals(history[history.Count - 1], snapshot, StringComparison.Ordinal))
            {
                return;
            }

            history.Add(snapshot);
            while (history.Count > 40)
            {
                history.RemoveAt(0);
            }
        }

        private void ResetSessionHistoriesToCurrent()
        {
            var current = GetProfileComparisonJson();
            foreach (var entry in sessions)
            {
                var session = entry.Value;
                if (session == null)
                {
                    continue;
                }

                session.UndoHistory.Clear();
                session.RedoHistory.Clear();
                session.LastObservedProfileJson = current;
                session.SuppressHistoryCapture = false;
            }
        }

        private bool ApplyHistorySnapshot(BasePlayer player, bool redo)
        {
            if (player == null)
            {
                return false;
            }

            var session = GetOrCreateSession(player);
            CaptureHistoryIfChanged(session);
            var source = redo ? session.RedoHistory : session.UndoHistory;
            var destination = redo ? session.UndoHistory : session.RedoHistory;
            if (source.Count == 0)
            {
                SetStatus(session, redo ? "Nothing to redo." : "Nothing to undo.", "");
                ShowEditorUi(player);
                return false;
            }

            var current = GetProfileComparisonJson();
            var targetIndex = source.Count - 1;
            var target = source[targetIndex];
            source.RemoveAt(targetIndex);
            AddBoundedHistorySnapshot(destination, current);

            try
            {
                profileFile = JsonConvert.DeserializeObject<VisualProfileFile>(target) ?? CreateDefaultProfileFile();
                NormalizeProfileFile();
            }
            catch (Exception ex)
            {
                PrintWarning("Could not apply editor history snapshot: " + ex.Message);
                SetStatus(session, "Could not apply history snapshot.", ex.Message);
                ShowEditorUi(player);
                return false;
            }

            session.SuppressHistoryCapture = true;
            session.LastObservedProfileJson = GetProfileComparisonJson();
            session.SelectedPayloadEvent = null;
            CancelPendingAxisInput(session);
            ClearPendingValueEdit(session);
            DestroyPreview(session);

            VisualProfileConfig profile;
            if (!string.IsNullOrWhiteSpace(session.ProfileId) && profileFile.Profiles.TryGetValue(session.ProfileId, out profile) && profile != null)
            {
                session.SelectedWaypointIndex = profile.Waypoints == null || profile.Waypoints.Count == 0 ? -1 : Mathf.Clamp(session.SelectedWaypointIndex, 0, profile.Waypoints.Count - 1);
                session.SelectedPayloadEventIndex = profile.PayloadEvents == null || profile.PayloadEvents.Count == 0 ? -1 : Mathf.Clamp(session.SelectedPayloadEventIndex, 0, profile.PayloadEvents.Count - 1);
                if (session.SelectedPayloadEventIndex >= 0)
                {
                    session.SelectedPayloadEvent = profile.PayloadEvents[session.SelectedPayloadEventIndex];
                }
            }
            else
            {
                session.ProfileId = "";
                session.SelectedWaypointIndex = -1;
                session.SelectedPayloadEventIndex = -1;
            }

            RebuildMarkers(player, session);
            SetStatus(session, redo ? "Redid the last editor change." : "Undid the last editor change.", "Save to persist the restored state.");
            ShowEditorUi(player);
            return true;
        }

        private void BackupProfileFile(string label)
        {
            try
            {
                var path = Path.Combine(Interface.Oxide.DataDirectory, "PortableAirstrikes", "VisualProfiles.json");
                if (!File.Exists(path))
                {
                    return;
                }

                var safeLabel = string.IsNullOrWhiteSpace(label) ? "backup" : label.Replace(" ", "-");
                var backup = path + "." + safeLabel + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                File.Copy(path, backup, false);
                Puts("Backed up VisualProfiles.json to " + backup + " before normalization.");
            }
            catch (Exception ex)
            {
                PrintWarning("Could not back up VisualProfiles.json before normalization: " + ex.Message);
            }
        }

        private void BackupBadProfileFile()
        {
            try
            {
                var path = Path.Combine(Interface.Oxide.DataDirectory, "PortableAirstrikes", "VisualProfiles.json");
                if (!File.Exists(path))
                {
                    return;
                }

                var backup = path + ".bad-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                File.Copy(path, backup, true);
                PrintWarning("Backed up unreadable VisualProfiles.json to " + backup + ".");
            }
            catch
            {
            }
        }

        private void NormalizeProfileFile()
        {
            if (profileFile == null)
            {
                profileFile = CreateDefaultProfileFile();
            }

            if (profileFile.SchemaVersion != 1 && profileFile.SchemaVersion != 2)
            {
                profileFile.SchemaVersion = DefaultSchemaVersion;
            }
            if (profileFile.Profiles == null)
            {
                profileFile.Profiles = new Dictionary<string, VisualProfileConfig>(StringComparer.OrdinalIgnoreCase);
            }

            var normalized = new Dictionary<string, VisualProfileConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in profileFile.Profiles)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value == null)
                {
                    continue;
                }

                var id = NormalizeProfileId(entry.Key);
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = entry.Key.Trim().Replace(" ", "_");
                }

                NormalizeProfile(id, entry.Value);
                normalized[id] = entry.Value;
            }

            profileFile.Profiles = normalized;
        }

        private void NormalizeProfile(string id, VisualProfileConfig profile)
        {
            if (profile == null)
            {
                return;
            }

            profile.Vehicle = NormalizeVehicle(profile.Vehicle) ?? "f15";
            profile.DurationSeconds = Mathf.Clamp(profile.DurationSeconds <= 0f ? 8f : profile.DurationSeconds, 0.5f, 120f);
            profile.FirstPayloadDelaySeconds = Mathf.Clamp(profile.FirstPayloadDelaySeconds < 0f ? 0f : profile.FirstPayloadDelaySeconds, 0f, profile.DurationSeconds);
            profile.RotationSmoothTimeSeconds = Mathf.Clamp(profile.RotationSmoothTimeSeconds <= 0f ? 0.12f : profile.RotationSmoothTimeSeconds, 0.02f, 2f);
            profile.MinimumTerrainClearance = Mathf.Clamp(profile.MinimumTerrainClearance <= 0f ? (string.Equals(profile.Vehicle, "drone", StringComparison.OrdinalIgnoreCase) ? DefaultDroneClearance : DefaultAircraftClearance) : profile.MinimumTerrainClearance, 0f, 250f);
            if (profile.Waypoints == null)
            {
                profile.Waypoints = new List<VisualProfileWaypoint>();
            }

            profile.Waypoints.RemoveAll(wp => wp == null);
            foreach (var waypoint in profile.Waypoints)
            {
                waypoint.Time = Mathf.Clamp(waypoint.Time, 0f, profile.DurationSeconds);
                waypoint.X = Mathf.Clamp(waypoint.X, -2000f, 2000f);
                waypoint.Y = Mathf.Clamp(waypoint.Y, -100f, 1000f);
                waypoint.Z = Mathf.Clamp(waypoint.Z, -3000f, 3000f);
                waypoint.RotationX = NormalizeDegrees(waypoint.RotationX);
                waypoint.RotationY = NormalizeDegrees(waypoint.RotationY);
                waypoint.RotationZ = NormalizeDegrees(waypoint.RotationZ);
            }

            profile.Waypoints.Sort((a, b) => a.Time.CompareTo(b.Time));
            for (var i = 1; i < profile.Waypoints.Count; i++)
            {
                if (profile.Waypoints[i].Time <= profile.Waypoints[i - 1].Time + 0.005f)
                {
                    profile.Waypoints[i].Time = Mathf.Min(profile.DurationSeconds, profile.Waypoints[i - 1].Time + 0.01f);
                }
            }

            if (profile.PayloadEvents == null)
            {
                profile.PayloadEvents = new List<VisualPayloadEvent>();
            }

            profile.PayloadEvents.RemoveAll(ev => ev == null);
            profile.PayloadReleaseMode = NormalizePayloadReleaseMode(profile.PayloadReleaseMode);
            profile.MaxPayloadCount = Mathf.Clamp(profile.MaxPayloadCount, 0, 1000);
            profile.PayloadReleaseIntervalSeconds = Mathf.Clamp(profile.PayloadReleaseIntervalSeconds <= 0f ? DefaultPayloadReleaseIntervalSeconds : profile.PayloadReleaseIntervalSeconds, 0.05f, 30f);
            if (profile.ReleaseTemplate == null)
            {
                profile.ReleaseTemplate = new VisualPayloadEvent();
            }

            NormalizePayloadEvent(profile.ReleaseTemplate, profile, false, 0);
            NormalizePayloadEvents(profile);
        }

        private void NormalizePayloadEvents(VisualProfileConfig profile)
        {
            if (profile == null)
            {
                return;
            }

            if (profile.PayloadEvents == null)
            {
                profile.PayloadEvents = new List<VisualPayloadEvent>();
            }

            profile.PayloadEvents.RemoveAll(ev => ev == null);
            if (profile.PayloadEvents.Count > MaxPayloadEventsInProfile)
            {
                profile.PayloadEvents.RemoveRange(MaxPayloadEventsInProfile, profile.PayloadEvents.Count - MaxPayloadEventsInProfile);
            }

            profile.PayloadEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
            for (var i = 0; i < profile.PayloadEvents.Count; i++)
            {
                NormalizePayloadEvent(profile.PayloadEvents[i], profile, true, i);
            }

            if (string.Equals(profile.PayloadReleaseMode, "manual", StringComparison.OrdinalIgnoreCase) && profile.PayloadEvents.Count > 0)
            {
                profile.FirstPayloadDelaySeconds = Mathf.Clamp(profile.PayloadEvents[0].Time, 0f, Mathf.Max(0.1f, profile.DurationSeconds));
            }
        }

        private void NormalizePayloadEvent(VisualPayloadEvent payloadEvent, VisualProfileConfig profile, bool assignIndex, int index)
        {
            if (payloadEvent == null)
            {
                return;
            }

            payloadEvent.Time = Mathf.Clamp(payloadEvent.Time, 0f, profile == null ? 120f : profile.DurationSeconds);
            payloadEvent.Payload = NormalizePayload(payloadEvent.Payload);
            payloadEvent.Count = Mathf.Clamp(payloadEvent.Count <= 0 ? 1 : payloadEvent.Count, 1, 1000);
            payloadEvent.CarrierOffsetX = Mathf.Clamp(payloadEvent.CarrierOffsetX, -200f, 200f);
            payloadEvent.CarrierOffsetY = Mathf.Clamp(payloadEvent.CarrierOffsetY, -200f, 200f);
            payloadEvent.CarrierOffsetZ = Mathf.Clamp(payloadEvent.CarrierOffsetZ, -200f, 200f);
            payloadEvent.TargetOffsetX = Mathf.Clamp(payloadEvent.TargetOffsetX, -500f, 500f);
            payloadEvent.TargetOffsetY = Mathf.Clamp(payloadEvent.TargetOffsetY, -200f, 500f);
            payloadEvent.TargetOffsetZ = Mathf.Clamp(payloadEvent.TargetOffsetZ, -500f, 500f);
            payloadEvent.SpreadRadius = ClampOptional(payloadEvent.SpreadRadius, 0f, 250f);
            payloadEvent.LaunchSpeed = ClampOptional(payloadEvent.LaunchSpeed, 0f, 500f);
            payloadEvent.FuseSeconds = ClampOptional(payloadEvent.FuseSeconds, 0f, 60f);
            payloadEvent.DamageScale = Mathf.Clamp(payloadEvent.DamageScale <= 0f ? 1f : payloadEvent.DamageScale, 0f, 10f);
            payloadEvent.VehicleDamageScale = ClampOptional(payloadEvent.VehicleDamageScale, 0f, 10f);
            payloadEvent.SplashRadius = ClampOptional(payloadEvent.SplashRadius, 0f, 100f);
            payloadEvent.ImpactRadius = ClampOptional(payloadEvent.ImpactRadius, 0f, 50f);
            payloadEvent.MaxTrackingSeconds = ClampOptional(payloadEvent.MaxTrackingSeconds, 0f, 120f);
            payloadEvent.MaxTrackingDistance = ClampOptional(payloadEvent.MaxTrackingDistance, 0f, 3000f);
            if (payloadEvent.DamageScales == null)
            {
                payloadEvent.DamageScales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            }

            NormalizeDamageScale(payloadEvent, "Players");
            NormalizeDamageScale(payloadEvent, "Buildings");
            NormalizeDamageScale(payloadEvent, "Vehicles");
            NormalizeDamageScale(payloadEvent, "Turrets");
            NormalizeDamageScale(payloadEvent, "Deployables");
            if (assignIndex)
            {
                payloadEvent.Index = index + 1;
            }
            else
            {
                payloadEvent.Index = Math.Max(0, payloadEvent.Index);
            }
        }

        private float ClampOptional(float value, float min, float max)
        {
            return value < 0f ? -1f : Mathf.Clamp(value, min, max);
        }

        private void NormalizeDamageScale(VisualPayloadEvent payloadEvent, string key)
        {
            if (payloadEvent == null || payloadEvent.DamageScales == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (!payloadEvent.DamageScales.ContainsKey(key))
            {
                payloadEvent.DamageScales[key] = 1f;
                return;
            }

            payloadEvent.DamageScales[key] = Mathf.Clamp(payloadEvent.DamageScales[key], 0f, 10f);
        }

        private string NormalizePayloadReleaseMode(string mode)
        {
            var normalized = (mode ?? "").Trim().ToLowerInvariant();
            return normalized == "generated" || normalized == "auto" || normalized == "pattern" || normalized == "repeated" || normalized == "repeat" ? "generated" : "manual";
        }

        private void PreparePreviewPayloadSchedule(EditorSession session, VisualProfileConfig profile)
        {
            if (session == null)
            {
                return;
            }

            session.PreviewPayloadSchedule.Clear();
            foreach (var payloadEvent in BuildEffectiveReleaseSchedule(profile))
            {
                var snapshot = ClonePayloadEvent(payloadEvent);
                if (snapshot != null)
                {
                    session.PreviewPayloadSchedule.Add(snapshot);
                }
            }
            session.PreviewPayloadSchedule.Sort((a, b) => a.Time.CompareTo(b.Time));
            session.NextPreviewPayloadIndex = 0;
        }

        private bool IsRepeatedPatternMode(VisualProfileConfig profile)
        {
            return profile != null && string.Equals(profile.PayloadReleaseMode, "generated", StringComparison.OrdinalIgnoreCase);
        }

        private List<VisualPayloadEvent> BuildEffectiveReleaseSchedule(VisualProfileConfig profile)
        {
            var schedule = new List<VisualPayloadEvent>();
            if (profile == null)
            {
                return schedule;
            }

            if (!IsRepeatedPatternMode(profile))
            {
                if (profile.PayloadEvents == null)
                {
                    return schedule;
                }

                foreach (var payloadEvent in profile.PayloadEvents)
                {
                    if (payloadEvent != null)
                    {
                        schedule.Add(payloadEvent);
                    }
                }

                schedule.Sort((a, b) => a.Time.CompareTo(b.Time));
                return schedule;
            }

            var template = ClonePayloadEvent(profile.ReleaseTemplate) ?? new VisualPayloadEvent();
            if (string.IsNullOrWhiteSpace(template.Payload))
            {
                template.Payload = GetDefaultPayloadForVehicle(profile.Vehicle);
            }

            var unitsPerRelease = Mathf.Clamp(template.Count <= 0 ? 1 : template.Count, 1, 1000);
            var totalUnits = Mathf.Clamp(profile.MaxPayloadCount, 0, 1000);
            if (totalUnits <= 0)
            {
                return schedule;
            }

            var releaseGroups = Mathf.Clamp(Mathf.CeilToInt(totalUnits / (float)unitsPerRelease), 1, MaxGeneratedReleaseGroups);
            var interval = Mathf.Clamp(profile.PayloadReleaseIntervalSeconds <= 0f ? DefaultPayloadReleaseIntervalSeconds : profile.PayloadReleaseIntervalSeconds, 0.05f, 30f);
            var start = Mathf.Max(0f, profile.FirstPayloadDelaySeconds);
            for (var i = 0; i < releaseGroups; i++)
            {
                var alreadyReleased = i * unitsPerRelease;
                var remaining = totalUnits - alreadyReleased;
                if (remaining <= 0)
                {
                    break;
                }

                var generated = ClonePayloadEvent(template) ?? new VisualPayloadEvent();
                generated.Time = start + i * interval;
                generated.Index = i + 1;
                generated.Count = Math.Min(unitsPerRelease, remaining);
                schedule.Add(generated);
            }

            return schedule;
        }

        private int GetGeneratedReleaseGroupCount(VisualProfileConfig profile)
        {
            if (profile == null || profile.MaxPayloadCount <= 0)
            {
                return 0;
            }

            var unitsPerRelease = profile.ReleaseTemplate == null ? 1 : Math.Max(1, profile.ReleaseTemplate.Count);
            return Mathf.Clamp(Mathf.CeilToInt(profile.MaxPayloadCount / (float)unitsPerRelease), 0, MaxGeneratedReleaseGroups);
        }

        private int GetTotalPayloadUnits(VisualProfileConfig profile)
        {
            if (profile == null)
            {
                return 0;
            }

            if (IsRepeatedPatternMode(profile))
            {
                return Math.Max(0, profile.MaxPayloadCount);
            }

            var total = 0;
            if (profile.PayloadEvents != null)
            {
                foreach (var payloadEvent in profile.PayloadEvents)
                {
                    if (payloadEvent != null)
                    {
                        total += Math.Max(1, payloadEvent.Count);
                    }
                }
            }

            return total;
        }

        private float GetGeneratedLastReleaseTime(VisualProfileConfig profile)
        {
            var groups = GetGeneratedReleaseGroupCount(profile);
            if (groups <= 0 || profile == null)
            {
                return profile == null ? 0f : profile.FirstPayloadDelaySeconds;
            }

            return profile.FirstPayloadDelaySeconds + (groups - 1) * profile.PayloadReleaseIntervalSeconds;
        }

        private VisualPayloadEvent GetSelectedPayloadEvent(EditorSession session, VisualProfileConfig profile)
        {
            if (session == null || profile == null || profile.PayloadEvents == null || profile.PayloadEvents.Count == 0)
            {
                if (session != null)
                {
                    session.SelectedPayloadEventIndex = -1;
                    session.SelectedPayloadEvent = null;
                }

                return null;
            }

            if (session.SelectedPayloadEvent != null)
            {
                var retainedIndex = profile.PayloadEvents.IndexOf(session.SelectedPayloadEvent);
                if (retainedIndex >= 0)
                {
                    session.SelectedPayloadEventIndex = retainedIndex;
                    return session.SelectedPayloadEvent;
                }
            }

            session.SelectedPayloadEventIndex = Mathf.Clamp(session.SelectedPayloadEventIndex < 0 ? 0 : session.SelectedPayloadEventIndex, 0, profile.PayloadEvents.Count - 1);
            session.SelectedPayloadEvent = profile.PayloadEvents[session.SelectedPayloadEventIndex];
            return session.SelectedPayloadEvent;
        }

        private void SetSelectedPayloadEvent(EditorSession session, VisualProfileConfig profile, VisualPayloadEvent payloadEvent)
        {
            if (session == null)
            {
                return;
            }

            session.SelectedPayloadEvent = payloadEvent;
            session.SelectedPayloadEventIndex = profile == null || profile.PayloadEvents == null || payloadEvent == null ? -1 : profile.PayloadEvents.IndexOf(payloadEvent);
            if (session.SelectedPayloadEventIndex < 0 && profile != null && profile.PayloadEvents != null && profile.PayloadEvents.Count > 0)
            {
                session.SelectedPayloadEventIndex = 0;
                session.SelectedPayloadEvent = profile.PayloadEvents[0];
            }

            if (session.SelectedPayloadEventIndex >= 0)
            {
                session.ReleasePage = session.SelectedPayloadEventIndex / ReleaseRowsPerPage;
            }
        }

        private void NormalizeProfileKeepingRelease(EditorSession session, VisualProfileConfig profile, VisualPayloadEvent payloadEvent)
        {
            if (session == null || profile == null)
            {
                return;
            }

            NormalizeProfile(session.ProfileId, profile);
            SetSelectedPayloadEvent(session, profile, payloadEvent);
        }

        private bool PayloadTemplateEquivalent(VisualPayloadEvent a, VisualPayloadEvent b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (!string.Equals(NormalizePayload(a.Payload), NormalizePayload(b.Payload), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!NearlyEqual(a.CarrierOffsetX, b.CarrierOffsetX) || !NearlyEqual(a.CarrierOffsetY, b.CarrierOffsetY) || !NearlyEqual(a.CarrierOffsetZ, b.CarrierOffsetZ)
                || !NearlyEqual(a.TargetOffsetX, b.TargetOffsetX) || !NearlyEqual(a.TargetOffsetY, b.TargetOffsetY) || !NearlyEqual(a.TargetOffsetZ, b.TargetOffsetZ)
                || !NearlyEqual(a.SpreadRadius, b.SpreadRadius) || !NearlyEqual(a.LaunchSpeed, b.LaunchSpeed) || !NearlyEqual(a.FuseSeconds, b.FuseSeconds)
                || !NearlyEqual(a.DamageScale, b.DamageScale) || !NearlyEqual(a.VehicleDamageScale, b.VehicleDamageScale)
                || !NearlyEqual(a.SplashRadius, b.SplashRadius) || !NearlyEqual(a.ImpactRadius, b.ImpactRadius)
                || !NearlyEqual(a.MaxTrackingSeconds, b.MaxTrackingSeconds) || !NearlyEqual(a.MaxTrackingDistance, b.MaxTrackingDistance))
            {
                return false;
            }

            foreach (var key in new[] { "Players", "Buildings", "Vehicles", "Turrets", "Deployables" })
            {
                if (!NearlyEqual(GetPayloadDamageScale(a, key), GetPayloadDamageScale(b, key)))
                {
                    return false;
                }
            }

            return true;
        }

        private bool NearlyEqual(float a, float b, float tolerance = 0.001f)
        {
            return Mathf.Abs(a - b) <= tolerance;
        }

        private bool TryDetectRepeatedPattern(VisualProfileConfig profile, out RepeatedPatternDetection detection)
        {
            detection = null;
            if (profile == null || profile.PayloadEvents == null || profile.PayloadEvents.Count < 2)
            {
                return false;
            }

            NormalizePayloadEvents(profile);
            var first = profile.PayloadEvents[0];
            if (first == null)
            {
                return false;
            }

            var unitsPerRelease = Math.Max(1, first.Count);
            var totalUnits = 0;
            var interval = profile.PayloadEvents.Count > 1
                ? profile.PayloadEvents[1].Time - profile.PayloadEvents[0].Time
                : Mathf.Max(0.05f, profile.PayloadReleaseIntervalSeconds);
            if (interval <= 0f)
            {
                return false;
            }

            for (var i = 0; i < profile.PayloadEvents.Count; i++)
            {
                var payloadEvent = profile.PayloadEvents[i];
                if (payloadEvent == null || !PayloadTemplateEquivalent(first, payloadEvent))
                {
                    return false;
                }

                var count = Math.Max(1, payloadEvent.Count);
                if (i < profile.PayloadEvents.Count - 1 && count != unitsPerRelease)
                {
                    return false;
                }

                if (i == profile.PayloadEvents.Count - 1 && count > unitsPerRelease)
                {
                    return false;
                }

                if (i > 0)
                {
                    var currentInterval = payloadEvent.Time - profile.PayloadEvents[i - 1].Time;
                    if (Mathf.Abs(currentInterval - interval) > ReleasePatternDetectionToleranceSeconds)
                    {
                        return false;
                    }
                }

                totalUnits += count;
            }

            var template = ClonePayloadEvent(first) ?? new VisualPayloadEvent();
            template.Time = 0f;
            template.Index = 0;
            template.Count = unitsPerRelease;
            detection = new RepeatedPatternDetection
            {
                StartTime = first.Time,
                IntervalSeconds = Mathf.Max(0.05f, interval),
                UnitsPerRelease = unitsPerRelease,
                TotalUnits = totalUnits,
                ReleaseGroups = profile.PayloadEvents.Count,
                Template = template
            };
            return true;
        }

        private string GetReleaseValidationMessage(VisualProfileConfig profile)
        {
            if (profile == null)
            {
                return "No active profile.";
            }

            if (!IsRepeatedPatternMode(profile))
            {
                if (profile.PayloadEvents == null || profile.PayloadEvents.Count == 0)
                {
                    return "No manual releases yet. Add the first release at a waypoint or exact time.";
                }

                return profile.PayloadEvents.Count + " manual release event(s), " + GetTotalPayloadUnits(profile) + " total unit(s).";
            }

            if (profile.MaxPayloadCount <= 0)
            {
                return "Set total units above zero to generate releases.";
            }

            if (profile.ReleaseTemplate == null || string.IsNullOrWhiteSpace(profile.ReleaseTemplate.Payload))
            {
                return "Choose an ordnance type for the repeated pattern.";
            }

            var last = GetGeneratedLastReleaseTime(profile);
            if (last > profile.DurationSeconds + 0.001f)
            {
                return "Pattern ends at " + FormatSeconds(last) + ", after the profile ends at " + FormatSeconds(profile.DurationSeconds) + ".";
            }

            return GetGeneratedReleaseGroupCount(profile) + " release group(s), " + profile.MaxPayloadCount + " total unit(s), ending at " + FormatSeconds(last) + ".";
        }

        private VisualPayloadEvent CreatePayloadEventFromTemplate(VisualProfileConfig profile, float time, int selectedIndex)
        {
            VisualPayloadEvent source = null;
            if (profile?.PayloadEvents != null && selectedIndex >= 0 && selectedIndex < profile.PayloadEvents.Count)
            {
                source = profile.PayloadEvents[selectedIndex];
            }

            if (source == null)
            {
                source = profile?.ReleaseTemplate;
            }

            var ev = ClonePayloadEvent(source) ?? new VisualPayloadEvent();
            ev.Time = time;
            if (string.IsNullOrWhiteSpace(ev.Payload))
            {
                ev.Payload = GetDefaultPayloadForVehicle(profile == null ? "" : profile.Vehicle);
            }

            ev.Count = Math.Max(1, ev.Count);
            return ev;
        }

        private VisualPayloadEvent ClonePayloadEvent(VisualPayloadEvent source)
        {
            if (source == null)
            {
                return null;
            }

            var clone = new VisualPayloadEvent
            {
                Time = source.Time,
                Payload = source.Payload ?? "",
                Index = source.Index,
                Count = source.Count,
                CarrierOffsetX = source.CarrierOffsetX,
                CarrierOffsetY = source.CarrierOffsetY,
                CarrierOffsetZ = source.CarrierOffsetZ,
                TargetOffsetX = source.TargetOffsetX,
                TargetOffsetY = source.TargetOffsetY,
                TargetOffsetZ = source.TargetOffsetZ,
                SpreadRadius = source.SpreadRadius,
                LaunchSpeed = source.LaunchSpeed,
                FuseSeconds = source.FuseSeconds,
                DamageScale = source.DamageScale,
                VehicleDamageScale = source.VehicleDamageScale,
                SplashRadius = source.SplashRadius,
                ImpactRadius = source.ImpactRadius,
                MaxTrackingSeconds = source.MaxTrackingSeconds,
                MaxTrackingDistance = source.MaxTrackingDistance,
                DamageScales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            };

            if (source.DamageScales != null)
            {
                foreach (var entry in source.DamageScales)
                {
                    clone.DamageScales[entry.Key] = entry.Value;
                }
            }

            return clone;
        }

        private string GetDefaultPayloadForVehicle(string vehicle)
        {
            switch ((vehicle ?? "").Trim().ToLowerInvariant())
            {
                case "drone":
                    return "beancan";
                case "cargo_plane":
                    return "firebomb";
                case "attack_heli":
                    return "hv_rocket";
                case "a10":
                    return "bradley_longbarrel_burst";
                default:
                    return "mlrs_rocket";
            }
        }

        private string NormalizePayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return "";
            }

            var normalized = payload.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
            foreach (var value in PayloadValues)
            {
                if (string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }

            return normalized;
        }

        private string GetNextPayload(string payload)
        {
            var normalized = NormalizePayload(payload);
            for (var i = 0; i < PayloadValues.Length; i++)
            {
                if (string.Equals(PayloadValues[i], normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return PayloadValues[(i + 1) % PayloadValues.Length];
                }
            }

            return PayloadValues.Length == 0 ? "" : PayloadValues[0];
        }

        private string GetPayloadDisplay(string payload)
        {
            var normalized = NormalizePayload(payload);
            return string.IsNullOrWhiteSpace(normalized) ? "(strike payload)" : normalized;
        }

        private float GetPayloadDamageScale(VisualPayloadEvent payloadEvent, string key)
        {
            if (payloadEvent == null || payloadEvent.DamageScales == null || string.IsNullOrWhiteSpace(key))
            {
                return 1f;
            }

            float value;
            return payloadEvent.DamageScales.TryGetValue(key, out value) ? Mathf.Clamp(value, 0f, 10f) : 1f;
        }

        private void SetPayloadDamageScale(VisualPayloadEvent payloadEvent, string key, float value)
        {
            if (payloadEvent == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (payloadEvent.DamageScales == null)
            {
                payloadEvent.DamageScales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            }

            payloadEvent.DamageScales[key] = Mathf.Clamp(value, 0f, 10f);
        }

        private string NormalizePayloadReleaseField(string field)
        {
            var key = (field ?? "").Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
            switch (key)
            {
                case "t":
                case "time":
                case "seconds":
                    return "time";
                case "payload":
                case "ordnance":
                case "ordinance":
                    return "payload";
                case "count":
                case "amount":
                case "size":
                    return "count";
                case "carrierx":
                case "carrier_x":
                case "cx":
                    return "carrierx";
                case "carriery":
                case "carrier_y":
                case "cy":
                    return "carriery";
                case "carrierz":
                case "carrier_z":
                case "cz":
                    return "carrierz";
                case "targetx":
                case "target_x":
                case "tx":
                    return "targetx";
                case "targety":
                case "target_y":
                case "ty":
                    return "targety";
                case "targetz":
                case "target_z":
                case "tz":
                    return "targetz";
                case "spread":
                case "spreadradius":
                    return "spread";
                case "speed":
                case "launchspeed":
                    return "speed";
                case "fuse":
                case "fuseseconds":
                    return "fuse";
                case "damage":
                case "damagescale":
                    return "damage";
                case "vehiclescale":
                case "vehicledamage":
                case "vehicledamagescale":
                    return "vehiclescale";
                case "splash":
                case "splashradius":
                    return "splash";
                case "impact":
                case "impactradius":
                    return "impact";
                case "trackingseconds":
                case "trackseconds":
                case "maxtrackingseconds":
                    return "trackingseconds";
                case "trackingdistance":
                case "trackdistance":
                case "maxtrackingdistance":
                    return "trackingdistance";
                case "players":
                case "d_players":
                    return "d_players";
                case "buildings":
                case "d_buildings":
                    return "d_buildings";
                case "vehicles":
                case "d_vehicles":
                    return "d_vehicles";
                case "turrets":
                case "d_turrets":
                    return "d_turrets";
                case "deployables":
                case "d_deployables":
                    return "d_deployables";
                default:
                    return "";
            }
        }

        private void SetPayloadReleaseNumericField(VisualPayloadEvent ev, string key, float value)
        {
            if (ev == null)
            {
                return;
            }

            switch (key)
            {
                case "time":
                    ev.Time = value;
                    return;
                case "count":
                    ev.Count = Mathf.RoundToInt(value);
                    return;
                case "carrierx":
                    ev.CarrierOffsetX = value;
                    return;
                case "carriery":
                    ev.CarrierOffsetY = value;
                    return;
                case "carrierz":
                    ev.CarrierOffsetZ = value;
                    return;
                case "targetx":
                    ev.TargetOffsetX = value;
                    return;
                case "targety":
                    ev.TargetOffsetY = value;
                    return;
                case "targetz":
                    ev.TargetOffsetZ = value;
                    return;
                case "spread":
                    ev.SpreadRadius = value;
                    return;
                case "speed":
                    ev.LaunchSpeed = value;
                    return;
                case "fuse":
                    ev.FuseSeconds = value;
                    return;
                case "damage":
                    ev.DamageScale = value;
                    return;
                case "vehiclescale":
                    ev.VehicleDamageScale = value;
                    return;
                case "splash":
                    ev.SplashRadius = value;
                    return;
                case "impact":
                    ev.ImpactRadius = value;
                    return;
                case "trackingseconds":
                    ev.MaxTrackingSeconds = value;
                    return;
                case "trackingdistance":
                    ev.MaxTrackingDistance = value;
                    return;
                case "d_players":
                    SetPayloadDamageScale(ev, "Players", value);
                    return;
                case "d_buildings":
                    SetPayloadDamageScale(ev, "Buildings", value);
                    return;
                case "d_vehicles":
                    SetPayloadDamageScale(ev, "Vehicles", value);
                    return;
                case "d_turrets":
                    SetPayloadDamageScale(ev, "Turrets", value);
                    return;
                case "d_deployables":
                    SetPayloadDamageScale(ev, "Deployables", value);
                    return;
            }
        }

        private float GetPayloadReleaseNumericField(VisualPayloadEvent ev, string key)
        {
            if (ev == null)
            {
                return 0f;
            }

            switch (key)
            {
                case "time": return ev.Time;
                case "count": return Math.Max(1, ev.Count);
                case "carrierx": return ev.CarrierOffsetX;
                case "carriery": return ev.CarrierOffsetY;
                case "carrierz": return ev.CarrierOffsetZ;
                case "targetx": return ev.TargetOffsetX;
                case "targety": return ev.TargetOffsetY;
                case "targetz": return ev.TargetOffsetZ;
                case "spread": return ev.SpreadRadius;
                case "speed": return ev.LaunchSpeed;
                case "fuse": return ev.FuseSeconds;
                case "damage": return ev.DamageScale;
                case "vehiclescale": return ev.VehicleDamageScale;
                case "splash": return ev.SplashRadius;
                case "impact": return ev.ImpactRadius;
                case "trackingseconds": return ev.MaxTrackingSeconds;
                case "trackingdistance": return ev.MaxTrackingDistance;
                case "d_players": return GetPayloadDamageScale(ev, "Players");
                case "d_buildings": return GetPayloadDamageScale(ev, "Buildings");
                case "d_vehicles": return GetPayloadDamageScale(ev, "Vehicles");
                case "d_turrets": return GetPayloadDamageScale(ev, "Turrets");
                case "d_deployables": return GetPayloadDamageScale(ev, "Deployables");
            }

            return 0f;
        }

        private bool TryParsePayloadEventIndex(string value, VisualProfileConfig profile, out int index)
        {
            index = -1;
            if (profile == null || profile.PayloadEvents == null || profile.PayloadEvents.Count == 0)
            {
                return false;
            }

            int parsed;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return false;
            }

            var oneBased = parsed - 1;
            if (oneBased >= 0 && oneBased < profile.PayloadEvents.Count)
            {
                index = oneBased;
                return true;
            }

            if (parsed >= 0 && parsed < profile.PayloadEvents.Count)
            {
                index = parsed;
                return true;
            }

            return false;
        }
        private VisualProfileFile CreateDefaultProfileFile()
        {
            var file = new VisualProfileFile
            {
                SchemaVersion = DefaultSchemaVersion,
                AllowDangerousPayloadPreview = false,
                Profiles = new Dictionary<string, VisualProfileConfig>(StringComparer.OrdinalIgnoreCase)
            };

            file.Profiles["jet_mlrs_run"] = new VisualProfileConfig
            {
                Vehicle = "f15",
                DurationSeconds = 8.0f,
                FirstPayloadDelaySeconds = 3.5f,
                RotationSmoothTimeSeconds = 0.12f,
                MinimumTerrainClearance = 55.0f,
                Waypoints = new List<VisualProfileWaypoint>
                {
                    Wp(0.0f, 0.0f, 145.0f, -460.0f),
                    Wp(1.5f, -8.0f, 135.0f, -270.0f),
                    Wp(3.5f, 0.0f, 105.0f, -150.0f),
                    Wp(5.5f, 4.0f, 120.0f, 100.0f),
                    Wp(8.0f, 0.0f, 155.0f, 520.0f)
                },
                PayloadEvents = new List<VisualPayloadEvent>()
            };

            file.Profiles["a10_strafe_run"] = new VisualProfileConfig
            {
                Vehicle = "a10",
                DurationSeconds = 8.5f,
                FirstPayloadDelaySeconds = 3.8f,
                RotationSmoothTimeSeconds = 0.16f,
                MinimumTerrainClearance = 50.0f,
                Waypoints = new List<VisualProfileWaypoint>
                {
                    Wp(0.0f, 0.0f, 150.0f, -430.0f),
                    Wp(2.3f, -5.0f, 115.0f, -220.0f),
                    Wp(3.8f, 0.0f, 82.0f, -45.0f),
                    Wp(5.3f, 0.0f, 82.0f, 65.0f),
                    Wp(8.5f, 4.0f, 155.0f, 430.0f)
                },
                PayloadEvents = new List<VisualPayloadEvent>()
            };

            file.Profiles["cargo_heavy_drop"] = new VisualProfileConfig
            {
                Vehicle = "cargo_plane",
                DurationSeconds = 12.0f,
                FirstPayloadDelaySeconds = 6.0f,
                RotationSmoothTimeSeconds = 0.35f,
                MinimumTerrainClearance = 70.0f,
                Waypoints = new List<VisualProfileWaypoint>
                {
                    Wp(0.0f, 0.0f, 150.0f, -520.0f),
                    Wp(4.0f, 0.0f, 145.0f, -180.0f),
                    Wp(6.0f, 0.0f, 140.0f, 0.0f),
                    Wp(8.5f, 0.0f, 145.0f, 210.0f),
                    Wp(12.0f, 0.0f, 155.0f, 560.0f)
                },
                PayloadEvents = new List<VisualPayloadEvent>()
            };

            file.Profiles["attack_heli_rocket_run"] = new VisualProfileConfig
            {
                Vehicle = "attack_heli",
                DurationSeconds = 11.0f,
                FirstPayloadDelaySeconds = 6.0f,
                RotationSmoothTimeSeconds = 0.35f,
                MinimumTerrainClearance = 35.0f,
                Waypoints = new List<VisualProfileWaypoint>
                {
                    Wp(0.0f, -40.0f, 105.0f, -300.0f),
                    Wp(3.5f, -18.0f, 80.0f, -150.0f),
                    Wp(6.0f, 0.0f, 58.0f, -70.0f),
                    Wp(8.0f, 12.0f, 70.0f, 80.0f),
                    Wp(11.0f, 45.0f, 115.0f, 280.0f)
                },
                PayloadEvents = new List<VisualPayloadEvent>()
            };

            file.Profiles["drone_grenade_drop"] = new VisualProfileConfig
            {
                Vehicle = "drone",
                DurationSeconds = 10.0f,
                FirstPayloadDelaySeconds = 5.0f,
                RotationSmoothTimeSeconds = 0.35f,
                MinimumTerrainClearance = 12.0f,
                Waypoints = new List<VisualProfileWaypoint>
                {
                    Wp(0.0f, -5.0f, 28.0f, -75.0f),
                    Wp(2.5f, 6.0f, 26.0f, -38.0f),
                    Wp(5.0f, -4.0f, 24.0f, -6.0f),
                    Wp(7.0f, 5.0f, 24.0f, 18.0f),
                    Wp(10.0f, -3.0f, 30.0f, 80.0f)
                },
                PayloadEvents = new List<VisualPayloadEvent>()
            };

            return file;
        }

        private VisualProfileConfig CreateStarterProfileForVehicle(string vehicle)
        {
            var defaults = CreateDefaultProfileFile();
            switch ((vehicle ?? "").ToLowerInvariant())
            {
                case "drone":
                    return CloneProfile(defaults.Profiles["drone_grenade_drop"]);
                case "cargo_plane":
                    return CloneProfile(defaults.Profiles["cargo_heavy_drop"]);
                case "attack_heli":
                    return CloneProfile(defaults.Profiles["attack_heli_rocket_run"]);
                case "a10":
                    return CloneProfile(defaults.Profiles["a10_strafe_run"]);
                default:
                    return CloneProfile(defaults.Profiles["jet_mlrs_run"]);
            }
        }

        private VisualProfileConfig CloneProfile(VisualProfileConfig source)
        {
            return JsonConvert.DeserializeObject<VisualProfileConfig>(JsonConvert.SerializeObject(source)) ?? new VisualProfileConfig();
        }

        private VisualProfileWaypoint Wp(float time, float x, float y, float z)
        {
            return new VisualProfileWaypoint { Time = time, X = x, Y = y, Z = z };
        }

        private List<string> GetSortedProfileIds()
        {
            var ids = new List<string>();
            if (profileFile?.Profiles != null)
            {
                foreach (var entry in profileFile.Profiles)
                {
                    ids.Add(entry.Key);
                }
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        private int CountProfiles()
        {
            return profileFile?.Profiles == null ? 0 : profileFile.Profiles.Count;
        }

        private string FindProfileId(string requested)
        {
            if (string.IsNullOrWhiteSpace(requested) || profileFile?.Profiles == null)
            {
                return "";
            }

            VisualProfileConfig ignored;
            if (profileFile.Profiles.TryGetValue(requested.Trim(), out ignored))
            {
                return requested.Trim();
            }

            foreach (var id in profileFile.Profiles.Keys)
            {
                if (string.Equals(id, requested.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return id;
                }
            }

            return "";
        }

        private string NormalizeProfileId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return "";
            }

            id = id.Trim();
            var chars = new List<char>();
            foreach (var ch in id)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.')
                {
                    chars.Add(ch);
                }
            }

            return new string(chars.ToArray()).Trim('.', '-', '_');
        }

        private string NormalizeVehicle(string vehicle)
        {
            if (string.IsNullOrWhiteSpace(vehicle))
            {
                return null;
            }

            var normalized = vehicle.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
            if (normalized == "heli" || normalized == "patrol_heli" || normalized == "attackhelicopter")
            {
                normalized = "attack_heli";
            }
            else if (normalized == "cargo" || normalized == "plane" || normalized == "cargoplane")
            {
                normalized = "cargo_plane";
            }
            else if (normalized == "jet")
            {
                normalized = "f15";
            }

            foreach (var valid in VehicleValues)
            {
                if (string.Equals(valid, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return valid;
                }
            }

            return null;
        }

        private string GetNextVehicle(string vehicle)
        {
            var normalized = NormalizeVehicle(vehicle) ?? "f15";
            for (var i = 0; i < VehicleValues.Length; i++)
            {
                if (string.Equals(VehicleValues[i], normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return VehicleValues[(i + 1) % VehicleValues.Length];
                }
            }

            return VehicleValues[0];
        }

        private void SetProfileFloat(VisualProfileConfig profile, string field, float value)
        {
            switch ((field ?? "").ToLowerInvariant())
            {
                case "duration":
                    profile.DurationSeconds = value;
                    break;
                case "firstpayload":
                    if (!IsRepeatedPatternMode(profile) && profile.PayloadEvents != null && profile.PayloadEvents.Count > 0)
                    {
                        profile.PayloadEvents[0].Time = value;
                    }
                    else
                    {
                        profile.FirstPayloadDelaySeconds = value;
                    }
                    break;
                case "smooth":
                    profile.RotationSmoothTimeSeconds = value;
                    break;
                case "clearance":
                    profile.MinimumTerrainClearance = value;
                    break;
            }
        }

        private void ApplyProfileDelta(VisualProfileConfig profile, string field, float delta)
        {
            switch ((field ?? "").ToLowerInvariant())
            {
                case "duration":
                    profile.DurationSeconds += delta;
                    break;
                case "firstpayload":
                    if (!IsRepeatedPatternMode(profile) && profile.PayloadEvents != null && profile.PayloadEvents.Count > 0)
                    {
                        profile.PayloadEvents[0].Time += delta;
                    }
                    else
                    {
                        profile.FirstPayloadDelaySeconds += delta;
                    }
                    break;
                case "smooth":
                    profile.RotationSmoothTimeSeconds += delta;
                    break;
                case "clearance":
                    profile.MinimumTerrainClearance += delta;
                    break;
            }
        }

        private string NormalizeProfileValueEditField(string field)
        {
            var normalized = (field ?? "").Trim().ToLowerInvariant();
            return normalized == "duration" || normalized == "smooth" || normalized == "clearance" || normalized == "firstpayload" ? normalized : "";
        }

        private string NormalizePatternValueEditField(string field)
        {
            var normalized = (field ?? "").Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
            if (normalized == "start" || normalized == "time" || normalized == "interval" || normalized == "units"
                || normalized == "count" || normalized == "total" || normalized == "groups")
            {
                return normalized;
            }

            var releaseField = NormalizePayloadReleaseField(normalized);
            return releaseField == "time" || releaseField == "count" || releaseField == "payload" ? "" : releaseField;
        }

        private void OpenWaypointTimeEdit(BasePlayer player, string selector, bool fromPopup)
        {
            if (player == null)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            int index;
            if (!TryResolveWaypointEditIndex(session, profile, selector, out index))
            {
                Reply(player, "No waypoint is selected for time editing.");
                return;
            }

            var waypoint = profile.Waypoints[index];
            CancelPendingAxisInput(session);
            session.SelectedWaypointIndex = index;
            session.PendingValueEdit = new PendingValueEdit
            {
                ProfileId = session.ProfileId,
                Waypoint = waypoint,
                GenericScope = "waypointtime",
                GenericField = "time",
                FromPopup = fromPopup
            };
            session.ValueEditUiOpen = true;
            SetStatus(session, "Editing waypoint #" + DisplayIndex(index) + " time.", "Use the keypad buttons, then click APPLY.");
            ShowValueEditUi(player);
        }

        private void OpenProfileValueEdit(BasePlayer player, string field, bool fromPopup)
        {
            var normalized = NormalizeProfileValueEditField(field);
            if (player == null || string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            CancelPendingAxisInput(session);
            session.PendingValueEdit = new PendingValueEdit
            {
                ProfileId = session.ProfileId,
                GenericScope = "profile",
                GenericField = normalized,
                FromPopup = fromPopup
            };
            session.ValueEditUiOpen = true;
            SetStatus(session, "Editing profile " + normalized + ".", "Use the keypad buttons, then click APPLY.");
            ShowValueEditUi(player);
        }

        private void OpenPatternValueEdit(BasePlayer player, string field, bool fromPopup)
        {
            var normalized = NormalizePatternValueEditField(field);
            if (player == null || string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            if (profile.ReleaseTemplate == null)
            {
                profile.ReleaseTemplate = new VisualPayloadEvent();
            }

            CancelPendingAxisInput(session);
            session.PendingValueEdit = new PendingValueEdit
            {
                ProfileId = session.ProfileId,
                GenericScope = "pattern",
                GenericField = normalized,
                FromPopup = fromPopup
            };
            session.ValueEditUiOpen = true;
            SetStatus(session, "Editing repeated pattern " + normalized + ".", "Use the keypad buttons, then click APPLY.");
            ShowValueEditUi(player);
        }

        private void OpenWaypointValueEdit(BasePlayer player, string axis, bool rotation, bool fromPopup)
        {
            var normalizedAxis = NormalizeCoordinateAxis(axis);
            if (player == null || string.IsNullOrWhiteSpace(normalizedAxis))
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var waypoint = GetSelectedWaypoint(session, profile);
            if (waypoint == null)
            {
                Reply(player, "No waypoint is selected.");
                return;
            }

            CancelPendingAxisInput(session);
            session.PendingValueEdit = new PendingValueEdit
            {
                ProfileId = session.ProfileId,
                Waypoint = waypoint,
                Axis = normalizedAxis,
                Rotation = rotation,
                FromPopup = fromPopup
            };
            session.ValueEditUiOpen = true;

            SetStatus(session, "Editing waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + " " + (rotation ? "rotation " : "") + normalizedAxis.ToUpperInvariant() + ".", "Use the keypad buttons, then click APPLY.");
            ShowValueEditUi(player);
        }

        private void OpenWaypointDurationEdit(BasePlayer player, string selector, bool fromPopup)
        {
            if (player == null)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            int index;
            if (!TryResolveWaypointEditIndex(session, profile, selector, out index))
            {
                Reply(player, "No waypoint is selected for duration editing.");
                return;
            }

            OpenWaypointDurationEdit(player, index, fromPopup);
        }

        private void OpenWaypointDurationEdit(BasePlayer player, int index, bool fromPopup)
        {
            if (player == null)
            {
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile) || profile.Waypoints == null || profile.Waypoints.Count == 0)
            {
                return;
            }

            NormalizeProfile(session.ProfileId, profile);
            if (index < 0 || index >= profile.Waypoints.Count)
            {
                return;
            }

            var waypoint = profile.Waypoints[index];
            CancelPendingAxisInput(session);
            session.SelectedWaypointIndex = index;
            session.PendingValueEdit = new PendingValueEdit
            {
                ProfileId = session.ProfileId,
                Waypoint = waypoint,
                Axis = "",
                Duration = true,
                FromPopup = fromPopup
            };
            session.ValueEditUiOpen = true;

            SetStatus(session, "Editing waypoint #" + DisplayIndex(index) + " duration.", "Use the keypad buttons, then click APPLY.");
            ShowValueEditUi(player);
        }

        private void CapturePendingValueEditInput(BasePlayer player, string value)
        {
            if (player == null)
            {
                return;
            }

            EditorSession session;
            if (!sessions.TryGetValue(player.userID, out session) || session == null || session.PendingValueEdit == null)
            {
                return;
            }

            var pending = session.PendingValueEdit;
            var draft = (value ?? "").Trim();
            pending.DraftValue = draft;
            pending.HasDraft = !string.IsNullOrWhiteSpace(draft);
            if (pending.HasDraft)
            {
                SetStatus(session, "Captured pending value.", "Click APPLY to update " + GetFriendlyValueEditFieldName(pending, null, -1) + ".");
            }
        }

        private void ApplyPendingValueEditKey(BasePlayer player, string token)
        {
            if (player == null)
            {
                return;
            }

            EditorSession session;
            if (!sessions.TryGetValue(player.userID, out session) || session == null)
            {
                return;
            }

            PendingValueEdit edit;
            VisualProfileConfig profile;
            VisualProfileWaypoint waypoint;
            int index;
            if (!TryGetPendingValueEditContext(player, session, out edit, out profile, out waypoint, out index))
            {
                ClearPendingValueEdit(session);
                CuiHelper.DestroyUi(player, ValueEditUiName);
                RefreshOpenEditorSurfaces(player);
                return;
            }

            var key = (token ?? "").Trim().ToLowerInvariant();
            var draft = edit.HasDraft ? edit.DraftValue ?? "" : "";

            if (key.Length == 1 && key[0] >= '0' && key[0] <= '9')
            {
                draft += key;
            }
            else if (key == "dot" || key == ".")
            {
                if (!draft.Contains("."))
                {
                    draft = string.IsNullOrWhiteSpace(draft) ? "0." : draft + ".";
                }
            }
            else if (key == "minus" || key == "-")
            {
                draft = draft.StartsWith("-", StringComparison.Ordinal) ? draft.Substring(1) : "-" + draft;
            }
            else if (key == "back" || key == "del" || key == "delete")
            {
                if (draft.Length > 0)
                {
                    draft = draft.Substring(0, draft.Length - 1);
                }
            }
            else if (key == "clear" || key == "clr")
            {
                draft = "";
            }
            else if (key == "current" || key == "cur")
            {
                draft = IsGenericValueEdit(edit)
                    ? FormatFloat(GetGenericValueEditCurrentValue(edit, profile, waypoint))
                    : edit.ReleaseEvent ? FormatFloat(GetPayloadReleaseNumericField(edit.PayloadEvent, edit.ReleaseField))
                    : edit.Duration ? FormatFloat(GetTimelineSegmentDuration(profile, index))
                    : edit.Rotation ? FormatFloat(GetWaypointRotationAxis(waypoint, edit.Axis))
                    : FormatFloat(GetWaypointCoordinate(waypoint, edit.Axis));
            }
            else
            {
                return;
            }

            if (draft.Length > 32)
            {
                draft = draft.Substring(0, 32);
            }

            edit.DraftValue = draft;
            edit.HasDraft = !string.IsNullOrWhiteSpace(draft);
            SetStatus(session, "Editing " + GetFriendlyValueEditFieldName(edit, profile, index) + ".", GetPendingValueEditSubject(edit, session, index));
            ShowValueEditUi(player);
        }

        private void CommitPendingValueEdit(ulong userId)
        {
            var player = BasePlayer.FindByID(userId);
            if (player == null || !CanUse(player))
            {
                return;
            }

            EditorSession session;
            if (!sessions.TryGetValue(userId, out session) || session == null)
            {
                return;
            }

            PendingValueEdit edit;
            VisualProfileConfig profile;
            VisualProfileWaypoint waypoint;
            int index;
            if (!TryGetPendingValueEditContext(player, session, out edit, out profile, out waypoint, out index))
            {
                ClearPendingValueEdit(session);
                CuiHelper.DestroyUi(player, ValueEditUiName);
                RefreshOpenEditorSurfaces(player);
                return;
            }

            var fieldName = GetFriendlyValueEditFieldName(edit, profile, index);
            if (!edit.HasDraft || string.IsNullOrWhiteSpace(edit.DraftValue))
            {
                SetStatus(session, "No value captured for " + fieldName + ".", "Use the keypad buttons, then click APPLY again.");
                ShowValueEditUi(player);
                return;
            }

            float parsed;
            if (!TryParseInputFloat(edit.DraftValue, edit.Axis, out parsed))
            {
                SetStatus(session, "Invalid " + fieldName + " value.", "Enter a number such as -150, 0.25, 3, or 90.");
                ShowValueEditUi(player);
                return;
            }

            float appliedDuration;
            if (IsGenericValueEdit(edit))
            {
                if (!ApplyGenericValueEdit(edit, profile, waypoint, parsed))
                {
                    SetStatus(session, "Could not update " + fieldName + ".", "The edit context is no longer valid.");
                    ShowValueEditUi(player);
                    return;
                }
            }
            else if (edit.ReleaseEvent)
            {
                SetSelectedPayloadEvent(session, profile, edit.PayloadEvent);
                SetPayloadReleaseNumericField(edit.PayloadEvent, edit.ReleaseField, parsed);
            }
            else if (edit.Duration)
            {
                session.SelectedWaypointIndex = index;
                if (!SetWaypointSegmentDuration(profile, index, parsed, out appliedDuration))
                {
                    SetStatus(session, "Could not update waypoint duration.", "Duration must be a positive number of seconds.");
                    ShowValueEditUi(player);
                    return;
                }
            }
            else if (edit.Rotation)
            {
                session.SelectedWaypointIndex = index;
                SetWaypointRotationAxis(waypoint, edit.Axis, NormalizeDegrees(parsed));
            }
            else
            {
                session.SelectedWaypointIndex = index;
                SetWaypointCoordinate(waypoint, edit.Axis, parsed);
            }

            NormalizeProfile(session.ProfileId, profile);
            if (edit.ReleaseEvent)
            {
                SetSelectedPayloadEvent(session, profile, edit.PayloadEvent);
            }
            else if (waypoint != null)
            {
                session.SelectedWaypointIndex = profile.Waypoints.IndexOf(waypoint);
                if (session.SelectedWaypointIndex < 0 && profile.Waypoints.Count > 0)
                {
                    session.SelectedWaypointIndex = 0;
                }
            }

            RebuildMarkers(player, session);
            var appliedValue = FormatPendingValueEditCurrent(edit, profile, waypoint, index);
            var subject = GetPendingValueEditSubject(edit, session, IsGenericValueEdit(edit) && edit.GenericScope == "waypointtime" ? session.SelectedWaypointIndex : index);
            SetStatus(session, "Set " + fieldName + " to " + appliedValue + ".", subject + (edit.FromPopup ? " • Applied from popup." : " • Applied from editor."));

            ClearPendingValueEdit(session);
            CuiHelper.DestroyUi(player, ValueEditUiName);
            RefreshOpenEditorSurfaces(player);
        }

        private void CancelPendingValueEdit(BasePlayer player, bool refreshStatus)
        {
            if (player == null)
            {
                return;
            }

            EditorSession session;
            if (!sessions.TryGetValue(player.userID, out session) || session == null)
            {
                CuiHelper.DestroyUi(player, ValueEditUiName);
                return;
            }

            ClearPendingValueEdit(session);
            CuiHelper.DestroyUi(player, ValueEditUiName);
            if (refreshStatus)
            {
                SetStatus(session, "Exact value edit cancelled.", "");
            }
        }

        private void ClearPendingValueEdit(EditorSession session)
        {
            if (session == null)
            {
                return;
            }

            session.PendingValueEdit = null;
            session.ValueEditUiOpen = false;
        }

        private bool TryGetPendingValueEditContext(BasePlayer player, EditorSession session, out PendingValueEdit edit, out VisualProfileConfig profile, out VisualProfileWaypoint waypoint, out int index)
        {
            edit = null;
            profile = null;
            waypoint = null;
            index = -1;

            if (session == null || session.PendingValueEdit == null)
            {
                return false;
            }

            edit = session.PendingValueEdit;
            if (!string.Equals(session.ProfileId, edit.ProfileId, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(session, "Exact value edit expired.", "The active profile changed before APPLY.");
                return false;
            }

            if (!TryGetSessionProfile(player, session, out profile))
            {
                return false;
            }

            if (IsGenericValueEdit(edit))
            {
                var scope = edit.GenericScope.Trim().ToLowerInvariant();
                if (scope == "profile" || scope == "pattern")
                {
                    return true;
                }

                if (scope != "waypointtime" || profile.Waypoints == null)
                {
                    return false;
                }

                waypoint = edit.Waypoint;
                index = profile.Waypoints.IndexOf(waypoint);
                if (waypoint == null || index < 0)
                {
                    SetStatus(session, "Exact value edit expired.", "The selected waypoint changed before APPLY.");
                    return false;
                }

                return true;
            }

            if (edit.ReleaseEvent)
            {
                if (profile.PayloadEvents == null)
                {
                    return false;
                }

                index = profile.PayloadEvents.IndexOf(edit.PayloadEvent);
                if (edit.PayloadEvent == null || index < 0)
                {
                    SetStatus(session, "Exact value edit expired.", "The selected release changed before APPLY.");
                    return false;
                }

                return true;
            }

            if (profile.Waypoints == null)
            {
                return false;
            }

            waypoint = edit.Waypoint;
            index = profile.Waypoints.IndexOf(waypoint);
            if (waypoint == null || index < 0)
            {
                SetStatus(session, "Exact value edit expired.", "The selected waypoint changed before APPLY.");
                return false;
            }

            return true;
        }

        private void ApplySelectedWaypointCoordinateInput(BasePlayer player, string axis, string value, bool fromPopup, bool refreshUi = true)
        {
            var normalizedAxis = NormalizeCoordinateAxis(axis);
            if (string.IsNullOrWhiteSpace(normalizedAxis))
            {
                return;
            }

            float parsed;
            if (!TryParseInputFloat(value, normalizedAxis, out parsed))
            {
                var sessionForError = GetOrCreateSession(player);
                SetStatus(sessionForError, "Invalid " + normalizedAxis.ToUpperInvariant() + " coordinate.", "Enter a number like -150, 0, or 42.5.");
                RefreshOpenEditorSurfaces(player);
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var waypoint = GetSelectedWaypoint(session, profile);
            if (waypoint == null)
            {
                return;
            }

            SetWaypointCoordinate(waypoint, normalizedAxis, parsed);
            NormalizeProfile(session.ProfileId, profile);
            session.SelectedWaypointIndex = profile.Waypoints.IndexOf(waypoint);
            if (session.SelectedWaypointIndex < 0 && profile.Waypoints.Count > 0)
            {
                session.SelectedWaypointIndex = 0;
            }

            RebuildMarkers(player, session);
            SetStatus(session, "Set waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + " " + normalizedAxis.ToUpperInvariant() + " to " + FormatFloat(GetWaypointCoordinate(waypoint, normalizedAxis)) + ".", fromPopup ? "Typed value from waypoint popup." : "Typed value from full editor.");
            if (refreshUi)
            {
                RefreshOpenEditorSurfaces(player);
            }
        }

        private void ApplySelectedWaypointRotationInput(BasePlayer player, string axis, string value, bool fromPopup, bool refreshUi = true)
        {
            var normalizedAxis = NormalizeCoordinateAxis(axis);
            if (string.IsNullOrWhiteSpace(normalizedAxis))
            {
                return;
            }

            float parsed;
            if (!TryParseInputFloat(value, normalizedAxis, out parsed))
            {
                var sessionForError = GetOrCreateSession(player);
                SetStatus(sessionForError, "Invalid rotation " + normalizedAxis.ToUpperInvariant() + ".", "Enter degrees like 0, 90, -45, or 180.");
                RefreshOpenEditorSurfaces(player);
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var waypoint = GetSelectedWaypoint(session, profile);
            if (waypoint == null)
            {
                return;
            }

            SetWaypointRotationAxis(waypoint, normalizedAxis, NormalizeDegrees(parsed));
            NormalizeProfile(session.ProfileId, profile);
            session.SelectedWaypointIndex = profile.Waypoints.IndexOf(waypoint);
            if (session.SelectedWaypointIndex < 0 && profile.Waypoints.Count > 0)
            {
                session.SelectedWaypointIndex = 0;
            }

            RebuildMarkers(player, session);
            SetStatus(session, "Set waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + " rotation " + normalizedAxis.ToUpperInvariant() + " to " + FormatDegrees(GetWaypointRotationAxis(waypoint, normalizedAxis)) + ".", fromPopup ? "Typed value from waypoint popup." : "Typed value from full editor.");
            if (refreshUi)
            {
                RefreshOpenEditorSurfaces(player);
            }
        }

        private void ShowInputSubmitWarning(BasePlayer player, string fieldType)
        {
            var session = GetOrCreateSession(player);
            SetStatus(session, "Typed " + fieldType + " did not submit a value.", "Click the field, replace the number, then press Enter. Use SAVE after the value updates.");
            RefreshOpenEditorSurfaces(player);
        }

        private void QueueSelectedWaypointInput(BasePlayer player, string axis, string value, bool rotation, bool fromPopup)
        {
            var normalizedAxis = NormalizeCoordinateAxis(axis);
            if (player == null || string.IsNullOrWhiteSpace(normalizedAxis))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                ShowInputSubmitWarning(player, rotation ? "rotation" : "position");
                return;
            }

            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var waypoint = GetSelectedWaypoint(session, profile);
            if (waypoint == null)
            {
                return;
            }

            CancelPendingAxisInput(session);
            var pending = new PendingAxisInput
            {
                ProfileId = session.ProfileId,
                Waypoint = waypoint,
                Axis = normalizedAxis,
                Value = value,
                Rotation = rotation,
                FromPopup = fromPopup
            };

            session.PendingAxisInput = pending;
            session.PendingAxisInputTimer = timer.Once(TypedInputCommitDelaySeconds, () => CommitPendingAxisInput(player.userID, pending));
        }

        private void CommitPendingAxisInput(ulong userId, PendingAxisInput pending)
        {
            if (pending == null)
            {
                return;
            }

            EditorSession session;
            if (!sessions.TryGetValue(userId, out session) || session == null || !ReferenceEquals(session.PendingAxisInput, pending))
            {
                return;
            }

            session.PendingAxisInput = null;
            session.PendingAxisInputTimer = null;

            var player = BasePlayer.FindByID(userId);
            if (player == null || !CanUse(player))
            {
                return;
            }

            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile) || profile.Waypoints == null)
            {
                return;
            }

            if (!string.Equals(session.ProfileId, pending.ProfileId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var index = profile.Waypoints.IndexOf(pending.Waypoint);
            if (index < 0)
            {
                return;
            }

            session.SelectedWaypointIndex = index;
            if (pending.Rotation)
            {
                ApplySelectedWaypointRotationInput(player, pending.Axis, pending.Value, pending.FromPopup, false);
            }
            else
            {
                ApplySelectedWaypointCoordinateInput(player, pending.Axis, pending.Value, pending.FromPopup, false);
            }
        }

        private void CancelPendingAxisInput(EditorSession session)
        {
            if (session == null)
            {
                return;
            }

            if (session.PendingAxisInputTimer != null)
            {
                session.PendingAxisInputTimer.Destroy();
                session.PendingAxisInputTimer = null;
            }

            session.PendingAxisInput = null;
        }

        private void FlushPendingAxisInput(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            EditorSession session;
            if (!sessions.TryGetValue(player.userID, out session) || session == null || session.PendingAxisInput == null)
            {
                return;
            }

            var pending = session.PendingAxisInput;
            if (session.PendingAxisInputTimer != null)
            {
                session.PendingAxisInputTimer.Destroy();
                session.PendingAxisInputTimer = null;
            }

            CommitPendingAxisInput(player.userID, pending);
        }

        private void ApplyAxisOnlyInput(ConsoleSystem.Arg arg, string axis, bool rotation, bool fromPopup)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            var value = GetArgTail(arg, 0);
            if (string.IsNullOrWhiteSpace(value))
            {
                ShowInputSubmitWarning(player, rotation ? "rotation" : "position");
                return;
            }

            QueueSelectedWaypointInput(player, axis, value, rotation, fromPopup);
        }

        private void ApplyWaypointNormalizeCommand(BasePlayer player, string[] args, EditorSession session, VisualProfileConfig profile)
        {
            if (args.Length < 3)
            {
                Reply(player, "Usage: /airanim wp norm <x|y|z> <marked|all|clear|indices...>. Marked waypoints match the active waypoint on that axis.");
                return;
            }

            var axis = NormalizeCoordinateAxis(args[2]);
            if (string.IsNullOrWhiteSpace(axis))
            {
                Reply(player, "Normalize axis must be x, y, or z.");
                return;
            }

            session.NormalizeAxis = axis;
            if (args.Length >= 4)
            {
                var selector = (args[3] ?? "").Trim().ToLowerInvariant();
                if (selector == "clear" || selector == "none")
                {
                    ClearNormalizeSelection(session);
                    SetStatus(session, "Cleared waypoint normalization marks.", "");
                    Reply(player, "Cleared waypoint normalization marks.");
                    RefreshEditorUiIfOpen(player);
                    return;
                }

                if (selector == "all")
                {
                    SelectAllNormalizeWaypoints(session, profile);
                }
                else if (selector == "selected" || selector == "active")
                {
                    ClearNormalizeSelection(session);
                    if (session.SelectedWaypointIndex >= 0 && session.SelectedWaypointIndex < profile.Waypoints.Count)
                    {
                        SetNormalizeWaypointSelected(session, profile, session.SelectedWaypointIndex, true);
                    }
                }
                else if (selector != "marked" && selector != "marks")
                {
                    ClearNormalizeSelection(session);
                    for (var i = 3; i < args.Length; i++)
                    {
                        int index;
                        if (!TryParseWaypointIndex(args[i], profile, out index))
                        {
                            Reply(player, "Invalid waypoint index '" + args[i] + "'. Use /airanim wp list.");
                            RefreshEditorUiIfOpen(player);
                            return;
                        }

                        SetNormalizeWaypointSelected(session, profile, index, true);
                    }
                }
            }

            ApplyWaypointNormalization(player, true);
        }

        private void ApplyWaypointNormalization(BasePlayer player, bool reply = false)
        {
            var session = GetOrCreateSession(player);
            VisualProfileConfig profile;
            if (!TryGetSessionProfile(player, session, out profile))
            {
                return;
            }

            var axis = NormalizeCoordinateAxis(session.NormalizeAxis);
            if (string.IsNullOrWhiteSpace(axis))
            {
                axis = "y";
                session.NormalizeAxis = axis;
            }

            var anchor = GetSelectedWaypoint(session, profile);
            if (anchor == null)
            {
                Reply(player, "No active waypoint is selected.");
                return;
            }

            var marked = GetNormalizeWaypoints(session, profile);
            if (marked.Count == 0)
            {
                SetStatus(session, "No waypoints marked for normalization.", "Click MARK on waypoint rows or use ALL.");
                if (reply)
                {
                    Reply(player, "No waypoints are marked. Click MARK on rows, use ALL, or run /airanim wp norm " + axis + " all.");
                }

                RefreshOpenEditorSurfaces(player);
                return;
            }

            var referenceValue = GetWaypointCoordinate(anchor, axis);
            var changed = 0;
            foreach (var waypoint in marked)
            {
                var oldValue = GetWaypointCoordinate(waypoint, axis);
                SetWaypointCoordinate(waypoint, axis, referenceValue);
                if (Mathf.Abs(oldValue - referenceValue) > 0.001f)
                {
                    changed++;
                }
            }

            NormalizeProfile(session.ProfileId, profile);
            session.SelectedWaypointIndex = profile.Waypoints.IndexOf(anchor);
            if (session.SelectedWaypointIndex < 0 && profile.Waypoints.Count > 0)
            {
                session.SelectedWaypointIndex = 0;
            }

            PruneNormalizeSelection(session, profile);
            RebuildMarkers(player, session);
            var axisLabel = axis.ToUpperInvariant();
            var markedCount = marked.Count;
            SetStatus(session, "Normalized " + markedCount + " waypoint(s) on " + axisLabel + ".", "Matched active waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + " at " + FormatFloat(referenceValue) + "; changed " + changed + ".");
            if (reply)
            {
                Reply(player, "Normalized " + markedCount + " waypoint(s) on " + axisLabel + " to " + FormatFloat(referenceValue) + " from active waypoint #" + DisplayIndex(session.SelectedWaypointIndex) + ".");
            }

            RefreshOpenEditorSurfaces(player);
        }

        private string NormalizeCoordinateAxis(string axis)
        {
            var normalized = (axis ?? "").Trim().ToLowerInvariant();
            if (normalized == "x" || normalized == "y" || normalized == "z")
            {
                return normalized;
            }

            return "";
        }

        private float GetWaypointCoordinate(VisualProfileWaypoint waypoint, string axis)
        {
            if (waypoint == null)
            {
                return 0f;
            }

            switch (NormalizeCoordinateAxis(axis))
            {
                case "x":
                    return waypoint.X;
                case "z":
                    return waypoint.Z;
                default:
                    return waypoint.Y;
            }
        }

        private void SetWaypointCoordinate(VisualProfileWaypoint waypoint, string axis, float value)
        {
            if (waypoint == null)
            {
                return;
            }

            switch (NormalizeCoordinateAxis(axis))
            {
                case "x":
                    waypoint.X = value;
                    break;
                case "z":
                    waypoint.Z = value;
                    break;
                default:
                    waypoint.Y = value;
                    break;
            }
        }

        private void ClearNormalizeSelection(EditorSession session)
        {
            if (session != null)
            {
                session.NormalizeWaypoints.Clear();
            }
        }

        private bool ToggleNormalizeWaypointSelection(EditorSession session, VisualProfileConfig profile, int index)
        {
            PruneNormalizeSelection(session, profile);
            if (session == null || profile == null || profile.Waypoints == null || index < 0 || index >= profile.Waypoints.Count)
            {
                return false;
            }

            var waypoint = profile.Waypoints[index];
            if (session.NormalizeWaypoints.Contains(waypoint))
            {
                session.NormalizeWaypoints.Remove(waypoint);
                return false;
            }

            session.NormalizeWaypoints.Add(waypoint);
            return true;
        }

        private bool SetNormalizeWaypointSelected(EditorSession session, VisualProfileConfig profile, int index, bool selected)
        {
            PruneNormalizeSelection(session, profile);
            if (session == null || profile == null || profile.Waypoints == null || index < 0 || index >= profile.Waypoints.Count)
            {
                return false;
            }

            var waypoint = profile.Waypoints[index];
            if (selected)
            {
                session.NormalizeWaypoints.Add(waypoint);
            }
            else
            {
                session.NormalizeWaypoints.Remove(waypoint);
            }

            return true;
        }

        private void SelectAllNormalizeWaypoints(EditorSession session, VisualProfileConfig profile)
        {
            ClearNormalizeSelection(session);
            if (session == null || profile == null || profile.Waypoints == null)
            {
                return;
            }

            foreach (var waypoint in profile.Waypoints)
            {
                if (waypoint != null)
                {
                    session.NormalizeWaypoints.Add(waypoint);
                }
            }
        }

        private int CountNormalizeWaypoints(EditorSession session, VisualProfileConfig profile)
        {
            return GetNormalizeWaypoints(session, profile).Count;
        }

        private bool IsNormalizeWaypointSelected(EditorSession session, VisualProfileWaypoint waypoint)
        {
            return session != null && waypoint != null && session.NormalizeWaypoints.Contains(waypoint);
        }

        private List<VisualProfileWaypoint> GetNormalizeWaypoints(EditorSession session, VisualProfileConfig profile)
        {
            var waypoints = new List<VisualProfileWaypoint>();
            PruneNormalizeSelection(session, profile);
            if (session == null || profile == null || profile.Waypoints == null)
            {
                return waypoints;
            }

            foreach (var waypoint in profile.Waypoints)
            {
                if (waypoint != null && session.NormalizeWaypoints.Contains(waypoint))
                {
                    waypoints.Add(waypoint);
                }
            }

            return waypoints;
        }

        private void PruneNormalizeSelection(EditorSession session, VisualProfileConfig profile)
        {
            if (session == null)
            {
                return;
            }

            if (profile == null || profile.Waypoints == null || profile.Waypoints.Count == 0)
            {
                session.NormalizeWaypoints.Clear();
                return;
            }

            var allowed = new HashSet<VisualProfileWaypoint>(profile.Waypoints);
            session.NormalizeWaypoints.RemoveWhere(waypoint => waypoint == null || !allowed.Contains(waypoint));
        }

        private bool ApplyWaypointRotationChange(VisualProfileWaypoint waypoint, string axis, string value)
        {
            if (waypoint == null)
            {
                return false;
            }

            var normalizedAxis = (axis ?? "").Trim().ToLowerInvariant();
            if (normalizedAxis != "x" && normalizedAxis != "y" && normalizedAxis != "z")
            {
                return false;
            }

            var reset = string.Equals(value, "reset", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "zero", StringComparison.OrdinalIgnoreCase);
            var current = GetWaypointRotationAxis(waypoint, normalizedAxis);
            float next;
            if (reset)
            {
                next = 0f;
            }
            else
            {
                float delta;
                if (!TryParseFloat(value, out delta))
                {
                    return false;
                }

                next = current + delta;
            }

            SetWaypointRotationAxis(waypoint, normalizedAxis, NormalizeDegrees(next));
            return true;
        }

        private float GetWaypointRotationAxis(VisualProfileWaypoint waypoint, string axis)
        {
            switch (axis)
            {
                case "x":
                    return waypoint.RotationX;
                case "y":
                    return waypoint.RotationY;
                case "z":
                    return waypoint.RotationZ;
            }

            return 0f;
        }

        private void SetWaypointRotationAxis(VisualProfileWaypoint waypoint, string axis, float value)
        {
            switch (axis)
            {
                case "x":
                    waypoint.RotationX = value;
                    break;
                case "y":
                    waypoint.RotationY = value;
                    break;
                case "z":
                    waypoint.RotationZ = value;
                    break;
            }
        }

        private float NormalizeDegrees(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees))
            {
                return 0f;
            }

            degrees %= 360f;
            if (degrees > 180f)
            {
                degrees -= 360f;
            }
            else if (degrees < -180f)
            {
                degrees += 360f;
            }

            return degrees;
        }

        private bool TryParseWaypointIndex(string value, VisualProfileConfig profile, out int index)
        {
            index = -1;
            if (profile == null || profile.Waypoints == null || profile.Waypoints.Count == 0)
            {
                return false;
            }

            int parsed;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return false;
            }

            if (parsed == 0)
            {
                index = 0;
                return true;
            }

            var oneBased = parsed - 1;
            if (oneBased >= 0 && oneBased < profile.Waypoints.Count)
            {
                index = oneBased;
                return true;
            }

            if (parsed >= 0 && parsed < profile.Waypoints.Count)
            {
                index = parsed;
                return true;
            }

            return false;
        }

        private bool TryResolveWaypointEditIndex(EditorSession session, VisualProfileConfig profile, string selector, out int index)
        {
            index = -1;
            if (profile == null || profile.Waypoints == null || profile.Waypoints.Count == 0)
            {
                return false;
            }

            var token = (selector ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(token) || token == "selected" || token == "select" || token == "sel" || token == "active" || token == "current")
            {
                index = session == null ? -1 : session.SelectedWaypointIndex;
                if (index < 0 || index >= profile.Waypoints.Count)
                {
                    index = 0;
                }

                return true;
            }

            return TryParseWaypointIndex(token, profile, out index);
        }

        private bool TryParseFloat(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private bool TryParseInputFloat(string value, string axis, out float result)
        {
            result = 0f;
            var symbolic = StripInputQuotes(value).Trim().ToLowerInvariant();
            if (symbolic == "auto" || symbolic == "default" || symbolic == "inherit")
            {
                result = -1f;
                return true;
            }

            var cleaned = CleanNumericInput(value, axis);
            if (TryParseFloat(cleaned, out result))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var parts = value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                cleaned = CleanNumericInput(parts[i], axis);
                if (TryParseFloat(cleaned, out result))
                {
                    return true;
                }
            }

            return false;
        }

        private string CleanNumericInput(string value, string axis)
        {
            var text = (value ?? "").Trim();
            text = StripInputQuotes(text).Replace('−', '-').Replace('–', '-').Replace('—', '-');

            var normalizedAxis = NormalizeCoordinateAxis(axis);
            if (!string.IsNullOrWhiteSpace(normalizedAxis) && text.Length > 0)
            {
                var first = text.Substring(0, 1).ToLowerInvariant();
                if (first == normalizedAxis)
                {
                    text = text.Substring(1).Trim();
                }
            }

            while (text.StartsWith("=", StringComparison.Ordinal) || text.StartsWith(":", StringComparison.Ordinal))
            {
                text = text.Substring(1).Trim();
            }

            var equalsIndex = text.LastIndexOf('=');
            if (equalsIndex >= 0 && equalsIndex < text.Length - 1)
            {
                text = text.Substring(equalsIndex + 1).Trim();
            }

            text = StripInputQuotes(text);
            var lower = text.ToLowerInvariant();
            if (lower.EndsWith("degrees", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 7).Trim();
            }
            else if (lower.EndsWith("degree", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 6).Trim();
            }
            else if (lower.EndsWith("deg", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 3).Trim();
            }
            else if (lower.EndsWith("seconds", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 7).Trim();
            }
            else if (lower.EndsWith("second", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 6).Trim();
            }
            else if (lower.EndsWith("secs", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 4).Trim();
            }
            else if (lower.EndsWith("sec", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 3).Trim();
            }
            else if (lower.EndsWith("s", StringComparison.Ordinal) && text.Length > 1)
            {
                text = text.Substring(0, text.Length - 1).Trim();
            }

            while (text.EndsWith("°", StringComparison.Ordinal) || text.EndsWith("*", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 1).Trim();
            }

            return StripInputQuotes(text);
        }

        private string StripInputQuotes(string value)
        {
            var text = (value ?? "").Trim();
            while (text.Length >= 2)
            {
                var first = text[0];
                var last = text[text.Length - 1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\'') || (first == '`' && last == '`'))
                {
                    text = text.Substring(1, text.Length - 2).Trim();
                    continue;
                }

                break;
            }

            return text;
        }

        private bool TryGetAxisInput(ConsoleSystem.Arg arg, out string axis, out string value)
        {
            axis = "";
            value = "";
            if (arg?.Args == null || arg.Args.Length == 0)
            {
                return false;
            }

            var first = StripInputQuotes(arg.GetString(0) ?? "");
            axis = NormalizeCoordinateAxis(first);
            if (!string.IsNullOrWhiteSpace(axis))
            {
                value = GetArgTail(arg, 1);
                return !string.IsNullOrWhiteSpace(value);
            }

            if (first.Length > 0)
            {
                axis = NormalizeCoordinateAxis(first.Substring(0, 1));
                if (!string.IsNullOrWhiteSpace(axis))
                {
                    value = first.Length > 1 ? first.Substring(1).Trim() : GetArgTail(arg, 1);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        value = GetArgTail(arg, 1);
                    }

                    return !string.IsNullOrWhiteSpace(value);
                }
            }

            return false;
        }

        private string[] GetArgStrings(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Args == null || arg.Args.Length == 0)
            {
                return new string[0];
            }

            var values = new string[arg.Args.Length];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = arg.GetString(i) ?? "";
            }

            return values;
        }

        private string GetArgTail(ConsoleSystem.Arg arg, int startIndex)
        {
            if (arg == null || arg.Args == null || startIndex < 0 || arg.Args.Length <= startIndex)
            {
                return "";
            }

            var count = arg.Args.Length - startIndex;
            var values = new string[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = arg.GetString(startIndex + i) ?? "";
            }

            return string.Join(" ", values).Trim();
        }

        private string GetArgTail(string[] args, int startIndex)
        {
            if (args == null || startIndex < 0 || args.Length <= startIndex)
            {
                return "";
            }

            var count = args.Length - startIndex;
            var values = new string[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = args[startIndex + i] ?? "";
            }

            return string.Join(" ", values).Trim();
        }

        private bool IsStopToken(string value)
        {
            var token = (value ?? "").Trim().ToLowerInvariant();
            return token == "stop" || token == "off" || token == "end" || token == "exit" || token == "detach";
        }

        private bool IsStageToken(string value)
        {
            var token = (value ?? "").Trim().ToLowerInvariant();
            return token == "stage" || token == "prestage" || token == "start" || token == "load" || token == "preload";
        }

        private bool IsRideToken(string value)
        {
            var token = (value ?? "").Trim().ToLowerInvariant();
            return token == "ride" || token == "follow" || token == "camera";
        }

        private int DisplayIndex(int zeroBasedIndex)
        {
            return zeroBasedIndex + 1;
        }

        private double GetPreciseNow()
        {
            return DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
        }

        private bool CanUse(BasePlayer player)
        {
            return player != null && (player.IsAdmin || permission.UserHasPermission(player.UserIDString, AdminPermission));
        }

        private BasePlayer FindPlayerById(ulong playerId)
        {
            return playerId == 0UL ? null : BasePlayer.FindAwakeOrSleeping(playerId.ToString());
        }

        private BasePlayer FindOnlinePlayer(string query)
        {
            var needle = (query ?? "").Trim();
            if (string.IsNullOrWhiteSpace(needle))
            {
                return null;
            }

            ulong userId;
            if (ulong.TryParse(needle, out userId))
            {
                var byId = BasePlayer.FindByID(userId);
                if (byId != null && byId.IsConnected)
                {
                    return byId;
                }
            }

            BasePlayer partial = null;
            foreach (var candidate in BasePlayer.activePlayerList)
            {
                if (candidate == null || !candidate.IsConnected)
                {
                    continue;
                }

                var name = candidate.displayName ?? "";
                if (string.Equals(name, needle, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                if (name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (partial != null)
                    {
                        return null;
                    }

                    partial = candidate;
                }
            }

            return partial;
        }

        private BasePlayer GetArgPlayer(ConsoleSystem.Arg arg)
        {
            return arg == null ? null : arg.Player();
        }

        private void SetStatus(EditorSession session, string status, string warning)
        {
            if (session == null)
            {
                return;
            }

            session.LastStatus = status ?? "";
            session.LastWarning = warning ?? "";
        }

        private void Reply(BasePlayer player, string message)
        {
            if (player != null)
            {
                player.ChatMessage("<color=#ce422b>[AirAnim]</color> " + message);
            }
        }

        private string FormatSeconds(float seconds)
        {
            return Math.Max(0f, seconds).ToString("0.##", CultureInfo.InvariantCulture) + "s";
        }

        private string FormatMeters(float meters)
        {
            return meters.ToString("0.#", CultureInfo.InvariantCulture) + "m";
        }

        private string FormatFloat(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private string FormatOptionalFloat(float value)
        {
            return value < 0f ? "auto" : FormatFloat(value);
        }

        private string ShortenText(string value, int maxLength)
        {
            var text = value ?? "";
            if (maxLength <= 0 || text.Length <= maxLength)
            {
                return text;
            }

            if (maxLength <= 3)
            {
                return text.Substring(0, maxLength);
            }

            return text.Substring(0, maxLength - 3) + "...";
        }

        private string FormatDegrees(float value)
        {
            return NormalizeDegrees(value).ToString("0.#", CultureInfo.InvariantCulture) + "deg";
        }

        private string FormatPosition(Vector3 position)
        {
            return position.x.ToString("0.0", CultureInfo.InvariantCulture) + ", "
                + position.y.ToString("0.0", CultureInfo.InvariantCulture) + ", "
                + position.z.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private string FormatVectorShort(Vector3 value)
        {
            return value.x.ToString("0.##", CultureInfo.InvariantCulture) + ", " + value.y.ToString("0.##", CultureInfo.InvariantCulture) + ", " + value.z.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private void AddPanel(CuiElementContainer container, string parent, string anchorMin, string anchorMax, string color)
        {
            container.Add(new CuiPanel
            {
                Image = { Color = color },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);
        }

        private string AddOffsetPanel(CuiElementContainer container, string parent, float topOffset, float bottomOffset, string color)
        {
            return container.Add(new CuiPanel
            {
                Image = { Color = color },
                RectTransform =
                {
                    AnchorMin = "0 1",
                    AnchorMax = "1 1",
                    OffsetMin = "0 -" + FormatUiPixels(bottomOffset),
                    OffsetMax = "0 -" + FormatUiPixels(topOffset)
                }
            }, parent);
        }

        private string AddOffsetRectPanel(CuiElementContainer container, string parent, float leftOffset, float rightOffset, float bottomOffset, float topOffset, string color)
        {
            return container.Add(new CuiPanel
            {
                Image = { Color = color },
                RectTransform =
                {
                    AnchorMin = "0 0",
                    AnchorMax = "0 1",
                    OffsetMin = FormatUiPixels(leftOffset) + " " + FormatUiPixels(bottomOffset),
                    OffsetMax = FormatUiPixels(rightOffset) + " " + FormatUiPixels(topOffset)
                }
            }, parent);
        }

        private void AddLabel(CuiElementContainer container, string parent, string text, int size, TextAnchor align, string anchorMin, string anchorMax, string color)
        {
            container.Add(new CuiLabel
            {
                Text = { Text = text ?? "", FontSize = size, Align = align, Color = color },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);
        }

        private void AddButton(CuiElementContainer container, string parent, string text, string command, string anchorMin, string anchorMax, string color, int size)
        {
            container.Add(new CuiButton
            {
                Button = { Command = command ?? "", Color = color },
                Text = { Text = text ?? "", FontSize = size, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);
        }

        private void AddTextInput(CuiElementContainer container, string parent, string text, string command, string anchorMin, string anchorMax, string color, int size, int charsLimit, TextAnchor align)
        {
            var panel = container.Add(new CuiPanel
            {
                Image = { Color = color },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);

            container.Add(new CuiElement
            {
                Name = CuiHelper.GetGuid(),
                Parent = panel,
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = text ?? "",
                        Command = command ?? "",
                        CharsLimit = charsLimit,
                        NeedsKeyboard = true,
                        IsPassword = false,
                        LineType = InputField.LineType.SingleLine,
                        FontSize = size,
                        Align = align,
                        Color = "0.94 0.97 1 1"
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.045 0.08", AnchorMax = "0.955 0.92" }
                }
            });
        }

        private string AddScrollView(CuiElementContainer container, string parent, string anchorMin, string anchorMax, float contentHeight, bool autoHide)
        {
            var scrollName = UiName + ".Scroll." + CuiHelper.GetGuid();
            var contentRect = new CuiRectTransformComponent
            {
                AnchorMin = "0 1",
                AnchorMax = "1 1",
                OffsetMin = "0 -" + FormatUiPixels(contentHeight),
                OffsetMax = "0 0"
            };

            container.Add(new CuiElement
            {
                Name = scrollName,
                Parent = parent,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax },
                    new CuiScrollViewComponent
                    {
                        Horizontal = false,
                        Vertical = true,
                        MovementType = ScrollRect.MovementType.Elastic,
                        Elasticity = 0.08f,
                        Inertia = false,
                        DecelerationRate = 0.135f,
                        ScrollSensitivity = 80f,
                        ContentTransform = contentRect,
                        VerticalScrollbar = new CuiScrollbar
                        {
                            Invert = false,
                            AutoHide = autoHide,
                            Size = 5f,
                            HandleColor = "0.72 0.76 0.80 0.45",
                            HighlightColor = "0.88 0.92 0.96 0.65",
                            PressedColor = "1 1 1 0.80",
                            TrackColor = "0.05 0.06 0.07 0.35"
                        }
                    }
                }
            });

            return scrollName;
        }

        private string AddHorizontalScrollView(CuiElementContainer container, string parent, string anchorMin, string anchorMax, float contentWidth, bool autoHide)
        {
            var scrollName = TimelineUiName + ".Scroll." + CuiHelper.GetGuid();
            var contentRect = new CuiRectTransformComponent
            {
                AnchorMin = "0 0",
                AnchorMax = "0 1",
                OffsetMin = "0 0",
                OffsetMax = FormatUiPixels(contentWidth) + " 0"
            };

            container.Add(new CuiElement
            {
                Name = scrollName,
                Parent = parent,
                Components =
                {
                    new CuiImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = anchorMin, AnchorMax = anchorMax },
                    new CuiScrollViewComponent
                    {
                        Horizontal = true,
                        Vertical = false,
                        MovementType = ScrollRect.MovementType.Elastic,
                        Elasticity = 0.08f,
                        Inertia = false,
                        DecelerationRate = 0.135f,
                        ScrollSensitivity = 80f,
                        ContentTransform = contentRect,
                        HorizontalScrollbar = new CuiScrollbar
                        {
                            Invert = false,
                            AutoHide = autoHide,
                            Size = 5f,
                            HandleColor = "0.72 0.76 0.80 0.45",
                            HighlightColor = "0.88 0.92 0.96 0.65",
                            PressedColor = "1 1 1 0.80",
                            TrackColor = "0.05 0.06 0.07 0.35"
                        }
                    }
                }
            });

            return scrollName;
        }

        private string FormatUiPixels(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private Vector2 SplitAnchor(string anchor)
        {
            var parts = (anchor ?? "0 0").Split(' ');
            float x;
            float y;
            if (parts.Length < 2 || !TryParseFloat(parts[0], out x) || !TryParseFloat(parts[1], out y))
            {
                return Vector2.zero;
            }

            return new Vector2(x, y);
        }

        private string FormatAnchor(float x, float y)
        {
            return x.ToString("0.###", CultureInfo.InvariantCulture) + " " + y.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
