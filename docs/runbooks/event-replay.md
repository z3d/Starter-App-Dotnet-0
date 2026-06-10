# Event Replay Runbook

Operator recovery for the two places an event can strand after a failure. Both paths flow through
the normal pipeline — payload capture, audit, and subscriber replay-tolerance — never a side
channel, and replayed publishes carry `Replay=true` / `ReplayCount=<n>` Service Bus application
properties so audit can distinguish a republish from a first delivery.

## 1. Errored outbox rows (event never reached Service Bus)

`OutboxProcessor` marks a row errored after its retry budget is exhausted; errored rows are kept
but skipped on subsequent polls. Once the underlying fault is fixed, reset them with the
sanctioned replay verb (no hand-written SQL):

```bash
# Single message (the id is in the processor's error logs and the outbox_messages table)
dotnet run --project src/StarterApp.DbMigrator -- replay-outbox --id <outbox-message-guid>

# Everything currently errored (after a systemic fault, e.g. archive store outage under FailClosed)
dotnet run --project src/StarterApp.DbMigrator -- replay-outbox --all-errored
```

The verb only touches rows that are unprocessed AND errored: it clears the error and any stale
claim, restores the retry budget, and stamps `replay_count` / `replayed_on_utc`. The processor
republishes on its next poll. Exit codes: `0` reset performed, `1` the targeted row was not
eligible (missing, already processed, or not errored), `2` usage error.

In container deployments run the same verb from the DbMigrator image with the production
`ConnectionStrings__database` (the verb never runs migrations; the bare `replay-outbox` argument
selects it).

## 2. Dead-lettered subscription messages (event reached the topic; a consumer never processed it)

Subscriptions dead-letter on expiry (`DeadLetteringOnMessageExpiration`) and on
`MaxDeliveryCount` exhaustion. The event was published, so the outbox row is processed — recovery
happens on the Service Bus side:

1. Inspect the subscription's DLQ (Service Bus Explorer in the Azure portal, or
   `az servicebus topic subscription show` for counts). The dead-letter reason and the
   `EventType` / correlation-id application properties identify the message.
2. Fix the consumer fault first — replaying into a still-broken consumer just dead-letters again.
3. Re-submit the dead-lettered message to the `domain-events` topic with its original body and
   application properties (Service Bus Explorer's resubmit, or a copy-and-send). Subscribers are
   idempotent against redelivery and out-of-order arrival by contract, so a replayed copy is safe.
4. If the DLQ message is gone (DLQ TTL, accidental purge), reconstruct it from the archive: the
   payload-capture blobs hold the full outbound message body under
   `archive/{date}/{hour}/{minute}/{correlationId}.jsonl` (find the blob via the audit row or the
   `entity-index/` pointers), and the outbox row's `payload` column is a second copy.
5. Tag any manually re-submitted message with `Replay=true` so audit stays truthful.

Cross-reference: `docs/ROADMAP.md` (incident knowledge base) is the place to record recurring
DLQ patterns and their verified default actions.
