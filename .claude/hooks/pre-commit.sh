#!/usr/bin/env bash
# Format, build and test before an agent's `git commit`, in the tree the commit lands in.
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
printf '%s' "$command" | grep -qE -- '(^|[[:space:]])(-n|--no-verify)([[:space:]]|$)' && exit 0

# The tree is every `cd <dir>` before the commit, in order, then any `git -C <dir>` on the commit
# itself. A path the shell would expand (a variable, a glob) cannot be followed from here; the
# check then runs in the session's tree and says so.
target=$cwd
enter() {
  local dir=$1
  dir=${dir//\"/}; dir=${dir//\'/}
  case "$dir" in *'$'*|*'`'*|*'*'*|*'?'*|''|-) return 1 ;; esac
  case "$dir" in /*) ;; "~"*) dir="$HOME${dir#\~}" ;; *) dir="$target/$dir" ;; esac
  [ -d "$dir" ] || return 1
  target=$(cd "$dir" && pwd)
}
before_commit=$(printf '%s' "$command" | sed -E 's/(^|[^[:alnum:]_-])git([[:space:]]+-[cC][[:space:]]+[^[:space:]]+)*[[:space:]]+commit.*//')
resolved=1
while IFS= read -r dir; do
  [ -n "$dir" ] || continue
  enter "$dir" || { resolved=0; break; }
done < <(printf '%s' "$before_commit" | grep -oE '(^|[;&|(][[:space:]]*)(cd|pushd)[[:space:]]+("[^"]*"|'"'"'[^'"'"']*'"'"'|[^;&|[:space:]]+)' | sed -E 's/^[;&|(]*[[:space:]]*(cd|pushd)[[:space:]]+//')
c_dir=$(printf '%s' "$command" | grep -oE 'git([[:space:]]+-[cC][[:space:]]+[^[:space:]]+)+[[:space:]]+commit' | tail -1 | grep -oE -- '-C[[:space:]]+[^[:space:]]+' | tail -1 | sed -E 's/-C[[:space:]]+//')
if [ -n "$c_dir" ]; then
  enter "$c_dir" || resolved=0
fi
if [ "$resolved" = 0 ]; then
  echo "pre-commit: could not follow the command's directory; checking $cwd instead" >&2
  target=$cwd
fi

cd "$target" 2>/dev/null || exit 0
root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
cd "$root" || exit 2
[ -f tests/StarterApp.Tests/StarterApp.Tests.csproj ] || exit 0

dotnet --version >/dev/null 2>&1 || { export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"; }
echo "pre-commit: checking $root" >&2
dotnet format --verify-no-changes --verbosity minimal --no-restore \
  && dotnet build --no-restore \
  && dotnet test tests/StarterApp.Tests/StarterApp.Tests.csproj --no-build \
  || exit 2
