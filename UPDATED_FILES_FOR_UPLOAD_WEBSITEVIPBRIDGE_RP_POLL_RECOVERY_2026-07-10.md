WebsiteVipBridge RP poll recovery - 2026-07-10

What changed:
- WebsiteVipBridge is now v1.6.0.
- Oxide WebRequests receives timeout values in seconds; the bridge now converts the existing `WebRequestTimeoutMilliseconds` config value before enqueueing GET and POST calls.
- RP purchase and RP point polling now track the poll start time and a generation token.
- A poll that outlives the configured timeout plus a five-second grace window releases its stale in-flight guard.
- A late callback from an older poll cannot unlock a newer poll.

Rust server file to upload:
- oxide/plugins/WebsiteVipBridge.cs

Website files to deploy:
- server-plugins/WebsiteVipBridge.cs
- includes/monument-extraction.php
- includes/rewards.php
- pages/rp-games.php
- assets/js/monument-extraction.js
- assets/js/site.js

Reload after upload:
- oxide.reload WebsiteVipBridge

Useful live checks:
- websitevip.rp.points.status
- websitevip.rp.points.sync
- Confirm a queued `monument_wager` advances from `queued` to `confirmed` with one bridge attempt.
- Confirm the Monument run advances from `CREATING` to `AWAITING_ROOM_SELECTION`.
- Leave polling active for more than one timeout window and confirm the console does not repeat `previous poll is still in flight` indefinitely.

Verification completed:
- The live stuck wager was recovered with `oxide.reload WebsiteVipBridge` followed by `websitevip.rp.points.sync`.
- Live request #41 was confirmed with one bridge attempt and the run advanced to `AWAITING_ROOM_SELECTION`.
- Website and Rust checkout plugin copies have matching SHA256 hashes.
- Roslyn/.NET smoke compilation against the local Rust managed assemblies passed with zero errors.
- PHP lint, JavaScript syntax checks, 77 engine tests, and 37 integration tests passed.
- The v1.6.0 plugin source still requires a live Oxide reload after upload for final compile/runtime proof.

Manifest note:
- `UPDATED_FILES_FOR_UPLOAD.txt` currently belongs to unrelated Portable Airstrikes work and was intentionally not overwritten.
