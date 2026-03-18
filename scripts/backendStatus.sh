#!/usr/bin/env bash
# Reports the status of the SensorViewer backend on a Raspberry Pi.
# Shows: DLL presence, service status. On failure, dumps the recent journal.
#
# Usage: backendStatus.sh --host <pi-ip-address>
set -euo pipefail

# ── Constants ─────────────────────────────────────────────────────────────────
readonly PI_USER="pi"
readonly BACKEND_DLL="/home/pi/backend/backend.dll"
readonly SERVICE_NAME="sensorviewer"
readonly JOURNAL_LINES=40
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

echo "═══════════════════════════════════════════════════════"
echo "  SensorViewer backend — ${PI_USER}@${HOST}"
echo "═══════════════════════════════════════════════════════"

# ── 1. Check DLL presence ────────────────────────────────────────────────────
echo ""
echo "── Binary"
if ssh "${PI_USER}@${HOST}" "test -f '${BACKEND_DLL}'" 2>/dev/null; then
    DLL_INFO=$(ssh "${PI_USER}@${HOST}" "ls -lh '${BACKEND_DLL}'")
    echo "  ✔ Found: ${DLL_INFO}"
else
    echo "  ✘ Not found: ${BACKEND_DLL}"
    echo ""
    echo "  Run 'backendInstall.sh --host ${HOST}' after deploying the binary."
    exit 1
fi

# ── 2. Service status ────────────────────────────────────────────────────────
echo ""
echo "── Service (${SERVICE_NAME})"
SERVICE_STATUS=$(ssh "${PI_USER}@${HOST}" "systemctl is-active ${SERVICE_NAME} 2>/dev/null || true")

ssh "${PI_USER}@${HOST}" "sudo systemctl status ${SERVICE_NAME} --no-pager -l 2>/dev/null || true"

# ── 3. Journal on failure ────────────────────────────────────────────────────
if [[ "$SERVICE_STATUS" != "active" ]]; then
    echo ""
    echo "── Journal (last ${JOURNAL_LINES} lines)"
    ssh "${PI_USER}@${HOST}" "sudo journalctl -u ${SERVICE_NAME} -n ${JOURNAL_LINES} --no-pager 2>/dev/null || true"
    echo ""
    echo "Service is NOT running (status: ${SERVICE_STATUS})."
    exit 2
fi

echo ""
echo "✔ Service is running."
