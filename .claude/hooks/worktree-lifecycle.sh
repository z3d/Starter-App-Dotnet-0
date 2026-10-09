#!/usr/bin/env bash
# A worktree ends merged or abandoned, never parked (AGENTS.md, "Working alongside other sessions").
# Work another agent cannot see may as well not exist, and a branch left for days goes stale against
# main until nobody can merge it; a merged worktree left behind is only clutter, but clutter is how
# the parked ones hide.
#
#   SessionStart  start      records where every branch and worktree stood, and names anything
#                            parked (unmerged or uncommitted and idle for STALE_HOURS), anything
#                            merged but never removed, and old stashes
#   Stop          (default)  blocks once if a worktree THIS session created is ahead of origin/main,
#                            or is merged but not removed
#
# It looks at every worktree, because the shell's directory is reset to the primary checkout between
# calls and the work is usually not underfoot. It blocks only on a worktree THIS session created:
# one that was not there at SessionStart, on a branch that was not there either, and whose
# `git worktree add` (or EnterWorktree) is in this session's transcript or one of its subagents'.
# Several agents share this checkout, and another session's in-flight branch is not ours to merge
# or delete — pushing or discarding someone else's work is the failure this is meant to prevent —
# so a worktree that cannot be shown to be ours is named once and never blocked on. Anything touched in the last QUIET_MINUTES is left alone, so an agent still
# working in a worktree is told neither to merge nor to remove it. It never removes, merges or deletes anything
# itself; it says what to do. The block happens at most once per session, so a branch that cannot
# fast-forward can never trap the session in a loop.
set -uo pipefail

STALE_HOURS="${STARTERAPP_STALE_HOURS:-48}"
RECENT_HOURS="${STARTERAPP_UNMERGED_RECENT_HOURS:-24}"
QUIET_MINUTES="${STARTERAPP_WORKTREE_QUIET_MINUTES:-15}"
export GIT_OPTIONAL_LOCKS=0

payload=$(cat 2>/dev/null || true)
session=$(printf '%s' "$payload" | sed -n 's/.*"session_id"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
transcript=$(printf '%s' "$payload" | sed -n 's/.*"transcript_path"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
repo="${CLAUDE_PROJECT_DIR:-$PWD}"
state="${TMPDIR:-/tmp}/starterapp-unmerged-${session:-nosession}"
baseline="$state.baseline"
branches="$state.branches"

cd "$repo" 2>/dev/null || exit 0
git rev-parse --git-dir >/dev/null 2>&1 || exit 0
git rev-parse --verify --quiet origin/main >/dev/null 2>&1 || exit 0
common=$(cd "$(git rev-parse --git-common-dir)" && pwd)
primary=$(git worktree list --porcelain | awk '/^worktree /{print $2; exit}')
now=$(date +%s)

mtime() { local t; t=$(stat -c %Y "$1" 2>/dev/null || stat -f %m "$1" 2>/dev/null); case $t in ''|*[!0-9]*) echo 0 ;; *) echo "$t" ;; esac; }

# tree, branch (empty when detached) and sha for every worktree but the primary checkout, separated
# by the unit separator: a tab is whitespace to `read`, which would collapse the empty branch field.
US=$'\037'
worktrees() {
    git worktree list --porcelain | awk '
        /^worktree /{t=$2; b=""; h=""}
        /^HEAD /{h=$2}
        /^branch /{b=$2; sub("refs/heads/","",b)}
        /^$/{if (t!="") print t"\037"b"\037"h; t=""}
        END{if (t!="") print t"\037"b"\037"h}' | awk -F'\037' -v p="$primary" '$1!=p'
}

# The last moment anyone touched a worktree: its HEAD and index moving, the newest file it has
# uncommitted, and — only when it carries commits of its own — its last commit. A merged worktree's
# last commit is main's, which says nothing about whether anyone is still using the worktree.
# Reading status with GIT_OPTIONAL_LOCKS=0 does not refresh the index, so this hook never counts as
# activity itself.
activity() {
    local tree="$1" sha="$2" own="${3:-0}" newest=0 t name admin
    if (( own > 0 )); then t=$(git log -1 --format=%ct "$sha" 2>/dev/null || echo 0); (( t > newest )) && newest=$t; fi
    admin=$(git -C "$tree" rev-parse --git-dir 2>/dev/null)
    for f in "$admin/HEAD" "$admin/index"; do t=$(mtime "$f"); (( t > newest )) && newest=$t; done
    while IFS= read -r name; do
        t=$(mtime "$tree/$name"); (( t > newest )) && newest=$t
    done < <(git -C "$tree" status --porcelain 2>/dev/null | cut -c4- | sed 's/.* -> //')
    echo "$newest"
}

record_baseline() {
    worktrees > "$baseline" 2>/dev/null
    git for-each-ref --format='%(refname:short)' refs/heads > "$branches" 2>/dev/null
}

# Ours only when the worktree and its branch both appeared after SessionStart and this session's
# transcript (or a subagent's) holds the command that created the worktree.
created_here() {
    local tree="$1" branch="$2" name="${1##*/}"
    [[ -z $(awk -F"$US" -v t="$tree" '$1==t' "$baseline") ]] || return 1
    [[ -z "$branch" ]] || ! grep -qxF -- "$branch" "$branches" 2>/dev/null || return 1
    [[ -n "$transcript" && -f "$transcript" ]] || return 1
    { cat "$transcript" "${transcript%.jsonl}"/subagents/*.jsonl 2>/dev/null || true; } | grep -E 'worktree add|EnterWorktree' | grep -qF -- "$name"
}

ago() { local h=$(( ($now - $1) / 3600 )); (( h >= 48 )) && echo "$(( h / 24 )) days" || echo "$h hours"; }

# Key/value pairs -> one JSON object.
emit() { python3 -c 'import json,sys; a=sys.argv[1:]; print(json.dumps(dict(zip(a[::2], a[1::2]))))' "$@"; }

if [[ "${1:-}" == "start" || "${1:-}" == "baseline" ]]; then
    record_baseline

    parked="" leftover="" stale_branches="" stashes=""
    while IFS="$US" read -r tree branch sha; do
        [[ -d "$tree" ]] || continue
        ahead=$(git rev-list --count "origin/main..$sha" 2>/dev/null || echo 0)
        dirty=$(git -C "$tree" status --porcelain 2>/dev/null | wc -l | tr -d ' ')
        last=$(activity "$tree" "$sha" "$ahead")
        label="${branch:-detached at ${sha:0:7}}"
        if (( ahead == 0 && dirty == 0 )); then
            (( now - last >= QUIET_MINUTES * 60 )) && leftover+="  ${tree} (${label}, merged, idle $(ago "$last"))"$'\n'
        elif (( now - last >= STALE_HOURS * 3600 )); then
            what=""; (( ahead > 0 )) && what="${ahead} unmerged commit$([[ $ahead == 1 ]] || echo s)"
            (( dirty > 0 )) && what="${what:+$what, }${dirty} uncommitted file$([[ $dirty == 1 ]] || echo s)"
            parked+="  ${tree} (${label}: ${what}, idle $(ago "$last"))"$'\n'
        fi
    done < <(worktrees)

    checked_out=$(git worktree list --porcelain | sed -n 's|^branch refs/heads/||p')
    while IFS=$'\t' read -r branch when; do
        [[ "$branch" == "main" ]] && continue
        grep -qx "$branch" <<<"$checked_out" && continue
        ahead=$(git rev-list --count "origin/main..refs/heads/$branch" 2>/dev/null || echo 0)
        if (( ahead == 0 )); then
            stale_branches+="  ${branch} (merged; delete it)"$'\n'
        elif (( now - when >= STALE_HOURS * 3600 )); then
            stale_branches+="  ${branch} (${ahead} unmerged commit$([[ $ahead == 1 ]] || echo s), last $(ago "$when") ago, no worktree)"$'\n'
        fi
    done < <(git for-each-ref --format='%(refname:short)%09%(committerdate:unix)' refs/heads)

    while IFS=$'\t' read -r ref when subject; do
        (( now - when >= STALE_HOURS * 3600 )) && stashes+="  ${ref} ($(ago "$when") old): ${subject}"$'\n'
    done < <(git stash list --format='%gd%x09%ct%x09%gs' 2>/dev/null)

    [[ -z "$parked$leftover$stale_branches$stashes" ]] && exit 0
    report="A worktree ends merged or abandoned, never parked (AGENTS.md). Resolve these before starting new work; each is the user's or another session's unless you made it, so ask before discarding anything you did not make:"$'\n'
    [[ -n "$parked" ]] && report+="Parked (merge it: push to main; or abandon it: git worktree remove, git branch -D):"$'\n'"$parked"
    [[ -n "$leftover" ]] && report+="Merged but not removed (git worktree remove <path>, then git branch -d <branch>):"$'\n'"$leftover"
    [[ -n "$stale_branches" ]] && report+="Branches with no worktree:"$'\n'"$stale_branches"
    [[ -n "$stashes" ]] && report+="Old stashes (apply what is worth keeping to a branch, then drop):"$'\n'"$stashes"
    python3 -c 'import json,sys; r=sys.argv[1]; print(json.dumps({"systemMessage": r, "hookSpecificOutput": {"hookEventName": "SessionStart", "additionalContext": r}}))' "$report"
    exit 0
fi

# No baseline means this session started before the hook did: record one and stay quiet rather
# than blame this session for whatever was already there.
if [[ ! -f "$baseline" ]]; then
    record_baseline
    exit 0
fi

recent_cutoff=$(( now - RECENT_HOURS * 3600 ))
ours="" theirs="" left=""
while IFS="$US" read -r tree branch sha; do
    [[ -d "$tree" ]] || continue
    ahead=$(git rev-list --count "origin/main..$sha" 2>/dev/null) || continue
    if (( ahead > 0 )) && [[ -n "$branch" ]]; then
        committed=$(git log -1 --format=%ct "$sha" 2>/dev/null) || continue
        (( committed >= recent_cutoff )) || continue
        # Still being worked in (an agent mid-task commits as it goes): not finished, so not yet
        # something to merge.
        last=$(activity "$tree" "$sha" "$ahead")
        (( now - last >= QUIET_MINUTES * 60 )) || continue
        line="  ${branch} (${ahead} commit$([[ $ahead == 1 ]] || echo s), in ${tree##*/})"$'\n'
        if created_here "$tree" "$branch"; then ours+="$line"; else theirs+="$line"; fi
        continue
    fi
    # A worktree this session created, now merged and clean and quiet, is ours to remove.
    created_here "$tree" "$branch" || continue
    dirty=$(git -C "$tree" status --porcelain 2>/dev/null | wc -l | tr -d ' ')
    (( dirty == 0 )) || continue
    last=$(activity "$tree" "$sha" 0)
    (( now - last >= QUIET_MINUTES * 60 )) || continue
    left+="  ${tree}${branch:+ (${branch})}"$'\n'
done < <(worktrees)

if [[ -n "$ours$left" ]]; then
    message=""
    [[ -n "$ours" ]] && message+="main is behind work committed in this session:
${ours}Fast-forward it from the worktree: git push origin <branch>:main, then
git merge --ff-only origin/main in ${repo} while it is on main and clean."$'\n'
    [[ -n "$left" ]] && message+="This session left worktrees that are merged and done; remove them (a worktree ends merged or abandoned):
${left}git worktree remove <path>, then git branch -d <branch>."
    if [[ ! -f "$state" ]]; then
        : > "$state"
        emit decision block reason "$message"
        exit 0
    fi
    emit systemMessage "$message"
    exit 0
fi

# Someone else's branch: worth saying once, never worth blocking or merging.
if [[ -n "$theirs" && ! -f "$state" ]]; then
    : > "$state"
    emit systemMessage "Unmerged work from another session (not yours to merge):
${theirs}"
fi
exit 0
