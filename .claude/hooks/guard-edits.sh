#!/usr/bin/env bash
# PreToolUse hook: the file rules that must hold every time, enforced rather than asked.
#
#   * A secrets file is never written by an agent: .env*, appsettings.Development.json and the
#     other local settings, secrets/, key material. (permissions.deny in settings.json refuses
#     the reads; this is the write side of the same list.)
#   * A PackageReference never carries Version=; versions live in Directory.Packages.props.
#   * packages.lock.json is written by `dotnet restore` (--force-evaluate after an intentional
#     dependency change), never by hand.
#
# Reads the Edit/Write event JSON on stdin and denies with a reason on stdout (exit 0). Fails
# OPEN on any problem, the same posture as protect-commands.sh, so a hook bug never blocks work.

set -u

raw="$(cat 2>/dev/null || true)"
[ -z "$raw" ] && exit 0
command -v jq >/dev/null 2>&1 || exit 0

path="$(printf '%s' "$raw" | jq -r '.tool_input.file_path // empty' 2>/dev/null || true)"
[ -z "$path" ] && exit 0
text="$(printf '%s' "$raw" | jq -r '(.tool_input.new_string // .tool_input.content // "")' 2>/dev/null || true)"

deny() {
  jq -cn --arg r "$1" '{hookSpecificOutput:{hookEventName:"PreToolUse",permissionDecision:"deny",permissionDecisionReason:$r}}'
  exit 0
}

case "$path" in
  */.env|*/.env.*|.env|.env.*|*/appsettings.Development.json|*/appsettings.*.local.json|*/appsettings.Local.json|*.Local.json|*/secrets/*|*.pfx|*.pem)
    deny "A secrets file is never written by an agent (${path##*/}); the person keeps it, and the tracked .example template is what changes." ;;
  *packages.lock.json)
    deny "packages.lock.json is written by dotnet restore (--force-evaluate after an intentional dependency change), never edited by hand." ;;
  *.csproj|*.props|*.targets)
    if printf '%s' "$text" | grep -qE '<PackageReference[^>]*Version='; then
      deny "A PackageReference never carries Version=; put the version in Directory.Packages.props (central package management)."
    fi ;;
esac
exit 0
