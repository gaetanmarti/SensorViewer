#!/usr/bin/env bash
# Stops and removes the SensorViewer backend systemd service from a Raspberry Pi.
#
# Usage: backendRemove --host <pi-ip-address>
set -euo pipefail

# ── Constants ─────────────────────────────────────────────────────────────────
readonly PI_USER="pi"
readonly SERVICE_NAME="sensorviewer"
readonly SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
# ──────────────────────────────────────────────────────────────────────────────

usage() {
    echo "Usage: $(basename "$0") --host <pi-ip-address>"
    echo ""
    echo "  --host <ip>   IP address of the Raspberry Pi"
    exit 1
}

HOST=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --host) HOST="$2"; shift 2 ;;
        -h|--help) usage ;;
        *) echo "Unknown argument: $1"; usage ;;
    esac
done

[[ -z "$HOST" ]] && usage

echo "→ Removing '${SERVICE_NAME}' service from ${PI_USER}@${HOST} …"

ssh "${PI_USER}@${HOST}" "
    sudo systemctl stop ${SERVICE_NAME} 2>/dev/null || true
    sudo systemctl disable ${SERVICE_NAME} 2>/dev/null || true
    sudo rm -f '${SERVICE_FILE}'
    sudo systemctl daemon-reload
"

echo ""
echo "✔ Service '${SERVICE_NAME}' stopped and removed."
