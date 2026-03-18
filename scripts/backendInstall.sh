#!/usr/bin/env bash
# Installs the SensorViewer backend as a systemd service on a Raspberry Pi.
#
# Usage: backendInstall --host <pi-ip-address>
set -euo pipefail

# ── Constants ─────────────────────────────────────────────────────────────────
readonly PI_USER="pi"
readonly BACKEND_DLL="/home/pi/backend/backend.dll"
readonly WORKING_DIR="/home/pi/backend"
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

echo "→ Installing '${SERVICE_NAME}' service on ${PI_USER}@${HOST} …"

# Create the systemd unit file remotely via SSH heredoc
ssh "${PI_USER}@${HOST}" "sudo tee '${SERVICE_FILE}'" > /dev/null << EOF
[Unit]
Description=SensorViewer Backend
After=network.target

[Service]
Type=simple
User=${PI_USER}
WorkingDirectory=${WORKING_DIR}
ExecStart=/usr/local/bin/dotnet ${BACKEND_DLL}
Restart=on-failure
RestartSec=5
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
EOF

# Enable and start the service
ssh "${PI_USER}@${HOST}" "
    sudo systemctl daemon-reload
    sudo systemctl enable ${SERVICE_NAME}
    sudo systemctl restart ${SERVICE_NAME}
"

echo ""
echo "✔ Service '${SERVICE_NAME}' installed and started."
echo ""
ssh "${PI_USER}@${HOST}" "sudo systemctl status ${SERVICE_NAME} --no-pager"
