---
paths:
  - "src/StarterApp.Api/**/Commands/**"
  - "src/StarterApp.Api/**/Queries/**"
  - "docs/exemplars/**"
---
# Handler exemplars

One trap when a handler's dependencies change.

- **Adding a constructor dependency to every handler moves the consistency fingerprints.** `ConstructorDependencyCount` changes and `ExemplarAlignment_DocumentedDependencyCountsMatchCode` fails until the counts in `docs/exemplars/*/README.md` are updated to match.
