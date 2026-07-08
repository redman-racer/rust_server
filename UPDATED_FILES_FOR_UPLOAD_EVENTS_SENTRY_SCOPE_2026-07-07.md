RaidlandsEvents sentry ownerless scope hotfix - 2026-07-07

What changed:
- RaidlandsSentryTurrets is now v1.0.11.
- The ownerless CopyPaste sentry fallback is no longer automatic by default.
- RaidlandsEvents-tracked sentries are still managed through exact pasted entity IDs from `OnRaidlandsTrackedPasteFinished` and active `RaidlandsEvents` saved data.
- Untracked ownerless `sentry.scientist.static` entities are repaired back to native peacekeeper behavior on plugin load by default.
- The repair path also clears current targets and restores prefab range, max health, current health, and base protection.
- Broad ownerless management commands now require an explicit `confirm` argument because native Outpost sentries are ownerless too.
- `raidlands.sentry.scan` now reports `autoOwnerlessFallback` and labels ownerless switch matches as manual-only candidates.
- New emergency command: `raidlands.sentry.repairnative`.

Rust server plugin files to upload:
- oxide/plugins/RaidlandsSentryTurrets.cs

No config upload is required:
- Existing `oxide/config/RaidlandsSentryTurrets.json` parses successfully.
- On reload, the plugin will save the new default config keys:
  - `Allow Automatic Ownerless CopyPaste Sentry Fallback=false`
  - `Repair Unmanaged Ownerless Sentries On Load=true`

Reload after upload:
- oxide.reload RaidlandsSentryTurrets

Immediate live safety checks:
- Run `raidlands.sentry.scan all`.
- Expected: native Outpost sentries may appear as ownerless, but should not show `managed=True` unless they are an active RaidlandsEvents tracked ID.
- If Outpost turrets are still targeting players after reload, run:
  - raidlands.sentry.repairnative
- Run `raidlands.sentry.scan all` again and verify native Outpost sentries are not managed.

Raid event smoke checks:
- Start or keep an active RaidlandsEvents raid base.
- Expected server log includes `managedSentries=<count>` from RaidlandsEvents after paste completion.
- Event-pasted `sentry.scientist.static` turrets should still be destructible and attack-all because they are managed by exact tracked entity IDs.
- If a verified event-pasted sentry was missed, use its scanned entity ID:
  - raidlands.sentry.manage <entityId> confirm
- Avoid broad fallback unless you have verified the current scanned ownerless sentries are event-pasted:
  - raidlands.sentry.manage ownerless-switch confirm

Local verification:
- oxide/config/RaidlandsSentryTurrets.json parses successfully.
- Roslyn compile check passed for oxide/plugins/RaidlandsSentryTurrets.cs v1.0.11 against RustDedicated_Data/Managed with Oxide.References.dll and glTFast.Newtonsoft.dll excluded.
- Remaining warnings are expected Oxide plugin-reference warnings plus existing obsolete SetFlag warnings.
- Live Oxide reload and in-game Outpost/event smoke are still required to prove runtime behavior.
