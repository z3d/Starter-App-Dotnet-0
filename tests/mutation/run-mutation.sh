#!/usr/bin/env bash
# Mutation testing with Stryker.NET over the code the unit tests are meant to guard: the sample
# domain and the shared kernel. Slow, so it runs on a schedule and by hand, never in the pre-push
# gate. A derived project adds its own money and compliance projects to PROJECTS.
#
# Usage: tests/mutation/run-mutation.sh            # every project below
#        PROJECTS="StarterApp.SharedKernel.csproj" tests/mutation/run-mutation.sh
# Reports: tests/mutation/reports/<project>/ (HTML and JSON); the score per project is printed at the end.
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
PROJECTS="${PROJECTS:-StarterApp.SharedKernel.csproj StarterApp.Domain.csproj}"
# The unit tests only: the database-backed tests add minutes per mutant and guard other code.
FILTER="${FILTER:-FullyQualifiedName~StarterApp.Tests.Domain}"

dotnet tool restore >/dev/null
# Run beside the test project: at the solution root Stryker switches to solution mode and refuses a test project.
cd "$root/tests/StarterApp.Tests"
summary=""
for project in $PROJECTS; do
  name=${project%.csproj}
  output="$root/tests/mutation/reports/$name"
  rm -rf "$output"
  dotnet stryker \
    --project "$project" \
    --test-case-filter "$FILTER" \
    --reporter html --reporter json --reporter progress \
    --output "$output" \
    --break-at 0
  report=$(find "$output" -name 'mutation-report.json' | head -1)
  score=$(jq -r '[.files[].mutants[]] as $m
    | ($m | map(select(.status == "Killed" or .status == "Timeout")) | length) as $killed
    | ($m | map(select(.status == "Survived" or .status == "NoCoverage")) | length) as $alive
    | if ($killed + $alive) == 0 then "n/a" else (100 * $killed / ($killed + $alive) | floor | tostring) + "%" end
    + " (\($killed) killed, \($alive) survived or not covered)"' "$report")
  summary+="$name: $score"$'\n'
done
printf '\nMutation scores\n%s' "$summary"
