#!/usr/bin/env bash
# Runs deploy/homunculus-sec-fw against stub iptables/ip6tables/ip commands (state in files), so it can be tested without root:
#   bash deploy/tests/test-fw-wrapper.sh
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WRAPPER="$HERE/../homunculus-sec-fw"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
pass=0; fail=0

cat > "$WORK/fake-ipt.py" <<'PY'
import json, os, sys
state_file = os.environ["HS_TEST_STATE"] + "." + sys.argv[1]      # one state per address family
args = [a for a in sys.argv[2:] if a != "-w"]
state = json.load(open(state_file))
def save(): json.dump(state, open(state_file, "w"))
ch = state["chains"]
if args[:2] == ["-n", "-L"]:
    sys.exit(0 if args[2] in ch else 1)
if args[0] == "-N":
    ch.setdefault(args[1], []); save(); sys.exit(0)
rule = " ".join(args[2:])
if args[0] == "-C":
    sys.exit(0 if args[1] in ch and rule in ch[args[1]] else 1)
if args[0] == "-I":                                                 # -I CHAIN 1 -j X
    ch[args[1]].insert(0, " ".join(args[3:])); save(); sys.exit(0)
if args[0] == "-A":
    ch[args[1]].append(rule); save(); sys.exit(0)
if args[0] == "-D":
    ch[args[1]].remove(rule); save(); sys.exit(0)
sys.exit(2)
PY
for fam in v4 v6; do
  printf '#!/bin/sh\nexec python3 "%s/fake-ipt.py" %s "$@"\n' "$WORK" "$fam" > "$WORK/ipt-$fam"; chmod +x "$WORK/ipt-$fam"
done
cat > "$WORK/fake-ip" <<'SH'
#!/bin/sh
echo '1: lo    inet 127.0.0.1/8 scope host lo'
echo '2: eth0    inet 91.98.81.44/32 brd 91.98.81.44 scope global eth0'
echo '2: eth0    inet6 2a01:4f8:c012:721c::1/64 scope global'
SH
chmod +x "$WORK/fake-ip"

export HS_FW_IPTABLES="$WORK/ipt-v4" HS_FW_IP6TABLES="$WORK/ipt-v6" HS_FW_IP="$WORK/fake-ip" HS_TEST_STATE="$WORK/state"

reset() { # $1 = space separated parent chains that exist
  python3 - "$WORK/state" $1 <<'PY'
import json, sys
base = sys.argv[1]; parents = sys.argv[2:]
for fam in ("v4", "v6"):
    json.dump({"chains": {p: [] for p in parents}}, open(base + "." + fam, "w"))
PY
}
state() { python3 -c "import json,sys; print(json.dumps(json.load(open('$WORK/state.$1'))['chains'], sort_keys=True))"; }
check() { # name, expected exit, actual exit
  if [ "$2" = "$3" ]; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1 (expected exit $2, got $3)"; fi
}
expect_state() { # name, family, expected json
  local got; got="$(state "$2")"
  if [ "$got" = "$3" ]; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1"; echo "   expected: $3"; echo "   got:      $got"; fi
}
run() { bash "$WRAPPER" "$@" >"$WORK/out" 2>&1; echo $?; }

reset "INPUT DOCKER-USER"
check "block public ipv4" 0 "$(run block 8.8.8.8)"
expect_state "chain hooked first in INPUT and DOCKER-USER, rule added" v4 \
  '{"DOCKER-USER": ["-j HOMUNCULUS-SEC"], "HOMUNCULUS-SEC": ["-s 8.8.8.8 -j DROP"], "INPUT": ["-j HOMUNCULUS-SEC"]}'

check "block the same address again" 0 "$(run block 8.8.8.8)"
expect_state "idempotent: no duplicate rule or hook" v4 \
  '{"DOCKER-USER": ["-j HOMUNCULUS-SEC"], "HOMUNCULUS-SEC": ["-s 8.8.8.8 -j DROP"], "INPUT": ["-j HOMUNCULUS-SEC"]}'

check "block a second address" 0 "$(run block 9.9.9.9)"
check "unblock the first" 0 "$(run unblock 8.8.8.8)"
expect_state "only the first removed, hooks kept" v4 \
  '{"DOCKER-USER": ["-j HOMUNCULUS-SEC"], "HOMUNCULUS-SEC": ["-s 9.9.9.9 -j DROP"], "INPUT": ["-j HOMUNCULUS-SEC"]}'
check "unblock an address that is not blocked" 0 "$(run unblock 1.1.1.1)"

reset "INPUT DOCKER-USER"
check "block ipv6" 0 "$(run block 2606:4700:4700::1111)"
expect_state "ipv6 goes to the ip6tables family" v6 \
  '{"DOCKER-USER": ["-j HOMUNCULUS-SEC"], "HOMUNCULUS-SEC": ["-s 2606:4700:4700::1111 -j DROP"], "INPUT": ["-j HOMUNCULUS-SEC"]}'
expect_state "ipv4 untouched by an ipv6 block" v4 '{"DOCKER-USER": [], "INPUT": []}'

reset "INPUT"
check "block when Docker is not installed" 0 "$(run block 8.8.8.8)"
expect_state "only INPUT is hooked" v4 '{"HOMUNCULUS-SEC": ["-s 8.8.8.8 -j DROP"], "INPUT": ["-j HOMUNCULUS-SEC"]}'

reset "INPUT DOCKER-USER"
for bad in 127.0.0.1 10.0.0.5 192.168.1.1 172.16.0.9 169.254.1.1 0.0.0.0 224.0.0.1 ::1 fe80::1 91.98.81.44 2a01:4f8:c012:721c::1 \
           1.2.3 "1.2.3.4; reboot" --help 001.002.003.004 "" 8.8.8.8/32 " 8.8.8.8"; do
  check "refuse [$bad]" 65 "$(run block "$bad")"
done
expect_state "nothing changed by any refused request (v4)" v4 '{"DOCKER-USER": [], "INPUT": []}'
expect_state "nothing changed by any refused request (v6)" v6 '{"DOCKER-USER": [], "INPUT": []}'
check "usage: no arguments" 64 "$(run)"
check "usage: unknown action" 64 "$(run nuke 8.8.8.8)"
check "usage: too many arguments" 64 "$(run block 8.8.8.8 extra)"

echo "passed $pass, failed $fail"
[ "$fail" -eq 0 ]
