export const meta = {
  name: 'architect-review',
  description: 'Review the current branch diff with this repo\'s backend architect and security auditor in parallel (rule-adherence per CLAUDE.md + bug/security hunt), then adversarially verify each finding line-by-line. Returns findings tagged real/noise for the main agent\'s final triage against docs/ARCHITECTURE_REVIEW.md.',
  phases: [
    { title: 'Review' },
    { title: 'Verify' },
  ],
}

// args: { scope?: string[]  ("backend" | "security"), changedPaths?: string, planPath?: string }
const a = typeof args === 'string' ? JSON.parse(args) : (args || {})
const scope = (Array.isArray(a.scope) && a.scope.length ? a.scope : ['backend', 'security'])
const changed = a.changedPaths || 'the current branch diff vs origin/main (run `git diff origin/main...HEAD` and `git diff` to see committed + uncommitted changes)'
const planPath = a.planPath || null

const LENSES = {
  backend: { agentType: 'backend-architect', what: 'the .NET backend under src/ — CLAUDE.md rule adherence (CQRS separation, DDD, owner-scope, outbox, concurrency) and real bugs' },
  security: { agentType: 'security-auditor', what: 'backend security — OWASP Top 10 mapped to this stack, gateway-identity verification, owner-scope/IDOR, payload-archive PII handling' },
}

const FINDINGS_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  properties: {
    findings: {
      type: 'array',
      items: {
        type: 'object',
        additionalProperties: false,
        properties: {
          file: { type: 'string' },
          symbol: { type: 'string' },
          line: { type: 'string' },
          severity: { type: 'string', enum: ['critical', 'high', 'medium'] },
          kind: { type: 'string', enum: ['rule', 'bug', 'security'] },
          title: { type: 'string' },
          detail: { type: 'string' },
        },
        required: ['file', 'severity', 'kind', 'title', 'detail'],
      },
    },
  },
  required: ['findings'],
}

const VERDICT_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  properties: {
    real: { type: 'boolean' },
    confidence: { type: 'string', enum: ['high', 'medium', 'low'] },
    evidence: { type: 'string' },
    reason: { type: 'string' },
  },
  required: ['real', 'confidence', 'evidence', 'reason'],
}

const planNote = planPath ? ` The change implements the plan at \`${planPath}\`.` : ''

phase('Review')
const lenses = scope.filter((s) => LENSES[s])
log(`Reviewing ${changed} with: ${lenses.join(', ')}`)

const results = await pipeline(
  lenses,
  (s) =>
    agent(
      `Review ${changed} — focus on ${LENSES[s].what}.${planNote} Check adherence to the rules in CLAUDE.md and the matching .claude/skills, AND hunt for real bugs/security issues. Do not re-report anything already marked resolved in docs/ARCHITECTURE_REVIEW.md. Report each as a structured finding (file, symbol, line, severity, kind, title, detail). Be specific and verifiable — each finding is adversarially checked line-by-line, so do not pad with speculation.`,
      { agentType: LENSES[s].agentType, label: `review:${s}`, phase: 'Review', schema: FINDINGS_SCHEMA },
    ),
  // As soon as a lens returns, verify each of its findings adversarially (no barrier between lenses).
  (review, s) =>
    parallel(
      (review?.findings || []).map((f) => () =>
        agent(
          `Adversarially verify this finding by reading the actual code line-by-line. Decide REAL (a genuine bug or rule violation you can prove from the source) vs NOISE (unprovable, handled elsewhere — e.g. an existing convention test/domain guard/validator/pipeline behavior — or a style nit). Default to NOISE if uncertain.\nFinding (from ${s} review):\n${JSON.stringify(f, null, 2)}`,
          { agentType: 'findings-verifier', label: `verify:${f.file || '?'}`, phase: 'Verify', schema: VERDICT_SCHEMA },
        ).then((v) => ({ finding: f, lens: s, verdict: v })),
      ),
    ),
)

const verified = results.filter(Boolean).flat().filter(Boolean)
const real = verified.filter((r) => r.verdict && r.verdict.real === true)
log(`${verified.length} finding(s) verified; ${real.length} look real.`)

return {
  scope: lenses,
  rawCount: verified.length,
  verifiedReal: real.length,
  // The MAIN agent does the final line-by-line triage and updates docs/ARCHITECTURE_REVIEW.md —
  // these verdicts are a pre-filter, not the decision.
  findings: verified,
}
