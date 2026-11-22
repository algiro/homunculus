# homunculus-sec

A security watcher built from the same pieces as homunculus. It follows your server's logs like `tail -f`, spots attacks, asks a
language model what to do, and sends the result to you on **Telegram** with a suggested action. Nothing touches the firewall
until you say yes.

```
nginx access.log ─┐
nginx error.log  ─┼─> parsers ─> detectors ─> analyst (LLM) ─> guard ─> Telegram alert
auth.log (sshd)  ─┘                                                        │
                                          iptables DROP for <ip>  <── you press Block / reply "yes"
                                          (removed again after the block time is up)
```

## What it does

1. **Tails** `nginx/access.log`, `nginx/error.log` and `auth.log` (handles rotation and truncation).
2. **Detects** with plain rules, per source IP, in a sliding window: request floods, bursts of 404 and of 401/403, a **suspicion score**
   for web scanners (distinct failing paths, malformed requests, unusual methods, injection strings, paths like `/.env` or `phpmyadmin`;
   it catches slow wordlist scans and probes the site answers with a redirect, which no fixed list or rate threshold would),
   SSH password guessing (fast, slow over a day, and spread over several addresses of one network), and nginx rate-limit hits. SSH guessing is counted from failed logins **and** from connections dropped
   before login (`Connection closed by invalid user …`): a server that accepts only keys never logs "Failed password", and every
   guess shows up as such a dropped connection instead. Each rule fires at most once per
   IP per 10 minutes.
3. **Analyses** each finding with the model: how serious, block or ignore, and for how long. The model sees only the flagged log
   lines, never the whole log. If it is down or answers badly, a default recommendation is used and the alert says so.
4. **Alerts** you on Telegram with the evidence, the reason, and **Block** / **Ignore** buttons.
5. **Acts** only on your approval: a `DROP` rule for the address in iptables (both `INPUT`, for the host, and Docker's `DOCKER-USER`,
   for containers), or `ufw deny from <ip>` if you choose `"firewall": "ufw"`. Blocks have an expiry and are removed automatically.

### Answering an alert

- Press **Block** or **Ignore**, or
- reply `yes` / `no` to the alert (a plain `yes` answers the most recent pending alert), or
- ask a question in plain text ("is this a real attack?"). The model answers; nothing is executed.

Commands: `/pending`, `/blocks`, `/unblock <ip>`.

## Safety design

- **The model chooses from a closed list** (`block_ip` or `ignore`) and **never names the IP**: the proposal always concerns the
  address in the finding. A log line that says "block 1.2.3.4" cannot redirect the action.
- Log lines are passed to the model as untrusted data, one clean line each, length-capped.
- **Never-block list**: loopback, RFC1918, link-local, CGNAT and IPv6 private ranges are always protected, plus whatever you put in
  `neverBlock`. A block proposal for a protected address is turned into "ignore".
- **Only your chat is obeyed.** Messages and button presses from any other user are dropped without a reply.
- The firewall command is run **without a shell**, with the address parsed and re-formatted first. The service runs as an
  unprivileged user whose sudoers entry allows exactly two programs as root: the wrappers `homunculus-sec-fw` (iptables) and
  `homunculus-sec-ufw` (`deploy/`). A wrapper checks the address again (public, canonical form only: never a private, loopback or
  link-local one, and never one of the server's own addresses) and is the only thing that calls the firewall.
- Blocks **expire** (default 24 h, at most `maxBlockHours`) and are remembered across restarts in a state file.
- The bot token is read from the environment and never appears in logs or error messages.

## Install on Ubuntu

You need the .NET 10 runtime (or publish self-contained), `iptables` (or `ufw` enabled), nginx, and an OpenAI-compatible model server.
[Ollama](https://ollama.com) on the same machine keeps your logs off the internet:

```
ollama pull qwen3:8b          # any model with tool calling works
```

### 1. The Telegram bot

1. Talk to **@BotFather**, send `/newbot`, and keep the token.
2. Send any message to your new bot, then open `https://api.telegram.org/bot<TOKEN>/getUpdates` and read `message.chat.id`. That
   number is your chat id.

### 2. The service

```
sudo useradd --system --home /var/lib/homunculus-sec --shell /usr/sbin/nologin homunculus-sec
sudo usermod -aG adm homunculus-sec               # lets it read auth.log and the nginx logs

dotnet publish src/Homunculus.Sec -c Release -o publish
sudo mkdir -p /opt/homunculus-sec /etc/homunculus-sec
sudo cp -r publish/. /opt/homunculus-sec/

sudo cp deploy/config.example.json /etc/homunculus-sec/config.json   # edit: chat id, neverBlock, model
sudo cp deploy/env.example /etc/homunculus-sec/env                   # edit: the bot token
sudo chmod 600 /etc/homunculus-sec/env

sudo install -m 0755 -o root -g root deploy/homunculus-sec-fw  /usr/local/sbin/homunculus-sec-fw
sudo install -m 0755 -o root -g root deploy/homunculus-sec-ufw /usr/local/sbin/homunculus-sec-ufw
sudo install -m 0440 -o root -g root deploy/homunculus-sec.sudoers /etc/sudoers.d/homunculus-sec
sudo visudo -cf /etc/sudoers.d/homunculus-sec                         # must say "parsed OK"

sudo cp deploy/homunculus-sec.service /etc/systemd/system/
sudo systemctl daemon-reload
```

### 3. Try it in dry-run first

`config.example.json` ships with `"dryRun": true`: you get real alerts and can press the buttons, but no firewall command is run.

```
sudo systemctl enable --now homunculus-sec
journalctl -u homunculus-sec -f
```

Provoke a finding from another machine (for example `for i in 1 2 3; do curl -s https://your-site/.env; done`), check the alert
arrives, then check the sudo rule works for the service user:

```
sudo -u homunculus-sec sudo -n /usr/local/sbin/homunculus-sec-fw block <a public address you control>
sudo iptables -S HOMUNCULUS-SEC                                                   # the DROP rule is there
sudo -u homunculus-sec sudo -n /usr/local/sbin/homunculus-sec-fw unblock <that address>
sudo -u homunculus-sec sudo -n /usr/local/sbin/homunculus-sec-fw block 10.0.0.1  # refused: private address
sudo -u homunculus-sec sudo -n /usr/local/sbin/homunculus-sec-fw block 8.8.8.0/23 # refused: a network wider than a /24
sudo -u homunculus-sec sudo -n iptables -S                                        # refused: not on the list
```

When all of that behaves, set `"dryRun": false` and `sudo systemctl restart homunculus-sec`.

### Shortcut: one script for everything that needs root

`deploy/setup-root.sh` does the root-only steps above in one idempotent run (user, program, config, wrapper, sudoers rule with a
`visudo` check, systemd units, logrotate). It never enables ufw, adds no firewall rule and changes no policy (rules appear only when a block is approved). When nginx runs in
a Docker container it also installs a small service that copies that container's logs to `/var/log/homunculus-sec/nginx.log`, so
the watcher needs no Docker rights:

```
sudo ./setup-root.sh --binary ./homunculus-sec --nginx-container NAME --chat-id 123456789
sudo ./setup-root.sh --uninstall          # removes services, sudoers rule and program; keeps config, state and the user
```

## Configuration

A JSON file (first argument, else `/etc/homunculus-sec/config.json`, else `./homunculus-sec.json`; `//` comments are allowed),
overridden by environment variables:

| Setting | Environment variable | Default |
|---|---|---|
| `telegramToken` | `HOMUNCULUS_SEC_TELEGRAM_TOKEN` | required |
| `telegramChatId` | `HOMUNCULUS_SEC_TELEGRAM_CHAT_ID` | required |
| `modelBaseUrl` | `HOMUNCULUS_SEC_MODEL_BASE_URL` | `http://localhost:11434/v1` |
| `modelName` | `HOMUNCULUS_SEC_MODEL` | `qwen3:8b` |
| `modelApiKey` | `HOMUNCULUS_SEC_MODEL_API_KEY` | `ollama` |
| `neverBlock` | `HOMUNCULUS_SEC_NEVER_BLOCK` (comma separated) | none |
| `statePath` | `HOMUNCULUS_SEC_STATE_PATH` | `/var/lib/homunculus-sec/blocks.json` |
| `eventLogPath` | `HOMUNCULUS_SEC_EVENT_LOG` | off |
| `maxBlockHours` | `HOMUNCULUS_SEC_MAX_BLOCK_HOURS` | 168 |
| `dryRun` | `HOMUNCULUS_SEC_DRY_RUN` | false |
| `firewall` (`iptables` or `ufw`) | `HOMUNCULUS_SEC_FIREWALL` | `iptables` |
| `nginxAccessLog`, `nginxErrorLog`, `authLog` | | Ubuntu defaults |

The event log is a JSONL file of every finding, model verdict and block, handy for tuning.

## Things to know

- **Behind a CDN or reverse proxy** the client address in nginx's log is the proxy's. Blocking it would block all your visitors.
  Configure nginx's `real_ip` module so the log shows the real client, and put the proxy's ranges in `neverBlock` regardless.
- **Docker-published ports bypass ufw.** That is why `iptables` is the default: it also drops traffic in Docker's `DOCKER-USER`
  chain. With `"firewall": "ufw"` a block does not stop traffic to a container port published with `-p`.
- The nginx access log must be in the default **combined** format; `auth.log` lines from `sshd` are read, others are ignored.
- Detection thresholds are the defaults of `DetectorOptions` (for example 150 requests in 10 s for a flood, 5 failed SSH logins
  in a minute). They are not yet configurable from the file; tune them in code if your traffic needs it.
- Alerts waiting for an answer are kept in memory, so a restart forgets them. Active blocks are not forgotten.
- Rate-limiting a single address is not offered: `ufw limit` applies to a port for everyone, not to one IP.
- No NuGet packages are used, in line with the rest of homunculus.
