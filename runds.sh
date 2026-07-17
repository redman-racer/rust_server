#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
WIPE_ROOT="${LIVEADMIN_WIPE_ROOT:-$SCRIPT_DIR/server/rust}"
PENDING_MARKER="$WIPE_ROOT/.liveadmin-force-wipe.pending"
PROCESSING_MARKER="$WIPE_ROOT/.liveadmin-force-wipe.processing"
CLEANED_RECEIPT="$WIPE_ROOT/.liveadmin-force-wipe.cleaned"
WIPE_START_ARGS=()

marker_value() {
    sed -n "s/^$1=//p" "$PROCESSING_MARKER" | tail -n 1
}

if [ -f "$PENDING_MARKER" ]; then
    mv "$PENDING_MARKER" "$PROCESSING_MARKER"
    trap 'if [ -f "$PROCESSING_MARKER" ]; then mv "$PROCESSING_MARKER" "$PENDING_MARKER"; fi' EXIT

    WIPE_SEED="$(marker_value seed)"
    WIPE_SIZE="$(marker_value worldsize)"
    WIPE_MAP="$(marker_value wipe_map)"
    WIPE_BLUEPRINTS="$(marker_value wipe_blueprints)"
    WIPE_VERSION="$(marker_value version)"
    WIPE_MODE="$(marker_value mode)"

    if [ "$WIPE_VERSION" != "1" ] || [ "$WIPE_MODE" != "force" ]; then
        echo "Invalid LiveAdmin force-wipe marker" >&2
        exit 1
    fi
    case "$WIPE_SEED" in ''|*[!0-9]*) echo "Invalid LiveAdmin force-wipe seed" >&2; exit 1 ;; esac
    case "$WIPE_SIZE" in ''|*[!0-9]*) echo "Invalid LiveAdmin force-wipe world size" >&2; exit 1 ;; esac
    case "$WIPE_MAP" in 0|1) ;; *) echo "Invalid LiveAdmin map-wipe flag" >&2; exit 1 ;; esac
    case "$WIPE_BLUEPRINTS" in 0|1) ;; *) echo "Invalid LiveAdmin blueprint-wipe flag" >&2; exit 1 ;; esac
    if [ "$WIPE_SIZE" -lt 1000 ] || [ "$WIPE_SIZE" -gt 6000 ]; then
        echo "LiveAdmin force-wipe world size is outside the safe range" >&2
        exit 1
    fi

    MAP_DELETED=0
    BP_DELETED=0
    if [ "$WIPE_MAP" = "1" ]; then
        while IFS= read -r -d '' WIPE_FILE; do
            rm -f -- "$WIPE_FILE"
            MAP_DELETED=$((MAP_DELETED + 1))
        done < <(find "$WIPE_ROOT" -maxdepth 1 -type f \( -name '*.sav' -o -name '*.sav.*' -o -name '*.map' \) -print0)
    fi
    if [ "$WIPE_BLUEPRINTS" = "1" ]; then
        while IFS= read -r -d '' WIPE_FILE; do
            rm -f -- "$WIPE_FILE"
            BP_DELETED=$((BP_DELETED + 1))
        done < <(find "$WIPE_ROOT" -maxdepth 1 -type f -name 'player.blueprints*.db*' -print0)
    fi

    printf 'status=cleaned\nseed=%s\nworldsize=%s\nmap_deleted=%s\nblueprints_deleted=%s\n' \
        "$WIPE_SEED" "$WIPE_SIZE" "$MAP_DELETED" "$BP_DELETED" > "$CLEANED_RECEIPT"
    rm -f -- "$PROCESSING_MARKER"
    trap - EXIT
    WIPE_START_ARGS=(+server.seed "$WIPE_SEED" +server.worldsize "$WIPE_SIZE")
    echo "LiveAdmin offline force wipe completed: map files=$MAP_DELETED blueprint files=$BP_DELETED"
fi

export LD_LIBRARY_PATH="${LD_LIBRARY_PATH:+$LD_LIBRARY_PATH:}$SCRIPT_DIR/RustDedicated_Data/Plugins:$SCRIPT_DIR/RustDedicated_Data/Plugins/x86_64"
exec "$SCRIPT_DIR/RustDedicated" -batchmode "${WIPE_START_ARGS[@]}" -logfile 2>&1
