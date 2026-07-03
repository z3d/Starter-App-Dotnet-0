#!/usr/bin/env bash
# PreToolUse hook: gate destructive shell commands (Bash).
#
# Adapted from atherio-danp/cde-dotnetcc (protect-commands.ps1) for this repo's bash harness.
# Posture: permissions broadly ALLOW tools so safe work never prompts; this hook is
# the gate that escalates DESTRUCTIVE commands:
#   * deny — hard-block the truly catastrophic (wiping / or ~).
#   * ask  — force a permission prompt for recoverable-but-destructive actions (file deletion,
#            history rewrites, discarding work, dropping/truncating data).
#
# DELIBERATE DIVERGENCE from the upstream PowerShell version: it ASKs on every git
# add/commit/push ("git is confirm-per-action by governance"). This repo's CLAUDE.md instead
# says to ALWAYS commit after a task and runs its own format/build/test pre-commit gate, so
# gating routine commits here would fight the workflow. We therefore do NOT ask on
# add/commit/push (history-rewriting variants below are still gated).
#
# SCOPE: this gates the Bash tool only. On a PowerShell-primary harness (e.g. Windows),
# destructive cmdlets (Remove-Item -Recurse -Force, git reset --hard run via PowerShell) are
# NOT matched here. The secret-read denylist in settings.json is shell-agnostic and covers the
# higher-value risk.
#
# Matching uses `grep -E` with POSIX-only patterns (no \b / \s) so it behaves the same under
# Git Bash on Windows, macOS bash 3.2 / BSD regex, and GNU. Reads the PreToolUse event JSON on
# stdin; emits the decision JSON on stdout (exit 0). Fails OPEN on any problem so a hook bug
# never hard-blocks legitimate work.

set -u

raw="$(cat 2>/dev/null || true)"
[ -z "$raw" ] && exit 0

# Extract the command. Prefer jq; fall back to a permissive sed so a missing jq fails open.
if command -v jq >/dev/null 2>&1; then
  command="$(printf '%s' "$raw" | jq -r '.tool_input.command // empty' 2>/dev/null || true)"
else
  command="$(printf '%s' "$raw" | sed -n 's/.*"command"[[:space:]]*:[[:space:]]*"\(.*\)".*/\1/p' | head -1)"
fi
[ -z "$command" ] && exit 0

# Collapse whitespace and pad with spaces so simple ' token ' patterns act as word boundaries.
norm=" $(printf '%s' "$command" | tr '\n\t' '  ' | tr -s ' ') "

emit() { # $1 = decision (ask|deny), $2 = reason
  if command -v jq >/dev/null 2>&1; then
    jq -nc --arg d "$1" --arg r "$2" \
      '{hookSpecificOutput:{hookEventName:"PreToolUse",permissionDecision:$d,permissionDecisionReason:$r}}'
  else
    printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"%s","permissionDecisionReason":"%s"}}\n' "$1" "$2"
  fi
  exit 0
}

m() { printf '%s' "$norm" | grep -qiE "$1"; } # case-insensitive POSIX-ERE match

# --- deny: catastrophic, never legitimate (checked first). A recursive-force rm of / or ~. ---
if m ' rm +-[a-z]*r[a-z]*f[a-z]* +(/|~)( |/|\*|$)' || m ' rm +-[a-z]*f[a-z]*r[a-z]* +(/|~)( |/|\*|$)'; then
  emit deny "Refusing rm -rf of / or ~ (catastrophic). Run it yourself if you truly intend it."
fi

# --- ask: destructive but recoverable — force a prompt (first match wins) ---
m ' rm '                                  && emit ask "Confirm destructive action: file/directory deletion (rm)."
m ' rmdir '                               && emit ask "Confirm destructive action: directory deletion (rmdir)."
m ' git +push .*--force([^-]|$)'          && emit ask "Confirm destructive action: git push --force (rewrites shared history)."
m ' git +reset +--hard'                   && emit ask "Confirm destructive action: git reset --hard (discards uncommitted work)."
m ' git +clean .*-[a-z]*f'                && emit ask "Confirm destructive action: git clean -f (deletes untracked files)."
m ' dotnet +ef +database +drop'           && emit ask "Confirm destructive action: dotnet ef database drop."
m ' dotnet +ef +migrations +remove'       && emit ask "Confirm destructive action: dotnet ef migrations remove."
m ' drop +(database|schema|table) '       && emit ask "Confirm destructive action: SQL DROP DATABASE/SCHEMA/TABLE."
m ' truncate '                            && emit ask "Confirm destructive action: SQL TRUNCATE."

# Unqualified DELETE / UPDATE (no WHERE) — heuristic SQL guard.
if m ' delete +from ' && ! m ' where '; then
  emit ask "Confirm destructive action: SQL DELETE without WHERE."
fi
if m ' update .* set ' && ! m ' where '; then
  emit ask "Confirm destructive action: SQL UPDATE without WHERE."
fi

exit 0
