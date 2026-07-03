#!/usr/bin/env python3
"""Decode Oxide permission data into a readable audit report.

This is intentionally read-only unless --output is provided. It does not modify
Oxide's binary data files.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any


MANAGED_GROUPS = [
    "vip_bronze",
    "vip_gold",
    "vip_elite",
    "perk_personal_mini",
    "perk_skinbox",
    "perk_raid_kit",
    "perk_queue_priority",
    "perk_supporter_badge",
]

DEFAULT_REVIEW_PERMS = [
    "lockedcratetimer.conf.use",
    "autolock.use",
    "autolock.item.bypass",
    "friendlyfire.changestate",
    "targetabledrones.untargetable",
    "toolcupboardturrets.ignore",
]


def read_varint(data: bytes, offset: int) -> tuple[int, int]:
    shift = 0
    value = 0

    while offset < len(data):
        byte = data[offset]
        offset += 1
        value |= (byte & 0x7F) << shift

        if not byte & 0x80:
            return value, offset

        shift += 7

    raise ValueError("Unexpected end of varint")


def read_length_delimited(data: bytes, offset: int) -> tuple[bytes, int]:
    length, offset = read_varint(data, offset)
    end = offset + length

    if end > len(data):
        raise ValueError("Length-delimited field exceeds data length")

    return data[offset:end], end


def parse_fields(data: bytes) -> list[tuple[int, int, Any]]:
    fields: list[tuple[int, int, Any]] = []
    offset = 0

    while offset < len(data):
        key, offset = read_varint(data, offset)
        field_number = key >> 3
        wire_type = key & 7

        if wire_type == 0:
            value, offset = read_varint(data, offset)
        elif wire_type == 2:
            value, offset = read_length_delimited(data, offset)
        else:
            raise ValueError(f"Unsupported wire type {wire_type} at field {field_number}")

        fields.append((field_number, wire_type, value))

    return fields


def decode_text(value: bytes) -> str:
    return value.decode("utf-8", errors="replace")


def parse_dictionary(path: Path) -> dict[str, bytes]:
    items: dict[str, bytes] = {}

    for field_number, wire_type, entry in parse_fields(path.read_bytes()):
        if field_number != 1 or wire_type != 2:
            continue

        key = None
        value = None

        for entry_field, entry_wire, entry_value in parse_fields(entry):
            if entry_field == 1 and entry_wire == 2:
                key = decode_text(entry_value)
            elif entry_field == 2 and entry_wire == 2:
                value = entry_value

        if key is not None and value is not None:
            items[key] = value

    return items


def parse_groups(path: Path) -> dict[str, dict[str, Any]]:
    groups: dict[str, dict[str, Any]] = {}

    for name, raw_value in parse_dictionary(path).items():
        group = {
            "title": "",
            "rank": 0,
            "parent": "",
            "permissions": [],
        }

        for field_number, wire_type, value in parse_fields(raw_value):
            if field_number == 1 and wire_type == 2:
                group["parent"] = decode_text(value)
            elif field_number == 2 and wire_type == 2:
                group["permissions"].append(decode_text(value))
            elif field_number == 3 and wire_type == 0:
                group["rank"] = value
            elif field_number == 4 and wire_type == 2:
                group["title"] = decode_text(value)

        group["permissions"] = sorted(set(group["permissions"]), key=str.lower)
        groups[name] = group

    return dict(sorted(groups.items(), key=lambda item: item[0].lower()))


def parse_users(path: Path) -> dict[str, dict[str, Any]]:
    users: dict[str, dict[str, Any]] = {}

    for steam_id, raw_value in parse_dictionary(path).items():
        user = {
            "nickname": "",
            "groups": [],
            "permissions": [],
        }

        for field_number, wire_type, value in parse_fields(raw_value):
            if field_number == 1 and wire_type == 2:
                user["groups"].append(decode_text(value))
            elif field_number == 2 and wire_type == 2:
                user["nickname"] = decode_text(value)
            elif field_number == 3 and wire_type == 2:
                user["permissions"].append(decode_text(value))

        user["groups"] = sorted(set(user["groups"]), key=str.lower)
        user["permissions"] = sorted(set(user["permissions"]), key=str.lower)
        users[steam_id] = user

    return dict(sorted(users.items(), key=lambda item: item[0]))


def build_audit(root: Path) -> dict[str, Any]:
    groups_path = root / "oxide" / "data" / "oxide.groups.data"
    users_path = root / "oxide" / "data" / "oxide.users.data"
    groups = parse_groups(groups_path)
    users = parse_users(users_path)

    direct_user_drift = {
        steam_id: user
        for steam_id, user in users.items()
        if user["permissions"] or [group for group in user["groups"] if group != "default"]
    }

    warnings: list[str] = []
    default_group = groups.get("default")

    if default_group and default_group["parent"]:
        warnings.append(f"default inherits from {default_group['parent']}")

    missing_managed = [group for group in MANAGED_GROUPS if group not in groups]
    if missing_managed:
        warnings.append("missing managed groups: " + ", ".join(missing_managed))

    empty_managed = [
        group
        for group in MANAGED_GROUPS
        if group in groups and not groups[group]["permissions"] and not groups[group]["parent"]
    ]
    if empty_managed:
        warnings.append("managed groups with no permissions or parent: " + ", ".join(empty_managed))

    default_review_hits = [
        permission
        for permission in DEFAULT_REVIEW_PERMS
        if default_group and permission in default_group["permissions"]
    ]
    if default_review_hits:
        warnings.append("default review permissions present: " + ", ".join(default_review_hits))

    if direct_user_drift:
        warnings.append(f"{len(direct_user_drift)} users have direct permissions or non-default groups")

    return {
        "groups_path": str(groups_path),
        "users_path": str(users_path),
        "groups": groups,
        "users": users,
        "direct_user_drift": direct_user_drift,
        "warnings": warnings,
    }


def render_markdown(audit: dict[str, Any]) -> str:
    lines: list[str] = [
        "# Oxide Permission Audit",
        "",
        f"- Groups file: `{audit['groups_path']}`",
        f"- Users file: `{audit['users_path']}`",
        "",
        "## Warnings",
    ]

    if audit["warnings"]:
        lines.extend(f"- {warning}" for warning in audit["warnings"])
    else:
        lines.append("- None")

    lines.extend(
        [
            "",
            "## Group Summary",
            "",
            "| Group | Title | Rank | Parent | Permissions |",
            "| --- | --- | ---: | --- | ---: |",
        ]
    )

    for name, group in audit["groups"].items():
        lines.append(
            f"| `{name}` | `{group['title']}` | {group['rank']} | "
            f"`{group['parent']}` | {len(group['permissions'])} |"
        )

    lines.extend(["", "## Group Permissions"])

    for name, group in audit["groups"].items():
        lines.extend(["", f"### {name}"])
        lines.append(f"- Title: `{group['title']}`")
        lines.append(f"- Rank: `{group['rank']}`")
        lines.append(f"- Parent: `{group['parent']}`")

        if group["permissions"]:
            lines.append("- Permissions:")
            lines.extend(f"  - `{permission}`" for permission in group["permissions"])
        else:
            lines.append("- Permissions: none")

    lines.extend(["", "## Direct User Drift"])

    if audit["direct_user_drift"]:
        for steam_id, user in audit["direct_user_drift"].items():
            groups = ", ".join(f"`{group}`" for group in user["groups"]) or "none"
            permissions = ", ".join(f"`{permission}`" for permission in user["permissions"]) or "none"
            nickname = user["nickname"] or "Unnamed"
            lines.append(f"- `{steam_id}` `{nickname}`: groups {groups}; direct permissions {permissions}")
    else:
        lines.append("- None")

    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description="Audit Oxide permission data.")
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path)
    parser.add_argument("--json", action="store_true", help="Emit JSON instead of Markdown")
    args = parser.parse_args()

    audit = build_audit(args.root.resolve())
    rendered = json.dumps(audit, indent=2) + "\n" if args.json else render_markdown(audit)

    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
