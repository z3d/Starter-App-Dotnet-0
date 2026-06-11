# Incident Knowledge Base

Machine-readable memory for recurring async-failure investigations (dead-lettered messages,
errored outbox rows, payload-capture failures). Each failure domain keeps one
`knowledge-base.json` in its own subdirectory; the first investigation of a pattern records the
diagnosis once, and every later occurrence is resolved by following the recorded default action
after its verification query confirms the pattern applies.

`KnowledgeBaseConventionTests` validates every knowledge-base file mechanically on each build.

## File shape

```jsonc
{
  "knownPatterns": [
    {
      "id": "kebab-case-stable-id",
      "symptom": "what the operator observes",
      "rootCause": "the verified explanation",
      "defaultAction": "what to do once verification confirms the pattern",
      "verification": {
        "description": "what the query proves",
        "query": "the exact SQL/KQL/log query to run BEFORE acting"
      },
      "occurrences": 1,
      "lastOccurrence": "2026-06-10"
    }
  ],
  "knownDefects": [
    {
      "id": "kebab-case-stable-id",
      "description": "the code defect this domain keeps hitting",
      "location": "file or component",
      "fixCommit": "<sha>",            // required unless acceptedLimitationRef is set
      "acceptedLimitationRef": ""       // reference into docs/ARCHITECTURE_REVIEW.md
    }
  ],
  "verificationTemplates": [
    { "id": "...", "description": "...", "query": "reusable query for this domain" }
  ],
  "investigationHistory": [
    { "date": "2026-06-10", "summary": "what was investigated, decided, and learned" }
  ]
}
```

## Rules

- **Verification before action.** A pattern's `defaultAction` may only be taken after its
  `verification.query` confirms the pattern applies to the current incident — pattern names are
  hypotheses, queries are evidence.
- **No quietly aging bugs.** Every `knownDefects` entry must carry either a non-empty `fixCommit`
  or a non-empty `acceptedLimitationRef` pointing at the corresponding accepted-limitation entry
  in `docs/ARCHITECTURE_REVIEW.md`. The convention test fails the build otherwise: a knowledge
  base is for resolving incidents, not for cataloguing defects nobody decided about.
- **Update after every investigation.** Append to `investigationHistory`, bump `occurrences` on
  matched patterns, and add genuinely new patterns — the value compounds only if the file stays
  current.
- **No payloads or PII.** Reference correlation ids, blob names, and queries — never inline
  captured payload content (that lives in the archive/audit blobs).

Related: `docs/runbooks/event-replay.md` (the recovery mechanics this knowledge base decides
between), `docs/ARCHITECTURE_REVIEW.md` (where accepted limitations live).
