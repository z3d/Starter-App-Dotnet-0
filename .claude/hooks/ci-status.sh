#!/usr/bin/env bash
# SessionStart: names any workflow whose latest run on the default branch failed or never started,
# and GitHub's own reason when it refused to start the jobs (billing, spending limit). Silent when
# CI is green, and when gh is missing, signed out or offline. CI_STATUS_CHECK=0 turns it off.
set -uo pipefail

[ "${CI_STATUS_CHECK:-1}" = 0 ] && exit 0
LIMIT="${CI_STATUS_TIMEOUT:-8}"
export GH_PROMPT_DISABLED=1 NO_COLOR=1 GIT_OPTIONAL_LOCKS=0

cat >/dev/null 2>&1 || true
command -v gh >/dev/null 2>&1 || exit 0
command -v python3 >/dev/null 2>&1 || exit 0
cd "${CLAUDE_PROJECT_DIR:-$PWD}" 2>/dev/null || exit 0

slug=$(git remote get-url origin 2>/dev/null | sed -nE 's#^.*github\.com[:/]([^/]+/[^/]+)$#\1#p' | sed 's/\.git$//')
[ -n "$slug" ] || exit 0
branch=$(git symbolic-ref --short refs/remotes/origin/HEAD 2>/dev/null | sed 's#^origin/##')
branch=${branch:-main}

bounded() { perl -e 'alarm shift; exec @ARGV' "$LIMIT" "$@" 2>/dev/null; }

runs=$(bounded gh run list -R "$slug" --branch "$branch" --limit 40 \
  --json databaseId,workflowName,status,conclusion,createdAt,url) || exit 0

bad=$(printf '%s' "$runs" | python3 -c '
import json, sys
latest = {}
for r in json.load(sys.stdin):
    if r["workflowName"] not in latest or r["createdAt"] > latest[r["workflowName"]]["createdAt"]:
        latest[r["workflowName"]] = r
for r in sorted(latest.values(), key=lambda r: r["createdAt"], reverse=True):
    if r["status"] == "completed" and r["conclusion"] in ("failure", "startup_failure", "timed_out", "action_required"):
        print("\t".join([str(r["databaseId"]), r["workflowName"], r["conclusion"], r["createdAt"][:10], r["url"]]))
' 2>/dev/null) || exit 0
[ -n "$bad" ] || exit 0

first=$(printf '%s\n' "$bad" | head -1 | cut -f1)
refused=$(bounded gh run view "$first" -R "$slug" | grep -m1 -E 'job was not started|spending limit|account payments|billing' | sed -E 's/^[[:space:]]*X[[:space:]]+//')

list=$(printf '%s\n' "$bad" | awk -F'\t' '{printf "  %s: %s on %s, %s\n", $2, $3, $4, $5}')
msg="CI on ${branch} of ${slug} is not green. The latest run of each of these workflows failed:"$'\n'"${list}"
if [ -n "$refused" ]; then
  msg+=$'\n'"GitHub never started the jobs: \"${refused}\" CI is off for this repository until the account's billing is fixed, so nothing pushed is being checked. Tell the person first; only they can fix it."
else
  msg+=$'\n'"Read why before starting other work (gh run view <id> -R ${slug} --log-failed), and tell the person if it is not yours to fix."
fi
python3 -c 'import json,sys; r=sys.argv[1]; print(json.dumps({"systemMessage": r, "hookSpecificOutput": {"hookEventName": "SessionStart", "additionalContext": r}}))' "$msg"
exit 0
