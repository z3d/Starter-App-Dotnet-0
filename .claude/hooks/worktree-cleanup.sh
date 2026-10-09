#!/usr/bin/env bash
# SessionStart: remove what is provably finished, so leftovers never pile up (AGENTS.md, "Working
# alongside other sessions"). Only two things go, and only after IDLE_HOURS untouched:
#   - a linked worktree whose HEAD is in origin/main and which has no uncommitted or untracked file
#     (then its branch, with `git branch -d`, which refuses anything unmerged)
#   - a local branch with no worktree whose tip is in origin/main
# A worktree another session has just made from main is also "merged and clean", which is why the
# idle clock is the worktree's own git metadata and the branch's reflog, never the commit date.
# It never forces, never touches the primary checkout, a locked worktree or a remote branch, and
# never fetches (the session-start budget is seconds). WORKTREE_CLEANUP=0 turns it off.
set -uo pipefail

[ "${WORKTREE_CLEANUP:-1}" = 0 ] && exit 0
IDLE_HOURS="${WORKTREE_CLEANUP_IDLE_HOURS:-24}"
export GIT_OPTIONAL_LOCKS=0 GIT_TERMINAL_PROMPT=0

cat >/dev/null 2>&1 || true
cd "${CLAUDE_PROJECT_DIR:-$PWD}" 2>/dev/null || exit 0
git rev-parse --git-dir >/dev/null 2>&1 || exit 0
git rev-parse --verify --quiet origin/main >/dev/null 2>&1 || exit 0

now=$(date +%s)
cutoff=$(( now - IDLE_HOURS * 3600 ))
common=$(cd "$(git rev-parse --git-common-dir)" && pwd)
primary=$(git worktree list --porcelain | sed -n '1s/^worktree //p')
here=$(git rev-parse --show-toplevel 2>/dev/null)

# GNU `stat -f` is the file-system form and prints a block to stdout on any argument, so it goes last.
mtime() { local t; t=$(stat -c %Y "$1" 2>/dev/null || stat -f %m "$1" 2>/dev/null); case $t in ''|*[!0-9]*) echo "$now" ;; *) echo "$t" ;; esac; }
newest() { local m=0 f t seen=0; for f in "$@"; do [ -e "$f" ] || continue; seen=1; t=$(mtime "$f"); [ "$t" -gt "$m" ] && m=$t; done; [ "$seen" = 1 ] && echo "$m" || echo "$now"; }
merged() { git merge-base --is-ancestor "$1" origin/main 2>/dev/null; }

removed=()
while IFS='|' read -r path head ref locked; do
  [ -n "$path" ] && [ "$path" != "$primary" ] && [ "$path" != "$here" ] || continue
  [ -z "$locked" ] || continue
  [ -d "$path" ] || continue
  merged "$head" || continue
  [ -z "$(git -C "$path" status --porcelain --untracked-files=normal 2>/dev/null)" ] || continue
  admin=$(cd "$(git -C "$path" rev-parse --git-dir 2>/dev/null)" 2>/dev/null && pwd) || continue
  [ "$(newest "$admin/HEAD" "$admin/index" "$admin/logs/HEAD" "$path")" -lt "$cutoff" ] || continue
  git worktree remove "$path" 2>/dev/null || continue
  removed+=("worktree $path")
  branch=${ref#refs/heads/}
  if [ -n "$ref" ] && git branch -d "$branch" >/dev/null 2>&1; then removed+=("branch $branch"); fi
done < <(git worktree list --porcelain | awk '
  /^worktree /{p=substr($0,10); h=""; b=""; l=""}
  /^HEAD /{h=$2} /^branch /{b=$2} /^locked/{l="locked"}
  /^$/{print p"|"h"|"b"|"l}
  END{if(p!="") print p"|"h"|"b"|"l}' | sort -u)

in_worktree=$(git worktree list --porcelain | sed -n 's/^branch refs\/heads\///p')
for branch in $(git for-each-ref --format='%(refname:short)' refs/heads); do
  [ "$branch" = main ] && continue
  printf '%s\n' "$in_worktree" | grep -qxF "$branch" && continue
  merged "$branch" || continue
  [ "$(newest "$common/logs/refs/heads/$branch")" -lt "$cutoff" ] || continue
  git branch -d "$branch" >/dev/null 2>&1 && removed+=("branch $branch")
done
git worktree prune 2>/dev/null

[ ${#removed[@]} -eq 0 ] && exit 0
msg="Cleaned up ${#removed[@]} merged, clean item(s) idle over ${IDLE_HOURS}h: $(printf '%s, ' "${removed[@]}")"
msg=${msg%, }
msg=${msg//\\/\\\\}
msg=${msg//\"/\\\"}
printf '{"systemMessage": "%s"}\n' "$msg"
