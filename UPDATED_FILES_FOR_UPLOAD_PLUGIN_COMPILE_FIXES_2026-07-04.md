Raidlands Rust plugin compile fixes - 2026-07-04

Context:
- The console errors were caused by a Rust/Oxide API shift after the server update.
- WebsiteVipBridge compiled successfully in the screenshot; these fixes are for the failing third-party plugins around it.
- The patched plugin files below passed a local Roslyn compile check against the server's RustDedicated_Data/Managed assemblies. Live Oxide reload is still the final verification.

Rust server plugin files to upload:
- oxide/plugins/Arkan.cs
- oxide/plugins/BuildingSkins.cs
- oxide/plugins/CustomVendingSetup.cs
- oxide/plugins/DiscordStatus.cs
- oxide/plugins/FancyDrop.cs
- oxide/plugins/Guardian.cs
- oxide/plugins/InstantBuy.cs
- oxide/plugins/Skins.cs
- oxide/plugins/Trade.cs

AutoLock live cleanup:
- Do not upload the current local oxide/plugins/AutoLock.cs as-is.
- The current local file is a truncated AutoWipe source that ends mid-method and does not match the AutoLock config/data/lang files.
- To stop the live AutoLock compile error immediately, remove or rename the broken live oxide/plugins/AutoLock.cs file outside the plugins folder.
- To restore the auto-lock feature, replace it with a current Auto Lock plugin build, then reload AutoLock.
- Public reference checked during triage: https://umod.org/plugins/auto-lock and https://github.com/MONaH-Rasta/AutoLock/releases/tag/v2.4.7

Reload after upload:
- oxide.reload Arkan
- oxide.reload BuildingSkins
- oxide.reload CustomVendingSetup
- oxide.reload DiscordStatus
- oxide.reload FancyDrop
- oxide.reload Guardian
- oxide.reload InstantBuy
- oxide.reload Skins
- oxide.reload Trade

Optional AutoLock handling:
- If removing/renaming the broken AutoLock.cs without replacement, no reload is needed; Oxide will stop trying to compile it after the file leaves oxide/plugins.
- If replacing AutoLock.cs with a current Auto Lock build, run:
  - oxide.reload AutoLock

Verify:
- Watch the console for "Failed to compile" lines for the nine uploaded plugins.
- If a plugin still fails on live, capture the new line number; after these fixes, any remaining error should be a different API surface than the screenshot showed.
- Confirm WebsiteVipBridge still logs its normal sync startup after the reload batch.
