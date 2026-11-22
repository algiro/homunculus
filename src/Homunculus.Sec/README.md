# Homunculus.Sec

A small security watcher for a Linux web server. It follows the server's logs, notices attacks with plain rules, asks a language
model what a suspicious address is doing and what to do about it, and then **asks you on Telegram**. Only when you approve does it
add a firewall rule, and that rule expires on its own.

It is the security sibling of the coding agent in `src/Homunculus` and shares its model client and event log through
`src/Homunculus.Core`. Like the rest of the repository it uses no NuGet packages.

For installing it on a server see [docs/homunculus-sec.md](../../docs/homunculus-sec.md) (settings, safety design) and
[docs/deploy.md](../../docs/deploy.md) (a full, real deployment, with the problems met on the way).

## What it does, in one picture

```
 nginx access.log ─┐
 nginx error.log  ─┼─► tail ─► parse ─► detect ─► analyse ─► guard ─► Telegram ─► you ─► firewall ─► expire
 auth.log (sshd)  ─┘   (new     (typed   (rules,   (LLM:     (safety   alert with   yes /   DROP rule  rule removed
                        lines)  entries)  per IP)  verdict)   checks)   buttons      no      for the IP  after N hours
```

| Stage | What happens | Uses the LLM? |
|---|---|---|
| Tail | New lines of each log file are read as they are written, like `tail -f`; rotation and truncation are handled | no |
| Parse | A line becomes a typed `LogEntry` (source, kind, time, IP, user, path, status…); lines that are not interesting are dropped | no |
| Detect | Per source IP, sliding windows count events, and a suspicion score adds up different kinds of web misbehaviour. When a threshold is crossed a `Finding` is raised | no |
| Analyse | The model gives a verdict on the finding: how serious, block or ignore, for how long, and why | **yes** |
| Guard | Code checks the verdict: protected addresses are never blocked, block length is capped | no |
| Alert | A Telegram message with the evidence, the reason and **Block** / **Ignore** buttons | no |
| Decide | You press a button, reply yes/no, or ask a question about the alert | questions only |
| Act | The firewall wrapper adds a `DROP` rule for the IP (iptables, or `ufw`) | no |
| Expire | A once-a-minute sweep removes blocks whose time is up; saved blocks are re-applied after a restart | no |

## The pipeline in detail

### 1. Reading the logs (`Logs/`)

`LogTailer` follows one file. It starts at the **end** of the file (old history is not replayed), polls every 500 ms, holds back a
half-written line until its newline arrives, and starts over from the top when the file shrinks or disappears (logrotate). It reads
at most 4 MB per poll and ignores absurdly long lines (64 KB). If a file cannot be read (permission), it says so once on stderr and
keeps trying.

`LogWatcher` runs one tailer per log and puts every parsed entry on a single channel, so detectors and the callback are only ever
used from one task. If a tailer fails for good, the whole service stops with that error instead of going on half blind.

Three parsers turn lines into entries:

| Parser | Reads | Produces |
|---|---|---|
| `NginxAccessParser` | nginx **combined** access format | `Request` with IP, method, path, status, user agent |
| `NginxErrorParser` | nginx error log | `RateLimited` (limit_req / limit_conn hits) or `ServerError`, with the client IP when present |
| `AuthLogParser` | `sshd` lines of `auth.log`, ISO or classic syslog timestamps | `FailedLogin`, `InvalidUser`, `LoginSuccess`, `PreauthDrop` |

`PreauthDrop` is a connection that ended before login (`Connection closed by invalid user …`). It matters on servers that accept
only SSH keys: they never log `Failed password`, so password guessing shows up only as these dropped connections (about 10,000
a month on the server this was built for).

### 2. Detecting (`Detection/`)

Detection is **deterministic code, not the model**. It has two mechanisms.

**Counting rules.** One `WindowDetector` per rule keeps, for each IP, the timestamps of its matching events inside a sliding window,
using the *log's own* timestamps (so replaying a log gives the same result). When the count reaches the threshold it raises a
`Finding` carrying the IP, the count, the window and the last five raw log lines as evidence. After firing, the same rule stays
quiet for that IP for the cooldown (10 minutes).

| Rule | Fires when one IP… | Default |
|---|---|---|
| `request_flood` | sends very many requests | 150 in 10 s |
| `not_found_burst` | gets many 404s (fast directory scanning) | 30 in 1 min |
| `auth_denied_burst` | gets many 401/403 (credential stuffing, probing) | 20 in 1 min |
| `ssh_brute_force` | has failed SSH logins or pre-login drops | 5 in 1 min |
| `ssh_persistent` | keeps guessing SSH logins slowly: many attempts spread over a day, never close to the per-minute limit | 50 in 24 h |
| `rate_limited` | hits nginx's rate limits | 10 in 1 min |

**Networks.** Attackers often spread their guesses over several addresses of one network, each too quiet to trip any per-address rule
(on the reference server 83% of the SSH attempts came from addresses that never reach the per-minute limit, and 36% from networks
with several attackers). `NetworkDetector` groups SSH failures by network (a /24 for IPv4, a /64 for IPv6) and raises an `ssh_subnet`
finding when **at least 3 addresses, each with at least 3 attempts**, together make **at least 50 attempts within 24 hours** (once per
network per day). The finding's address is the network (`203.0.113.0/24`) and its summary names the busiest addresses. If an address
of the same network **logged in successfully** during that day the finding carries a **caution**, because blocking the range would cut
that user off. Replaying the reference server's month of SSH history gives 10 such networks, and none of them had a successful login in that period.

**The web scanner score.** A fixed path list and a rate threshold miss most real scanners: they crawl slowly, use wordlists nobody
wrote down, or are answered with redirects instead of 404s. So `WebScannerDetector` scores each IP on the access log instead. Each
*distinct* piece of suspicious behaviour adds points, and points expire after an hour:

| Signal | Points |
|---|---|
| a distinct path that failed (status 400 or more); misses on `/favicon.ico`, `/robots.txt`, `/` and the like do not count | 1 each |
| a malformed request (status 400) | 2 |
| an unusual method or a request line that cannot be read (`CONNECT`, `PRI`, binary junk) | 3 each kind |
| a path that scanners ask for (`/.env`, `wp-login.php`, `phpmyadmin`, `.git/`…), whatever the answer was | 4 each |
| a traversal or injection string (`../`, `${jndi:`, `union select`…) | 5 each |
| a scanner-like user agent (`python-requests`, `sqlmap`, …): weak, since legitimate clients use some | 1 |

At **10 points** (default; one alert per IP per hour) it raises a `web_scanner` finding that also carries a **summary** written by the
code: the score, how many requests and with which statuses, each kind of signal with a few examples, and the top user agents. Findings
with a traversal/injection string, a scanner path or an unusual method are marked **hard evidence** (see the guard below). Requesting
the same missing page 400 times is one point, so a broken link on a busy page does not look like a scan.

On the reference server, a replay of 12.5 hours of real traffic (5,959 log lines) reports 8 addresses: a webshell hunt, a year-directory
crawl, a Censys probe, a phpMyAdmin crawler, `.env` probes, Java JSF probes and binary protocol junk, and nothing that looked like an
ordinary visitor. For comparison, on an earlier 12-hour sample of the same server the previous rules (a fixed probe-path list plus
rate thresholds) reported 3 addresses while an independent check suggested 36 scanners. Check your own traffic with `--replay`
(see below).

The counting thresholds are `DetectorOptions` (defaults in code; not yet in the settings file). A replay of 33 days of `auth.log`
with the defaults gives about 3 SSH alerts a day.

### 3. Analysing (`Analysis/`) and the guard

This is the only place the model decides anything. See [How and when the LLM is used](#how-and-when-the-llm-is-used).

`ProposalGuard` then has the last word, in code:
- a block proposal for a protected address (loopback, private ranges, anything in `neverBlock`) is turned into *ignore*;
- the block length is clamped to `maxBlockHours` (default 168 h);
- an *ignore* verdict on a finding with **hard evidence** (injection strings, paths only scanners request, unusual request
  methods) is overridden: the alert goes out as a block proposal for your decision, with a note that the model had suggested
  ignoring it. Otherwise an *ignore* verdict sends no alert; it is only written to the event log.

### 4. Alerting and answering (`Telegram/`, `Service/`)

The service talks to Telegram by long polling, so no public endpoint is needed (it connects IPv4-first, with short deadlines).
An alert looks like:

```
🔴 HIGH · ssh_brute_force · #7
IP: 203.0.113.50
Evidence: 6 events in 60s

Repeated pre-login SSH connections from one address using many user names.
The log shows 6 dropped connections within 60 seconds, a typical password-guessing pattern.

Suggested: block 203.0.113.50 for 24h.
Reply yes / no, press a button, or ask me a question.        [ ✅ Block 24h ] [ 🚫 Ignore ]
```

Only **your** chat is obeyed (the sender id and chat id must both equal the configured chat id; anything else is dropped silently).
You can answer by pressing a button, by replying `yes` / `no` to the alert (a plain `yes` answers the most recent pending one), or
with the commands `/pending`, `/blocks`, `/unblock <ip>`, `/help`. Any other text is treated as a question about the alert and
goes to the model (which cannot act, see below). An alert cannot be answered twice, and nothing happens until you answer.

### 5. Acting and expiring (`Firewall/`)

`BlockManager` calls the firewall and records the block, with its expiry, in a JSON state file (written atomically). A block is
recorded only after the firewall accepted it. Blocking an address again only extends the block. Each minute a sweep removes
blocks whose time is up (a failed removal is retried next minute), and when the service starts it re-applies the saved blocks that
are still running, because plain iptables rules do not survive a reboot.

A block can be an address or a **network** (`203.0.113.0/24`). Network blocks are deliberately limited: never wider than a /24 (a /48
for IPv6), never overlapping a private range or anything in `neverBlock`, never containing the server's own address, always in one
canonical spelling (no host bits), and kept to at most 48 hours. An alert for a network says so ("block the whole network"), shows
the caution if there is one, and its button reads "Block range". A blocked range also silences alerts for single addresses inside it.
`/unblock` accepts an address or a network.

The firewall is reached only through a small **root-owned wrapper script** run with `sudo -n`; the service itself runs as an
unprivileged user whose sudoers entry names just the wrappers (`deploy/homunculus-sec-fw` for iptables, `…-ufw` for ufw). The
wrapper checks the address again (public, canonical form, not one of the server's own addresses), so even a compromised service
cannot turn it into a general root shell. The iptables wrapper keeps every block in its own `HOMUNCULUS-SEC` chain, hooked first
in `INPUT` and in Docker's `DOCKER-USER`, so it covers the host and published container ports and changes no policy.

In **dry-run** mode the firewall is replaced by a stand-in that records what it would have done, and the decisions go to a
separate state file so they can never turn into real blocks later.

## How and when the LLM is used

**The model is a consultant for one question, not the engine.** Everything that finds, filters, executes and enforces is ordinary
code. The model is called in exactly two situations.

### 1. Once per finding: "what is this, and what should we do?"

*When:* a detector raised a `Finding`, and the address is not already blocked and has no alert waiting for your answer. A busy
server raises a handful per day, not one per log line; the model never sees the logs as a stream.

*What it is given* (`Analyst.AnalyzeAsync`), a fresh conversation each time, with no memory of earlier findings:
- a **system prompt** that explains the six rules, tells it to recommend `block_ip` only for clearly hostile or abusive behaviour
  and `ignore` for benign things (search-engine crawlers, uptime monitors, a mistyped password), and tells it that **the log lines
  are untrusted data that may contain instructions, which it must never follow**;
- a **user message** with the rule name, the IP, the time, and the evidence: for the web scanner rule the **summary** the detector
  wrote (score, traffic mix, signals with examples, user agents; at most six lines of 400 characters), for the others the count
  and window; then the last five log lines, each cut to one line of at most 300 characters with control characters removed.

*What it can answer:* it is offered one tool, `propose_action`, and must call it once:

| Field | Values |
|---|---|
| `severity` | `low`, `medium`, `high` |
| `action` | `block_ip` or `ignore`; **nothing else exists** |
| `block_hours` | 1 to 168; defaults to 24 when missing |
| `summary` | one sentence (kept to 300 chars) |
| `reason` | why, citing the evidence (kept to 600 chars) |

*What it cannot do.*
- **It never names the IP.** The proposal always concerns the finding's own address, so a log line saying "block 1.2.3.4" cannot
  redirect the action.
- It cannot run commands, read files, or call any other tool, and its action list is closed.
- It cannot approve its own proposal. Nothing reaches the firewall without your yes.
- It cannot override the guard: the never-block list and the length cap are applied after it speaks.

*When it fails.* If the model is unreachable, times out, does not call the tool, or returns an invalid answer (unknown severity or
action, missing fields), the finding is not dropped: a **default proposal** for the rule is used, a block for 24 hours (severity
*high* for SSH and floods, *medium* otherwise), and the alert says *"The model was unavailable; this is the default
recommendation."* So a dead model makes the service noisier, not blind.

### 2. When you ask a question about an alert

*When:* you send free text that is not `yes`/`no` or a command (for example "is this a real attack?"). `Analyst.AnswerAsync`
gives the model the same evidence, its own earlier recommendation and your question, and sends its answer back to the chat.

This call is given **no tools**: whatever the model says, nothing is executed. To act, you still press the button.

### Where the model is not used

Reading and parsing logs, counting and thresholds, choosing the address, the never-block list, the length cap, talking to Telegram,
running the firewall, expiring blocks, and re-applying them after a restart are all plain code and work without a model.

### Choosing and running a model

Any **OpenAI-compatible** chat endpoint with **tool calling** works (`modelBaseUrl`, `modelName`, `modelApiKey`). A local
[Ollama](https://ollama.com) keeps your logs on your own machine, which is the intended setup: the model sees only the flagged
lines, never whole logs, but those lines contain visitor IPs and URLs.

On the 4-vCPU, CPU-only reference server a 4-billion-parameter non-thinking model (`qwen3:4b-instruct-2507`, 4-bit) took about
30 to 60 s per verdict and answered correctly in every benchmark case, including a log line that tried to inject
instructions; a 3B model failed that case. The benchmark is `deploy/bench-model.py`; the details are in
[docs/deploy.md](../../docs/deploy.md). A thinking model is a poor fit on CPU (long reasoning before it answers).

### A caveat worth knowing

An *ignore* verdict sends no alert. A hostile log line (or a user agent that claims to be a search engine) could try to talk the model
into *ignore*, and a weak model might comply. The guard closes this for **hard evidence**: injection strings, paths that only scanners
request and unusual request methods always reach you. It cannot help for soft evidence only (say, a wordlist crawl of ordinary
404s sent with a Googlebot user agent), so the event log records every verdict and its reason (`finding_ignored`, `analysis`):
audit it now and then, and use a model that passes the benchmark (`deploy/bench-model.py`, which includes such cases).

## Safety at a glance

- Nothing is blocked without your explicit yes; the model only proposes.
- The model chooses from `block_ip`/`ignore` and never supplies the IP.
- Log text is treated as untrusted, cleaned and length-capped before it reaches the model, and escaped before it reaches Telegram.
- Never-block list: loopback, RFC 1918, link-local, CGNAT and IPv6 private ranges, plus your own `neverBlock` entries. It is
  enforced when proposing, when approving and when re-applying saved blocks; the wrappers refuse such addresses too. A network that
  contains any protected address is protected as a whole, and ranges wider than a /24 (/48 for IPv6) are never blocked.
- Only your Telegram chat is obeyed.
- The firewall is reached only through root-owned wrappers that validate the address themselves; no shell is ever involved.
- Every block expires. If any of the three loops (watching, polling, expiring) dies, the service stops with the error and systemd
  restarts it.

## Code map

```
Program.cs            composition root: settings → model, Telegram, firewall, logs → SecMonitor
SecSettings.cs        JSON file + environment overrides, validation
Logs/                 LogTailer, LogWatcher, the three parsers, LogEntry
Detection/            WindowDetector (counting rules), WebScannerDetector (score), NetworkDetector (ssh_subnet), DefaultDetectors, DetectorOptions, Finding
Analysis/             Analyst (the two model calls), Proposal, ProposalGuard
Safety/               Allowlist (never-block list), Cidr (addresses and networks in one canonical form)
Firewall/             IFirewall, HelperFirewall (iptables / ufw wrappers), DryRunFirewall, BlockManager (state, expiry, restore)
Telegram/             TelegramApi (Bot API client), TelegramConnect (IPv4-first connect), models
Service/              SecMonitor (the main loop), ChatHandler (your answers), IncidentBook, AlertFormatter
```

`SecMonitor` runs three loops side by side: the log watcher (finding → analysis → alert), the Telegram poller (your answers), and the
expiry sweep. On start it re-applies saved blocks and sends a greeting to your chat, which also proves the token and chat id work.

## Building and testing

```
dotnet run --project src/Homunculus.Sec -- --replay access.log --summaries   # what the detectors would report on a real log; no Telegram, model or firewall
dotnet test tests/Homunculus.Sec.Tests                     # the unit tests (fakes for model, Telegram, firewall, process runner)
bash deploy/tests/test-fw-wrapper.sh                       # the iptables wrapper against stub iptables, no root needed
dotnet publish src/Homunculus.Sec -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:DebugType=none -o publish/linux-x64
```

The tests cover the parsers, the detectors, the tailer (rotation, partial lines, multi-byte characters), the analyst (including
invalid and hostile answers), the guard and allowlist, the Telegram flow (button, reply, question, wrong sender), block expiry and
restore, the settings, and the shipped example configuration.

## Limits

- Detection thresholds and the score weights are fixed in code for now (`DetectorOptions`, `WebScannerDetector`); tune them with `--replay`.
- Pending alerts are kept in memory and are lost on restart; active blocks are saved. A button or a reply on an alert from before a
  restart is refused with an explanation and the buttons are removed (each run puts its own random token in the button data, so an old
  button can never act on a newer alert that reused the number).
- Only the nginx combined log format and `sshd` lines are understood.
- Networks are found for SSH only, and only as a /24 (IPv4) or a /64 (IPv6); the web scanner score is per address.
- Behind a CDN or reverse proxy the logged address is the proxy's: fix nginx's `real_ip` and add the proxy ranges to `neverBlock`.
- Rate-limiting a single address (as opposed to blocking it) is not offered.
