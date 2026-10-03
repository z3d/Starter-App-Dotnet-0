---
paths:
  - ".editorconfig"
---
# .editorconfig

One trap when adding a section.

- **`.editorconfig` sections written as `**/*.cs` do not match files at the directory root.** `[src/X/**/*.cs]` misses `src/X/Program.cs`; add a sibling `[src/X/*.cs]` section, as the test-project sections do.
