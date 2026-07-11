Portable Airstrikes editor payload-release flares - 2026-07-10

What changed:
- PortableAirstrikesAnimationEditor is now v0.1.29.
- Editor previews pop a harmless aircraft flare burst at every legacy first-payload cue and authored payload release event.
- Release effects originate from the visible preview carrier's live transform, including native cargo-plane previews.
- All preview carriers use the bundled attack-helicopter flare burst from autoloaded AssetScene-prefabs.
- Removed the F-15 and patrol-helicopter flare prefabs after live output proved they require non-autoloaded AssetScene-props.other.
- Existing rocket-launch/spark release cues remain in place, and dangerous payload preview remains disabled/unimplemented.

Rust server file to upload:
- oxide/plugins/PortableAirstrikesAnimationEditor.cs

Documentation/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD.txt
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_EDITOR_RELEASE_FLARES_2026-07-10.md

Reload after upload:
- oxide.reload PortableAirstrikesAnimationEditor

Quick live smoke:
- Open /airanim edit <profileId> and PREVIEW a legacy profile; confirm a flare burst appears at FirstPayloadDelaySeconds.
- Preview a multi-release profile with P1/P2/... events; confirm one flare burst appears at the visible carrier for each release.
- Preview patrol-heli and cargo-plane/F-15 profiles; confirm the shared flare burst renders from the visible carrier without an asset-scene error.
- Confirm the editor does not spawn damaging ordnance for these cues.

Local verification:
- Bundles/AssetSceneManifest.json confirms the selected attack-helicopter flare effect is in AssetScene-prefabs with AutoLoad=true and CanUnload=false.
- The rejected F-15 and patrol-helicopter flare effects are both in non-autoloaded AssetScene-props.other.
- Roslyn compile check passed for oxide/plugins/PortableAirstrikesAnimationEditor.cs v0.1.29 against RustDedicated_Data/Managed.
- Targeted git diff --check and trailing-whitespace checks passed for the editor, development log, and upload notes.
- Remaining warnings were the existing Unity Rigidbody.velocity deprecation warnings at the preview movement helpers.
- Live Oxide reload and in-game effect rendering are still required.

Local source hash:
- oxide/plugins/PortableAirstrikesAnimationEditor.cs SHA256: 7CD1CFAC0914156939CDEA87E7937E43071678B8DB0FC5B13CC4A416BFB810AF
