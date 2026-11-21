#!/usr/bin/env bash
# homunculus-sec: the steps that need root, in one script. Run it with sudo; it is safe to run again.
#
#   sudo ./setup-root.sh --binary ./homunculus-sec [--nginx-container NAME] [--chat-id ID] [--model NAME]
#
# What it does (and nothing else):
#   1. creates the system user "homunculus-sec" and adds it to the "adm" group (so it can read /var/log/auth.log)
#   2. installs the program into /opt/homunculus-sec
#   3. installs /etc/homunculus-sec/config.json and /etc/homunculus-sec/env, only if they do not exist yet (dry-run on)
#   4. installs /usr/local/sbin/homunculus-sec-ufw (validates the address, then calls ufw) and a sudoers rule that lets that
#      user run only that wrapper (the rule is checked with visudo before it is used)
#   5. installs two systemd services:
#        homunculus-sec             the watcher
#        homunculus-sec-nginx-logs  copies the logs of an nginx container to /var/log/homunculus-sec/nginx.log
#                                   (needed when nginx runs in Docker: the service user then needs no Docker rights)
#   6. installs a logrotate rule for that copied log
#
# What it does NOT do: enable ufw, touch Docker or iptables rules, install Ollama, or put the bot token anywhere.
# Use --uninstall to remove the services, the sudoers rule and the program (config, state and user are kept).

set -euo pipefail

SERVICE_USER="homunculus-sec"
INSTALL_DIR="/opt/homunculus-sec"
CONFIG_DIR="/etc/homunculus-sec"
STATE_DIR="/var/lib/homunculus-sec"
LOG_DIR="/var/log/homunculus-sec"
SUDOERS_FILE="/etc/sudoers.d/homunculus-sec"
SERVICE_FILE="/etc/systemd/system/homunculus-sec.service"
BRIDGE_FILE="/etc/systemd/system/homunculus-sec-nginx-logs.service"
LOGROTATE_FILE="/etc/logrotate.d/homunculus-sec"
TOKEN_PLACEHOLDER="replace-with-the-token-from-BotFather"

BINARY=""
NGINX_CONTAINER=""
CHAT_ID="0"
MODEL="qwen2.5:3b"
UNINSTALL=0
WRAPPER_FILE="/usr/local/sbin/homunculus-sec-ufw"
WRAPPER_SOURCE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/homunculus-sec-ufw"

say() { printf '\n==> %s\n' "$*"; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }

usage() {
    sed -n '2,/^$/p' "$0" | sed 's/^# \{0,1\}//'
    exit "${1:-0}"
}

while [ $# -gt 0 ]; do
    case "$1" in
        --binary) BINARY="${2:-}"; shift 2 ;;
        --nginx-container) NGINX_CONTAINER="${2:-}"; shift 2 ;;
        --chat-id) CHAT_ID="${2:-}"; shift 2 ;;
        --model) MODEL="${2:-}"; shift 2 ;;
        --uninstall) UNINSTALL=1; shift ;;
        -h|--help) usage 0 ;;
        *) printf 'unknown option: %s\n\n' "$1" >&2; usage 2 ;;
    esac
done

[ "$(id -u)" -eq 0 ] || die "run this with sudo."
command -v systemctl >/dev/null || die "systemd is required."

if [ "$UNINSTALL" -eq 1 ]; then
    say "Removing services, sudoers rule and program (config, state and the user are kept)"
    systemctl disable --now homunculus-sec.service homunculus-sec-nginx-logs.service 2>/dev/null || true
    rm -f "$SERVICE_FILE" "$BRIDGE_FILE" "$SUDOERS_FILE" "$LOGROTATE_FILE" "$WRAPPER_FILE"
    rm -rf "$INSTALL_DIR"
    systemctl daemon-reload
    echo "Done. Left in place: $CONFIG_DIR, $STATE_DIR, $LOG_DIR and the user $SERVICE_USER."
    exit 0
fi

# ---- checks before anything is changed ---------------------------------------------------------------------------------
[ -n "$BINARY" ] || die "give the program with --binary PATH (the self-contained homunculus-sec you copied here)."
[ -f "$BINARY" ] || die "no such file: $BINARY"
case "$CHAT_ID" in ''|*[!0-9-]*) die "--chat-id must be a number." ;; esac
case "$MODEL" in ''|*[!A-Za-z0-9_.:/-]*) die "--model contains unexpected characters." ;; esac
command -v visudo >/dev/null || die "visudo is required to check the sudoers rule."
[ -x /usr/sbin/ufw ] || die "ufw is not installed at /usr/sbin/ufw."
command -v python3 >/dev/null || die "python3 is required (the wrapper uses it to validate addresses)."
[ -f "$WRAPPER_SOURCE" ] || die "homunculus-sec-ufw must be next to this script (looked for $WRAPPER_SOURCE)."

if [ -n "$NGINX_CONTAINER" ]; then
    case "$NGINX_CONTAINER" in *[!A-Za-z0-9_.-]*) die "--nginx-container contains unexpected characters." ;; esac
    command -v docker >/dev/null || die "--nginx-container needs docker."
    docker inspect --type container "$NGINX_CONTAINER" >/dev/null 2>&1 || die "no running or stopped container named '$NGINX_CONTAINER'."
    docker logs --tail 1 "$NGINX_CONTAINER" >/dev/null 2>&1 || die "cannot read the logs of '$NGINX_CONTAINER' (is its log driver json-file or journald?)."
fi

# ---- 1. the user -------------------------------------------------------------------------------------------------------
say "User $SERVICE_USER"
if ! id "$SERVICE_USER" >/dev/null 2>&1; then
    useradd --system --home-dir "$STATE_DIR" --create-home --shell /usr/sbin/nologin "$SERVICE_USER"
fi
usermod -aG adm "$SERVICE_USER"

# ---- 2. the program ---------------------------------------------------------------------------------------------------
say "Program in $INSTALL_DIR"
install -d -m 0755 -o root -g root "$INSTALL_DIR"
install -m 0755 -o root -g root "$BINARY" "$INSTALL_DIR/homunculus-sec"

# ---- 3. configuration (never overwritten) ------------------------------------------------------------------------------
say "Configuration in $CONFIG_DIR"
install -d -m 0750 -o root -g "$SERVICE_USER" "$CONFIG_DIR"
install -d -m 0750 -o "$SERVICE_USER" -g "$SERVICE_USER" "$STATE_DIR"

if [ -n "$NGINX_CONTAINER" ]; then
    ACCESS_LOG="$LOG_DIR/nginx.log"
    ERROR_LOG="$LOG_DIR/nginx.log"   # the container's stdout and stderr end up in one file; each parser skips the other's lines
else
    ACCESS_LOG="/var/log/nginx/access.log"
    ERROR_LOG="/var/log/nginx/error.log"
fi

if [ ! -e "$CONFIG_DIR/config.json" ]; then
    cat > "$CONFIG_DIR/config.json" <<EOF
{
  // Dry run: alerts and buttons work, but no firewall command is ever run. Set to false only after the checks in docs/deploy.md.
  "dryRun": true,

  "nginxAccessLog": "$ACCESS_LOG",
  "nginxErrorLog": "$ERROR_LOG",
  "authLog": "/var/log/auth.log",

  // The bot token is in /etc/homunculus-sec/env (HOMUNCULUS_SEC_TELEGRAM_TOKEN), not here.
  "telegramChatId": $CHAT_ID,

  "modelBaseUrl": "http://127.0.0.1:11434/v1",
  "modelName": "$MODEL",
  "modelApiKey": "ollama",

  // Never blocked. Loopback and private ranges are always protected. Add your own IPs and any CDN or proxy ranges.
  "neverBlock": [],

  "statePath": "$STATE_DIR/blocks.json",
  "eventLogPath": "$STATE_DIR/events.jsonl",
  "maxBlockHours": 168
}
EOF
    chown root:"$SERVICE_USER" "$CONFIG_DIR/config.json"
    chmod 0640 "$CONFIG_DIR/config.json"
    echo "created $CONFIG_DIR/config.json"
else
    echo "kept existing $CONFIG_DIR/config.json"
fi

if [ ! -e "$CONFIG_DIR/env" ]; then
    printf 'HOMUNCULUS_SEC_TELEGRAM_TOKEN=%s\n' "$TOKEN_PLACEHOLDER" > "$CONFIG_DIR/env"
    chown root:root "$CONFIG_DIR/env"
    chmod 0600 "$CONFIG_DIR/env"
    echo "created $CONFIG_DIR/env (edit it and put the bot token in)"
else
    echo "kept existing $CONFIG_DIR/env"
fi

# ---- 4. the ufw wrapper and the sudoers rule (validated before it is installed) ---------------------------------------
say "ufw wrapper $WRAPPER_FILE and sudoers rule $SUDOERS_FILE"
install -m 0755 -o root -g root "$WRAPPER_SOURCE" "$WRAPPER_FILE"
TMP_SUDOERS="$(mktemp)"
trap 'rm -f "$TMP_SUDOERS"' EXIT
cat > "$TMP_SUDOERS" <<EOF
# homunculus-sec: the service user may run exactly one program as root, without a password: the wrapper, which checks the
# address itself and then calls ufw.
$SERVICE_USER ALL=(root) NOPASSWD: $WRAPPER_FILE
EOF
visudo -cf "$TMP_SUDOERS" >/dev/null || die "the generated sudoers rule did not pass visudo; nothing was installed."
install -m 0440 -o root -g root "$TMP_SUDOERS" "$SUDOERS_FILE"

# ---- 5. systemd services -----------------------------------------------------------------------------------------------
say "systemd units"
cat > "$SERVICE_FILE" <<EOF
[Unit]
Description=homunculus-sec: log watcher that alerts on Telegram and blocks IPs with ufw after approval
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=$SERVICE_USER
Group=$SERVICE_USER
SupplementaryGroups=adm
EnvironmentFile=$CONFIG_DIR/env
ExecStart=$INSTALL_DIR/homunculus-sec $CONFIG_DIR/config.json
Restart=on-failure
RestartSec=5
# Keep the watcher small next to the other things on this machine.
MemoryMax=300M
# NoNewPrivileges and ProtectSystem are deliberately not set: the first would make sudo unusable, the second would make
# /etc/ufw read-only for the sudo child process.
PrivateTmp=yes
ProtectHome=yes
ProtectKernelModules=yes
ProtectControlGroups=yes
RestrictRealtime=yes

[Install]
WantedBy=multi-user.target
EOF
chmod 0644 "$SERVICE_FILE"

if [ -n "$NGINX_CONTAINER" ]; then
    install -d -m 2750 -o root -g adm "$LOG_DIR"
    cat > "$BRIDGE_FILE" <<EOF
[Unit]
Description=Copy the logs of the $NGINX_CONTAINER container to a file for homunculus-sec
After=docker.service
Requires=docker.service

[Service]
Type=simple
# New lines only (--tail 0). Files created here belong to group adm (the directory is setgid) and are not world readable.
UMask=0027
ExecStart=/bin/sh -c 'exec /usr/bin/docker logs --follow --tail 0 $NGINX_CONTAINER >>$LOG_DIR/nginx.log 2>&1'
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF
    chmod 0644 "$BRIDGE_FILE"
    cat > "$LOGROTATE_FILE" <<EOF
$LOG_DIR/nginx.log {
    daily
    rotate 3
    maxsize 200M
    missingok
    notifempty
    compress
    delaycompress
    copytruncate
    create 0640 root adm
}
EOF
    chmod 0644 "$LOGROTATE_FILE"
fi

systemctl daemon-reload

# ---- 6. start what can be started --------------------------------------------------------------------------------------
say "Starting"
if [ -n "$NGINX_CONTAINER" ]; then
    systemctl enable homunculus-sec-nginx-logs.service >/dev/null 2>&1
    systemctl restart homunculus-sec-nginx-logs.service
    echo "log copy: running"
fi

systemctl enable homunculus-sec.service >/dev/null 2>&1
if grep -q "$TOKEN_PLACEHOLDER" "$CONFIG_DIR/env" || ! grep -Eq '"telegramChatId": *[1-9-]' "$CONFIG_DIR/config.json"; then
    cat <<EOF

The watcher is installed and enabled but NOT started: it still needs the Telegram settings.
  1. put the bot token in $CONFIG_DIR/env           (sudo nano $CONFIG_DIR/env)
  2. put your chat id in $CONFIG_DIR/config.json    ("telegramChatId")
  3. sudo systemctl restart homunculus-sec && journalctl -u homunculus-sec -f
EOF
else
    systemctl restart homunculus-sec.service
    echo "watcher: running (dry run)"
fi

# ---- notes -------------------------------------------------------------------------------------------------------------
say "Things to check by hand"
if [ -n "$NGINX_CONTAINER" ]; then
    echo "* The client addresses in the copied log must be real visitors, not a Docker gateway or a CDN. Look at a few lines in a minute:"
    echo "    sudo tail -n 3 $LOG_DIR/nginx.log"
fi
if command -v ufw >/dev/null && ufw status 2>/dev/null | grep -qi inactive; then
    echo "* ufw is installed but INACTIVE, so no ufw rule blocks anything yet. This script does not enable it: do that yourself,"
    echo "  after allowing SSH, and only when you have read the Docker note in docs/homunculus-sec.md."
fi
if command -v docker >/dev/null; then
    echo "* Docker-published ports (for example 80 and 443) bypass ufw. A ufw block will not stop web traffic to a container;"
    echo "  it does protect SSH and anything that is not published by Docker."
fi
echo "* The Ollama server for model '$MODEL' must be reachable at http://127.0.0.1:11434 (it is not installed by this script)."
echo
echo "Done."
