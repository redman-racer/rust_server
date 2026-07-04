import fs from "node:fs/promises";
import path from "node:path";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const rootDir = "C:/wamp64/www/rust_server";
const inputPath = "C:/Users/user/Downloads/scrapland kits/raidlands_vip_kits_permissions_mapping_with_claimables.xlsx";
const outputDir = `${rootDir}/outputs/kit-plan-plugin-alignment`;
const outputPath = `${outputDir}/raidlands_vip_kits_permissions_mapping_with_claimables_plugin_aligned.xlsx`;
const runStamp = new Date().toISOString().replace(/[:.]/g, "-");
const beforePreviewDir = `${outputDir}/previews-${runStamp}-before`;
const afterPreviewDir = `${outputDir}/previews-${runStamp}-after`;

const sheetNames = [
  "Dashboard",
  "README",
  "Website Products",
  "Server Groups",
  "Kits & Packages",
  "Kit Access Matrix",
  "Perk Products",
  "Playtime RP",
  "Group Permissions",
  "Rank Matrix",
  "Kit Item Manifest",
  "Vehicle Pack",
  "Config Changes",
  "Pending Custom",
  "Sources",
  "In-Game Claimables",
];

const selectedEditSheets = [
  "README",
  "Website Products",
  "Server Groups",
  "Kits & Packages",
  "Perk Products",
  "Group Permissions",
  "Rank Matrix",
  "Kit Item Manifest",
  "Vehicle Pack",
  "Config Changes",
  "Pending Custom",
  "Sources",
  "In-Game Claimables",
];

const vehicleMap = new Map([
  ["RHIB", { support: "RaidlandsVehicleTokens + VehicleLicence", command: "RHIB Token / VehicleLicence type RHIB", notes: "RaidlandsVehicleTokens grants a wrappedgift RHIB Token and opens a short-lived VehicleLicence spawn intent; failed spawns do not consume the token." }],
  ["Tugboat", { support: "RaidlandsVehicleTokens + VehicleLicence", command: "Tugboat Token / VehicleLicence type Tugboat", notes: "RaidlandsVehicleTokens grants a wrappedgift Tugboat Token and opens a short-lived VehicleLicence spawn intent; failed spawns do not consume the token." }],
  ["Solo Submarine", { support: "RaidlandsVehicleTokens + VehicleLicence", command: "Solo Submarine Token / VehicleLicence type SubmarineSolo", notes: "RaidlandsVehicleTokens grants a wrappedgift Solo Submarine Token and opens a short-lived VehicleLicence spawn intent; failed spawns do not consume the token." }],
  ["Duo Submarine", { support: "RaidlandsVehicleTokens + VehicleLicence", command: "Duo Submarine Token / VehicleLicence type SubmarineDuo", notes: "RaidlandsVehicleTokens grants a wrappedgift Duo Submarine Token and opens a short-lived VehicleLicence spawn intent; failed spawns do not consume the token." }],
  ["Snowmobile", { support: "RaidlandsVehicleTokens + VehicleLicence", command: "Snowmobile Token / VehicleLicence type Snowmobile", notes: "RaidlandsVehicleTokens grants a wrappedgift Snowmobile Token and opens a short-lived VehicleLicence spawn intent; failed spawns do not consume the token." }],
  ["Hot Air Balloon", { support: "RaidlandsVehicleTokens + VehicleLicence", command: "Hot Air Balloon Token / VehicleLicence type HotAirBalloon", notes: "RaidlandsVehicleTokens grants a wrappedgift Hot Air Balloon Token and opens a short-lived VehicleLicence spawn intent; failed spawns do not consume the token." }],
]);

const vehicleManifest = new Map([
  ["RHIB", "RaidlandsVehicleTokens issues real wrappedgift items named RHIB Token; VehicleLicence is used only during token-backed spawn intent."],
  ["Tugboat", "RaidlandsVehicleTokens issues real wrappedgift items named Tugboat Token; VehicleLicence is used only during token-backed spawn intent."],
  ["Solo Submarine", "RaidlandsVehicleTokens issues real wrappedgift items named Solo Submarine Token; VehicleLicence is used only during token-backed spawn intent."],
  ["Duo Submarine", "RaidlandsVehicleTokens issues real wrappedgift items named Duo Submarine Token; VehicleLicence is used only during token-backed spawn intent."],
  ["Snowmobile", "RaidlandsVehicleTokens issues real wrappedgift items named Snowmobile Token; VehicleLicence is used only during token-backed spawn intent."],
  ["Hot Air Balloon", "RaidlandsVehicleTokens issues real wrappedgift items named Hot Air Balloon Token; VehicleLicence is used only during token-backed spawn intent."],
]);

function colName(col1) {
  let n = col1;
  let out = "";
  while (n > 0) {
    n -= 1;
    out = String.fromCharCode(65 + (n % 26)) + out;
    n = Math.floor(n / 26);
  }
  return out;
}

function a1(row1, col1) {
  return `${colName(col1)}${row1}`;
}

function asText(value) {
  return value === null || value === undefined ? "" : String(value);
}

function normalizeText(value) {
  return asText(value).trim().toLowerCase();
}

function getSheet(workbook, name) {
  return workbook.worksheets.getItem(name);
}

function usedValues(sheet) {
  const range = sheet.getUsedRange();
  const values = range.values || [];
  return values.map((row) => row.map((value) => (value === undefined ? null : value)));
}

function findHeaderRow(values, requiredHeaders) {
  const required = requiredHeaders.map(normalizeText);
  for (let r = 0; r < values.length; r += 1) {
    const row = values[r].map(normalizeText);
    if (required.every((header) => row.includes(header))) {
      return r;
    }
  }
  throw new Error(`Could not find header row with ${requiredHeaders.join(", ")}`);
}

function headerMap(values, headerRow) {
  const map = new Map();
  values[headerRow].forEach((header, index) => {
    const key = normalizeText(header);
    if (key) {
      map.set(key, index);
    }
  });
  return map;
}

function colIndex(headers, headerName) {
  const index = headers.get(normalizeText(headerName));
  if (index === undefined) {
    throw new Error(`Missing column: ${headerName}`);
  }
  return index;
}

function findRow(values, headerRow, headers, headerName, key) {
  const col = colIndex(headers, headerName);
  for (let r = headerRow + 1; r < values.length; r += 1) {
    if (asText(values[r]?.[col]) === key) {
      return r;
    }
  }
  return -1;
}

function setCell(sheet, row0, col0, value) {
  sheet.getRange(a1(row0 + 1, col0 + 1)).values = [[value]];
}

function setByHeader(sheet, values, row0, headers, headerName, value) {
  const col0 = colIndex(headers, headerName);
  setCell(sheet, row0, col0, value);
  values[row0][col0] = value;
}

function isEmptyRow(row) {
  return !row || row.every((value) => asText(value).trim() === "");
}

function lastNonEmptyRow(values) {
  for (let r = values.length - 1; r >= 0; r -= 1) {
    if (!isEmptyRow(values[r])) {
      return r;
    }
  }
  return 0;
}

function appendOrUpdateRow(sheet, values, headerRow, headers, keyHeader, keyValue, rowByHeader) {
  let row0 = findRow(values, headerRow, headers, keyHeader, keyValue);
  const colCount = values[headerRow].length;
  if (row0 < 0) {
    row0 = lastNonEmptyRow(values) + 1;
    const sourceRow0 = Math.max(headerRow + 1, row0 - 1);
    const target = sheet.getRange(`${a1(row0 + 1, 1)}:${a1(row0 + 1, colCount)}`);
    const source = sheet.getRange(`${a1(sourceRow0 + 1, 1)}:${a1(sourceRow0 + 1, colCount)}`);
    try {
      target.copyFrom(source, "all");
    } catch {
      // Values are the source of truth here; style copy is best effort for appended rows.
    }
    values[row0] = new Array(colCount).fill(null);
    setByHeader(sheet, values, row0, headers, keyHeader, keyValue);
  }
  for (const [header, value] of Object.entries(rowByHeader)) {
    setByHeader(sheet, values, row0, headers, header, value);
  }
  return row0;
}

function replaceInCellText(text) {
  let updated = text;
  const replacements = [
    ["queuepriority.use / needs plugin", "bypassqueue.allow / verify live queue smoke test"],
    ["queuepriority.use", "bypassqueue.allow"],
    ["Queue plugin / host integration", "BypassQueue"],
    ["Queue plugin / host", "BypassQueue"],
    ["Plugin not confirmed in repo.", "BypassQueue is staged; verify live queue bypass smoke test."],
    ["Requires queue priority/bypass integration.", "BypassQueue is staged; verify live queue bypass smoke test."],
    ["SignArtist not confirmed in repo; add/verify plugin.", "SignArtist is staged; verify /sil on a player-owned sign."],
    ["SignArtist not found in repo; verify or add.", "SignArtist is staged; verify /sil on a player-owned sign."],
    ["signartist.url / needs plugin", "signartist.url / verify /sil smoke test"],
    ["Only SpawnHeli confirmed for helis", "SpawnHeli covers helis; VehicleLicence config staged for non-heli vehicles"],
    ["RaidlandsVehicleTokens.cs or vehicle license plugin", "RaidlandsVehicleTokens.cs plus VehicleLicence"],
    ["custom.pending.portafort", "wrappedgift / Portafort Token"],
    ["custom.pending.super_serum", "maxhealthtea.pure / Super Serum"],
    ["custom.pending.vehicle", "wrappedgift / Raidlands vehicle tokens"],
    ["Portafort still needs RaidlandsPortaforts.cs.", "Portafort is implemented by RaidlandsPortaforts.cs."],
    ["Vehicle pack and vehicle HP still need RaidlandsVehicleTokens.cs.", "Vehicle pack and vehicle HP are implemented by RaidlandsVehicleTokens.cs."],
    ["Super Serum still needs RaidlandsConsumables.cs.", "Super Serum is implemented by RaidlandsConsumables.cs."],
    ["Pending custom implementation", "Implemented custom plugin"],
    ["Recommended custom consumable plugin.", "Implemented by RaidlandsConsumables.cs; verify live consume/reconnect/death flow."],
    ["Recommended custom token + CopyPaste plugin.", "Implemented by RaidlandsPortaforts.cs; verify live CopyPaste placement."],
    ["Remaining non-heli vehicles need custom vehicle support.", "Remaining non-heli vehicles are token-backed through RaidlandsVehicleTokens.cs."],
  ];
  for (const [from, to] of replacements) {
    updated = updated.split(from).join(to);
  }
  return updated;
}

function replaceTextAcrossSheets(workbook) {
  for (const sheetName of selectedEditSheets) {
    const sheet = getSheet(workbook, sheetName);
    const values = usedValues(sheet);
    for (let r = 0; r < values.length; r += 1) {
      for (let c = 0; c < (values[r]?.length || 0); c += 1) {
        if (typeof values[r][c] !== "string") {
          continue;
        }
        const updated = replaceInCellText(values[r][c]);
        if (updated !== values[r][c]) {
          setCell(sheet, r, c, updated);
          values[r][c] = updated;
        }
      }
    }
  }
}

function updateWebsiteProducts(workbook) {
  const sheet = getSheet(workbook, "Website Products");
  const values = usedValues(sheet);
  const headerRow = findHeaderRow(values, ["Product ID", "Status", "Notes"]);
  const headers = headerMap(values, headerRow);
  for (const [id, notes] of [
    ["perk_queue_priority", "BypassQueue plugin and no-config permission backend are staged; verify live queue bypass before final approval."],
    ["perk_sign_art", "SignArtist plugin/config are staged; verify /sil on a player-owned sign before final approval."],
  ]) {
    const row0 = findRow(values, headerRow, headers, "Product ID", id);
    if (row0 >= 0) {
      setByHeader(sheet, values, row0, headers, "Status", "Verify");
      setByHeader(sheet, values, row0, headers, "Notes", notes);
    }
  }
}

function updateServerGroups(workbook) {
  const sheet = getSheet(workbook, "Server Groups");
  const values = usedValues(sheet);
  const headerRow = findHeaderRow(values, ["Server Group", "Status", "Notes"]);
  const headers = headerMap(values, headerRow);
  for (const [group, notes] of [
    ["perk_queue_priority", "BypassQueue is staged; verify live queue bypass before final approval."],
    ["perk_sign_art", "SignArtist is staged; verify /sil before final approval."],
  ]) {
    const row0 = findRow(values, headerRow, headers, "Server Group", group);
    if (row0 >= 0) {
      setByHeader(sheet, values, row0, headers, "Status", "Verify");
      setByHeader(sheet, values, row0, headers, "Notes", notes);
    }
  }
}

function updatePerkProducts(workbook) {
  const sheet = getSheet(workbook, "Perk Products");
  const values = usedValues(sheet);
  const headerRow = findHeaderRow(values, ["Perk Group", "Status", "Notes"]);
  const headers = headerMap(values, headerRow);
  for (const [group, plugin, notes] of [
    ["perk_queue_priority", "BypassQueue", "Use bypassqueue.allow; plugin is staged, but live queue smoke test is still required."],
    ["perk_sign_art", "SignArtist", "Use signartist.url; plugin/config are staged, but /sil smoke test is still required."],
  ]) {
    const row0 = findRow(values, headerRow, headers, "Perk Group", group);
    if (row0 >= 0) {
      setByHeader(sheet, values, row0, headers, "Plugin / System", plugin);
      setByHeader(sheet, values, row0, headers, "Status", "Verify");
      setByHeader(sheet, values, row0, headers, "Notes", notes);
    }
  }
}

function updateGroupPermissions(workbook) {
  const sheet = getSheet(workbook, "Group Permissions");
  const values = usedValues(sheet);
  const headerRow = findHeaderRow(values, ["Server Group", "Permission / Config Key", "Status"]);
  const headers = headerMap(values, headerRow);
  const permissionCol = colIndex(headers, "Permission / Config Key");
  for (let r = headerRow + 1; r < values.length; r += 1) {
    const permission = asText(values[r]?.[permissionCol]);
    if (permission === "bypassqueue.allow") {
      setByHeader(sheet, values, r, headers, "Plugin / System", "BypassQueue");
      setByHeader(sheet, values, r, headers, "Status", "Verify");
      setByHeader(sheet, values, r, headers, "Notes", "Plugin staged; verify live queue bypass before final approval.");
    }
    if (permission === "signartist.url") {
      setByHeader(sheet, values, r, headers, "Plugin / System", "SignArtist");
      setByHeader(sheet, values, r, headers, "Status", "Verify");
      setByHeader(sheet, values, r, headers, "Notes", "Plugin/config staged; verify /sil on live server.");
    }
  }
}

function updateRankMatrix(workbook) {
  const sheet = getSheet(workbook, "Rank Matrix");
  const values = usedValues(sheet);
  const headerRow = findHeaderRow(values, ["Perk / Capability", "Implementation Key / Notes"]);
  const headers = headerMap(values, headerRow);
  const perkCol = colIndex(headers, "Perk / Capability");
  const notesCol = colIndex(headers, "Implementation Key / Notes");
  for (let r = headerRow + 1; r < values.length; r += 1) {
    const perk = asText(values[r]?.[perkCol]);
    if (perk === "Queue bypass") {
      setCell(sheet, r, notesCol, "bypassqueue.allow / verify live queue smoke test");
    }
    if (perk === "Custom sign art /sil") {
      setCell(sheet, r, notesCol, "signartist.url / verify /sil smoke test");
    }
  }
}

function updateVehiclePack(workbook) {
  const sheet = getSheet(workbook, "Vehicle Pack");
  const values = usedValues(sheet);
  const headerRow = findHeaderRow(values, ["Vehicle", "Server Support", "Command / Hook", "Status", "Notes"]);
  const headers = headerMap(values, headerRow);
  for (const [vehicle, data] of vehicleMap.entries()) {
    const row0 = findRow(values, headerRow, headers, "Vehicle", vehicle);
    if (row0 >= 0) {
      setByHeader(sheet, values, row0, headers, "Server Support", data.support);
      setByHeader(sheet, values, row0, headers, "Command / Hook", data.command);
      setByHeader(sheet, values, row0, headers, "Status", "Verify");
      setByHeader(sheet, values, row0, headers, "Notes", data.notes);
    }
  }
}

function updateKitItemManifest(workbook) {
  const sheet = getSheet(workbook, "Kit Item Manifest");
  const values = usedValues(sheet);
  const headerRow = findHeaderRow(values, ["Kit / Pack ID", "Item Name", "Status", "Notes"]);
  const headers = headerMap(values, headerRow);
  const kitCol = colIndex(headers, "Kit / Pack ID");
  const itemCol = colIndex(headers, "Item Name");
  for (let r = headerRow + 1; r < values.length; r += 1) {
    const kit = asText(values[r]?.[kitCol]);
    const item = asText(values[r]?.[itemCol]);
    if (kit === "pack_vehicle" && vehicleManifest.has(item)) {
      setByHeader(sheet, values, r, headers, "Status", "Verify");
      setByHeader(sheet, values, r, headers, "Notes", vehicleManifest.get(item));
    }
  }
}

function updateConfigChanges(workbook) {
  const sheet = getSheet(workbook, "Config Changes");
  const values = usedValues(sheet);
  const headerRow = findHeaderRow(values, ["File", "Setting / Area", "Current / Existing", "Recommended", "Status"]);
  const headers = headerMap(values, headerRow);

  appendOrUpdateRow(sheet, values, headerRow, headers, "File", "oxide/plugins/BypassQueue.cs", {
    "Setting / Area": "Queue bypass dependency",
    "Current / Existing": "Plugin file staged; no config by design",
    "Recommended": "Use bypassqueue.allow for rank/perk queue bypass grants",
    "Permission(s) / Group(s)": "bypassqueue.allow",
    "Priority": "High",
    "Status": "Verify",
    "Notes": "Live queue smoke test is still required before final approval.",
  });

  appendOrUpdateRow(sheet, values, headerRow, headers, "File", "oxide/plugins/SignArtist.cs", {
    "Setting / Area": "Sign art dependency",
    "Current / Existing": "Plugin file and generated config staged",
    "Recommended": "Grant signartist.url for /sil access",
    "Permission(s) / Group(s)": "signartist.url",
    "Priority": "High",
    "Status": "Verify",
    "Notes": "Verify /sil on a player-owned sign after live reload.",
  });

  appendOrUpdateRow(sheet, values, headerRow, headers, "File", "oxide/config/CopyPaste.json", {
    "Setting / Area": "CopyPaste generated config",
    "Current / Existing": "Generated config imported",
    "Recommended": "Keep CopyPaste loaded before RaidlandsPortaforts token placement",
    "Permission(s) / Group(s)": "CopyPaste hook",
    "Priority": "Medium",
    "Status": "Verify",
    "Notes": "RaidlandsPortaforts consumes Portafort Tokens only after CopyPaste.TryPasteFromSteamId succeeds.",
  });

  appendOrUpdateRow(sheet, values, headerRow, headers, "File", "oxide/config/VehicleLicence.json", {
    "Setting / Area": "Non-heli vehicle command surface",
    "Current / Existing": "Generated/tuned config imported",
    "Recommended": "Expose only tugboat/tug, rhib, hab/hotairballoon, subsolo/solo, subduo/duo, and snow/snowmobile",
    "Permission(s) / Group(s)": "VehicleLicence API behind RaidlandsVehicleTokens",
    "Priority": "High",
    "Status": "Verify",
    "Notes": "RaidlandsVehicleTokens blocks direct VehicleLicence spawns for token-only vehicles and opens a short-lived spawn intent only while redeeming a token.",
  });

  appendOrUpdateRow(sheet, values, headerRow, headers, "File", "oxide/plugins/RaidlandsPortaforts.cs", {
    "Setting / Area": "Portafort token + CopyPaste placement",
    "Current / Existing": "Custom plugin implemented",
    "Recommended": "Use wrappedgift named Portafort Token; default CopyPaste file raidlands_portafort",
    "Permission(s) / Group(s)": "raidlands.portaforts.admin",
    "Priority": "High",
    "Status": "Verify",
    "Notes": "Missing paste file and failed paste attempts do not consume the token.",
  });

  appendOrUpdateRow(sheet, values, headerRow, headers, "File", "oxide/config/RaidlandsPortaforts.json", {
    "Setting / Area": "Portafort token config",
    "Current / Existing": "Generated config added",
    "Recommended": "Upload with RaidlandsPortaforts.cs and create oxide/data/copypaste/raidlands_portafort.json before final smoke test",
    "Permission(s) / Group(s)": "raidlands.portaforts.admin",
    "Priority": "High",
    "Status": "Verify",
    "Notes": "Default CopyPaste args enable deployables, disable inventories, enable autoheight, and block collisions.",
  });

  appendOrUpdateRow(sheet, values, headerRow, headers, "File", "oxide/plugins/RaidlandsVehicleTokens.cs", {
    "Setting / Area": "Vehicle token items + HP permissions",
    "Current / Existing": "Custom plugin implemented",
    "Recommended": "Use wrappedgift named per-vehicle Token; SpawnHeli handles helis and VehicleLicence handles RHIB/tug/subs/snowmobile/balloon",
    "Permission(s) / Group(s)": "raidlands.vehicletokens.admin; raidlands.vehicletokens.bypass; raidlands.vehicle.hp.125; raidlands.vehicle.hp.150",
    "Priority": "High",
    "Status": "Verify",
    "Notes": "The pack_vehicle kit grant issues 5 tangible tokens for each configured vehicle.",
  });

  appendOrUpdateRow(sheet, values, headerRow, headers, "File", "oxide/config/RaidlandsVehicleTokens.json", {
    "Setting / Area": "Vehicle token config",
    "Current / Existing": "Generated config added",
    "Recommended": "Keep default token shortname wrappedgift and require display-name match for each vehicle token",
    "Permission(s) / Group(s)": "raidlands.vehicle.hp.125; raidlands.vehicle.hp.150",
    "Priority": "High",
    "Status": "Verify",
    "Notes": "Direct VehicleLicence spawn blocking is enabled for configured token-only vehicle types.",
  });

  appendOrUpdateRow(sheet, values, headerRow, headers, "File", "oxide/plugins/RaidlandsConsumables.cs", {
    "Setting / Area": "Super Serum item + persistent buffs",
    "Current / Existing": "Custom plugin implemented",
    "Recommended": "Use maxhealthtea.pure named Super Serum; persist active state until death and refresh configured tea/pie-style modifiers",
    "Permission(s) / Group(s)": "raidlands.consumables.admin",
    "Priority": "High",
    "Status": "Verify",
    "Notes": "Serum state is saved across reconnect/restart and cleared on death or new save.",
  });

  appendOrUpdateRow(sheet, values, headerRow, headers, "File", "oxide/config/RaidlandsConsumables.json", {
    "Setting / Area": "Super Serum config",
    "Current / Existing": "Generated config added",
    "Recommended": "Keep default maxhealthtea.pure named Super Serum and tune Modifier Values only after live validation",
    "Permission(s) / Group(s)": "raidlands.consumables.admin",
    "Priority": "High",
    "Status": "Verify",
    "Notes": "Source Buff Items documents the tea and pie effects the serum is intended to mirror.",
  });

  appendOrUpdateRow(sheet, values, headerRow, headers, "File", "oxide/config/WebsiteVipBridge.json", {
    "Setting / Area": "ManagedGroups + KitPermissionManagedGroups",
    "Current / Existing": "Generated config imported/deduped; older group model still needs final mapping pass",
    "Recommended": "Add final rank_*, perk_*, and claim_* groups after workbook approval and smoke tests",
    "Permission(s) / Group(s)": "All rank/perk/claim groups",
    "Priority": "High",
    "Status": "Approved",
    "Notes": "Do not wire new grants until the kit/perk group mapping pass is complete.",
  });

  appendOrUpdateRow(sheet, values, headerRow, headers, "File", "UPDATED_FILES_FOR_UPLOAD.txt", {
    "Setting / Area": "Plugin staging upload handoff",
    "Current / Existing": "Manifest updated for generated plugin configs",
    "Recommended": "Upload/reload CopyPaste, SignArtist, BypassQueue, VehicleLicence, SpawnHeli, Kits, and WebsiteVipBridge",
    "Permission(s) / Group(s)": "bypassqueue.allow; signartist.url; vehiclelicence.*",
    "Priority": "High",
    "Status": "Verify",
    "Notes": "Paths are verified locally; live upload/reload and smoke tests remain.",
  });

  const nonHeliRow = findRow(values, headerRow, headers, "Setting / Area", "Non-heli vehicle pack support");
  if (nonHeliRow >= 0) {
    setByHeader(sheet, values, nonHeliRow, headers, "File", "RaidlandsVehicleTokens.cs");
    setByHeader(sheet, values, nonHeliRow, headers, "Setting / Area", "Vehicle pack token accounting");
    setByHeader(sheet, values, nonHeliRow, headers, "Current / Existing", "RaidlandsVehicleTokens implemented with SpawnHeli and VehicleLicence backends");
    setByHeader(sheet, values, nonHeliRow, headers, "Recommended", "Use tangible wrappedgift vehicle tokens and keep direct VehicleLicence spawns blocked for token-only vehicles");
    setByHeader(sheet, values, nonHeliRow, headers, "Permission(s) / Group(s)", "raidlands.vehicletokens.admin; raidlands.vehicle.hp.125; raidlands.vehicle.hp.150");
    setByHeader(sheet, values, nonHeliRow, headers, "Status", "Verify");
    setByHeader(sheet, values, nonHeliRow, headers, "Notes", "Smoke-test each token spawn and both vehicle HP permissions on the live server.");
  }
}

function updatePendingCustom(workbook) {
  const sheet = getSheet(workbook, "Pending Custom");
  const values = usedValues(sheet);
  const headerRow = findHeaderRow(values, ["System", "Recommended Plugin", "Status", "Notes"]);
  const headers = headerMap(values, headerRow);
  const updates = new Map([
    ["Portafort", {
      "Recommended Plugin": "RaidlandsPortaforts.cs + CopyPaste",
      "Notes": "Implemented with wrappedgift Portafort Token, /portafort, admin grant hook, CopyPaste missing-file no-consume guard, and success-only token consumption.",
    }],
    ["Vehicle Pack", {
      "Recommended Plugin": "RaidlandsVehicleTokens.cs + SpawnHeli + VehicleLicence",
      "Notes": "Implemented with tangible wrappedgift vehicle tokens, pack_vehicle 5-each kit grant, SpawnHeli heli APIs, VehicleLicence non-heli APIs, and direct VehicleLicence spawn blocking.",
    }],
    ["Vehicle HP", {
      "Recommended Plugin": "RaidlandsVehicleTokens.cs",
      "Notes": "Implemented with raidlands.vehicle.hp.150 winning over raidlands.vehicle.hp.125 for token-spawned SpawnHeli and VehicleLicence vehicles.",
    }],
    ["Super Serum", {
      "Recommended Plugin": "RaidlandsConsumables.cs",
      "Notes": "Implemented with maxhealthtea.pure named Super Serum, persistent active-state data, configured tea/pie-style modifier refresh, reconnect persistence, and death/new-save clearing.",
    }],
  ]);
  for (const [system, rowUpdates] of updates.entries()) {
    const row0 = findRow(values, headerRow, headers, "System", system);
    if (row0 >= 0) {
      for (const [header, value] of Object.entries(rowUpdates)) {
        setByHeader(sheet, values, row0, headers, header, value);
      }
      setByHeader(sheet, values, row0, headers, "Status", "Verify");
    }
  }
}

function updateReadme(workbook) {
  const sheet = getSheet(workbook, "README");
  const values = usedValues(sheet);
  const headerRow = findHeaderRow(values, ["Section", "Decision / Assumption", "Status"]);
  const headers = headerMap(values, headerRow);
  const stackRow = findRow(values, headerRow, headers, "Section", "Server stack");
  if (stackRow >= 0) {
    setByHeader(sheet, values, stackRow, headers, "Decision / Assumption", "Rust/uMod with Kits, ServerRewards, PlaytimeTracker, Backpacks, NTeleportation, SpawnHeli, CupboardLimiter, CopyPaste, SignArtist, BypassQueue, VehicleLicence, and WebsiteVipBridge");
    setByHeader(sheet, values, stackRow, headers, "Notes", "Plugin staging thread confirmed dependencies are present; smoke tests still gate final approval for newly staged perks.");
  }
  const portafortRow = findRow(values, headerRow, headers, "Section", "Portafort");
  if (portafortRow >= 0) {
    setByHeader(sheet, values, portafortRow, headers, "Decision / Assumption", "Implemented custom Portafort token using RaidlandsPortaforts.cs and CopyPaste");
    setByHeader(sheet, values, portafortRow, headers, "Status", "Verify");
    setByHeader(sheet, values, portafortRow, headers, "Notes", "Default CopyPaste file is raidlands_portafort; missing paste data or paste failure does not consume the token.");
  }
  const serumRow = findRow(values, headerRow, headers, "Section", "Super Serum");
  if (serumRow >= 0) {
    setByHeader(sheet, values, serumRow, headers, "Decision / Assumption", "Implemented Super Serum using RaidlandsConsumables.cs");
    setByHeader(sheet, values, serumRow, headers, "Status", "Verify");
    setByHeader(sheet, values, serumRow, headers, "Notes", "Default item is maxhealthtea.pure named Super Serum; active state persists across reconnect/restart and clears on death/new save.");
  }
  appendOrUpdateRow(sheet, values, headerRow, headers, "Section", "Plugin staging thread", {
    "Decision / Assumption": "CopyPaste, SignArtist, BypassQueue, and tuned VehicleLicence are staged in the Rust server tree",
    "Owner / Source": "codex://threads/019f295e-43b8-7430-863d-9752c2c35687",
    "Status": "Verify",
    "Notes": "Use Verify until /sil, queue bypass, and VehicleLicence smoke tests pass live.",
  });
}

function updateSources(workbook) {
  const sheet = getSheet(workbook, "Sources");
  const values = usedValues(sheet);
  const headerRow = findHeaderRow(values, ["Source", "URL / Path", "What it supports"]);
  const headers = headerMap(values, headerRow);
  appendOrUpdateRow(sheet, values, headerRow, headers, "Source", "Codex plugin staging thread", {
    "URL / Path": "codex://threads/019f295e-43b8-7430-863d-9752c2c35687",
    "What it supports": "Completed staging/config source for CopyPaste, SignArtist, BypassQueue, and tuned VehicleLicence.",
  });
  appendOrUpdateRow(sheet, values, headerRow, headers, "Source", "Codex custom token plugin implementation thread", {
    "URL / Path": "codex://threads/019f29cb-bd04-7180-a626-cf1f32e6a137",
    "What it supports": "Implemented RaidlandsPortaforts.cs, RaidlandsVehicleTokens.cs, RaidlandsConsumables.cs, matching configs, token item mapping, and live upload handoff.",
  });
}

async function renderAll(workbook, dir) {
  await fs.mkdir(dir, { recursive: true });
  const rendered = [];
  for (const sheetName of sheetNames) {
    const blob = await workbook.render({ sheetName, autoCrop: "all", scale: 1, format: "png" });
    const bytes = new Uint8Array(await blob.arrayBuffer());
    const file = `${dir}/${sheetName.replace(/[^a-z0-9]+/gi, "_")}.png`;
    await fs.writeFile(file, bytes);
    rendered.push({ sheetName, file, bytes: bytes.length });
  }
  return rendered;
}

async function verifyLocalTruth() {
  const requiredFiles = [
    "oxide/plugins/CopyPaste.cs",
    "oxide/plugins/SignArtist.cs",
    "oxide/plugins/BypassQueue.cs",
    "oxide/plugins/VehicleLicence.cs",
    "oxide/plugins/RaidlandsPortaforts.cs",
    "oxide/plugins/RaidlandsVehicleTokens.cs",
    "oxide/plugins/RaidlandsConsumables.cs",
    "oxide/config/CopyPaste.json",
    "oxide/config/SignArtist.json",
    "oxide/config/VehicleLicence.json",
    "oxide/config/WebsiteVipBridge.json",
    "oxide/config/RaidlandsPortaforts.json",
    "oxide/config/RaidlandsVehicleTokens.json",
    "oxide/config/RaidlandsConsumables.json",
    "Bundles/items/wrappedgift.json",
    "Bundles/items/maxhealthtea.pure.json",
  ];
  for (const rel of requiredFiles) {
    await fs.stat(path.join(rootDir, rel));
  }

  JSON.parse(await fs.readFile(path.join(rootDir, "oxide/config/WebsiteVipBridge.json"), "utf8"));
  const portafortConfig = JSON.parse(await fs.readFile(path.join(rootDir, "oxide/config/RaidlandsPortaforts.json"), "utf8"));
  const vehicleTokenConfig = JSON.parse(await fs.readFile(path.join(rootDir, "oxide/config/RaidlandsVehicleTokens.json"), "utf8"));
  const consumablesConfig = JSON.parse(await fs.readFile(path.join(rootDir, "oxide/config/RaidlandsConsumables.json"), "utf8"));
  if (portafortConfig?.["Token Item"]?.Shortname !== "wrappedgift") {
    throw new Error("RaidlandsPortaforts token shortname must be wrappedgift");
  }
  if (consumablesConfig?.["Super Serum Item"]?.Shortname !== "maxhealthtea.pure") {
    throw new Error("RaidlandsConsumables serum shortname must be maxhealthtea.pure");
  }
  const vehicleTokens = vehicleTokenConfig?.["Vehicle Tokens"] || [];
  if (vehicleTokens.length !== 9 || vehicleTokens.some((entry) => entry["Token Shortname"] !== "wrappedgift")) {
    throw new Error("RaidlandsVehicleTokens must define nine wrappedgift vehicle token items");
  }
  const vehicleConfig = JSON.parse(await fs.readFile(path.join(rootDir, "oxide/config/VehicleLicence.json"), "utf8"));
  const enabled = [];
  function walk(obj) {
    if (Array.isArray(obj)) {
      obj.forEach(walk);
    } else if (obj && typeof obj === "object") {
      if (Object.prototype.hasOwnProperty.call(obj, "Permission") && Object.prototype.hasOwnProperty.call(obj, "Commands")) {
        const commands = obj.Commands || [];
        if (commands.length) {
          enabled.push({ permission: obj.Permission, commands });
        }
      }
      Object.values(obj).forEach(walk);
    }
  }
  walk(vehicleConfig);
  const actual = enabled
    .map((entry) => `${entry.permission}:${entry.commands.join(",")}`)
    .sort();
  const expected = [
    "vehiclelicence.hotairballoon:hab,hotairballoon",
    "vehiclelicence.rhib:rhib",
    "vehiclelicence.snowmobile:snow,snowmobile",
    "vehiclelicence.submarineduo:subduo,duo",
    "vehiclelicence.submarinesolo:subsolo,solo",
    "vehiclelicence.tug:tugboat,tug",
  ].sort();
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new Error(`VehicleLicence enabled commands mismatch. Actual=${JSON.stringify(actual)}`);
  }
  return { requiredFiles: requiredFiles.length, vehicleEnabled: enabled.length };
}

async function inspectWorkbook(workbook, label) {
  const summary = await workbook.inspect({
    kind: "workbook,sheet,table",
    maxChars: 7000,
    tableMaxRows: 5,
    tableMaxCols: 8,
    tableMaxCellChars: 90,
    summary: `${label} compact workbook summary`,
  });
  console.log(`INSPECT ${label}`);
  console.log(summary.ndjson);

  const styles = await workbook.inspect({
    kind: "computedStyle",
    sheetId: "Perk Products",
    range: "A1:K8",
    maxChars: 2500,
    summary: `${label} style sample`,
  });
  console.log(`STYLE ${label}`);
  console.log(styles.ndjson);
}

async function verifyExportedWorkbook() {
  const blob = await FileBlob.load(outputPath);
  const workbook = await SpreadsheetFile.importXlsx(blob);

  const formulaErrors = await workbook.inspect({
    kind: "match",
    searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",
    options: { useRegex: true, maxResults: 300 },
    summary: "final formula error scan",
  });
  console.log("FORMULA_ERROR_SCAN");
  console.log(formulaErrors.ndjson);

  const staleTerms = [
    "queuepriority.use",
    "SignArtist not confirmed",
    "SignArtist not found",
    "still pending",
    "remains pending",
  ];
  for (const term of staleTerms) {
    const result = await workbook.inspect({
      kind: "match",
      searchTerm: term,
      options: { maxResults: 50 },
      summary: `stale term scan ${term}`,
      maxChars: 2000,
    });
    console.log(`STALE_TERM ${term}`);
    console.log(result.ndjson);
  }

  const vehicleTbd = await workbook.inspect({
    kind: "match",
    sheetId: "Vehicle Pack",
    searchTerm: "TBD",
    options: { maxResults: 50 },
    summary: "Vehicle Pack TBD scan",
    maxChars: 2000,
  });
  console.log("VEHICLE_TBD_SCAN");
  console.log(vehicleTbd.ndjson);

  return workbook;
}

async function main() {
  await fs.mkdir(outputDir, { recursive: true });
  const localTruth = await verifyLocalTruth();
  console.log("LOCAL_TRUTH", JSON.stringify(localTruth));

  const inputBlob = await FileBlob.load(inputPath);
  const workbook = await SpreadsheetFile.importXlsx(inputBlob);

  await inspectWorkbook(workbook, "before");
  const beforeRendered = await renderAll(workbook, beforePreviewDir);
  console.log("BEFORE_RENDERED", JSON.stringify(beforeRendered));

  replaceTextAcrossSheets(workbook);
  updateReadme(workbook);
  updateWebsiteProducts(workbook);
  updateServerGroups(workbook);
  updatePerkProducts(workbook);
  updateGroupPermissions(workbook);
  updateRankMatrix(workbook);
  updateVehiclePack(workbook);
  updateKitItemManifest(workbook);
  updateConfigChanges(workbook);
  updatePendingCustom(workbook);
  updateSources(workbook);

  const dashboardCheck = await workbook.inspect({
    kind: "table",
    sheetId: "Dashboard",
    range: "A1:H18",
    include: "values,formulas",
    tableMaxRows: 20,
    tableMaxCols: 8,
    maxChars: 5000,
    summary: "Dashboard after edit check",
  });
  console.log("DASHBOARD_CHECK");
  console.log(dashboardCheck.ndjson);

  await inspectWorkbook(workbook, "after");
  const afterRendered = await renderAll(workbook, afterPreviewDir);
  console.log("AFTER_RENDERED", JSON.stringify(afterRendered));

  const output = await SpreadsheetFile.exportXlsx(workbook);
  await output.save(outputPath);
  console.log("EXPORTED", outputPath);

  await verifyExportedWorkbook();
  console.log("DONE");
  process.exit(0);
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
