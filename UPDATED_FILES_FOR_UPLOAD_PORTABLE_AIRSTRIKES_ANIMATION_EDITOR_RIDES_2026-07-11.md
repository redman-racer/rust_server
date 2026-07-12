# PortableAirstrikes animation editor look-safe preview HUD and rides - 2026-07-11

What changed:
- `PortableAirstrikesAnimationEditor` is now v0.2.9.
- The hidden-editor preview HUD no longer requests the cursor, so admins keep normal mouse-look while watching `/airanim preview`.
- The preview HUD now shows chat-command controls for ride, pause/resume, stop, and reopen instead of clickable buttons that force the mouse cursor up.
- Active `/airanim preview` sessions now support a third-person chase ride behind the selected profile vehicle.
- Admins can queue the next rider before preview starts with `/airanim ride <playerNameOrSteamId>`.
- Admins can move the queued or named rider to the profile start chase point before preview starts with `/airanim ride stage [playerNameOrSteamId]`.
- Admins can ride themselves with `/airanim ride`.
- Admins can put another online player on the ride with `/airanim ride <playerNameOrSteamId>`.
- Riders can be detached and returned to their starting position with `/airanim ride stop [player]`.
- Ride mode now hides the editor and preview CUI while the rider is attached, so riders keep their own camera/look control while their position follows the preview vehicle.
- Ride mode now uses a close 5m back / 2.5m up chase offset instead of the original distant 18m / 7m observer offset.
- Non-admin riders can run `/airanim ride stop` to detach themselves without being granted editor access.
- Cargo-plane previews now use the editor's manual waypoint playback path, so `/airanim pause` and resume behavior freeze the visible plane instead of only pausing the UI clock.
- Preview stop, preview completion, session cleanup, plugin unload, and rider disconnect all clean up ride state.

Rust server file to upload:
- oxide/plugins/PortableAirstrikesAnimationEditor.cs

Reload after upload:
```text
oxide.reload PortableAirstrikesAnimationEditor
```

Quick live smoke:
- Confirm the reload banner reports `PortableAirstrikesAnimationEditor` v0.2.9.
- Open an authored profile with `/airanim edit <profileId>`.
- Before starting preview, run `/airanim ride <playerNameOrSteamId>`; expected result: the player is queued for the next preview ride.
- Run `/airanim ride stage`; expected result: the queued player moves to the profile start chase point and can load the area before playback begins.
- Start `/airanim preview`.
- Expected result: the staged/queued player automatically attaches to the preview vehicle ride when playback starts.
- Run `/airanim ride`; expected result: the admin follows close behind the visible preview vehicle and the editor/preview CUI disappears.
- While riding, move the mouse/camera; expected result: the rider can look around normally while staying in the chase position.
- While previewing without riding, move the mouse/camera; expected result: the look-safe preview HUD stays visible without forcing the mouse cursor up.
- Run `/airanim pause`; expected result: the preview vehicle and preview clock freeze. Run `/airanim resume`; expected result: playback continues from the paused timestamp.
- Run `/airanim ride stop`; expected result: the admin returns to the position where the ride started.
- Start another preview, run `/airanim ride <playerNameOrSteamId>`, then `/airanim ride stop <playerNameOrSteamId>`; expected result: the target player follows the preview vehicle and returns cleanly.
- Have the target player run `/airanim ride stop`; expected result: the rider can detach themselves even without editor permission.
- Repeat pause/resume on a `cargo_plane` profile; expected result: the visible cargo plane freezes and resumes like the other preview vehicles.
- Let a preview complete or stop it with `/airanim stop`; expected result: any active riders are returned and the preview vehicle is cleaned up.

Local verification:
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikesAnimationEditor.cs` v0.2.9 against `RustDedicated_Data/Managed`.
- Remaining warnings were the existing Unity `Rigidbody.velocity` deprecation warnings in preview movement helpers.
- Live Oxide reload and in-game preview mouse-look smoke testing are still required.
