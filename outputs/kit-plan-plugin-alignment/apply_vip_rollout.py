import json
import re
from copy import deepcopy
from pathlib import Path


RUST_ROOT = Path(r"C:\wamp64\www\rust_server")
WEB_ROOT = Path(r"C:\wamp64\www\raidlands")
WORKBOOK_JSON = RUST_ROOT / "outputs" / "kit-plan-plugin-alignment" / "workbook-extract.json"

RANK_GROUPS = [
    "rank_vip",
    "rank_vip_plus",
    "rank_mvp",
    "rank_golden_vip",
    "rank_diamond_vip",
    "rank_ultimate_vip",
    "rank_titan_vip",
]

WEBSITE_TO_SERVER_GROUP = {
    "vip": "rank_vip",
    "vip_plus": "rank_vip_plus",
    "mvp": "rank_mvp",
    "golden_vip": "rank_golden_vip",
    "diamond_vip": "rank_diamond_vip",
    "ultimate_vip": "rank_ultimate_vip",
    "titan_vip": "rank_titan_vip",
}

LEGACY_ALIASES = {
    "vip_bronze": "rank_vip",
    "vip_gold": "rank_golden_vip",
    "vip_elite": "rank_ultimate_vip",
}

CLAIM_GROUP_BY_KIT = {
    "kit_claim_steam_name_rewards": "claim_steam_name",
    "kit_claim_steam_rewards": "claim_steam_group",
    "kit_claim_discord_booster": "claim_discord_booster",
    "kit_claim_discord_raid": "claim_discord_member",
    "kit_claim_discord": "claim_discord_member",
}

OLD_CLAIM_KITS = {
    "Build Kit",
    "Cards",
    "Raid Kit",
    "Medical",
    "comps",
    "scuba kit",
    "discord",
}

LEGACY_STORE_SLUGS = [
    "vip-bronze",
    "vip-gold",
    "vip-elite",
    "personal-mini",
    "skinbox-access",
    "raid-kit-unlock",
]

MISSING_PRODUCT_IDS = {
    "perk_spawn_full",
    "perk_shop_sale_25",
    "perk_shop_sale_50",
    "perk_shop_sale_75",
}

DEFERRED_PERMISSION_PREFIXES = (
    "raidlands.shop.sale.",
)

DEFERRED_PERMISSIONS = {
    "raidlands.spawn.full",
}

KIT_ICON_BY_ID = {
    "kit_claim_build": "https://raidlands.net/assets/media/kits/build-kit.png",
    "kit_claim_cards": "https://raidlands.net/assets/media/kits/cards-kit.png",
    "kit_claim_components": "https://raidlands.net/assets/media/kits/comps-kit.png",
    "kit_claim_medical": "https://raidlands.net/assets/media/kits/medical-kit.png",
    "kit_claim_raid": "https://raidlands.net/assets/media/kits/raid-kit.png",
    "kit_claim_scuba": "https://raidlands.net/assets/media/kits/scuba-kit.png",
    "pack_portafort": "https://raidlands.net/assets/media/kits/portafort-token.webp",
}

VEHICLE_TOKEN_NAMES = {
    "minicopter": "Minicopter Token",
    "scrap_transport_helicopter": "Scrap Transport Helicopter Token",
    "attack_helicopter": "Attack Helicopter Token",
    "rhib": "RHIB Token",
    "tugboat": "Tugboat Token",
    "solo_submarine": "Solo Submarine Token",
    "duo_submarine": "Duo Submarine Token",
    "snowmobile": "Snowmobile Token",
    "hot_air_balloon": "Hot Air Balloon Token",
}

ARMOR_SET = [
    "metal.facemask",
    "metal.plate.torso",
    "hoodie",
    "roadsign.kilt",
    "pants",
    "shoes.boots",
    "tactical.gloves",
]

WEAPON_AMMO_DEFAULTS = {
    "rifle.ak": (30, "ammo.rifle"),
    "rifle.lr300": (30, "ammo.rifle"),
    "smg.mp5": (30, "ammo.pistol"),
    "rifle.l96": (5, "ammo.rifle"),
    "lmg.m249": (100, "ammo.rifle"),
    "m16a2": (4, "ammo.rifle"),
    "multiplegrenadelauncher": (6, "ammo.grenadelauncher.he"),
    "rocket.launcher": (0, "ammo.rocket.basic"),
}

CHAT_GROUPS = {
    "vip": ("VIP", "#38D39F", 20),
    "vip_plus": ("VIP+", "#60A5FA", 25),
    "mvp": ("MVP", "#A78BFA", 30),
    "golden_vip": ("Golden VIP", "#FFD166", 35),
    "diamond_vip": ("Diamond VIP", "#67E8F9", 40),
    "ultimate_vip": ("Ultimate VIP", "#FB923C", 45),
    "titan_vip": ("Titan VIP", "#F87171", 50),
    "perk_chat_title": ("Supporter", "#FFD166", 15),
}


def rows_for(sheet, expected_first_header):
    raw = json.loads(WORKBOOK_JSON.read_text(encoding="utf-8"))[sheet]
    header_index = next(i for i, row in enumerate(raw) if row and row[0] == expected_first_header)
    headers = [str(value) if value is not None else "" for value in raw[header_index]]
    rows = []
    for row in raw[header_index + 1:]:
        if not row or row[0] in (None, ""):
            continue
        padded = row + [None] * (len(headers) - len(row))
        rows.append({headers[i]: padded[i] for i in range(len(headers))})
    return rows


def as_int(value, default=0):
    if value is None or value == "":
        return default
    if isinstance(value, (int, float)):
        return int(value)
    text = str(value).replace(",", "").replace("$", "").strip()
    if text == "":
        return default
    match = re.search(r"-?\d+", text)
    return int(match.group(0)) if match else default


def split_semicolon(value):
    if value is None:
        return []
    return [part.strip() for part in str(value).split(";") if part and part.strip()]


def slugify(value):
    return re.sub(r"[^a-z0-9-]+", "-", value.lower().replace("_", "-")).strip("-")


def sql_str(value):
    if value is None:
        return "NULL"
    return "'" + str(value).replace("\\", "\\\\").replace("'", "''") + "'"


def sql_int(value):
    return str(int(value))


def sql_json(value):
    if value is None:
        return "NULL"
    return "CAST(" + sql_str(json.dumps(value, separators=(",", ":"), ensure_ascii=False)) + " AS JSON)"


def php_scalar(value):
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, int):
        return str(value)
    return "'" + str(value).replace("\\", "\\\\").replace("'", "\\'") + "'"


def php_array(value, indent=0):
    pad = " " * indent
    child_pad = " " * (indent + 4)
    if isinstance(value, list):
        if not value:
            return "[]"
        lines = ["["]
        for item in value:
            lines.append(child_pad + php_array(item, indent + 4) + ",")
        lines.append(pad + "]")
        return "\n".join(lines)
    if isinstance(value, dict):
        if not value:
            return "[]"
        lines = ["["]
        for key, item in value.items():
            lines.append(child_pad + php_scalar(key) + " => " + php_array(item, indent + 4) + ",")
        lines.append(pad + "]")
        return "\n".join(lines)
    return php_scalar(value)


def load_item_defs():
    items = {}
    for path in (RUST_ROOT / "Bundles" / "items").glob("*.json"):
        data = json.loads(path.read_text(encoding="utf-8"))
        items[data["shortname"]] = data
    return items


def load_existing_item_defaults():
    data = json.loads((RUST_ROOT / "oxide" / "data" / "Kits" / "kits_data.json").read_text(encoding="utf-8"))
    defaults = {}
    for kit in data.get("_kits", {}).values():
        for container in ("MainItems", "WearItems", "BeltItems"):
            for item in kit.get(container, []):
                shortname = item.get("Shortname")
                if shortname and shortname not in defaults:
                    defaults[shortname] = item
    return defaults, data


def normalize_permission(group, permission):
    if not permission:
        return ""
    permission = str(permission).strip().lower()
    if permission == "raidlands.vehicle.hp.15":
        permission = "raidlands.vehicle.hp.150"
    if permission == "betterchat.group.<tier>":
        permission = "betterchat.group.perk_chat_title"
    if group == "rank_diamond_vip" and permission == "kits.vip":
        return "kits.vip.diamond"
    if group == "rank_diamond_vip" and permission == "kits.vipplus":
        return "kits.vipplus.diamond"
    if permission in DEFERRED_PERMISSIONS:
        return ""
    if any(permission.startswith(prefix) for prefix in DEFERRED_PERMISSION_PREFIXES):
        return ""
    return permission


def permission_plugin(permission, plugin_hint=""):
    prefix = permission.split(".", 1)[0]
    if plugin_hint and plugin_hint not in {"Website / ServerRewards custom", "Website / Shop custom"}:
        return plugin_hint.split()[0]
    return {
        "backpacks": "Backpacks",
        "betterchat": "BetterChat",
        "bypassqueue": "BypassQueue",
        "cupboardlimiter": "CupboardLimiter",
        "kits": "Kits",
        "nteleportation": "NTeleportation",
        "playtimetracker": "PlaytimeTracker",
        "raidlands": "Raidlands",
        "serverrewards": "ServerRewards",
        "signartist": "SignArtist",
        "spawnheli": "SpawnHeli",
    }.get(prefix, prefix)


def build_item(shortname, amount, item_defs, existing_defaults, display_name=None):
    if shortname not in item_defs:
        raise ValueError(f"Unknown Rust item shortname: {shortname}")
    definition = item_defs[shortname]
    condition = definition.get("condition", {})
    max_condition = float(condition.get("max") or 0) if condition.get("enabled") else 0.0
    default = existing_defaults.get(shortname, {})
    ammo, ammo_type = WEAPON_AMMO_DEFAULTS.get(
        shortname,
        (int(default.get("Ammo") or 0), default.get("Ammotype")),
    )
    return {
        "Shortname": shortname,
        "DisplayName": display_name,
        "Skin": int(default.get("Skin") or 0),
        "Amount": int(amount),
        "Condition": float(default.get("Condition") if default.get("Condition") not in (None, 0, 0.0) else max_condition),
        "MaxCondition": float(default.get("MaxCondition") if default.get("MaxCondition") not in (None, 0, 0.0) else max_condition),
        "Ammo": ammo,
        "Ammotype": ammo_type,
        "Position": 0,
        "Frequency": -1,
        "BlueprintShortname": None,
        "Text": None,
        "Contents": None,
        "Container": None,
    }


def normalize_manifest_item(row, item_defs, existing_defaults):
    container = str(row["Container"]).strip().lower()
    if container not in {"main", "wear", "belt"}:
        container = "main"
    key = str(row["Shortname / Key"]).strip()
    amount = max(1, as_int(row["Live Count 1,000x Weekly"], 1))

    if key == "metal/roadsign/clothes set":
        return "wear", [build_item(shortname, 1, item_defs, existing_defaults) for shortname in ARMOR_SET]
    if key == "custom.weapon.m16a2":
        return container, [build_item("m16a2", amount, item_defs, existing_defaults)]
    if key == "custom.sentry":
        return container, [build_item("autoturret", amount, item_defs, existing_defaults)]
    if key == "wall.external.high.wood":
        return container, [build_item("wall.external.high", amount, item_defs, existing_defaults)]
    if key == "wrappedgift / Portafort Token":
        return container, [build_item("grenade.smoke", amount, item_defs, existing_defaults, "Portafort Token")]
    if key == "maxhealthtea.pure / Super Serum":
        return container, [build_item("maxhealthtea.pure", amount, item_defs, existing_defaults, "Super Serum")]
    if key in VEHICLE_TOKEN_NAMES:
        return container, [build_item("wrappedgift", amount, item_defs, existing_defaults, VEHICLE_TOKEN_NAMES[key])]
    return container, [build_item(key, amount, item_defs, existing_defaults)]


def build_kits(kit_rows, manifest_rows, item_defs, existing_defaults, existing_kits_data):
    manifest_by_kit = {}
    for row in manifest_rows:
        kit_id = str(row["Kit / Pack ID"]).strip()
        container, items = normalize_manifest_item(row, item_defs, existing_defaults)
        manifest_by_kit.setdefault(kit_id, {"main": [], "wear": [], "belt": []})
        manifest_by_kit[kit_id][container].extend(items)

    kits = {}
    for index, row in enumerate(kit_rows, start=1):
        kit_id = str(row["Kit / Pack ID"]).strip()
        containers = deepcopy(manifest_by_kit.get(kit_id, {"main": [], "wear": [], "belt": []}))
        while len(containers["belt"]) > 6:
            containers["main"].append(containers["belt"].pop())
        for container_name, items in containers.items():
            for pos, item in enumerate(items):
                item["Position"] = pos
        kits[kit_id] = {
            "Name": kit_id,
            "Description": str(row["Notes"] or row["Display Name"] or kit_id),
            "RequiredPermission": str(row["Required Permission"] or "").strip().lower(),
            "MaximumUses": as_int(row["Max Uses / Wipe"], 0),
            "RequiredAuth": 0,
            "Cooldown": as_int(row["Cooldown Sec"], 0),
            "Cost": 0,
            "IsHidden": False,
            "CopyPasteFile": "",
            "KitImage": KIT_ICON_BY_ID.get(kit_id, ""),
            "MainItems": containers["main"],
            "WearItems": containers["wear"],
            "BeltItems": containers["belt"],
            "_sort": 200 + index * 10,
            "_display": str(row["Display Name"] or kit_id),
            "_rp": as_int(row["RP Redeem Price"], 0),
            "_status": str(row["Status"] or ""),
            "_type": str(row["Type"] or ""),
        }

    for source, alias, permission in [
        ("kit_vip", "kit_vip_diamond", "kits.vip.diamond"),
        ("kit_vip_plus", "kit_vip_plus_diamond", "kits.vipplus.diamond"),
    ]:
        clone = deepcopy(kits[source])
        clone["Name"] = alias
        clone["Description"] = clone["Description"] + " Diamond cooldown alias."
        clone["RequiredPermission"] = permission
        clone["Cooldown"] = 18000
        clone["IsHidden"] = False
        clone["_display"] = clone["_display"] + " (Diamond)"
        clone["_rp"] = 0
        clone["_sort"] = kits[source]["_sort"] + 1
        kits[alias] = clone

    preserved = {}
    for name, kit in existing_kits_data.get("_kits", {}).items():
        if name in OLD_CLAIM_KITS:
            continue
        if name.startswith("raidlands_pvp_") or name in {"Starter Kit", "autokit"}:
            preserved[name] = kit

    new_kits = {}
    for name, kit in preserved.items():
        new_kits[name] = kit
    for name, kit in sorted(kits.items(), key=lambda pair: (pair[1]["_sort"], pair[0])):
        public = {key: value for key, value in kit.items() if not key.startswith("_")}
        new_kits[name] = public

    return kits, {"_kits": new_kits}


def group_access_for_kits(kits, kit_rows):
    access = {group: set() for group in ["default", "discord", *RANK_GROUPS, *LEGACY_ALIASES.keys(), *CLAIM_GROUP_BY_KIT.values()]}
    for row in kit_rows:
        kit_id = str(row["Kit / Pack ID"]).strip()
        permission = kits[kit_id]["RequiredPermission"]
        included = str(row["Included In Website Groups"] or "").strip()
        if not permission:
            continue
        if kit_id in CLAIM_GROUP_BY_KIT:
            access[CLAIM_GROUP_BY_KIT[kit_id]].add(permission)
        elif "default / all players" in included:
            access["default"].add(permission)
        elif included == "all rank packages":
            for group in RANK_GROUPS:
                access[group].add(permission)
        else:
            for website_group in split_semicolon(included):
                server_group = WEBSITE_TO_SERVER_GROUP.get(website_group)
                if server_group and not (server_group == "rank_diamond_vip" and permission in {"kits.vip", "kits.vipplus"}):
                    access[server_group].add(permission)

    access["rank_diamond_vip"].update({"kits.vip.diamond", "kits.vipplus.diamond"})
    for legacy, target in LEGACY_ALIASES.items():
        access[legacy].update(access[target])
    return access


def build_groups_and_permissions(group_rows, permission_rows, kit_access):
    groups = {}
    for index, row in enumerate(group_rows, start=1):
        group = str(row["Server Group"]).strip()
        if group == "":
            continue
        group_type = str(row["Group Type"] or "custom").lower()
        category = "rank" if "rank" in group_type else "perk" if "perk" in group_type else "claim" if "claim" in group_type else "legacy" if "legacy" in group_type else "custom"
        if group in {"default", "discord"}:
            category = "public"
        groups[group] = {
            "group_name": group,
            "title": group,
            "rank": as_int(row["Rank Order"], 0),
            "parent": "",
            "category": category,
            "managed": 1 if str(row["Managed by Bridge"] or "").lower() in {"yes", "conditional", "previously"} else 0,
            "protected": 1 if group in {"default", "discord"} else 0,
            "read_only": 0,
            "active": 1,
            "sort": 100 + index * 10,
            "notes": str(row["Notes"] or ""),
        }

    for group in ["default", "discord"]:
        groups.setdefault(group, {
            "group_name": group,
            "title": group,
            "rank": 0,
            "parent": "",
            "category": "public",
            "managed": 1,
            "protected": 1,
            "read_only": 0,
            "active": 1,
            "sort": 10 if group == "default" else 20,
            "notes": "",
        })

    grants = {group: set(perms) for group, perms in kit_access.items()}
    permission_meta = {}
    for row in permission_rows:
        group = str(row["Server Group"] or "").strip()
        permission = normalize_permission(group, row["Permission / Config Key"])
        if not group or not permission:
            continue
        groups.setdefault(group, {
            "group_name": group,
            "title": group,
            "rank": 0,
            "parent": "",
            "category": "custom",
            "managed": 1,
            "protected": 0,
            "read_only": 0,
            "active": 1,
            "sort": 500,
            "notes": "",
        })
        grants.setdefault(group, set()).add(permission)
        prefix = permission.split(".", 1)[0]
        permission_meta[permission] = {
            "permission": permission,
            "plugin": permission_plugin(permission, str(row["Plugin / System"] or "")),
            "prefix": prefix,
            "source": "workbook",
        }

    for group, permissions in kit_access.items():
        for permission in permissions:
            prefix = permission.split(".", 1)[0]
            permission_meta.setdefault(permission, {
                "permission": permission,
                "plugin": permission_plugin(permission),
                "prefix": prefix,
                "source": "workbook",
            })

    for legacy, target in LEGACY_ALIASES.items():
        grants[legacy] = set(grants.get(target, set()))

    return groups, grants, permission_meta


def build_products(product_rows, perk_rows, kits, grants):
    perk_primary = {}
    for row in perk_rows:
        group = str(row["Perk Group"] or "").strip()
        permission = normalize_permission(group, row["Primary Permission / Config Key"])
        if group and permission:
            perk_primary[group] = permission

    products = []
    product_kits = {}
    product_permission_grants = {}
    for index, row in enumerate(product_rows, start=1):
        product_id = str(row["Product ID"]).strip()
        product_type = str(row["Type"] or "").strip()
        slug = slugify(product_id)
        oxide_group = str(row["Server Group Granted"] or "").strip()
        active = 0 if product_id in MISSING_PRODUCT_IDS else 1
        rp_price = as_int(row["RP Price"], 0)
        access_interval = "one_time" if "Redeem" in product_type else "week"
        duration = 0 if access_interval == "one_time" else 604800
        allow_auto_renew = 1 if product_type == "Rank" else 0
        db_type = "kit_bundle" if product_type == "Rank" else "kit_unlock" if "Redeem" in product_type else "perk"
        products.append({
            "id": product_id,
            "slug": slug,
            "name": str(row["Display Name"] or product_id),
            "product_type": db_type,
            "short_description": str(row["Notes"] or ""),
            "description": str(row["Notes"] or ""),
            "oxide_group": oxide_group,
            "tier_priority": index * 10 if product_type == "Rank" else 0,
            "is_stackable": 0 if product_type in {"Rank", "Perk"} else 1,
            "is_active": active,
            "is_featured": 1 if product_type == "Rank" else 0,
            "sort_order": index * 10,
            "rp_price": rp_price,
            "access_interval": access_interval,
            "access_duration_seconds": duration,
            "allow_auto_renew": allow_auto_renew,
            "cash_anchor": str(row["Cash Anchor"] or ""),
        })

        kit_ids = []
        for kit_id in split_semicolon(row["Kit/Pack Bundle"]):
            if product_id == "rank_diamond_vip" and kit_id == "kit_vip":
                kit_ids.append("kit_vip_diamond")
            elif product_id == "rank_diamond_vip" and kit_id == "kit_vip_plus":
                kit_ids.append("kit_vip_plus_diamond")
            else:
                kit_ids.append(kit_id)
        if kit_ids:
            product_kits[product_id] = kit_ids

        permission_list = []
        if oxide_group:
            for permission in sorted(grants.get(oxide_group, set())):
                if not permission.startswith("kits."):
                    permission_list.append(permission)
        if product_id in perk_primary:
            permission_list.append(perk_primary[product_id])
        product_permission_grants[product_id] = sorted(set(permission_list))

    return products, product_kits, product_permission_grants


def write_json(path, data):
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def update_server_rewards(products, product_kits, kits):
    path = RUST_ROOT / "oxide" / "data" / "ServerRewards" / "products.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    reward_products = [p for p in products if p["product_type"] == "kit_unlock" and p["is_active"] and product_kits.get(p["id"])]
    managed_kit_names = {product_kits[p["id"]][0] for p in reward_products}
    existing_by_name = {kit.get("KitName"): kit for kit in data.get("Kits", []) if kit.get("KitName") in managed_kit_names}
    existing_kits = [kit for kit in data.get("Kits", []) if kit.get("KitName") not in managed_kit_names]
    next_id = max(as_int(data.get("ProductIndex"), 0), max([as_int(k.get("ID"), 0) for k in data.get("Kits", [])] + [0]) + 1)
    new_rows = []
    for product in reward_products:
        kit_id = product_kits[product["id"]][0]
        kit = kits[kit_id]
        existing_id = as_int(existing_by_name.get(kit_id, {}).get("ID"), -1)
        product_id = existing_id if existing_id >= 0 else next_id
        new_rows.append({
            "KitName": kit_id,
            "Description": kit["Description"],
            "ID": product_id,
            "DisplayName": product["name"],
            "Cost": product["rp_price"],
            "Cooldown": 0,
            "IconURL": kit.get("KitImage") or None,
            "Permission": None,
        })
        next_id = max(next_id, product_id + 1)
    data["Kits"] = existing_kits + new_rows
    data["Commands"] = data.get("Commands", [])
    data["ProductIndex"] = next_id
    write_json(path, data)
    return {row["KitName"]: row["ID"] for row in new_rows}


def update_configs(playtime_rows, groups):
    playtime_path = RUST_ROOT / "oxide" / "config" / "PlaytimeTracker.json"
    playtime = json.loads(playtime_path.read_text(encoding="utf-8"))
    playtime["Reward Options"]["Playtime rewards"]["Reward interval (seconds)"] = 300
    playtime["Reward Options"]["Playtime rewards"]["Reward amount"] = 50
    multipliers = {}
    for row in playtime_rows:
        permission = str(row["Playtime Permission"] or "").strip()
        if permission:
            multipliers[permission] = float(row["Multiplier"])
    playtime["Reward Options"]["Custom reward multipliers (permission / multiplier)"] = multipliers
    write_json(playtime_path, playtime)

    kits_config_path = RUST_ROOT / "oxide" / "config" / "Kits.json"
    kits_config = json.loads(kits_config_path.read_text(encoding="utf-8"))
    kits_config["Wipe player data when the server is wiped"] = True
    kits_config["Post wipe cooldowns (kit name | seconds)"] = {}
    write_json(kits_config_path, kits_config)

    cupboard_path = RUST_ROOT / "oxide" / "config" / "CupboardLimiter.json"
    cupboard = json.loads(cupboard_path.read_text(encoding="utf-8"))
    cupboard["Max amount of TC(s) to place"]["Limit Default"] = 8
    cupboard["Max amount of TC(s) to place"]["Limit Vip"] = 8
    cupboard["Max amount of TC(s) to place"]["Limit Others"] = [12]
    write_json(cupboard_path, cupboard)

    spawn_path = RUST_ROOT / "oxide" / "config" / "SpawnHeli.json"
    spawn = json.loads(spawn_path.read_text(encoding="utf-8"))
    spawn["Minicopter"]["Instant takeoff"]["Enabled"] = True
    spawn["Minicopter"]["Instant takeoff"]["Require permission"] = True
    write_json(spawn_path, spawn)

    backpack_path = RUST_ROOT / "oxide" / "config" / "Backpacks.json"
    backpack = json.loads(backpack_path.read_text(encoding="utf-8"))
    sizes = set(backpack["Backpack size"]["Permission sizes"])
    sizes.update([36, 42, 48])
    backpack["Backpack size"]["Permission sizes"] = sorted(sizes)
    backpack["Drop on Death (true/false)"] = True
    backpack["Erase on Death (true/false)"] = False
    backpack["Clear on wipe"]["Enabled"] = True
    backpack["Clear on wipe"]["Enable legacy keeponwipe permission"] = False
    backpack["Clear on wipe"]["Default ruleset"]["Max slots to keep"] = 0
    backpack["Clear on wipe"]["Rulesets by permission"] = [{
        "Name": "all",
        "Max slots to keep": -1,
        "Allowed item categories": ["All"],
        "Disallowed item categories": [],
        "Allowed item short names": [],
        "Disallowed item short names": [],
        "Allowed skin IDs": [],
        "Disallowed skin IDs": [],
    }]
    write_json(backpack_path, backpack)

    ntele_path = RUST_ROOT / "oxide" / "config" / "NTeleportation.json"
    ntele = json.loads(ntele_path.read_text(encoding="utf-8"))
    home = ntele["Home"]
    home.setdefault("VIP Cooldowns", {})["nteleportation.home.5s"] = 5
    home.setdefault("VIP Countdowns", {})["nteleportation.home.5s"] = 5
    instant = "nteleportation.instant"
    ntele["Settings"]["TPB"].setdefault("VIP Countdowns", {})[instant] = 0
    for section in [ntele["TPR"], *ntele["Dynamic Commands"].values()]:
        section.setdefault("VIP Cooldowns", {})[instant] = 0
        section.setdefault("VIP Countdowns", {})[instant] = 0
    write_json(ntele_path, ntele)

    better_path = RUST_ROOT / "oxide" / "data" / "BetterChat.json"
    existing = json.loads(better_path.read_text(encoding="utf-8"))
    by_name = {row["GroupName"]: row for row in existing}
    for group_name, (title, color, priority) in CHAT_GROUPS.items():
        by_name[group_name] = {
            "GroupName": group_name,
            "Priority": priority,
            "Title": {
                "Text": title,
                "Color": color,
                "Size": 15,
                "Hidden": False,
                "HiddenIfNotPrimary": False,
            },
            "Username": {
                "Color": color,
                "Size": 15,
            },
            "Message": {
                "Color": "white",
                "Size": 15,
            },
            "Format": {
                "Chat": "{Title} {Username}: {Message}",
                "Console": "{Title} {Username}: {Message}",
            },
        }
    write_json(better_path, sorted(by_name.values(), key=lambda row: (row["Priority"], row["GroupName"])))

    port_path = RUST_ROOT / "oxide" / "config" / "RaidlandsPortaforts.json"
    port = json.loads(port_path.read_text(encoding="utf-8"))
    port["Kit Grants"] = {k: v for k, v in port.get("Kit Grants", {}).items() if k != "pack_portafort"}
    write_json(port_path, port)

    vehicle_path = RUST_ROOT / "oxide" / "config" / "RaidlandsVehicleTokens.json"
    vehicle = json.loads(vehicle_path.read_text(encoding="utf-8"))
    vehicle["Kit Grants"] = {k: v for k, v in vehicle.get("Kit Grants", {}).items() if k != "pack_vehicle"}
    write_json(vehicle_path, vehicle)

    consumable_path = RUST_ROOT / "oxide" / "config" / "RaidlandsConsumables.json"
    consumable = json.loads(consumable_path.read_text(encoding="utf-8"))
    consumable["Kit Grants"] = {
        k: v for k, v in consumable.get("Kit Grants", {}).items()
        if k not in {"pack_super_serum", "kit_titan_vip"}
    }
    write_json(consumable_path, consumable)

    managed = [
        *RANK_GROUPS,
        "perk_queue_priority",
        "perk_teleport_instant",
        "perk_home_5s",
        "perk_sign_art",
        "perk_chat_title",
        "perk_backpack_36",
        "perk_backpack_42",
        "perk_backpack_48",
        "perk_backpack_keep_death",
        "perk_backpack_keep_wipe",
        "perk_spawn_full",
        "perk_vehicle_hp_125",
        "perk_vehicle_hp_150",
        "perk_tc_12",
        "perk_minicopter_instant_takeoff",
        "perk_shop_sale_25",
        "perk_shop_sale_50",
        "perk_shop_sale_75",
        *LEGACY_ALIASES.keys(),
        "claim_steam_name",
        "claim_steam_group",
        "claim_discord_member",
        "claim_discord_booster",
    ]
    kit_managed = [
        "default",
        "discord",
        *RANK_GROUPS,
        *LEGACY_ALIASES.keys(),
        "claim_steam_name",
        "claim_steam_group",
        "claim_discord_member",
        "claim_discord_booster",
    ]
    bridge_path = RUST_ROOT / "oxide" / "config" / "WebsiteVipBridge.json"
    bridge = json.loads(bridge_path.read_text(encoding="utf-8"))
    bridge["ManagedGroups"] = managed
    bridge["KitPermissionManagedGroups"] = kit_managed
    bridge["KitPermissionPrefixes"] = ["kits.", "serverrewards."]
    write_json(bridge_path, bridge)


def values_clause(rows):
    return ",\n".join("  (" + ", ".join(row) + ")" for row in rows)


def generate_sql(groups, grants, permissions, kits, kit_access, products, product_kits, product_permission_grants, reward_ids):
    lines = [
        "-- Raidlands VIP kits, groups, permissions, and product seed.",
        "-- Generated from raidlands_vip_kits_permissions_mapping_with_claimables_plugin_aligned.xlsx.",
        "",
        "SET @rollout_revision := UNIX_TIMESTAMP();",
        "",
    ]

    group_rows = []
    for group in sorted(groups.values(), key=lambda row: (row["sort"], row["group_name"])):
        group_rows.append([
            sql_str(group["group_name"]), sql_str(group["title"]), sql_int(group["rank"]), sql_str(group["parent"]),
            sql_str(group["category"]), sql_int(group["managed"]), sql_int(group["protected"]), sql_int(group["read_only"]),
            sql_int(group["active"]), sql_int(group["sort"]), sql_str(group["notes"]),
        ])
    lines += [
        "INSERT INTO oxide_groups",
        "  (group_name, title, group_rank, parent_group, category, is_managed, is_protected, is_read_only, is_active, sort_order, notes, draft_revision, published_revision, published_at, deleted_at, deleted_revision)",
        "VALUES",
        values_clause([row + ["@rollout_revision", "@rollout_revision", "NOW()", "NULL", "0"] for row in group_rows]),
        "ON DUPLICATE KEY UPDATE",
        "  title = VALUES(title), group_rank = VALUES(group_rank), parent_group = VALUES(parent_group),",
        "  category = VALUES(category), is_managed = VALUES(is_managed), is_protected = VALUES(is_protected),",
        "  is_read_only = VALUES(is_read_only), is_active = VALUES(is_active), sort_order = VALUES(sort_order),",
        "  notes = VALUES(notes), draft_revision = VALUES(draft_revision), published_revision = VALUES(published_revision),",
        "  published_at = NOW(), deleted_at = NULL, deleted_revision = 0, updated_at = NOW();",
        "",
    ]

    permission_rows = []
    for permission in sorted(permissions.values(), key=lambda row: row["permission"]):
        permission_rows.append([
            sql_str(permission["permission"]), sql_str(permission["plugin"]), sql_str(permission["prefix"]),
            sql_str(permission["source"]), "1", "NOW()",
        ])
    lines += [
        "INSERT INTO oxide_permissions",
        "  (permission_name, plugin_name, permission_prefix, source, is_active, last_seen_at)",
        "VALUES",
        values_clause(permission_rows),
        "ON DUPLICATE KEY UPDATE",
        "  plugin_name = VALUES(plugin_name), permission_prefix = VALUES(permission_prefix),",
        "  source = IF(source = '', VALUES(source), source), is_active = 1, last_seen_at = NOW(), updated_at = NOW();",
        "",
    ]

    managed_group_names = sorted(grants.keys())
    lines += [
        "DELETE ogpg FROM oxide_group_permission_grants ogpg",
        "INNER JOIN oxide_groups og ON og.id = ogpg.group_id",
        "WHERE og.group_name IN (" + ", ".join(sql_str(group) for group in managed_group_names) + ");",
        "",
    ]
    grant_selects = []
    for group, perms in sorted(grants.items()):
        for permission in sorted(perms):
            grant_selects.append(
                f"SELECT og.id, op.id, 'workbook' FROM oxide_groups og INNER JOIN oxide_permissions op ON op.permission_name = {sql_str(permission)} WHERE og.group_name = {sql_str(group)}"
            )
    lines += [
        "INSERT INTO oxide_group_permission_grants (group_id, permission_id, source)",
        "\nUNION ALL\n".join(grant_selects) + ";",
        "",
    ]

    kit_rows = []
    for kit in sorted(kits.values(), key=lambda row: (row["_sort"], row["Name"])):
        reward_id = reward_ids.get(kit["Name"], -1)
        reward_enabled = 1 if reward_id >= 0 else 0
        reward_cost = kit.get("_rp", 0) if reward_enabled else 0
        reward_name = kit.get("_display", kit["Name"]) if reward_enabled else ""
        kit_rows.append([
            sql_str(kit["Name"]), sql_str(""), sql_str(kit["Description"]), sql_str(kit["RequiredPermission"]),
            sql_int(kit["MaximumUses"]), "0", sql_int(kit["Cooldown"]), "0", "1" if kit["IsHidden"] else "0",
            sql_str(kit["CopyPasteFile"]), sql_str(kit["KitImage"]), "1", sql_int(kit["_sort"]),
            sql_int(reward_enabled), sql_int(reward_id), sql_str(reward_name), sql_str(kit["Description"] if reward_enabled else ""),
            sql_int(reward_cost), "0", sql_str(kit["KitImage"] if reward_enabled else ""), sql_str(""),
            "@rollout_revision", "@rollout_revision", "NOW()", "NULL", "0",
        ])
    lines += [
        "INSERT INTO game_kits",
        "  (kit_name, previous_kit_name, description, required_permission, maximum_uses, required_auth, cooldown_seconds, cost, is_hidden, copy_paste_file, image_path, is_active, sort_order, reward_enabled, reward_product_id, reward_display_name, reward_description, reward_cost, reward_cooldown, reward_icon_url, reward_permission, draft_revision, published_revision, published_at, deleted_at, deleted_revision)",
        "VALUES",
        values_clause(kit_rows),
        "ON DUPLICATE KEY UPDATE",
        "  previous_kit_name = VALUES(previous_kit_name), description = VALUES(description), required_permission = VALUES(required_permission),",
        "  maximum_uses = VALUES(maximum_uses), required_auth = VALUES(required_auth), cooldown_seconds = VALUES(cooldown_seconds),",
        "  cost = VALUES(cost), is_hidden = VALUES(is_hidden), copy_paste_file = VALUES(copy_paste_file), image_path = VALUES(image_path),",
        "  is_active = VALUES(is_active), sort_order = VALUES(sort_order), reward_enabled = VALUES(reward_enabled),",
        "  reward_product_id = VALUES(reward_product_id), reward_display_name = VALUES(reward_display_name),",
        "  reward_description = VALUES(reward_description), reward_cost = VALUES(reward_cost), reward_cooldown = VALUES(reward_cooldown),",
        "  reward_icon_url = VALUES(reward_icon_url), reward_permission = VALUES(reward_permission),",
        "  draft_revision = VALUES(draft_revision), published_revision = VALUES(published_revision), published_at = NOW(),",
        "  deleted_at = NULL, deleted_revision = 0, updated_at = NOW();",
        "",
        "UPDATE game_kits SET is_active = 0, deleted_at = NOW(), deleted_revision = @rollout_revision, updated_at = NOW()",
        "WHERE kit_name IN (" + ", ".join(sql_str(name) for name in sorted(OLD_CLAIM_KITS)) + ");",
        "",
        "DELETE gki FROM game_kit_items gki",
        "INNER JOIN game_kits gk ON gk.id = gki.kit_id",
        "WHERE gk.kit_name IN (" + ", ".join(sql_str(name) for name in sorted(kits.keys())) + ");",
        "",
    ]

    item_selects = []
    for kit in sorted(kits.values(), key=lambda row: (row["_sort"], row["Name"])):
        for source_name, container_name in [("MainItems", "main"), ("WearItems", "wear"), ("BeltItems", "belt")]:
            for sort_order, item in enumerate(kit[source_name], start=1):
                item_selects.append(
                    "SELECT gk.id, "
                    + ", ".join([
                        sql_str(container_name),
                        sql_int(item["Position"]),
                        sql_str(item["Shortname"]),
                        sql_str(item["DisplayName"]) if item["DisplayName"] else "NULL",
                        sql_int(item["Skin"]),
                        sql_int(item["Amount"]),
                        str(float(item["Condition"])),
                        str(float(item["MaxCondition"])),
                        sql_int(item["Ammo"]),
                        sql_str(item["Ammotype"]) if item["Ammotype"] else "NULL",
                        sql_int(item["Frequency"]),
                        "NULL",
                        "NULL",
                        sql_json(item["Contents"]),
                        sql_json(item["Container"]),
                        sql_int(sort_order),
                    ])
                    + f" FROM game_kits gk WHERE gk.kit_name = {sql_str(kit['Name'])}"
                )
    lines += [
        "INSERT INTO game_kit_items",
        "  (kit_id, container_name, position, shortname, display_name, skin, amount, condition_value, max_condition, ammo, ammo_type, frequency, blueprint_shortname, text_value, contents_json, container_json, sort_order)",
        "\nUNION ALL\n".join(item_selects) + ";",
        "",
        "DELETE gkga FROM game_kit_group_access gkga",
        "INNER JOIN game_kits gk ON gk.id = gkga.kit_id",
        "WHERE gk.kit_name IN (" + ", ".join(sql_str(name) for name in sorted(kits.keys())) + ");",
        "",
    ]

    group_access_selects = []
    perm_to_kits = {kit["RequiredPermission"]: kit["Name"] for kit in kits.values() if kit["RequiredPermission"]}
    for group, perms in sorted(kit_access.items()):
        for permission in sorted(perms):
            kit_name = perm_to_kits.get(permission)
            if kit_name:
                group_access_selects.append(
                    f"SELECT gk.id, {sql_str(group)}, 1 FROM game_kits gk WHERE gk.kit_name = {sql_str(kit_name)}"
                )
    lines += [
        "INSERT INTO game_kit_group_access (kit_id, oxide_group, is_granted)",
        "\nUNION ALL\n".join(group_access_selects) + ";",
        "",
    ]

    lines += [
        "UPDATE store_products SET is_active = 0, updated_at = NOW()",
        "WHERE slug IN (" + ", ".join(sql_str(slug) for slug in LEGACY_STORE_SLUGS) + ");",
        "",
    ]
    product_rows_sql = []
    for product in products:
        product_rows_sql.append([
            sql_str(product["slug"]), sql_str(product["name"]), sql_str(product["product_type"]),
            sql_str(product["short_description"][:255]), sql_str(product["description"]), sql_str(product["oxide_group"]),
            sql_int(product["tier_priority"]), sql_int(product["is_stackable"]), sql_int(product["is_active"]),
            sql_int(product["is_featured"]), sql_int(product["sort_order"]),
        ])
    lines += [
        "INSERT INTO store_products",
        "  (slug, name, product_type, short_description, description, oxide_group, tier_priority, is_stackable, is_active, is_featured, sort_order)",
        "VALUES",
        values_clause(product_rows_sql),
        "ON DUPLICATE KEY UPDATE",
        "  name = VALUES(name), product_type = VALUES(product_type), short_description = VALUES(short_description),",
        "  description = VALUES(description), oxide_group = VALUES(oxide_group), tier_priority = VALUES(tier_priority),",
        "  is_stackable = VALUES(is_stackable), is_active = VALUES(is_active), is_featured = VALUES(is_featured),",
        "  sort_order = VALUES(sort_order), updated_at = NOW();",
        "",
    ]

    price_selects = []
    for product in products:
        if product["rp_price"] <= 0:
            continue
        price_selects.append(
            "SELECT p.id, 'rp', "
            + ", ".join([
                sql_str(f"rp_{product['slug']}_{product['access_interval']}"),
                sql_str("RP " + product["access_interval"].replace("_", " ").title()),
                "0",
                sql_str("usd"),
                sql_int(product["rp_price"]),
                sql_str("one_time"),
                sql_str(product["access_interval"]),
                sql_int(product["access_duration_seconds"]),
                sql_int(product["allow_auto_renew"]),
                sql_int(product["is_active"]),
                "1",
            ])
            + f" FROM store_products p WHERE p.slug = {sql_str(product['slug'])}"
        )
    lines += [
        "INSERT INTO store_prices",
        "  (product_id, payment_method, stripe_price_id, label, amount_cents, currency, rp_cost, billing_interval, access_interval, access_duration_seconds, allow_auto_renew, is_active, is_default)",
        "\nUNION ALL\n".join(price_selects),
        "ON DUPLICATE KEY UPDATE",
        "  product_id = VALUES(product_id), payment_method = VALUES(payment_method), label = VALUES(label),",
        "  amount_cents = VALUES(amount_cents), currency = VALUES(currency), rp_cost = VALUES(rp_cost),",
        "  billing_interval = VALUES(billing_interval), access_interval = VALUES(access_interval),",
        "  access_duration_seconds = VALUES(access_duration_seconds), allow_auto_renew = VALUES(allow_auto_renew),",
        "  is_active = VALUES(is_active), is_default = VALUES(is_default), updated_at = NOW();",
        "",
    ]

    product_slugs = [product["slug"] for product in products]
    lines += [
        "DELETE pfa FROM product_fulfillment_actions pfa",
        "INNER JOIN store_products p ON p.id = pfa.product_id",
        "WHERE p.slug IN (" + ", ".join(sql_str(slug) for slug in product_slugs) + ") AND pfa.action_type = 'grant_group';",
        "",
    ]
    fulfillment_selects = [
        f"SELECT p.id, 'grant_group', {sql_str(product['oxide_group'])}, 10 FROM store_products p WHERE p.slug = {sql_str(product['slug'])}"
        for product in products if product["oxide_group"]
    ]
    lines += [
        "INSERT INTO product_fulfillment_actions (product_id, action_type, oxide_group, sort_order)",
        "\nUNION ALL\n".join(fulfillment_selects) + ";",
        "",
        "DELETE spk FROM store_product_kits spk",
        "INNER JOIN store_products p ON p.id = spk.product_id",
        "WHERE p.slug IN (" + ", ".join(sql_str(slug) for slug in product_slugs) + ");",
        "",
    ]

    product_kit_selects = []
    slug_by_id = {product["id"]: product["slug"] for product in products}
    for product_id, kit_ids in product_kits.items():
        for sort_order, kit_id in enumerate(kit_ids, start=1):
            product_kit_selects.append(
                f"SELECT p.id, gk.id, {sort_order * 10} FROM store_products p INNER JOIN game_kits gk ON gk.kit_name = {sql_str(kit_id)} WHERE p.slug = {sql_str(slug_by_id[product_id])}"
            )
    lines += [
        "INSERT INTO store_product_kits (product_id, kit_id, sort_order)",
        "\nUNION ALL\n".join(product_kit_selects) + ";",
        "",
        "DELETE spg FROM store_product_permission_grants spg",
        "INNER JOIN store_products p ON p.id = spg.product_id",
        "WHERE p.slug IN (" + ", ".join(sql_str(slug) for slug in product_slugs) + ");",
        "",
    ]

    product_perm_selects = []
    for product_id, perms in product_permission_grants.items():
        for sort_order, permission in enumerate(perms, start=1):
            product_perm_selects.append(
                f"SELECT p.id, {sql_str(permission)}, {sql_str(permission)}, {sort_order * 10} FROM store_products p WHERE p.slug = {sql_str(slug_by_id[product_id])}"
            )
    if product_perm_selects:
        lines += [
            "INSERT INTO store_product_permission_grants (product_id, permission_name, display_label, sort_order)",
            "\nUNION ALL\n".join(product_perm_selects) + ";",
            "",
        ]

    lines += [
        "INSERT INTO game_kit_sync_log (revision, status, payload_json, payload_hash, message)",
        "VALUES (@rollout_revision, 'pending', NULL, '', 'Published Raidlands VIP kit workbook rollout.');",
        "",
        "INSERT INTO oxide_permission_sync_log (revision, status, payload_json, payload_hash, message)",
        "VALUES (@rollout_revision, 'pending', NULL, '', 'Published Raidlands VIP permission workbook rollout.');",
        "",
    ]

    return "\n".join(lines)


def generate_php_catalog(products):
    catalog = []
    for product in products:
        prices = []
        if product["rp_price"] > 0:
            prices.append({
                "id": 0,
                "payment_method": "rp",
                "stripe_price_id": f"rp_{product['slug']}_{product['access_interval']}",
                "label": "RP " + product["access_interval"].replace("_", " ").title(),
                "amount_cents": 0,
                "currency": "usd",
                "rp_cost": product["rp_price"],
                "billing_interval": "one_time",
                "access_interval": product["access_interval"],
                "access_duration_seconds": product["access_duration_seconds"],
                "allow_auto_renew": product["allow_auto_renew"],
                "is_active": product["is_active"],
                "is_default": 1,
            })
        catalog.append({
            "id": 0,
            "slug": product["slug"],
            "name": product["name"],
            "product_type": product["product_type"],
            "short_description": product["short_description"][:255],
            "description": product["description"],
            "oxide_group": product["oxide_group"],
            "tier_priority": product["tier_priority"],
            "is_stackable": product["is_stackable"],
            "is_active": product["is_active"],
            "is_featured": product["is_featured"],
            "sort_order": product["sort_order"],
            "prices": prices,
        })
    return "<?php\n\nreturn " + php_array(catalog, 0) + ";\n"


def generate_php_permissions(groups, permissions):
    data = {
        "groups": sorted(groups),
        "permissions": sorted(permissions),
    }
    return "<?php\n\nreturn " + php_array(data, 0) + ";\n"


def validate_kits(kits, item_defs):
    issues = []
    limits = {"MainItems": 24, "WearItems": 8, "BeltItems": 6}
    for kit in kits.values():
        for container, limit in limits.items():
            if len(kit[container]) > limit:
                issues.append(f"{kit['Name']} {container} has {len(kit[container])} entries; limit {limit}")
        for container in limits:
            for item in kit[container]:
                if item["Shortname"] not in item_defs:
                    issues.append(f"{kit['Name']} has unknown item {item['Shortname']}")
    if issues:
        raise ValueError("\n".join(issues))


def main():
    item_defs = load_item_defs()
    existing_defaults, existing_kits_data = load_existing_item_defaults()

    kit_rows = rows_for("Kits & Packages", "Kit / Pack ID")
    manifest_rows = rows_for("Kit Item Manifest", "Kit / Pack ID")
    product_rows = rows_for("Website Products", "Product ID")
    perk_rows = rows_for("Perk Products", "Perk Group")
    group_rows = rows_for("Server Groups", "Server Group")
    permission_rows = rows_for("Group Permissions", "Server Group")
    playtime_rows = rows_for("Playtime RP", "Tier")

    kits, kits_data = build_kits(kit_rows, manifest_rows, item_defs, existing_defaults, existing_kits_data)
    validate_kits(kits, item_defs)
    kit_access = group_access_for_kits(kits, kit_rows)
    groups, grants, permission_meta = build_groups_and_permissions(group_rows, permission_rows, kit_access)
    products, product_kits, product_permission_grants = build_products(product_rows, perk_rows, kits, grants)

    write_json(RUST_ROOT / "oxide" / "data" / "Kits" / "kits_data.json", kits_data)
    reward_ids = update_server_rewards(products, product_kits, kits)
    update_configs(playtime_rows, groups)

    migration = generate_sql(groups, grants, permission_meta, kits, kit_access, products, product_kits, product_permission_grants, reward_ids)
    (WEB_ROOT / "database" / "migrations" / "019_raidlands_vip_kits_permissions_seed.sql").write_text(migration, encoding="utf-8")
    (WEB_ROOT / "includes" / "store-vip-rollout-catalog.php").write_text(generate_php_catalog(products), encoding="utf-8")
    (WEB_ROOT / "includes" / "permissions-vip-rollout.php").write_text(
        generate_php_permissions(groups.keys(), permission_meta.keys()), encoding="utf-8"
    )

    summary = {
        "kits": len(kits),
        "products": len(products),
        "groups": len(groups),
        "permissions": len(permission_meta),
        "server_rewards_new_kits": len(reward_ids),
    }
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
