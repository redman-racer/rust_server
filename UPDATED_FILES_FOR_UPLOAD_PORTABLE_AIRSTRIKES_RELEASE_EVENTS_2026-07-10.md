Portable Airstrikes carrier-tied release events - 2026-07-10

What changed:
- PortableAirstrikes is now v0.1.47.
- PortableAirstrikesAnimationEditor is now v0.1.27.
- VisualProfiles.json now supports multi-release payload contracts through PayloadReleaseMode, MaxPayloadCount, PayloadReleaseIntervalSeconds, ReleaseTemplate, and rich PayloadEvents.
- /airanim timeline now supports ADD RELEASE plus clickable P1/P2/... release markers; waypoint PAY adds a release at that waypoint time.
- The release popup edits time, payload type, count, carrier/target offsets, spread, launch/fuse, damage, splash/impact, tracking, and per-target damage scales.
- Chat commands were added for /airanim payload add/edit/remove/clear/mode/max/interval/set.
- Carrier-backed strikes now resolve their visual profile before scheduling payload timers and release configured payload events from the live delivery carrier transform plus event offsets.
- Release events can mix current payload IDs: drone grenades, heavy drops, rocket variants, MLRS rockets, homing missiles, mortar shells, and A-10 cannon pulses.
- Runtime release units are capped by the selected strike's existing count budget and optional profile MaxPayloadCount; generated schedules truncate when events would exceed DurationSeconds.
- Homing release profiles require a vehicle-backed target before RP/item charge.
- Custom damage paths for homing and A-10 apply release balance fields directly; native projectile/explosive payloads get speed/fuse/spread/offset support plus best-effort tagged damage scaling.

Rust server files to upload:
- oxide/plugins/PortableAirstrikes.cs
- oxide/plugins/PortableAirstrikesAnimationEditor.cs
- oxide/data/PortableAirstrikes/VisualProfiles.json

Important ignored data file:
- oxide/data/PortableAirstrikes/VisualProfiles.json is ignored by Git via /oxide/*, but it changed and must be copied to the live server data folder.

Reload after upload:
- oxide.reload PortableAirstrikesAnimationEditor
- oxide.reload PortableAirstrikes

Quick live smoke:
- Open /airanim edit <profileId>, click ADD RELEASE, then confirm P1 appears on the timeline.
- Click P1, edit payload/count/time plus a few numeric fields in the popup, then SAVE.
- Use DUP, NEXT/PREV, and DEL; confirm markers renumber and save/reload keeps release details.
- Use /airanim payload mode generated, /airanim payload max <count>, and /airanim payload interval <seconds>; confirm generated releases truncate before the visual DurationSeconds ends.
- Run a cargo profile mixing heavy payloads and confirm release points come from the cargo plane.
- Run a heli profile mixing rocket variants and confirm release points come from the patrol heli.
- Run an F-15 profile mixing MLRS and homing only against a vehicle ping; confirm ground/non-vehicle calls reject before charge.
- Destroy the carrier before first release and confirm no payloads fire.
- Destroy the carrier after one release and confirm already released payloads continue while unreleased events stop.

Local verification:
- oxide/data/PortableAirstrikes/VisualProfiles.json parsed successfully.
- oxide/config/PortableAirstrikes.json parsed successfully.
- Roslyn compile check passed for oxide/plugins/PortableAirstrikes.cs v0.1.47 against RustDedicated_Data/Managed.
- Roslyn compile check passed for oxide/plugins/PortableAirstrikesAnimationEditor.cs v0.1.27 against RustDedicated_Data/Managed.
- Targeted git diff --check passed for the tracked plugin/doc/upload-note changes; VisualProfiles.json is ignored by Git and was verified with JSON parse/hash.
- Remaining warnings were Unity Rigidbody.velocity deprecation warnings in existing visual movement/preview helpers plus the new carrier velocity capture.

Local source hashes:
- oxide/plugins/PortableAirstrikes.cs SHA256: 8E7B396DD17EAE205F4219353CA882B83CBCF64305A6D270D2C985B5D790D75E
- oxide/plugins/PortableAirstrikesAnimationEditor.cs SHA256: D9335559293B125B935D416FB90422D8811D23ED1E3619E9E351CF2A93A47CE7
- oxide/data/PortableAirstrikes/VisualProfiles.json SHA256: A9936B79E1999F703D131784593BA40476B1380404908CC0A7FB93D57C8F68B7
- Docs/AirStrikes/portable_airstrikes_development_log.md SHA256: FF89670EE953C7F33D76755520EFD51E34A4A9AB281A40A1B0E3F203B2BCEA4B
