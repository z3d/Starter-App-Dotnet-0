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
# The next AppHost run rebuilds both from scratch (allow roughly a minute for SQL).
set -euo pipefail

engine="${CONTAINER_ENGINE:-docker}"

mapfile -t pair < <("$engine" ps -a --format '{{.Names}}' | grep -E '^servicebus(-mssql)?-' || true)

if ((${#pair[@]} == 0)); then
    echo "No servicebus emulator containers present; nothing to do."
    exit 0
fi

printf 'Removing: %s\n' "${pair[@]}"
"$engine" rm -f "${pair[@]}" >/dev/null
echo "Emulator state cleared — the next AppHost run starts it fresh."
