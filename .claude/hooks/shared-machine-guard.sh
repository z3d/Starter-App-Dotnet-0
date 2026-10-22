#!/usr/bin/env bash
# PreToolUse (Bash): refuses what reaches past this session on a machine other sessions share
# (AGENTS.md, "Working alongside other sessions"): killing by name or pattern, stopping or resetting
# the container VM, pruning or repairing its shared state, and heavy work when the disk is nearly full.
set -uo pipefail

MIN_FREE_GB="${AGENT_MIN_FREE_GB:-10}"

payload=$(cat 2>/dev/null || true)
command=$(printf '%s' "$payload" | python3 -c 'import json,sys
try: print(json.load(sys.stdin).get("tool_input",{}).get("command",""))
except Exception: pass' 2>/dev/null)
[ -n "$command" ] || exit 0

at='(^|[;&|({`]|\$\()[[:space:]]*(sudo[[:space:]]+)?(command[[:space:]]+)?'
engine='(podman|docker)'
refuse() {
  printf 'Refused on a shared machine: %s\nOther sessions run on this machine (AGENTS.md, "Working alongside other sessions"). %s\n' "$1" "$2" >&2
  exit 2
}

if printf '%s' "$command" | grep -qE "${at}(pkill|killall)([[:space:]]|$)"; then
  refuse "killing processes by name or pattern" "Kill only the PIDs you started: keep \$! when you start something, or find yours with lsof -ti :<your port>, and pass them to kill. If it has to happen, ask the person to run it."
fi
if printf '%s' "$command" | grep -qE "${at}${engine}[[:space:]]+machine[[:space:]]+(stop|reset|rm|restart|init)([[:space:]]|$)"; then
  refuse "stopping, resetting or recreating the container VM" "It takes down every session's containers. If it has to happen, ask the person to run it."
fi
if printf '%s' "$command" | grep -qE "${at}${engine}[[:space:]]+(system[[:space:]]+(reset|prune|migrate|renumber)|((image|container|volume|network|builder|pod)[[:space:]]+)?prune)([[:space:]]|$)" \
  || printf '%s' "$command" | grep -qE "${at}${engine}[[:space:]]+system[[:space:]]+check[^;&|]*--repair"; then
  refuse "pruning, resetting or repairing the container engine's shared state" "Remove only the images, containers and volumes you made, by name or id. If it has to happen, ask the person to run it."
fi

heavy="${at}((dotnet[[:space:]]+(build|test|publish|pack|restore))|(${engine}[[:space:]]+(build|compose|pull|load|save|run))|(npm[[:space:]]+(ci|install|test))|(npx[[:space:]]+playwright)|([^[:space:]]*run-e2e\.sh)|act([[:space:]]|$))"
if printf '%s' "$command" | grep -qE "$heavy"; then
  free_kb=$(df -Pk "${CLAUDE_PROJECT_DIR:-$PWD}" 2>/dev/null | awk 'NR==2{print $4}')
  case $free_kb in ''|*[!0-9]*) exit 0 ;; esac
  if [ "$free_kb" -lt $(( MIN_FREE_GB * 1024 * 1024 )) ]; then
    refuse "heavy work with $(( free_kb / 1024 / 1024 )) GB free, under ${MIN_FREE_GB} GB" "A full disk fails every session at once. Remove what you built (your images, tarballs, publish output), then tell the person how much is left; do not prune what others made."
  fi
fi
exit 0
