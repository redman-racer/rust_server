Raidlands event-pasted sentry durability - 2026-07-07

What changed:
- RaidlandsSentryTurrets is now v1.0.10.
- RaidlandsEvents is now v0.1.7 in the current working tree.
- Event-pasted `sentry.scientist.static` NPC turrets are now explicitly registered with RaidlandsSentryTurrets even when CopyPaste leaves them ownerless via `entityowner false`.
- Registered event sentries get the same durability tuning as player-deployed Outpost Sentry Turrets: native base protection is cleared, effective max health is applied, and manual damage handling can kill them.
- RaidlandsSentryTurrets also listens directly to CopyPaste's `OnRaidlandsTrackedPasteFinished` hook, so future tracked event pastes are covered even if RaidlandsEvents load order changes.
- On reload, RaidlandsSentryTurrets reads active RaidlandsEvents saved entity IDs and restores durability tuning for already-active event sentries without requiring RaidlandsEvents to unload the event.
- Ownerless CopyPaste-style sentries with a child simple switch are now also managed as a fallback for live event flows where the RaidlandsEvents entity-ID handoff does not fire.
- `raidlands.sentry.scan` now reports live sentry entity type, owner, managed state, CopyPaste child-switch detection, health/max health, sight range, position, and prefab.
- `raidlands.sentry.manage <entityId|ownerless-switch|ownerless-all>` can explicitly apply durability tuning to scanned live sentries. Prefer an exact `entityId`; `ownerless-all` is intentionally broad.
- RaidlandsEvents now calls the sentry API after tracked paste completion and logs `managedSentries=<count>` beside `turretsAttackAll=<count>`.
- RaidlandsEvents also re-registers active event sentries when RaidlandsSentryTurrets loads.
- Event-managed sentries use durability handling only; hammer pickup and RemoverTool refund/info remain restricted to player-owned managed sentries.
- Native monument/outpost NPC sentries are still protected from this path because ownerless sentries are only managed when their exact entity IDs come from RaidlandsEvents/CopyPaste tracking.

Rust server plugin files to upload:
- oxide/plugins/RaidlandsSentryTurrets.cs
- oxide/plugins/RaidlandsEvents.cs

No config/data upload is required for this fix:
- oxide/config/RaidlandsSentryTurrets.json parsed successfully but was not changed.
- oxide/config/RaidlandsEvents.json parsed successfully but was not changed.

Reload after upload:
- oxide.reload RaidlandsSentryTurrets
- oxide.reload RaidlandsEvents

Active event note:
- If an event is currently running, reload `RaidlandsSentryTurrets` first. Expected: it can restore durability tuning from active RaidlandsEvents saved entity IDs and may log `Restored durability tuning for <n> active RaidlandsEvents sentry turret(s).`
- Reload `RaidlandsEvents` when no active event needs to be preserved, because its existing `CleanupOnUnload` setting can despawn active pasted events during a plugin reload.

Smoke checks:
- Run `oxide.reload RaidlandsSentryTurrets`.
- If an event is already active, damage an event sentry with raid ammo. Expected: it loses health and can die instead of acting indestructible.
- Run `raidlands.sentry.scan all`. Expected copied-base sentries show `managed=True`, `copySwitch=True`, configured `hp=<current>/<max>`, and a lowered `range=<value>`.
- If a copied event sentry still shows `managed=False`, run `raidlands.sentry.manage <entityId>` for that scanned id and re-run `raidlands.sentry.scan all`.
- Start a fresh copied-base event, for example `revents.layouts enable justintoactive-1` then `revents.start justintoactive-1 here`.
- Expected server log includes `turretsAttackAll=<number>, managedSentries=<number>`.
- Damage one pasted sentry with rockets/HV/C4/bullets. Expected: it follows the configured sentry durability (`Effective Max Health=500`, `Incoming Damage Multiplier=1`) and dies once damage exceeds health.
- Confirm normal player-placed Outpost Sentry Turrets still deploy and remain destructible.
- Confirm native monument/outpost sentries were not globally made destructible by this fix.

Local verification:
- oxide/config/RaidlandsEvents.json parses successfully.
- oxide/config/RaidlandsSentryTurrets.json parses successfully.
- Roslyn compile check passed for oxide/plugins/RaidlandsEvents.cs v0.1.7 and oxide/plugins/RaidlandsSentryTurrets.cs v1.0.10 against RustDedicated_Data/Managed with Oxide.References.dll and glTFast.Newtonsoft.dll excluded.
- Remaining warnings are expected Oxide plugin-reference warnings plus the existing obsolete SetFlag warnings.
- Live Oxide reload and in-game damage smoke are still required to prove runtime behavior.
