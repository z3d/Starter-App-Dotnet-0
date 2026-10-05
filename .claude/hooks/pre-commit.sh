#!/usr/bin/env bash
# Format, build and test before an agent's `git commit`, in the tree the commit lands in.
# A permission-rule `if: Bash(git commit*)` only matches a command that starts with `git commit`, so
# `cd ../wt && git commit` and `git -C ../wt commit` used to skip the check; and the hook's own
# directory is the session's, not the worktree being committed, so the check has to move there.
set -uo pipefail

payload=$(cat 2>/dev/null || true)
read_field() {
  printf '%s' "$payload" | python3 -c 'import json,sys
try: d=json.load(sys.stdin)
except Exception: sys.exit(0)
v=d
for k in sys.argv[1].split("."): v=v.get(k,"") if isinstance(v,dict) else ""
print(v)' "$1" 2>/dev/null
}

command=$(read_field tool_input.command)
cwd=$(read_field cwd)
[ -n "$command" ] || command=$payload
[ -n "$cwd" ] || cwd=$PWD

commit_re='(^|[^[:alnum:]_-])git([[:space:]]+-[cC][[:space:]]+[^[:space:]]+)*[[:space:]]+commit([^[:alnum:]-]|$)'
printf '%s' "$command" | grep -qE "$commit_re" || exit 0

# The tree is the last `cd <dir>` before the commit, then any `git -C <dir>` on the commit itself.
target=$cwd
before_commit=$(printf '%s' "$command" | sed -E 's/(^|[^[:alnum:]_-])git([[:space:]]+-[cC][[:space:]]+[^[:space:]]+)*[[:space:]]+commit.*//')
cd_dir=$(printf '%s' "$before_commit" | grep -oE '(^|[;&|(][[:space:]]*)cd[[:space:]]+[^;&|[:space:]]+' | tail -1 | sed -E 's/.*cd[[:space:]]+//')
if [ -n "$cd_dir" ]; then
  case "$cd_dir" in /*) target=$cd_dir ;; "~"*) target="$HOME${cd_dir#\~}" ;; *) target="$target/$cd_dir" ;; esac
fi
c_dir=$(printf '%s' "$command" | grep -oE 'git([[:space:]]+-[cC][[:space:]]+[^[:space:]]+)+[[:space:]]+commit' | tail -1 | grep -oE -- '-C[[:space:]]+[^[:space:]]+' | tail -1 | sed -E 's/-C[[:space:]]+//')
if [ -n "$c_dir" ]; then
  case "$c_dir" in /*) target=$c_dir ;; "~"*) target="$HOME${c_dir#\~}" ;; *) target="$target/$c_dir" ;; esac
fi

target=${target//\"/}
target=${target//\'/}
cd "$target" 2>/dev/null || { echo "pre-commit: cannot enter $target" >&2; exit 2; }
root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
cd "$root" || exit 2
[ -f tests/StarterApp.Tests/StarterApp.Tests.csproj ] || exit 0

dotnet --version >/dev/null 2>&1 || { export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"; }
echo "pre-commit: checking $root" >&2
dotnet format --verify-no-changes --verbosity minimal --no-restore \
  && dotnet build --no-restore \
  && dotnet test tests/StarterApp.Tests/StarterApp.Tests.csproj --no-build \
  || exit 2
