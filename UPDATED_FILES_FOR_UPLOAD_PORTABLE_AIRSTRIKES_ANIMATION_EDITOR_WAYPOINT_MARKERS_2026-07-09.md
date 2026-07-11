# PortableAirstrikes animation editor playback controls - 2026-07-11

## Upload

- `oxide/plugins/PortableAirstrikesAnimationEditor.cs`

## Reload

```text
oxide.reload PortableAirstrikesAnimationEditor
```

## In-game check

```text
/airanim edit <profileId>
/airanim markers
```

Expected result: waypoint markers render as client-side world bubbles with an arrow inside each bubble. The arrow follows the profile's 3D path tangent, and the small red/green attitude ticks show the marker's right/up orientation.

Start `/airanim preview` or use the `PREVIEW` button. The preview bar now has clickable `PAUSE`, `STOP`, and `OPEN` buttons, and the main editor action bars show `PAUSE`/`RESUME` while a preview is active. Click `PAUSE`; expected result: the preview clock and manually moved preview vehicle freeze at the current timestamp and the bar changes to `RESUME`. Click `RESUME`; expected result: playback continues from the paused timestamp instead of jumping ahead. Chat equivalents are `/airanim pause` and `/airanim resume`.

Use `/airanim target` or the `TARGET` button while looking at the intended impact point or after placing a Rust ping. Expected result: the target point gets a native map marker plus a short refreshed client-side target column, so the admin can see where the target-relative path is anchored. `/airanim markers` refreshes those markers. `/airanim end`, switching targets, or closing the session cleans up the native map markers, and no native airdrop/supply-signal smoke effect is fired.

Walk within 8m of a waypoint bubble, look at the bubble, and left-click. A standalone waypoint CUI popup should open in the bottom-right corner. Use the popup to nudge position, switch waypoint selection, refresh markers, save, open the full editor, and rotate the selected waypoint on X/Y/Z with +/-5 degree, +/-15 degree, reset, and 180-degree buttons.

Rotation fields are saved per waypoint as `RotationX`, `RotationY`, and `RotationZ`. Existing profiles without these fields load as zero rotation.

Exact waypoint editing is available in both the waypoint popup and the full editor's `Waypoint Controls` panel. Click a local `X`, `Y`, or `Z` coordinate value, a rotation `X`, `Y`, or `Z` value, or the `DUR` value to open the focused edit modal. `DUR` edits the segment duration after the selected waypoint; on the final waypoint it edits the end/tail duration by adjusting the profile total duration. Build the new value with the modal keypad buttons, then click `APPLY`. `DEL` backspaces, `CLR` clears the draft, `CUR` loads the current value, `-` toggles the sign, and `.` adds a decimal point. `CANCEL` closes the modal without changing the waypoint. The waypoint is mutated only by `APPLY`, and the modal no longer uses Rust's text input or keyboard OK path, so clicking OK cannot reset the waypoint to an unintended `1`. Chat equivalent for rotation remains `/airanim wp rot <index> <rotX> <rotY> <rotZ>`; coordinate editing can still use `/airanim wp set <index> <x> <y> <z>`, and segment duration can use `/airanim wp duration <index> <seconds>`.

The v0.1.13-v0.1.17 editor tried to make direct CUI text fields reliable with axis-specific command names, no immediate panel rebuild, and debounced commits. The v0.1.19 chat-prompt fallback was not ergonomic because the open CUI blocks chat input. The v0.1.20 editor restored real CUI text boxes, but those prefilled fields can still submit stale focus or partial values. The v0.1.21 editor moved to a focused modal, but live testing showed the modal's text input could still submit `1` through Rust's OK path. The v0.1.22 editor removes that text input entirely and uses server-side keypad button commands.

Silent vehicle/object outlines render at each waypoint by default when a preview is not running. Use `/airanim objects` or the `OBJ ON/OFF` CUI button to toggle only the silent outlines; waypoint bubbles remain visible.

Waypoint display mode no longer spawns native drone, cargo plane, attack heli, F15, or A10 prefabs, and the marker refresh loop no longer fires deploy/impact effects. Watch waypoint mode for at least 10 seconds; the route bubbles and object outlines should refresh without stuck/looping audio. Real vehicle prefabs and flyby sound cues remain reserved for `/airanim preview`.

Use `/airanim wp here` or `ADD HERE` to capture the admin's current eye position and view direction. The insert popup lets you place the captured waypoint first, before/after the selected waypoint, last, or in a specific slot.

Use `/airanim timeline` or the `TIMELINE` CUI button to open the bottom timeline. The timeline stays visible after `/airanim hide`, and its `X` closes only the timeline when the main editor is hidden. Waypoint nodes are scaled by segment duration, the scrollbar remains visible, and the `|<`, `<`, `>`, and `>|` buttons pan long profiles to offscreen nodes. The duration badge on each node opens the same exact edit modal; non-final nodes show `dur`, and the final node shows `tail`. The v0.1.24 editor preserves the v0.1.23 minimum-width node slots so short segments like 0.25s or 0.5s do not overlap the neighboring waypoint buttons.

Insert a waypoint from the timeline or waypoint popup, then select nodes after the inserted waypoint. Timeline node selection uses the zero-based node index sent by the CUI buttons, so selected bubbles and detail panels should match the clicked node.

Use `GO WP` from the main editor action bar or the timeline header to teleport the admin to the currently selected waypoint marker. Chat equivalent: `/airanim wp go [index]`.

Waypoint normalization is available in the full editor's Waypoints panel. Click `SELECT` on the waypoint you want to use as the anchor, click `MARK` on any rows that should be normalized, choose `X`, `Y`, or `Z`, then click `NORM`. Expected result: marked waypoints copy the active waypoint's chosen local coordinate while keeping their time, order, and other coordinates unchanged. Chat equivalent: `/airanim wp norm <x|y|z> <marked|all|clear|indices...>`.

Start a preview and stop or let it complete. Silent object outlines should stop refreshing during the preview, expire after the short marker draw duration, and return to the waypoint bubbles afterward.

Cargo-plane previews use the native cargo-plane route setup instead of the generic manual-move preview path. Start `/airanim preview` on a `cargo_plane` profile and confirm the cargo plane renders and flies the authored start-to-end route.

`PortableAirstrikes` v0.1.40+ reads `oxide/data/PortableAirstrikes/VisualProfiles.json` from the editor and applies compatible waypoint profiles to runtime delivery visuals without changing strike config or profile JSON schema. This v0.1.24 editor update does not require a `PortableAirstrikes.cs` reload unless that runtime plugin is also being updated in the same deployment.

## Local verification

- Roslyn compile check passed for `oxide/plugins/PortableAirstrikesAnimationEditor.cs` against `RustDedicated_Data/Managed`.
- Remaining warnings were the existing Unity `Rigidbody.velocity` deprecation warnings in the preview movement helpers.
- Targeted `git diff --check` passed for the editor plugin, development log, and this upload note. Git only warned that LF will be normalized to CRLF when it next touches the files.
