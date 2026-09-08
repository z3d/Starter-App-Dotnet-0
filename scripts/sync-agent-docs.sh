#!/usr/bin/env bash
# Regenerates AGENTS.md and .agents/skills from CLAUDE.md and .claude/skills, swapping the
# harness-specific tokens (CLAUDE.md -> AGENTS.md, .claude/skills -> .agents/skills).
# The Claude side is the source of truth: edit there, then run this (the pre-commit hook does).
set -euo pipefail
cd "$(dirname "$0")/.."

mirror() { sed -e 's|\.claude/skills|.agents/skills|g' -e 's|CLAUDE\.md|AGENTS.md|g' "$1" > "$2"; }

mirror CLAUDE.md AGENTS.md
rm -rf .agents/skills
find .claude/skills -type f ! -name '.*' | while read -r src; do
  dst=".agents/skills/${src#.claude/skills/}"
  mkdir -p "$(dirname "$dst")"
  mirror "$src" "$dst"
done
