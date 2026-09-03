#!/usr/bin/env bash
# Recover the local Service Bus emulator when it stops answering.
#
# Symptoms: AMQP links that time out on publish, consumers that never receive, or
# "BufferQueue not found" in the emulator logs after several AppHost restarts (see the
# emulator-readiness watch-item in docs/ARCHITECTURE_REVIEW.md). Because the AppHost keeps
# the emulator persistent (ContainerLifetime.Persistent), a wedged instance survives
# restarts until its containers are removed.
#
# The emulator is two containers that share state: the broker itself and its SQL Server
# sidecar. Always remove them as a pair — dropping only one leaves the survivor holding
# state the fresh container doesn't know about, which recreates the wedge on next boot.
# The Aspire network they sat on goes too: a stale network is the most common reason a
# clean retry wedges the same way (development-workflow skill, "Recovery from a wedged
# state"). The next AppHost run rebuilds everything from scratch (allow ~a minute for SQL).
#
# Portable to bash 3.2 (macOS /bin/bash): no mapfile/readarray, no associative arrays.
set -euo pipefail

engine="${CONTAINER_ENGINE:-docker}"

pair=()
while IFS= read -r name; do
    [[ -n "$name" ]] && pair+=("$name")
done < <("$engine" ps -a --format '{{.Names}}' | grep -E '^servicebus(-mssql)?-' || true)

if ((${#pair[@]} == 0)); then
    echo "No servicebus emulator containers present; nothing to do."
    exit 0
fi

# Collect the Aspire networks the pair is attached to before the containers disappear. Persistent
# resources (postgres, redis, keycloak, azurite) share the same solution-wide persistent network,
# so a network is only removed once nothing — running or stopped — is still attached to it.
networks=()
for container in "${pair[@]}"; do
    while IFS= read -r network; do
        [[ "$network" == aspire-* ]] && networks+=("$network")
    done < <("$engine" inspect --format '{{range $name, $_ := .NetworkSettings.Networks}}{{println $name}}{{end}}' "$container" 2>/dev/null || true)
done

printf 'Removing: %s\n' "${pair[@]}"
"$engine" rm -f "${pair[@]}" >/dev/null

if ((${#networks[@]} > 0)); then
    for network in $(printf '%s\n' "${networks[@]}" | sort -u); do
        if [[ -n "$("$engine" ps -aq --filter "network=$network")" ]]; then
            echo "Network still has containers attached, left in place: $network"
        elif "$engine" network rm "$network" >/dev/null 2>&1; then
            echo "Removed network: $network"
        else
            echo "Could not remove network (left in place): $network"
        fi
    done
fi

echo "Emulator state cleared — the next AppHost run starts it fresh."
