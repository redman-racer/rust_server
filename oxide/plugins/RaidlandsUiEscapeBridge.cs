using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Raidlands UI Escape Bridge", "Raidlands", "1.0.9")]
    [Description("Provides server-visible ESC/TAB closing for registered modal CUI by maintaining a private native loot session.")]
    public class RaidlandsUiEscapeBridge : RustPlugin
    {
        private const string CloseCommand = "raidlandsui.close";
        private const string TestUiRoot = "RaidlandsUiEscapeBridge.TestUI";
        private const string TestPermission = "raidlandsuiescapebridge.test";

        private Configuration _config;
        private int _nextGeneration;
        private int _nextSessionId;

        private enum BridgeState
        {
            WaitingForReady,
            DrainingOldLoot,
            OpeningNativeLoot,
            Armed,
            Closing
        }

        private sealed class UiRegistration
        {
            public Plugin Owner;
            public string OwnerName;
            public string RootName;
            public string CloseHook;
        }

        private sealed class BridgeSession
        {
            public int SessionId;
            public ulong UserId;
            public BasePlayer Player;
            public StorageContainer Container;
            public BridgeState State;
            public int Generation;
            public float CreatedAt;
            public readonly List<UiRegistration> Registrations = new List<UiRegistration>();
        }

        private sealed class Configuration
        {
            [JsonProperty(PropertyName = "Dummy storage prefab")]
            public string DummyStoragePrefab = "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab";

            [JsonProperty(PropertyName = "Native loot panel name")]
            public string NativeLootPanel = "generic";

            [JsonProperty(PropertyName = "Old loot hook drain delay seconds")]
            public float OldLootDrainDelaySeconds = 0.15f;

            [JsonProperty(PropertyName = "Native loot verification delay seconds")]
            public float NativeLootVerificationDelaySeconds = 0.20f;

            [JsonProperty(PropertyName = "Close existing native loot when a registered UI opens")]
            public bool CloseExistingNativeLoot = true;

            [JsonProperty(PropertyName = "Refuse to open while real native loot is active when close-existing is disabled")]
            public bool RefuseWhileRealLootIsActive = true;

            [JsonProperty(PropertyName = "Add interaction backdrop behind registered modal UIs")]
            public bool AddInteractionShield = true;

            [JsonProperty(PropertyName = "Interaction backdrop parent layer")]
            public string InteractionShieldParent = "Hud.Menu";

            [JsonProperty(PropertyName = "Interaction backdrop color")]
            public string InteractionShieldColor = "0.015 0.018 0.024 0.86";

            [JsonProperty(PropertyName = "Notify player when the bridge cannot arm")]
            public bool NotifyPlayerOnFailure = true;

            [JsonProperty(PropertyName = "Debug logging")]
            public bool DebugLogging = false;
        }

        private readonly Dictionary<ulong, BridgeSession> _sessions =
            new Dictionary<ulong, BridgeSession>();

        private readonly Dictionary<StorageContainer, ulong> _containerOwners =
            new Dictionary<StorageContainer, ulong>();

        #region Oxide lifecycle

        private void Init()
        {
            permission.RegisterPermission(TestPermission, this);
        }

        protected override void LoadDefaultConfig()
        {
            _config = new Configuration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null)
                    throw new Exception("Configuration deserialized to null.");
            }
            catch (Exception exception)
            {
                PrintWarning($"Could not read configuration; using defaults. {exception.Message}");
                LoadDefaultConfig();
            }

            _config.OldLootDrainDelaySeconds = Mathf.Clamp(
                _config.OldLootDrainDelaySeconds,
                0.05f,
                2f);

            _config.NativeLootVerificationDelaySeconds = Mathf.Clamp(
                _config.NativeLootVerificationDelaySeconds,
                0.05f,
                2f);

            if (string.IsNullOrWhiteSpace(_config.DummyStoragePrefab))
                _config.DummyStoragePrefab = "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab";

            if (string.IsNullOrWhiteSpace(_config.NativeLootPanel))
                _config.NativeLootPanel = "generic";

            if (string.IsNullOrWhiteSpace(_config.InteractionShieldParent))
                _config.InteractionShieldParent = "Hud.Menu";

            if (string.IsNullOrWhiteSpace(_config.InteractionShieldColor))
                _config.InteractionShieldColor = "0.015 0.018 0.024 0.86";

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void Unload()
        {
            List<BridgeSession> sessions = new List<BridgeSession>(_sessions.Values);

            // Remove state first so native loot cleanup cannot re-enter a live session.
            _sessions.Clear();
            _containerOwners.Clear();

            foreach (BridgeSession session in sessions)
            {
                if (session == null)
                    continue;

                BasePlayer player = session.Player ?? BasePlayer.FindByID(session.UserId);
                if (player != null)
                {
                    DestroyShield(player);

                    foreach (UiRegistration registration in session.Registrations)
                    {
                        if (!string.IsNullOrWhiteSpace(registration?.RootName))
                            CuiHelper.DestroyUi(player, registration.RootName);
                    }

                    ForceCloseNativeLoot(player, "bridge plugin unload");
                }

                KillContainer(session.Container);
            }

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                DestroyShield(player);
                CuiHelper.DestroyUi(player, TestUiRoot);
            }
        }

        #endregion

        #region Public plugin API

        /*
         * Register BEFORE calling CuiHelper.AddUi so the optional transparent shield
         * is inserted below the plugin's modal UI.
         *
         * Example from another plugin:
         *
         * [PluginReference] private Plugin RaidlandsUiEscapeBridge;
         *
         * bool registered = (bool)(RaidlandsUiEscapeBridge?.Call(
         *     "RegisterUi", player, this, UiRoot, "OnRaidlandsUiBridgeClosed") ?? false);
         *
         * private void OnRaidlandsUiBridgeClosed(BasePlayer player, string reason)
         * {
         *     _openPlayers.Remove(player.userID);
         *     CuiHelper.DestroyUi(player, UiRoot);
         * }
         */

        private object RegisterUi(BasePlayer player, Plugin owner, string rootName, string closeHook)
        {
            return RegisterUiInternal(player, owner, rootName, closeHook, false);
        }

        private object RegisterUi(BasePlayer player, Plugin owner, string rootName, string closeHook, bool waitForReady)
        {
            return RegisterUiInternal(player, owner, rootName, closeHook, waitForReady);
        }

        private object ArmUi(BasePlayer player, Plugin owner, string rootName)
        {
            if (player == null || owner == null || string.IsNullOrWhiteSpace(rootName))
            {
                DebugLog($"ArmUi rejected: invalid arguments. player={player?.userID.ToString() ?? "null"}, owner={owner?.Name ?? "null"}, root={rootName ?? "null"}.");
                return false;
            }

            BridgeSession session;
            if (!_sessions.TryGetValue(player.userID, out session) ||
                session == null ||
                session.State == BridgeState.Closing)
            {
                DebugLog($"ArmUi rejected for {owner.Name}:{rootName}, player={player.userID}; no active bridge session.");
                return false;
            }

            bool registered = false;
            foreach (UiRegistration registration in session.Registrations)
            {
                if (registration == null)
                    continue;

                if (ReferenceEquals(registration.Owner, owner) &&
                    string.Equals(registration.RootName, rootName, StringComparison.Ordinal))
                {
                    registered = true;
                    break;
                }
            }

            if (!registered)
            {
                Trace(session, $"ArmUi rejected: {owner.Name}:{rootName} is not registered to this session.");
                return false;
            }

            session.Player = player;

            if (session.State == BridgeState.WaitingForReady)
            {
                session.State = BridgeState.DrainingOldLoot;
                session.Generation = ++_nextGeneration;

                QueueNativeBridgeOpen(session);
                Trace(session, $"Deferred registration armed by {owner.Name}:{rootName}.");
            }
            else
            {
                Trace(session, $"ArmUi acknowledged for {owner.Name}:{rootName}; state is already {session.State}.");
            }

            return true;
        }

        private object UnregisterUi(BasePlayer player, Plugin owner, string rootName)
        {
            if (player == null || owner == null || string.IsNullOrWhiteSpace(rootName))
                return false;

            BridgeSession session;
            if (!_sessions.TryGetValue(player.userID, out session) || session == null || session.State == BridgeState.Closing)
                return false;

            bool removed = false;

            for (int index = session.Registrations.Count - 1; index >= 0; index--)
            {
                UiRegistration registration = session.Registrations[index];
                if (registration == null)
                    continue;

                if (ReferenceEquals(registration.Owner, owner) &&
                    string.Equals(registration.RootName, rootName, StringComparison.Ordinal))
                {
                    session.Registrations.RemoveAt(index);
                    removed = true;
                }
            }

            if (!removed)
            {
                Trace(session, $"UnregisterUi ignored: {owner.Name}:{rootName} was not registered.");
                return false;
            }

            Trace(session, $"Unregistered {owner.Name}:{rootName}; roots remaining={session.Registrations.Count}.");

            if (session.Registrations.Count == 0)
            {
                CloseSession(
                    session,
                    "last registered UI closed normally",
                    true,
                    false,
                    false);
            }

            return true;
        }

        private object ClosePlayerUis(BasePlayer player, string reason)
        {
            if (player == null)
                return false;

            BridgeSession session;
            if (!_sessions.TryGetValue(player.userID, out session) || session == null)
            {
                DebugLog($"ClosePlayerUis ignored for {player.userID}; no active session. reason='{reason}'.");
                return false;
            }

            Trace(session, $"ClosePlayerUis requested. reason='{reason}'.");
            CloseSession(
                session,
                string.IsNullOrWhiteSpace(reason) ? "external close request" : reason,
                true,
                true,
                true);

            return true;
        }

        private object IsBridgeActive(BasePlayer player)
        {
            if (player == null)
                return false;

            BridgeSession session;
            return _sessions.TryGetValue(player.userID, out session) &&
                   session != null &&
                   session.State != BridgeState.Closing;
        }

        private object IsBridgeArmed(BasePlayer player)
        {
            if (player == null)
                return false;

            BridgeSession session;
            return _sessions.TryGetValue(player.userID, out session) &&
                   session != null &&
                   session.State == BridgeState.Armed;
        }

        private object GetCloseCommand()
        {
            return CloseCommand;
        }

        private object IsBridgeLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null)
                return false;

            StorageContainer container = entity as StorageContainer;
            if (container == null)
                return false;

            BridgeSession session;
            return _sessions.TryGetValue(player.userID, out session) &&
                   session != null &&
                   session.Container == container;
        }

        #endregion

        #region Registration and bridge opening

        private bool RegisterUiInternal(BasePlayer player, Plugin owner, string rootName, string closeHook, bool waitForReady)
        {
            if (player == null || !player.IsConnected)
            {
                PrintWarning("RegisterUi was called without a connected player.");
                return false;
            }

            if (owner == null)
            {
                PrintWarning($"RegisterUi was called for {player.displayName} without an owner plugin.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(rootName))
            {
                PrintWarning($"RegisterUi was called by {owner.Name} with an empty root name.");
                return false;
            }

            BridgeSession session;
            if (!_sessions.TryGetValue(player.userID, out session) || session == null || session.State == BridgeState.Closing)
            {
                if (!_config.CloseExistingNativeLoot &&
                    _config.RefuseWhileRealLootIsActive &&
                    IsRealNativeLootActive(player))
                {
                    DebugLog($"Refused {owner.Name}:{rootName} because {player.displayName} is actively looting a real entity.");
                    return false;
                }

                session = new BridgeSession
                {
                    SessionId = ++_nextSessionId,
                    UserId = player.userID,
                    Player = player,
                    State = waitForReady ? BridgeState.WaitingForReady : BridgeState.DrainingOldLoot,
                    Generation = ++_nextGeneration,
                    CreatedAt = Time.realtimeSinceStartup
                };

                _sessions[player.userID] = session;

                Trace(session, $"Created session from {owner.Name}:{rootName}; deferred={waitForReady}, closeExistingLoot={_config.CloseExistingNativeLoot}.");

                if (!waitForReady && _config.AddInteractionShield)
                    AddShield(player);

                AddOrUpdateRegistration(session, owner, rootName, closeHook);

                if (!waitForReady)
                    QueueNativeBridgeOpen(session);

                return true;
            }

            session.Player = player;
            AddOrUpdateRegistration(session, owner, rootName, closeHook);
            Trace(session, $"Updated session from {owner.Name}:{rootName}; deferred={waitForReady}.");

            if (_config.AddInteractionShield && session.State != BridgeState.WaitingForReady)
                AddShieldIfMissing(player, session);

            return true;
        }

        private void AddOrUpdateRegistration(
            BridgeSession session,
            Plugin owner,
            string rootName,
            string closeHook)
        {
            foreach (UiRegistration existing in session.Registrations)
            {
                if (existing == null)
                    continue;

                if (ReferenceEquals(existing.Owner, owner) &&
                    string.Equals(existing.RootName, rootName, StringComparison.Ordinal))
                {
                    existing.CloseHook = closeHook;
                    existing.OwnerName = owner.Name;
                    return;
                }
            }

            session.Registrations.Add(new UiRegistration
            {
                Owner = owner,
                OwnerName = owner.Name,
                RootName = rootName,
                CloseHook = closeHook
            });

            Trace(session, $"Registered {owner.Name}:{rootName}; roots={session.Registrations.Count}.");
        }

        private void QueueNativeBridgeOpen(BridgeSession session)
        {
            if (!IsCurrentSession(session))
                return;

            BasePlayer player = session.Player;

            int generation = session.Generation;
            Trace(session, $"Queued native loot open; delay={_config.OldLootDrainDelaySeconds:0.000}s, generation={generation}.");

            timer.Once(_config.OldLootDrainDelaySeconds, () =>
            {
                BridgeSession current;
                if (!_sessions.TryGetValue(session.UserId, out current) ||
                    !ReferenceEquals(current, session) ||
                    current.Generation != generation ||
                    !IsCurrentSession(current))
                {
                    Trace(session, "Skipped queued native open because the session was replaced, closed, or generation changed.");
                    return;
                }

                if (current.Registrations.Count == 0)
                {
                    CloseSession(current, "no UI registrations remained before native open", false, false, false);
                    return;
                }

                if (_config.CloseExistingNativeLoot)
                {
                    Trace(current, "Closing current native loot before bridge container creation.");
                    // Do not send client inventory-close RPCs here. They can arrive
                    // after PlayerOpenLoot and immediately close this new bridge.
                    ForceCloseNativeLoot(player, "preparing UI Escape bridge", false);
                }

                CreateNativeLootContainer(current);
            });
        }

        private void CreateNativeLootContainer(BridgeSession session)
        {
            if (!IsCurrentSession(session))
                return;

            BasePlayer player = session.Player;
            Vector3 spawnPosition = player.transform.position + Vector3.down * 2.25f;

            StorageContainer container = GameManager.server.CreateEntity(
                _config.DummyStoragePrefab,
                spawnPosition) as StorageContainer;

            if (container == null)
            {
                FailSession(session, "could not create the private dummy StorageContainer");
                return;
            }

            container.enableSaving = false;
            container.OwnerID = player.userID;

            session.Container = container;
            session.State = BridgeState.OpeningNativeLoot;
            Trace(session, $"Created private StorageContainer at {spawnPosition}; spawning now.");

            // Register before Spawn so CanNetworkTo can isolate its first snapshot.
            _containerOwners[container] = player.userID;
            container.Spawn();
            Trace(session, $"Private StorageContainer spawned; netId={container.net?.ID}.");

            int generation = session.Generation;
            NextTick(() =>
            {
                BridgeSession current;
                if (!_sessions.TryGetValue(session.UserId, out current) ||
                    !ReferenceEquals(current, session) ||
                    current.Generation != generation ||
                    !IsCurrentSession(current))
                {
                    Trace(session, "Skipped PlayerOpenLoot because the session was replaced, closed, or generation changed.");
                    KillContainer(container);
                    return;
                }

                OpenNativeLoot(current);
            });
        }

        private void OpenNativeLoot(BridgeSession session)
        {
            if (!IsCurrentSession(session) || session.Container == null || session.Container.IsDestroyed)
                return;

            BasePlayer player = session.Player;
            Trace(session, $"Calling PlayerOpenLoot with panel='{_config.NativeLootPanel}'.");

            bool opened;
            try
            {
                opened = session.Container.PlayerOpenLoot(player, _config.NativeLootPanel, false);
            }
            catch (Exception exception)
            {
                FailSession(session, $"PlayerOpenLoot threw an exception: {exception.Message}");
                return;
            }

            if (!opened)
            {
                FailSession(session, "StorageContainer.PlayerOpenLoot returned false");
                return;
            }

            int generation = session.Generation;
            Trace(session, $"PlayerOpenLoot returned true; verification in {_config.NativeLootVerificationDelaySeconds:0.000}s.");

            timer.Once(_config.NativeLootVerificationDelaySeconds, () =>
            {
                BridgeSession current;
                if (!_sessions.TryGetValue(session.UserId, out current) ||
                    !ReferenceEquals(current, session) ||
                    current.Generation != generation ||
                    !IsCurrentSession(current))
                {
                    Trace(session, "Skipped native loot verification because the session was replaced, closed, or generation changed.");
                    return;
                }

                VerifyAndArm(current);
            });
        }

        private void VerifyAndArm(BridgeSession session)
        {
            if (!IsCurrentSession(session))
                return;

            PlayerLoot loot = session.Player.inventory?.loot;
            bool entityMatched = loot != null && loot.entitySource == session.Container;
            Trace(session, $"Verification: lootPresent={loot != null}, entityMatched={entityMatched}, source={DescribeLootSource(loot)}.");

            if (!entityMatched)
            {
                FailSession(session, "native loot session did not remain open through verification");
                return;
            }

            session.State = BridgeState.Armed;

            Trace(session, $"Bridge armed; roots={session.Registrations.Count}, container={session.Container.net?.ID}.");
        }

        private bool IsCurrentSession(BridgeSession session)
        {
            if (session == null || session.State == BridgeState.Closing)
                return false;

            BasePlayer player = session.Player;
            if (player == null || !player.IsConnected)
                return false;

            BridgeSession current;
            return _sessions.TryGetValue(session.UserId, out current) &&
                   ReferenceEquals(current, session);
        }

        private bool IsRealNativeLootActive(BasePlayer player)
        {
            PlayerLoot loot = player?.inventory?.loot;
            if (loot == null || loot.entitySource == null)
                return false;

            StorageContainer source = loot.entitySource as StorageContainer;
            if (source != null && _containerOwners.ContainsKey(source))
                return false;

            return true;
        }

        private void FailSession(BridgeSession session, string reason)
        {
            if (session == null)
                return;

            PrintWarning($"[RLUIB] Escape bridge failed for {session.Player?.displayName ?? session.UserId.ToString()}: {reason}. {DescribeSession(session)}");

            BasePlayer player = session.Player;
            CloseSession(session, $"bridge failed: {reason}", true, false, false);

            if (_config.NotifyPlayerOnFailure && player != null && player.IsConnected)
            {
                player.ChatMessage(
                    "<color=#ff7666>UI ESCAPE BRIDGE FAILED:</color> Escape/Tab close support is unavailable for this menu. " +
                    "Use the menu close button.");
            }
        }

        #endregion

        #region Native loot hooks

        private void OnPlayerLootEnd(PlayerLoot loot)
        {
            if (loot == null)
                return;

            BasePlayer player = loot.GetComponent<BasePlayer>();
            if (player == null)
                return;

            BridgeSession session;
            if (!_sessions.TryGetValue(player.userID, out session) || session == null || session.State == BridgeState.Closing)
                return;

            bool entityMatched = loot.entitySource == session.Container;
            Trace(session, $"OnPlayerLootEnd received; entityMatched={entityMatched}, source={DescribeLootSource(loot)}.");

            if (session.State != BridgeState.Armed)
            {
                DebugLog(
                    $"Ignored opening-time OnPlayerLootEnd for {player.displayName} ({player.userID}); " +
                    $"state={session.State}, matched={entityMatched}, " +
                    $"age={Time.realtimeSinceStartup - session.CreatedAt:0.000}s.");
                return;
            }

            CloseSession(
                session,
                $"native loot ended (OnPlayerLootEnd, matched={entityMatched})",
                false,
                true,
                true);
        }

        private void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            if (player == null || entity == null)
                return;

            BridgeSession session;
            if (!_sessions.TryGetValue(player.userID, out session) || session == null || session.State == BridgeState.Closing)
                return;

            if (entity != session.Container)
                return;

            Trace(session, $"OnLootEntityEnd received for private container; entity={entity.net?.ID}.");

            if (session.State != BridgeState.Armed)
            {
                DebugLog(
                    $"Ignored opening-time OnLootEntityEnd for {player.displayName} ({player.userID}); " +
                    $"state={session.State}, age={Time.realtimeSinceStartup - session.CreatedAt:0.000}s.");
                return;
            }

            CloseSession(
                session,
                "native loot ended (OnLootEntityEnd)",
                false,
                true,
                true);
        }

        #endregion

        #region Closing and callbacks

        [ConsoleCommand(CloseCommand)]
        private void CommandCloseRegisteredUi(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg?.Connection?.player as BasePlayer;
            if (player == null)
                return;

            BridgeSession session;
            if (!_sessions.TryGetValue(player.userID, out session) || session == null)
                return;

            Trace(session, "Central bridge close console command received.");
            CloseSession(session, "central UI close button", true, true, true);
        }

        private void CloseSession(
            BridgeSession session,
            string reason,
            bool endNativeLoot,
            bool invokeCallbacks,
            bool destroyRegisteredRoots)
        {
            if (session == null || session.State == BridgeState.Closing)
                return;

            Trace(session, $"Closing session. reason='{reason}', endLoot={endNativeLoot}, callbacks={invokeCallbacks}, destroyRoots={destroyRegisteredRoots}.");
            session.State = BridgeState.Closing;

            // Copy registrations before removing state. Owner callbacks may call back
            // into UnregisterUi or open a new UI immediately.
            List<UiRegistration> registrations =
                new List<UiRegistration>(session.Registrations);

            _sessions.Remove(session.UserId);

            StorageContainer container = session.Container;
            if (container != null)
                _containerOwners.Remove(container);

            BasePlayer player = session.Player ?? BasePlayer.FindByID(session.UserId);

            if (player != null)
            {
                DestroyShield(player);

                // End the old native bridge before callbacks. If a callback opens a new
                // registered UI, this cleanup cannot accidentally close that new bridge.
                if (endNativeLoot)
                    ForceCloseNativeLoot(player, reason);
            }

            KillContainer(container);

            if (invokeCallbacks && player != null)
            {
                DebugLog($"[RLUIB] S{session.SessionId} invoking {registrations.Count} registered close callback(s).");
                InvokeCloseCallbacks(player, registrations, reason);
            }

            if (destroyRegisteredRoots && player != null)
            {
                foreach (UiRegistration registration in registrations)
                {
                    if (!string.IsNullOrWhiteSpace(registration?.RootName))
                        CuiHelper.DestroyUi(player, registration.RootName);
                }
            }

            DebugLog($"[RLUIB] S{session.SessionId} closed. reason='{reason}', registrations={registrations.Count}.");
        }

        private void InvokeCloseCallbacks(
            BasePlayer player,
            List<UiRegistration> registrations,
            string reason)
        {
            HashSet<string> invokedCallbacks = new HashSet<string>(StringComparer.Ordinal);

            foreach (UiRegistration registration in registrations)
            {
                if (registration?.Owner == null || string.IsNullOrWhiteSpace(registration.CloseHook))
                    continue;

                string ownerName = string.IsNullOrWhiteSpace(registration.OwnerName)
                    ? registration.Owner.Name
                    : registration.OwnerName;

                string callbackKey = ownerName + "\u001f" + registration.CloseHook;
                if (!invokedCallbacks.Add(callbackKey))
                    continue;

                try
                {
                    DebugLog($"[RLUIB] S{GetSessionId(player)} invoking {ownerName}.{registration.CloseHook}; reason='{reason}'.");
                    registration.Owner.Call(registration.CloseHook, player, reason);
                }
                catch (Exception exception)
                {
                    PrintWarning(
                        $"Close callback {ownerName}.{registration.CloseHook} failed for " +
                        $"{player.displayName} ({player.userID}): {exception.Message}");
                }
            }
        }

        private void ForceCloseNativeLoot(BasePlayer player, string reason, bool closeClientInventory = true)
        {
            if (player == null)
                return;

            DebugLog($"[RLUIB] S{GetSessionId(player)} ForceCloseNativeLoot. reason='{reason}', closeClient={closeClientInventory}, source={DescribeLootSource(player.inventory?.loot)}.");

            if (player.inventory?.loot != null)
            {
                try
                {
                    player.EndLooting();
                }
                catch (Exception exception)
                {
                    PrintWarning(
                        $"EndLooting failed for {player.displayName} ({player.userID}) while closing '{reason}': " +
                        exception.Message);
                }

                try
                {
                    player.inventory.loot.Clear();
                }
                catch (Exception exception)
                {
                    PrintWarning(
                        $"PlayerLoot.Clear failed for {player.displayName} ({player.userID}) while closing '{reason}': " +
                        exception.Message);
                }

                try
                {
                    player.inventory.loot.MarkDirty();
                    player.inventory.loot.SendImmediate();
                }
                catch (Exception exception)
                {
                    PrintWarning(
                        $"PlayerLoot.SendImmediate failed for {player.displayName} ({player.userID}) while closing '{reason}': " +
                        exception.Message);
                }
            }

            if (!closeClientInventory)
                return;

            ForceCloseClientInventory(player, reason);
            NextTick(() => ForceCloseClientInventory(player, reason + " follow-up"));
        }

        private void ForceCloseClientInventory(BasePlayer player, string reason)
        {
            if (player == null || !player.IsConnected)
                return;

            // PlayerOpenLoot creates native CUI roots. EndLooting alone can clear the
            // server state while leaving those roots visible on some Rust clients.
            CloseNativeLootPanels(player);

            try
            {
                player.SendConsoleCommand("inventory.endloot", null);
            }
            catch (Exception exception)
            {
                PrintWarning(
                    $"inventory.endloot failed for {player.displayName} ({player.userID}) while closing '{reason}': " +
                    exception.Message);
            }

        }

        private void CloseNativeLootPanels(BasePlayer player)
        {
            if (player == null)
                return;

            CuiHelper.DestroyUi(player, _config.NativeLootPanel);
            CuiHelper.DestroyUi(player, "LootPanel");
        }

        #endregion

        #region Shield and entity isolation

        private string GetShieldRoot(ulong userId)
        {
            return $"RaidlandsUiEscapeBridge.Shield.{userId}";
        }

        private void AddShield(BasePlayer player)
        {
            if (player == null)
                return;

            string root = GetShieldRoot(player.userID);
            CuiHelper.DestroyUi(player, root);

            CuiElementContainer elements = new CuiElementContainer();
            elements.Add(new CuiPanel
            {
                Image =
                {
                    // Visible and raycastable, so native loot is obscured while the
                    // registered modal remains clickable above it on the same layer.
                    Color = _config.InteractionShieldColor
                },
                RectTransform =
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1"
                },
                CursorEnabled = false
            }, _config.InteractionShieldParent, root);

            CuiHelper.AddUi(player, elements);
        }

        private void AddShieldIfMissing(BasePlayer player, BridgeSession session)
        {
            // CUI has no server-side existence query. Re-adding would put the shield on
            // top of an already-open UI, so only add it for the session's first root.
            if (session.Registrations.Count <= 1)
                AddShield(player);
        }

        private void DestroyShield(BasePlayer player)
        {
            if (player != null)
                CuiHelper.DestroyUi(player, GetShieldRoot(player.userID));
        }

        private object CanNetworkTo(BaseNetworkable entity, BasePlayer target)
        {
            StorageContainer container = entity as StorageContainer;
            if (container == null || target == null)
                return null;

            ulong ownerId;
            if (!_containerOwners.TryGetValue(container, out ownerId))
                return null;

            return ownerId == target.userID ? null : (object)false;
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            HandleBridgeContainerDestroyed(entity as StorageContainer);
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            HandleBridgeContainerDestroyed(entity as StorageContainer);
        }

        private void HandleBridgeContainerDestroyed(StorageContainer container)
        {
            if (container == null)
                return;

            ulong ownerId;
            if (!_containerOwners.TryGetValue(container, out ownerId))
                return;

            BridgeSession session;
            if (!_sessions.TryGetValue(ownerId, out session) || session == null || session.Container != container)
            {
                _containerOwners.Remove(container);
                return;
            }

            // Intentional Kill during CloseSession occurs after session state was removed.
            Trace(session, $"Private bridge container was destroyed unexpectedly; netId={container.net?.ID}.");
            CloseSession(session, "private bridge container was destroyed", true, true, true);
        }

        private void KillContainer(StorageContainer container)
        {
            if (container == null)
                return;

            _containerOwners.Remove(container);

            if (!container.IsDestroyed)
                container.Kill();
        }

        #endregion

        #region Player/plugin cleanup

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null)
                return;

            BridgeSession session;
            if (_sessions.TryGetValue(player.userID, out session) && session != null)
            {
                CloseSession(
                    session,
                    "player disconnected",
                    false,
                    true,
                    true);
            }
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player == null)
                return;

            BridgeSession session;
            if (_sessions.TryGetValue(player.userID, out session) && session != null)
            {
                CloseSession(
                    session,
                    "player died",
                    true,
                    true,
                    true);
            }
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin == null)
                return;

            List<BridgeSession> sessions = new List<BridgeSession>(_sessions.Values);

            foreach (BridgeSession session in sessions)
            {
                if (session == null || session.State == BridgeState.Closing)
                    continue;

                bool removedAny = false;

                for (int index = session.Registrations.Count - 1; index >= 0; index--)
                {
                    UiRegistration registration = session.Registrations[index];
                    if (registration != null && ReferenceEquals(registration.Owner, plugin))
                    {
                        BasePlayer player = session.Player ?? BasePlayer.FindByID(session.UserId);
                        if (player != null && !string.IsNullOrWhiteSpace(registration.RootName))
                            CuiHelper.DestroyUi(player, registration.RootName);

                        session.Registrations.RemoveAt(index);
                        removedAny = true;
                    }
                }

                if (removedAny && session.Registrations.Count == 0)
                {
                    CloseSession(
                        session,
                        $"owner plugin {plugin.Name} unloaded",
                        true,
                        false,
                        false);
                }
            }
        }

        #endregion

        #region Built-in test UI

        [ChatCommand("rluibridgetest")]
        private void CommandBridgeTest(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;

            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, TestPermission))
            {
                player.ChatMessage("<color=#ff7666>You do not have permission to use this test.</color>");
                return;
            }

            BridgeSession existing;
            if (_sessions.TryGetValue(player.userID, out existing) && existing != null)
            {
                CloseSession(existing, "test command toggle", true, true, true);
                return;
            }

            bool registered = RegisterUiInternal(
                player,
                this,
                TestUiRoot,
                nameof(OnBridgeTestClosed),
                false);

            if (!registered)
            {
                player.ChatMessage(
                    "<color=#ff7666>BRIDGE TEST COULD NOT OPEN:</color> " +
                    "You may currently be looting a real entity, depending on the bridge configuration.");
                return;
            }

            ShowTestUi(player);
            player.ChatMessage(
                "<color=#ffd166>RAIDLANDS UI BRIDGE TEST:</color> Wait about half a second, then press " +
                "<color=#ffffff>ESC or TAB</color>. The red button tests the same full cleanup path.");
        }

        private void OnBridgeTestClosed(BasePlayer player, string reason)
        {
            if (player == null)
                return;

            CuiHelper.DestroyUi(player, TestUiRoot);

            if (player.IsConnected)
            {
                player.ChatMessage(
                    $"<color=#7dff8a>BRIDGE TEST CLOSED:</color> <color=#ffffff>{reason}</color>");
            }
        }

        private void ShowTestUi(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, TestUiRoot);

            CuiElementContainer elements = new CuiElementContainer();

            elements.Add(new CuiPanel
            {
                Image =
                {
                    Color = "0.035 0.025 0.020 0.985"
                },
                RectTransform =
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 1"
                },
                CursorEnabled = true
            }, _config.InteractionShieldParent, TestUiRoot);

            elements.Add(new CuiPanel
            {
                Image =
                {
                    Color = "0.12 0.085 0.055 1"
                },
                RectTransform =
                {
                    AnchorMin = "0.30 0.27",
                    AnchorMax = "0.70 0.73"
                }
            }, TestUiRoot, TestUiRoot + ".Panel");

            elements.Add(new CuiLabel
            {
                Text =
                {
                    Text = "RAIDLANDS UI ESCAPE BRIDGE",
                    FontSize = 26,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.82 0.40 1"
                },
                RectTransform =
                {
                    AnchorMin = "0.05 0.76",
                    AnchorMax = "0.95 0.94"
                }
            }, TestUiRoot + ".Panel");

            elements.Add(new CuiLabel
            {
                Text =
                {
                    Text =
                        "This is the production bridge's built-in integration test.\n\n" +
                        "PRESS ESC OR TAB\n\n" +
                        "The native loot-end event should close this registered CUI,\n" +
                        "invoke its owner callback, destroy the root as a fallback,\n" +
                        "close native loot, and remove the temporary container.\n\n" +
                        "The red button runs the exact same centralized cleanup path.",
                    FontSize = 18,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0.94 0.92 0.88 1"
                },
                RectTransform =
                {
                    AnchorMin = "0.08 0.25",
                    AnchorMax = "0.92 0.76"
                }
            }, TestUiRoot + ".Panel");

            elements.Add(new CuiButton
            {
                Button =
                {
                    Command = CloseCommand,
                    Color = "0.82 0.25 0.10 1"
                },
                Text =
                {
                    Text = "CLOSE REGISTERED UI + NATIVE LOOT",
                    FontSize = 16,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 1"
                },
                RectTransform =
                {
                    AnchorMin = "0.20 0.07",
                    AnchorMax = "0.80 0.19"
                }
            }, TestUiRoot + ".Panel");

            CuiHelper.AddUi(player, elements);
        }

        #endregion

        #region Helpers

        private void DebugLog(string message)
        {
            if (_config != null && _config.DebugLogging)
                Puts(message);
        }

        private void Trace(BridgeSession session, string message)
        {
            if (_config == null || !_config.DebugLogging)
                return;

            Puts($"[RLUIB] {DescribeSession(session)} {message}");
        }

        private int GetSessionId(BasePlayer player)
        {
            BridgeSession session;
            return player != null && _sessions.TryGetValue(player.userID, out session) && session != null
                ? session.SessionId
                : 0;
        }

        private string DescribeSession(BridgeSession session)
        {
            if (session == null)
                return "session=null";

            List<string> roots = new List<string>();
            foreach (UiRegistration registration in session.Registrations)
            {
                if (registration != null)
                    roots.Add($"{registration.OwnerName}:{registration.RootName}");
            }

            return $"S{session.SessionId} player={session.UserId} state={session.State} generation={session.Generation} roots=[{string.Join(",", roots.ToArray())}]";
        }

        private string DescribeLootSource(PlayerLoot loot)
        {
            if (loot == null)
                return "loot=null";

            BaseEntity source = loot.entitySource;
            return source == null
                ? "source=null"
                : $"source={source.ShortPrefabName}:{source.net?.ID}";
        }

        #endregion
    }
}
