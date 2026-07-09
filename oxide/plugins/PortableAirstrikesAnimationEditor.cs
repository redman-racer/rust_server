using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.UI;

#pragma warning disable 0649

namespace Oxide.Plugins
{
    [Info("PortableAirstrikesAnimationEditor", "Raidlands", "0.1.4")]
    [Description("Standalone admin CUI editor for PortableAirstrikes delivery visual waypoint profiles.")]
    public class PortableAirstrikesAnimationEditor : RustPlugin
    {
        private const string AdminPermission = "portableairstrikesanimationeditor.admin";
        private const string DataFileName = "PortableAirstrikes/VisualProfiles";
        private const string UiName = "PortableAirstrikesAnimationEditor.UI";
        private const string ConfirmUiName = "PortableAirstrikesAnimationEditor.Confirm";
        private const int SchemaVersion = 1;

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

        private const float DefaultPreviewMoveIntervalSeconds = 0.04f;
        private const float MinimumPreviewMoveIntervalSeconds = 0.025f;
        private const float MaximumPreviewMoveIntervalSeconds = 0.10f;
        private const float TangentSampleSeconds = 0.18f;
        private const float MarkerRefreshSeconds = 0.95f;
        private const float MarkerNativeRadius = 0.035f;
        private const float SelectedMarkerNativeRadius = 0.060f;
        private const float TargetMarkerNativeRadius = 0.080f;
        private const float MarkerDebugDrawDurationSeconds = 1.20f;
        private const float MarkerBubbleRadius = 1.75f;
        private const float SelectedMarkerBubbleRadius = 2.45f;
        private const float MarkerArrowLength = 2.35f;
        private const float SelectedMarkerArrowLength = 3.10f;
        private const float MarkerArrowHeadSize = 0.30f;
        private const float SelectedMarkerArrowHeadSize = 0.42f;
        private const float MarkerAttitudeTickScale = 0.42f;
        private const float DefaultDroneClearance = 12f;
        private const float DefaultAircraftClearance = 55f;
        private const int MaxChatRows = 12;
        private const int MaxProfilesInUi = 200;
        private const int MaxWaypointsInUi = 120;

        private static readonly Color WaypointBubbleColor = new Color(0.15f, 0.70f, 1f, 0.82f);
        private static readonly Color WaypointArrowColor = new Color(0.90f, 0.98f, 1f, 1f);
        private static readonly Color SelectedWaypointBubbleColor = new Color(1f, 0.55f, 0.12f, 0.95f);
        private static readonly Color SelectedWaypointArrowColor = new Color(1f, 0.92f, 0.32f, 1f);
        private static readonly Color WaypointRightAxisColor = new Color(1f, 0.22f, 0.18f, 0.90f);
        private static readonly Color WaypointUpAxisColor = new Color(0.20f, 1f, 0.36f, 0.90f);

        private static readonly string[] VehicleValues =
        {
            "drone",
            "cargo_plane",
            "f15",
            "a10",
            "attack_heli"
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
        private readonly Dictionary<ulong, EditorSession> sessions = new Dictionary<ulong, EditorSession>();

        /*
         * Standalone integration notes:
         * 1. Profiles are saved to oxide/data/PortableAirstrikes/VisualProfiles.json via DataFileName "PortableAirstrikes/VisualProfiles".
         * 2. Admins use /airanim create <id> <vehicle>, /airanim edit <id>, /airanim target, /airanim wp add/set/time/remove,
         *    /airanim preview, and /airanim save. The CUI mirrors these commands and adds quick buttons for common edits.
         * 3. This plugin previews and authors target-relative timed waypoint profiles independently. PortableAirstrikes does not need to be modified
         *    for profile authoring. Runtime strikes will only consume these profiles after PortableAirstrikes adds profile loading or an API hook.
         * 4. Smallest optional PortableAirstrikes integration later: load the same DataFileName, map strike/delivery IDs to profile IDs, then when
         *    building a DeliveryFlightPlan, convert profile waypoints using the strike target and planned approach. Alternatively expose a hook such as
         *    API_GetVisualProfile(string profileId) or call OnPortableAirstrikesBuildVisualProfile(strikeId, delivery, payload, target, approach).
         */

        private class VisualProfileFile
        {
            [JsonProperty("SchemaVersion")]
            public int SchemaVersion = PortableAirstrikesAnimationEditor.SchemaVersion;

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

            [JsonProperty("RotationSmoothTimeSeconds")]
            public float RotationSmoothTimeSeconds = 0.12f;

            [JsonProperty("MinimumTerrainClearance")]
            public float MinimumTerrainClearance = 55f;

            [JsonProperty("Waypoints")]
            public List<VisualProfileWaypoint> Waypoints = new List<VisualProfileWaypoint>();

            [JsonProperty("PayloadEvents")]
            public List<VisualPayloadEvent> PayloadEvents = new List<VisualPayloadEvent>();
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
        }

        private class VisualPayloadEvent
        {
            [JsonProperty("Time")]
            public float Time;

            [JsonProperty("Payload")]
            public string Payload = "";

            [JsonProperty("Index")]
            public int Index;
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
            public bool FiredFirstPayloadCue;
            public double PreviewStartedAt;
            public bool PreviewActive;
            public bool UiOpen;
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

            CloseSession(player.userID, false);
        }

        private void CmdAirAnim(BasePlayer player, string command, string[] args)
        {
            if (player == null)
            {
                return;
            }

            if (!CanUse(player))
            {
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
                SetStatus(session, "Preview stopped.", "Markers and the editor session are still active.");
                Reply(player, "Preview stopped. Markers and the editor session are still active.");
                RefreshEditorUiIfOpen(player);
                return;
            }

            if (sub == "markers" || sub == "refreshmarkers")
            {
                var session = GetOrCreateSession(player);
                RebuildMarkers(player, session);
                SetStatus(session, "Refreshed waypoint markers.", "Use /airanim hide if the panel is blocking your view.");
                Reply(player, "Waypoint markers refreshed.");
                RefreshEditorUiIfOpen(player);
                return;
            }

            if (sub == "help" || sub == "?")
            {
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
                SaveProfiles();
                SetStatus(GetOrCreateSession(player), "Saved VisualProfiles.json.", "");
                Reply(player, "Saved profiles to oxide/data/PortableAirstrikes/VisualProfiles.json.");
                RefreshEditorUiIfOpen(player);
                return;
            }

            if (sub == "reload")
            {
                LoadProfiles();
                SetStatus(GetOrCreateSession(player), "Reloaded profiles from disk.", "");
                Reply(player, "Reloaded VisualProfiles.json from disk. Unsaved in-memory edits were discarded.");
                RefreshEditorUiIfOpen(player);
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

            if (sub == "vehicle")
            {
                CmdSetVehicle(player, args);
                return;
            }

            Reply(player, "Unknown /airanim command. Use /airanim help.");
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
            SaveProfiles();

            EditorSession session;
            if (sessions.TryGetValue(player.userID, out session) && string.Equals(session.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            {
                DestroyPreview(session);
                DestroyMarkers(session);
                session.ProfileId = "";
                session.SelectedWaypointIndex = -1;
                SetStatus(session, "Deleted '" + profileId + "'.", "");
            }

            Reply(player, "Deleted profile '" + profileId + "' and saved the profile file.");
            RefreshEditorUiIfOpen(player);
        }

        private void CmdWaypoint(BasePlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Waypoint commands: list, add <time> <x> <y> <z>, select <index>, remove <index>, time <index> <seconds>, set <index> <x> <y> <z>.");
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
            DestroyPreview(session);
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
                session.PreviewActive = true;
                session.FiredPayloadEvents.Clear();
                session.FiredFirstPayloadCue = false;
                SetStatus(session, "Previewing '" + session.ProfileId + "'.", "Payload cues are harmless by default.");

                MovePreviewVehicle(session, profile, plan, 0f, true);
                SchedulePreviewSoundCues(player, session, profile, plan);
                SchedulePreviewStep(player, session, profile, plan);
                Reply(player, "Preview started for '" + session.ProfileId + "'. Vehicle=" + profile.Vehicle + ", duration=" + FormatSeconds(profile.DurationSeconds) + ". CUI hidden so you can watch it. Use /airanim to reopen or /airanim stop to end the preview.");
                HideEditorUi(player, false);
            }
            catch (Exception ex)
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill(BaseNetworkable.DestroyMode.None);
                }

                PrintWarning("Preview spawn failed for '" + session.ProfileId + "' vehicle '" + profile.Vehicle + "' prefab '" + prefab + "': " + ex.Message);
                Reply(player, "Preview spawn failed for prefab '" + prefab + "': " + ex.Message);
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

                var elapsed = (float)(GetPreciseNow() - session.PreviewStartedAt);
                var safeDuration = Mathf.Max(0.1f, profile.DurationSeconds);
                MovePreviewVehicle(session, profile, plan, elapsed, false);
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
            var targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
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

            if (!session.FiredFirstPayloadCue && profile.FirstPayloadDelaySeconds >= 0f && elapsed >= profile.FirstPayloadDelaySeconds)
            {
                session.FiredFirstPayloadCue = true;
                var release = EvaluatePlanPosition(plan, profile, profile.FirstPayloadDelaySeconds);
                RunSafeEffect(RocketLaunchEffect, release, "first payload release cue");
                Reply(player, "Payload release cue at " + FormatSeconds(profile.FirstPayloadDelaySeconds) + " for '" + session.ProfileId + "' (safe visual only).");
            }

            if (profile.PayloadEvents == null || profile.PayloadEvents.Count == 0)
            {
                return;
            }

            for (var i = 0; i < profile.PayloadEvents.Count; i++)
            {
                if (session.FiredPayloadEvents.Contains(i))
                {
                    continue;
                }

                var payloadEvent = profile.PayloadEvents[i];
                if (payloadEvent == null || elapsed < payloadEvent.Time)
                {
                    continue;
                }

                session.FiredPayloadEvents.Add(i);
                var release = EvaluatePlanPosition(plan, profile, payloadEvent.Time);
                RunSafeEffect(RocketLaunchEffect, release, "payload event cue");
                RunSafeEffect(BulletImpactEffect, release + Vector3.up * 0.25f, "payload event spark");
                var payload = string.IsNullOrWhiteSpace(payloadEvent.Payload) ? "payload" : payloadEvent.Payload;
                Reply(player, "Payload event #" + Math.Max(1, payloadEvent.Index) + " " + payload + " at " + FormatSeconds(payloadEvent.Time) + " (safe visual only). Dangerous payload preview is " + (profileFile.AllowDangerousPayloadPreview ? "configured true but not implemented in this editor build" : "disabled") + ".");
            }
        }

        private void CompletePreview(BasePlayer player, EditorSession session)
        {
            if (session == null)
            {
                return;
            }

            DestroyPreview(session);
            SetStatus(session, "Preview complete.", "");
            if (player != null && player.IsConnected)
            {
                Reply(player, "Preview complete.");
                RefreshEditorUiIfOpen(player);
            }
        }

        private void DestroyPreview(EditorSession session)
        {
            if (session == null)
            {
                return;
            }

            session.PreviewActive = false;
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
            session.FiredFirstPayloadCue = false;

            if (session.PreviewVehicle != null && !session.PreviewVehicle.IsDestroyed)
            {
                session.PreviewVehicle.Kill(BaseNetworkable.DestroyMode.None);
            }

            session.PreviewVehicle = null;
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

                    if (!IsSessionActive(player, session))
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
        }

        private void ShowHelp(BasePlayer player)
        {
            Reply(player, "PortableAirstrikes animation editor commands:");
            Reply(player, "/airanim or /airanim ui - open the CUI dashboard; /airanim close|hide - hide only the CUI; /airanim end - clean up session, markers, and preview.");
            Reply(player, "/airanim list; /airanim create <profileId> <vehicle>; /airanim edit <profileId>; /airanim target; /airanim markers; /airanim preview [profileId]; /airanim stop.");
            Reply(player, "/airanim save; /airanim reload; /airanim delete <profileId>.");
            Reply(player, "/airanim wp list; /airanim wp add <time> <x> <y> <z>; /airanim wp select <index>; /airanim wp remove <index>; /airanim wp time <index> <seconds>; /airanim wp set <index> <x> <y> <z>.");
            Reply(player, "/airanim nudge forward|back|left|right|up|down <meters>; /airanim duration <seconds>; /airanim firstpayload <seconds>; /airanim smooth <seconds>; /airanim clearance <meters>; /airanim vehicle <vehicle>.");
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
            }

            DestroyUi(player);

            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel
            {
                CursorEnabled = true,
                Image = { Color = "0.035 0.040 0.050 0.965" },
                RectTransform = { AnchorMin = "0.10 0.07", AnchorMax = "0.90 0.93" }
            }, "Overlay", UiName);

            AddPanel(container, root, "0.018 0.875", "0.982 0.982", "0.09 0.10 0.12 0.96");
            AddLabel(container, root, "Portable Airstrikes Animation Editor", 20, TextAnchor.MiddleLeft, "0.035 0.925", "0.55 0.975", "1 0.86 0.58 1");
            AddLabel(container, root, "Admin-only waypoint profile authoring • hide the panel to watch previews • safe payload cues", 11, TextAnchor.MiddleLeft, "0.036 0.887", "0.72 0.925", "0.68 0.76 0.82 1");
            AddButton(container, root, "TARGET", "airanim.ui.target", "0.725 0.910", "0.805 0.965", "0.14 0.36 0.42 0.95", 11);
            AddButton(container, root, "HELP", "airanim.ui.help", "0.812 0.910", "0.885 0.965", "0.19 0.23 0.28 0.95", 11);
            AddButton(container, root, "X", "airanim.ui.hide", "0.910 0.910", "0.965 0.965", "0.55 0.12 0.10 0.95", 14);

            AddProfileListUi(container, root, player, session);
            AddProfileDetailsUi(container, root, player, session, profile);
            AddWaypointListUi(container, root, session, profile);
            AddNudgePadUi(container, root, session, profile);
            AddBottomActionBarUi(container, root, session, profile);
            AddStatusBarUi(container, root, session);

            CuiHelper.AddUi(player, container);
            session.UiOpen = true;
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

            AddLabel(container, root, "Quick adjust", 10, TextAnchor.MiddleLeft, "0.350 0.620", "0.450 0.650", "0.58 0.66 0.72 1");
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
            AddLabel(container, root, count + " total", 10, TextAnchor.MiddleRight, "0.540 0.545", "0.655 0.578", "0.60 0.68 0.74 1");
            AddButton(container, root, "PREV", "airanim.ui.prevwp", "0.350 0.505", "0.420 0.536", "0.14 0.18 0.22 0.95", 9);
            AddButton(container, root, "NEXT", "airanim.ui.nextwp", "0.425 0.505", "0.495 0.536", "0.14 0.18 0.22 0.95", 9);
            AddButton(container, root, "ADD", "airanim.ui.addwp", "0.500 0.505", "0.570 0.536", "0.16 0.30 0.20 0.95", 9);
            AddButton(container, root, "REMOVE", "airanim.ui.removewp", "0.575 0.505", "0.655 0.536", "0.45 0.16 0.12 0.95", 9);

            if (count == 0)
            {
                AddLabel(container, root, "No waypoints yet. Click ADD or use /airanim wp add <time> <x> <y> <z>.", 10, TextAnchor.MiddleCenter, "0.350 0.360", "0.655 0.430", "0.70 0.76 0.82 1");
                return;
            }

            var rows = Math.Min(MaxWaypointsInUi, count);
            var contentHeight = Math.Max(250f, 6f + rows * 42f);
            var scroll = AddScrollView(container, root, "0.350 0.242", "0.655 0.496", contentHeight, true);
            for (var i = 0; i < rows; i++)
            {
                var waypoint = profile.Waypoints[i];
                var selected = i == session.SelectedWaypointIndex;
                var top = 6f + i * 42f;
                var bottom = top + 36f;
                var row = AddOffsetPanel(container, scroll, top, bottom, selected ? "0.27 0.15 0.08 0.94" : "0.10 0.12 0.145 0.88");
                AddLabel(container, row, "#" + DisplayIndex(i) + "  t=" + FormatSeconds(waypoint.Time), 10, TextAnchor.MiddleLeft, "0.030 0.51", "0.420 0.94", selected ? "1 0.86 0.55 1" : "0.92 0.96 1 1");
                AddLabel(container, row, "X " + FormatFloat(waypoint.X) + "   Y " + FormatFloat(waypoint.Y) + "   Z " + FormatFloat(waypoint.Z), 9, TextAnchor.MiddleLeft, "0.030 0.08", "0.720 0.48", "0.60 0.68 0.74 1");
                AddButton(container, row, selected ? "ACTIVE" : "SELECT", "airanim.ui.selectwp " + DisplayIndex(i), "0.725 0.18", "0.965 0.82", selected ? "0.50 0.20 0.10 0.95" : "0.18 0.24 0.30 0.95", 8);
            }
        }

        private void AddNudgePadUi(CuiElementContainer container, string root, EditorSession session, VisualProfileConfig profile)
        {
            AddPanel(container, root, "0.695 0.224", "0.982 0.585", "0.055 0.065 0.080 0.94");
            AddLabel(container, root, "Waypoint Controls", 15, TextAnchor.MiddleLeft, "0.715 0.545", "0.880 0.578", "1 1 1 1");
            AddLabel(container, root, "1m / 5m nudges", 10, TextAnchor.MiddleRight, "0.850 0.545", "0.960 0.578", "0.60 0.68 0.74 1");

            if (profile == null || profile.Waypoints == null || profile.Waypoints.Count == 0)
            {
                AddLabel(container, root, "Select or create a profile, then add waypoints to enable the nudge pad.", 10, TextAnchor.MiddleCenter, "0.720 0.380", "0.955 0.450", "0.70 0.76 0.82 1");
                return;
            }

            AddButton(container, root, "FWD +1", "airanim.ui.nudge forward 1", "0.790 0.490", "0.880 0.530", "0.15 0.25 0.32 0.95", 9);
            AddButton(container, root, "FWD +5", "airanim.ui.nudge forward 5", "0.885 0.490", "0.955 0.530", "0.15 0.25 0.32 0.95", 9);
            AddButton(container, root, "LEFT 1", "airanim.ui.nudge left 1", "0.715 0.440", "0.790 0.482", "0.15 0.25 0.32 0.95", 9);
            AddButton(container, root, "RIGHT 1", "airanim.ui.nudge right 1", "0.880 0.440", "0.955 0.482", "0.15 0.25 0.32 0.95", 9);
            AddButton(container, root, "LEFT 5", "airanim.ui.nudge left 5", "0.715 0.390", "0.790 0.432", "0.12 0.18 0.24 0.95", 9);
            AddButton(container, root, "RIGHT 5", "airanim.ui.nudge right 5", "0.880 0.390", "0.955 0.432", "0.12 0.18 0.24 0.95", 9);
            AddButton(container, root, "BACK -1", "airanim.ui.nudge back 1", "0.790 0.340", "0.880 0.382", "0.15 0.25 0.32 0.95", 9);
            AddButton(container, root, "BACK -5", "airanim.ui.nudge back 5", "0.885 0.340", "0.955 0.382", "0.15 0.25 0.32 0.95", 9);
            AddButton(container, root, "UP +1", "airanim.ui.nudge up 1", "0.715 0.290", "0.815 0.330", "0.16 0.30 0.20 0.95", 9);
            AddButton(container, root, "UP +5", "airanim.ui.nudge up 5", "0.820 0.290", "0.955 0.330", "0.16 0.30 0.20 0.95", 9);
            AddButton(container, root, "DOWN -1", "airanim.ui.nudge down 1", "0.715 0.242", "0.815 0.282", "0.42 0.18 0.12 0.95", 9);
            AddButton(container, root, "DOWN -5", "airanim.ui.nudge down 5", "0.820 0.242", "0.955 0.282", "0.42 0.18 0.12 0.95", 9);

            AddLabel(container, root, "Forward/back edits local Z. Left/right edits local X. Up/down edits local Y.", 9, TextAnchor.MiddleCenter, "0.715 0.195", "0.955 0.224", "0.54 0.62 0.68 1");
        }

        private void AddBottomActionBarUi(CuiElementContainer container, string root, EditorSession session, VisualProfileConfig profile)
        {
            AddPanel(container, root, "0.333 0.130", "0.982 0.208", "0.055 0.065 0.080 0.94");
            AddButton(container, root, "SET TARGET", "airanim.ui.target", "0.350 0.150", "0.455 0.190", "0.14 0.36 0.42 0.95", 10);
            AddButton(container, root, "REFRESH MARKERS", "airanim.ui.markers", "0.465 0.150", "0.585 0.190", "0.18 0.24 0.30 0.95", 10);
            AddButton(container, root, "PREVIEW", "airanim.ui.preview", "0.595 0.150", "0.675 0.190", profile == null ? "0.10 0.10 0.10 0.80" : "0.48 0.16 0.10 0.95", 10);
            AddButton(container, root, "STOP", "airanim.ui.stop", "0.685 0.150", "0.735 0.190", session.PreviewActive ? "0.46 0.13 0.10 0.95" : "0.12 0.15 0.19 0.80", 9);
            AddButton(container, root, "SAVE", "airanim.ui.save", "0.745 0.150", "0.795 0.190", "0.40 0.18 0.12 0.95", 9);
            AddButton(container, root, "HIDE", "airanim.ui.hide", "0.805 0.150", "0.855 0.190", "0.18 0.22 0.28 0.95", 8);
            AddButton(container, root, "END", "airanim.ui.endsession", "0.865 0.150", "0.915 0.190", "0.36 0.12 0.10 0.95", 8);
            if (profile != null)
            {
                AddButton(container, root, "DEL", "airanim.ui.deleteprompt", "0.925 0.150", "0.965 0.190", "0.44 0.10 0.08 0.95", 8);
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
            SetStatus(session, "Preview stopped.", "Markers and the editor session are still active.");
            Reply(player, "Preview stopped. Markers and the editor session are still active.");
            ShowEditorUi(player);
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
            SetStatus(session, "Refreshed waypoint markers.", "");
            ShowEditorUi(player);
        }

        [ConsoleCommand("airanim.ui.save")]
        private void CCmdUiSave(ConsoleSystem.Arg arg)
        {
            var player = GetArgPlayer(arg);
            if (player == null || !CanUse(player))
            {
                return;
            }

            SaveProfiles();
            SetStatus(GetOrCreateSession(player), "Saved VisualProfiles.json.", "");
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

            LoadProfiles();
            var session = GetOrCreateSession(player);
            DestroyPreview(session);
            RebuildMarkers(player, session);
            SetStatus(session, "Reloaded profile file.", "Unsaved edits were discarded.");
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
            CuiHelper.AddUi(player, container);
        }

        private void RefreshEditorUiIfOpen(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
            {
                return;
            }

            EditorSession session;
            if (!sessions.TryGetValue(player.userID, out session) || session == null || !session.UiOpen)
            {
                return;
            }

            ShowEditorUi(player);
        }

        private void HideEditorUi(BasePlayer player, bool reply)
        {
            if (player == null)
            {
                return;
            }

            DestroyUi(player);
            if (reply)
            {
                Reply(player, "CUI hidden. Your editor session, waypoint markers, and active preview remain. Use /airanim to reopen, /airanim stop to stop preview, or /airanim end to clean everything up.");
            }
        }

        private void DestroyUi(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            CuiHelper.DestroyUi(player, UiName);
            CuiHelper.DestroyUi(player, ConfirmUiName);

            EditorSession session;
            if (sessions.TryGetValue(player.userID, out session) && session != null)
            {
                session.UiOpen = false;
            }
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

            if (session.HasTarget)
            {
                RunSafeEffect(BulletImpactEffect, session.Target + Vector3.up * 0.25f, "editor target marker pulse");
            }

            var plan = BuildWorldWaypoints(session, profile);
            DrawWaypointDebugMarkers(player, session, plan);

            for (var i = 0; i < plan.Count; i++)
            {
                var isSelected = i == session.SelectedWaypointIndex;
                if (!isSelected && i % 2 != 0)
                {
                    continue;
                }

                RunSafeEffect(isSelected ? DroneDeployEffect : BulletImpactEffect, plan[i].Position, isSelected ? "selected waypoint pulse" : "waypoint pulse");
            }
        }

        private void DrawWaypointDebugMarkers(BasePlayer player, EditorSession session, List<WorldWaypoint> plan)
        {
            if (player == null || session == null || plan == null || plan.Count == 0)
            {
                return;
            }

            for (var i = 0; i < plan.Count; i++)
            {
                var waypoint = plan[i];
                var selected = i == session.SelectedWaypointIndex;
                var direction = GetWaypointMarkerDirection(plan, i, session.Approach);
                DrawWaypointDebugMarker(player, waypoint.Position, direction, selected);
            }
        }

        private void DrawWaypointDebugMarker(BasePlayer player, Vector3 center, Vector3 direction, bool selected)
        {
            var forward = NormalizeMarkerDirection(direction, Vector3.forward);
            var rotation = GetWaypointMarkerRotation(forward);
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

        private Vector3 GetWaypointMarkerDirection(List<WorldWaypoint> plan, int index, Vector3 fallback)
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
            SetStatus(session, "Target set from " + source + ".", "Target " + FormatPosition(target) + ".");
            if (reply)
            {
                Reply(player, "Editor target set from " + source + " at " + FormatPosition(target) + "; approach " + FormatVectorShort(session.Approach) + ".");
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
                var eased = Mathf.SmoothStep(0f, 1f, t);
                return Vector3.Lerp(a.Position, b.Position, eased);
            }

            return plan[last].Position;
        }

        private Vector3 GetPlanDirection(List<WorldWaypoint> plan, VisualProfileConfig profile, float elapsed, Vector3 fallback)
        {
            if (plan == null || plan.Count < 2)
            {
                return fallback;
            }

            var safeDuration = Mathf.Max(0.1f, profile == null ? plan[plan.Count - 1].Time : profile.DurationSeconds);
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
                return direction.normalized * speed;
            }

            return direction.normalized * Math.Max(1f, Vector3.Distance(plan[0].Position, plan[plan.Count - 1].Position) / Mathf.Max(0.1f, profile.DurationSeconds));
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
                return;
            }

            NormalizeProfileFile();
            SaveProfiles();
        }

        private void SaveProfiles()
        {
            NormalizeProfileFile();
            Interface.Oxide.DataFileSystem.WriteObject(DataFileName, profileFile ?? CreateDefaultProfileFile(), true);
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

            profileFile.SchemaVersion = SchemaVersion;
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
            foreach (var payloadEvent in profile.PayloadEvents)
            {
                payloadEvent.Time = Mathf.Clamp(payloadEvent.Time, 0f, profile.DurationSeconds);
                payloadEvent.Payload = payloadEvent.Payload ?? "";
                payloadEvent.Index = Math.Max(0, payloadEvent.Index);
            }

            profile.PayloadEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        private VisualProfileFile CreateDefaultProfileFile()
        {
            var file = new VisualProfileFile
            {
                SchemaVersion = SchemaVersion,
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
                    profile.FirstPayloadDelaySeconds = value;
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
                    profile.FirstPayloadDelaySeconds += delta;
                    break;
                case "smooth":
                    profile.RotationSmoothTimeSeconds += delta;
                    break;
                case "clearance":
                    profile.MinimumTerrainClearance += delta;
                    break;
            }
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

        private bool TryParseFloat(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
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
