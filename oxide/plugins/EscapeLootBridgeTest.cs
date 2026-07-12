
using System.Collections.Generic;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Escape Loot Bridge Test", "Raidlands", "0.3.0")]
    [Description("Tests a stabilized native loot-session bridge for server-visible ESC/TAB closing, with forced native-loot cleanup.")]
    public class EscapeLootBridgeTest : RustPlugin
    {
        private const string UiRoot = "EscapeLootBridgeTest.UI";
        private const string TestBoxPrefab = "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab";
        private const string NativeLootPanel = "generic";

        // Delays are intentional. Rust can dispatch the close hook from a previous
        // loot session one or more ticks after EndLooting() was requested.
        private const float OldLootDrainDelay = 0.15f;
        private const float NativeOpenVerifyDelay = 0.20f;

        private sealed class TestSession
        {
            public StorageContainer Container;
            public bool Armed;
            public bool Closing;
            public float CreatedAt;
        }

        private readonly Dictionary<ulong, TestSession> _sessions =
            new Dictionary<ulong, TestSession>();

        private readonly HashSet<ulong> _pendingOpens = new HashSet<ulong>();

        #region Commands

        [ChatCommand("esctest")]
        private void CommandEscapeTest(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;

            if (!player.IsAdmin)
            {
                player.ChatMessage("<color=#ff7666>Escape test is admin-only.</color>");
                return;
            }

            if (_pendingOpens.Contains(player.userID) || _sessions.ContainsKey(player.userID))
            {
                CloseSession(player, "chat command toggle", true, true);
                return;
            }

            QueueOpenSession(player);
        }

        [ConsoleCommand("esctest.close")]
        private void CommandCloseEscapeTest(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg?.Connection?.player as BasePlayer;
            if (player == null)
                return;

            CloseSession(player, "manual CLOSE button", true, true);
        }

        #endregion

        #region Test Session

        private void QueueOpenSession(BasePlayer player)
        {
            ulong userId = player.userID;
            _pendingOpens.Add(userId);

            CuiHelper.DestroyUi(player, UiRoot);

            // Important: do not register the new dummy container yet. The close event
            // for the player's old loot session can arrive after this call returns.
            ForceCloseNativeLoot(player, "pre-open cleanup");

            Puts($"[QUEUE] Waiting {OldLootDrainDelay:0.00}s for old loot hooks before opening for {player.displayName} ({userId}).");

            timer.Once(OldLootDrainDelay, () =>
            {
                if (!_pendingOpens.Remove(userId))
                    return;

                if (player == null || !player.IsConnected)
                    return;

                CreateAndOpenSession(player);
            });
        }

        private void CreateAndOpenSession(BasePlayer player)
        {
            Vector3 spawnPosition = player.transform.position + Vector3.down * 2.25f;
            StorageContainer container = GameManager.server.CreateEntity(TestBoxPrefab, spawnPosition) as StorageContainer;

            if (container == null)
            {
                player.ChatMessage("<color=#ff7666>ESC TEST V3 FAILED TO START:</color> Could not create the temporary StorageContainer.");
                PrintError($"Could not create test container for {player.displayName} ({player.userID}).");
                return;
            }

            container.enableSaving = false;
            container.OwnerID = player.userID;

            TestSession session = new TestSession
            {
                Container = container,
                Armed = false,
                Closing = false,
                CreatedAt = Time.realtimeSinceStartup
            };

            // Register before Spawn so CanNetworkTo can isolate the initial snapshot.
            _sessions[player.userID] = session;
            container.Spawn();

            NextTick(() => OpenNativeLoot(player, session));
        }

        private void OpenNativeLoot(BasePlayer player, TestSession session)
        {
            if (!IsCurrentSession(player, session))
                return;

            // PlayerOpenLoot may internally clear or replace PlayerLoot state. Hooks
            // fired during this setup are ignored because session.Armed is still false.
            bool opened = session.Container.PlayerOpenLoot(player, NativeLootPanel, false);
            if (!opened)
            {
                CloseSession(player, "StorageContainer.PlayerOpenLoot returned false", false, false);
                player.ChatMessage("<color=#ff7666>ESC TEST V3 FAILED TO START:</color> Rust refused to open the native loot session.");
                return;
            }

            Puts($"[OPENING] Native loot requested for {player.displayName} ({player.userID}); waiting for verification.");

            timer.Once(NativeOpenVerifyDelay, () => VerifyAndArmSession(player, session));
        }

        private void VerifyAndArmSession(BasePlayer player, TestSession session)
        {
            if (!IsCurrentSession(player, session))
                return;

            PlayerLoot loot = player.inventory?.loot;
            bool entityMatched = loot != null && loot.entitySource == session.Container;

            if (!entityMatched)
            {
                CloseSession(player, "native loot did not remain open through verification", false, false);
                player.ChatMessage(
                    "<color=#ff7666>ESC TEST V3 OPEN ABORTED:</color> The native loot session closed before it became stable. " +
                    "Check the server log for ignored opening hooks.");
                return;
            }

            session.Armed = true;
            ShowTestUi(player);

            Puts($"[ARMED] {player.displayName} ({player.userID}) Escape bridge is stable. Container={session.Container.net?.ID}");
            player.ChatMessage(
                "<color=#ffd166>ESC TEST V3 OPENED:</color> Press <color=#ffffff>ESC or TAB once</color>. " +
                "A PASS closes the orange panel through a server loot-end hook.");
        }

        private bool IsCurrentSession(BasePlayer player, TestSession session)
        {
            if (player == null || !player.IsConnected || session == null || session.Closing)
                return false;

            TestSession current;
            return _sessions.TryGetValue(player.userID, out current) &&
                   ReferenceEquals(current, session) &&
                   session.Container != null &&
                   !session.Container.IsDestroyed;
        }

        private void CloseSession(BasePlayer player, string reason, bool endNativeLoot, bool notifyPlayer)
        {
            if (player == null)
                return;

            ulong userId = player.userID;
            _pendingOpens.Remove(userId);

            TestSession session;
            bool hadSession = _sessions.TryGetValue(userId, out session);

            if (session != null)
                session.Closing = true;

            // Remove state first. EndLooting and Kill can synchronously invoke hooks.
            _sessions.Remove(userId);
            CuiHelper.DestroyUi(player, UiRoot);

            if (endNativeLoot)
                ForceCloseNativeLoot(player, reason);

            StorageContainer container = session?.Container;
            if (container != null && !container.IsDestroyed)
                container.Kill();

            if (!hadSession)
            {
                if (notifyPlayer && player.IsConnected)
                    player.ChatMessage("<color=#ffd166>ESC TEST V3:</color> Pending test cancelled.");
                return;
            }

            Puts($"[CLOSE] Escape test closed for {player.displayName} ({userId}). Reason: {reason}");

            if (notifyPlayer && player.IsConnected)
            {
                player.ChatMessage(
                    $"<color=#7dff8a>ESC TEST V3 CLOSED:</color> Server close reason: <color=#ffffff>{reason}</color>");
            }
        }

        /// <summary>
        /// Ends the server-side loot session and explicitly clears PlayerLoot as a
        /// fallback. EndLooting normally closes the native client loot panel; Clear
        /// covers cases where a stale/partially-open session survives the first call.
        /// Session state is removed before this runs, so any resulting hooks are ignored.
        /// </summary>
        private void ForceCloseNativeLoot(BasePlayer player, string reason)
        {
            if (player == null || player.inventory?.loot == null)
                return;

            try
            {
                player.EndLooting();
            }
            catch (System.Exception exception)
            {
                PrintWarning(
                    $"EndLooting failed for {player.displayName} ({player.userID}) while closing '{reason}': " +
                    exception.Message);
            }

            try
            {
                // Safe fallback for a stale native loot state. This may invoke
                // OnPlayerLootEnd again, but the session dictionary has already been
                // cleared before ForceCloseNativeLoot is called.
                player.inventory.loot.Clear();
            }
            catch (System.Exception exception)
            {
                PrintWarning(
                    $"PlayerLoot.Clear failed for {player.displayName} ({player.userID}) while closing '{reason}': " +
                    exception.Message);
            }
        }

        #endregion

        #region Loot Hooks

        private void OnPlayerLootEnd(PlayerLoot loot)
        {
            if (loot == null)
                return;

            BasePlayer player = loot.GetComponent<BasePlayer>();
            if (player == null)
                return;

            TestSession session;
            if (!_sessions.TryGetValue(player.userID, out session) || session == null || session.Closing)
                return;

            bool entityMatched = loot.entitySource == session.Container;

            if (!session.Armed)
            {
                Puts(
                    $"[IGNORED OPENING HOOK] OnPlayerLootEnd for {player.displayName} ({player.userID}); " +
                    $"entityMatched={entityMatched}, age={Time.realtimeSinceStartup - session.CreatedAt:0.000}s");
                return;
            }

            // Once armed, we already verified that this player's active loot source was
            // our dummy container. Any subsequent PlayerLoot end means the bridge ended.
            CloseSession(
                player,
                $"OnPlayerLootEnd fired (entity matched: {entityMatched})",
                false,
                true);
        }

        private void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            if (player == null || entity == null)
                return;

            TestSession session;
            if (!_sessions.TryGetValue(player.userID, out session) || session == null || session.Closing)
                return;

            if (entity != session.Container)
                return;

            if (!session.Armed)
            {
                Puts(
                    $"[IGNORED OPENING HOOK] OnLootEntityEnd for {player.displayName} ({player.userID}); " +
                    $"age={Time.realtimeSinceStartup - session.CreatedAt:0.000}s");
                return;
            }

            CloseSession(player, "OnLootEntityEnd fired", false, true);
        }

        #endregion

        #region CUI

        private void ShowTestUi(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UiRoot);

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
            }, "Overlay", UiRoot);

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
            }, UiRoot, UiRoot + ".Panel");

            elements.Add(new CuiLabel
            {
                Text =
                {
                    Text = "V3 — ESC / TAB NATIVE LOOT BRIDGE TEST",
                    FontSize = 26,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.82 0.40 1"
                },
                RectTransform =
                {
                    AnchorMin = "0.05 0.76",
                    AnchorMax = "0.95 0.94"
                }
            }, UiRoot + ".Panel");

            elements.Add(new CuiLabel
            {
                Text =
                {
                    Text =
                        "The plugin waited for old loot hooks to drain,\n" +
                        "opened a dummy native loot session, then verified it.\n\n" +
                        "PRESS ESC OR TAB ONCE\n\n" +
                        "PASS: This panel disappears and chat reports\n" +
                        "OnPlayerLootEnd or OnLootEntityEnd.\n\n" +
                        "The panel is on Overlay, so native menu visibility\n" +
                        "alone cannot hide it. The red button force-closes\n" +
                        "both this CUI and the active native loot window.",
                    FontSize = 18,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0.94 0.92 0.88 1"
                },
                RectTransform =
                {
                    AnchorMin = "0.08 0.25",
                    AnchorMax = "0.92 0.76"
                }
            }, UiRoot + ".Panel");

            elements.Add(new CuiButton
            {
                Button =
                {
                    Command = "esctest.close",
                    Color = "0.82 0.25 0.10 1"
                },
                Text =
                {
                    Text = "FORCE CLOSE UI + LOOT WINDOW",
                    FontSize = 16,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 1"
                },
                RectTransform =
                {
                    AnchorMin = "0.23 0.07",
                    AnchorMax = "0.77 0.19"
                }
            }, UiRoot + ".Panel");

            CuiHelper.AddUi(player, elements);
        }

        #endregion

        #region Cleanup / Isolation

        private object CanNetworkTo(BaseNetworkable entity, BasePlayer target)
        {
            if (entity == null || target == null)
                return null;

            foreach (KeyValuePair<ulong, TestSession> pair in _sessions)
            {
                TestSession session = pair.Value;
                if (session?.Container == entity && pair.Key != target.userID)
                    return false;
            }

            return null;
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null)
                return;

            CloseSession(player, "player disconnected", false, false);
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null)
                return;

            ulong ownerId = 0UL;
            foreach (KeyValuePair<ulong, TestSession> pair in _sessions)
            {
                if (pair.Value?.Container == entity)
                {
                    ownerId = pair.Key;
                    break;
                }
            }

            if (ownerId == 0UL)
                return;

            _sessions.Remove(ownerId);
            BasePlayer player = BasePlayer.FindByID(ownerId);
            if (player != null)
            {
                CuiHelper.DestroyUi(player, UiRoot);
                ForceCloseNativeLoot(player, "temporary test container destroyed");
                player.ChatMessage("<color=#ff7666>ESC TEST V3 ABORTED:</color> Temporary test container was destroyed.");
            }
        }

        private void Unload()
        {
            _pendingOpens.Clear();

            List<StorageContainer> containers = new List<StorageContainer>();
            List<ulong> sessionOwners = new List<ulong>();

            foreach (KeyValuePair<ulong, TestSession> pair in _sessions)
            {
                sessionOwners.Add(pair.Key);

                if (pair.Value?.Container != null)
                    containers.Add(pair.Value.Container);
            }

            // Remove plugin state before forcing the native panels closed so any
            // resulting loot hooks cannot re-enter CloseSession during unload.
            _sessions.Clear();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, UiRoot);

                if (sessionOwners.Contains(player.userID))
                    ForceCloseNativeLoot(player, "plugin unload");
            }

            foreach (StorageContainer container in containers)
            {
                if (container != null && !container.IsDestroyed)
                    container.Kill();
            }
        }

        #endregion
    }
}
