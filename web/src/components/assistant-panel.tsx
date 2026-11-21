import { RouteFields, missing, sendable, words } from '@/components/assistant-form'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { type ReactNode, useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Callout } from '@/components/ui/primitives'
import { type AssistantArea, pageFor, type Proposal, send } from '@/lib/assistant'
import { day } from '@/lib/format'
import { useAuth } from 'react-oidc-context'

// What a confirmed card did, for a caller that keeps the conversation going: what to show, what to tell the model, and
// how to open the record. A caller that gives no onConfirmed gets the card's own behaviour, which opens the record.
export interface Confirmed {
  said: string
  happened: string
  refused?: boolean
  open?: () => void
  // The change as it was sent and the id of what it made, for the steps that follow it.
  made?: {
    method: string
    path: string
    pathParams: Record<string, string>
    body: Record<string, unknown> | null
    id?: string
  }
}
type AfterConfirm = ((done: Confirmed) => void) | undefined

export function ProposalCard({
  proposal,
  area = 'app',
  onDone,
  onConfirmed,
}: {
  proposal: Proposal
  area?: AssistantArea
  onDone: () => void
  onConfirmed?: (done: Confirmed) => void
}) {
  return <RequestCard proposal={proposal} area={area} onDone={onDone} onConfirmed={onConfirmed} />
}

function Card({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section
      data-testid="assistant-proposal"
      aria-label={title}
      className="space-y-3 rounded-lg border border-line bg-surface p-3"
    >
      <h3 className="text-2xs font-medium tracking-[0.08em] text-ink-tertiary uppercase">Proposed: {title}</h3>
      {children}
    </section>
  )
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex gap-3">
      <dt className="w-24 shrink-0 text-ink-tertiary">{label}</dt>
      <dd className="min-w-0 flex-1">{children}</dd>
    </div>
  )
}

function shown(value: unknown) {
  if (value === true) return 'Yes'
  if (value === false) return 'No'
  if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)) return day(value)
  if (typeof value === 'object') return JSON.stringify(value)
  return String(value)
}

function RequestCard({
  proposal,
  area,
  onDone,
  onConfirmed,
}: {
  onConfirmed: AfterConfirm
  proposal: Extract<Proposal, { kind: 'request' }>
  area: AssistantArea
  onDone: () => void
}) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const auth = useAuth()
  const allowed = String(auth.user?.scope ?? '')
    .split(' ')
    .includes(proposal.scope)
  // What will be sent: what the assistant or an earlier step filled in, as the person has since changed it.
  const [values, setValues] = useState<Record<string, unknown>>(proposal.body ?? {})
  const body = () => {
    return proposal.fields.length === 0 && proposal.body === null ? null : sendable(proposal.fields, values)
  }
  const fields = proposal.fields
  const request = useMutation({
    mutationFn: () => send({ ...proposal, body: body() }),
    onSuccess: (made) => {
      void queryClient.invalidateQueries()
      if (!onConfirmed) toast.success(`${proposal.title}: done`)
      const page = pageFor(proposal, area)
      const id = made?.id
      const open = page && id ? () => void navigate({ to: page, params: { id } }) : undefined
      if (onConfirmed)
        return onConfirmed({
          said: `${proposal.title}: done.`,
          happened: `Confirmed and done: ${proposal.method} ${proposal.path}${id ? ` made the record with id ${id}` : ' succeeded'}.`,
          open,
          made: { method: proposal.method, path: proposal.path, pathParams: proposal.pathParams, body: body(), id },
        })
      onDone()
      open?.()
    },
  })
  const named = Object.entries(proposal.pathParams)
  // A route the product has no field list for shows what the assistant gave, as it gave it.
  const loose =
    proposal.fields.length === 0 ? Object.entries(proposal.body ?? {}).filter(([, value]) => value != null) : []
  const waiting = missing(fields, values)

  return (
    <Card title={proposal.title}>
      <p className="text-ink-secondary">{proposal.summary}</p>
      {named.length + loose.length > 0 ? (
        <dl className="space-y-1.5">
          {[...named, ...loose].map(([name, value]) => (
            <Row key={name} label={words(name)}>
              {shown(value)}
            </Row>
          ))}
        </dl>
      ) : null}
      <RouteFields
        fields={fields}
        values={values}
        pathParams={proposal.pathParams}
        area={area}
        onChange={(name, value) => setValues((current) => ({ ...current, [name]: value }))}
      />
      <p className="font-mono text-2xs text-ink-tertiary">
        {proposal.method} {proposal.path}
      </p>
      {request.isError ? (
        <Callout tone="negative">{`${request.error.message} Nothing was changed.`}</Callout>
      ) : null}
      {allowed ? null : (
        <Callout tone="caution">
          This needs the {proposal.scope} scope, which your sign-in does not have.
        </Callout>
      )}
      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="accent"
          disabled={!allowed || request.isPending || waiting.length > 0}
          onClick={() => request.mutate()}
        >
          Confirm
        </Button>
        <Button type="button" variant="ghost" onClick={onDone}>
          Cancel
        </Button>
        {waiting.length > 0 ? (
          <span className="basis-full text-sm text-ink-tertiary sm:basis-auto">
            Still needed: {waiting.map((field) => (field.label ?? words(field.name ?? '')).toLowerCase()).join(', ')}
          </span>
        ) : null}
      </div>
    </Card>
  )
}
