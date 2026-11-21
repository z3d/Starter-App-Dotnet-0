import { RouteFields, missing, sendable, words } from '@/components/assistant-form'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { type ReactNode, useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Callout } from '@/components/ui/primitives'
import { call, client } from '@/lib/api'
import { type AssistantArea, pageFor, type Proposal, send } from '@/lib/assistant'
import { day, money } from '@/lib/format'
import { useAuth } from 'react-oidc-context'

export interface Confirmed {
  said: string
  happened: string
  refused?: boolean
  open?: () => void
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
  if (proposal.kind === 'cancel-order')
    return <CancelOrderCard proposal={proposal} onDone={onDone} onConfirmed={onConfirmed} />
  return <RequestCard proposal={proposal} area={area} onDone={onDone} onConfirmed={onConfirmed} />
}

function useScopes() {
  const auth = useAuth()
  return String(auth.user?.scope ?? '').split(' ')
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

const CANCEL_SCOPE = 'orders:write'

function CancelOrderCard({
  proposal,
  onDone,
  onConfirmed,
}: {
  proposal: Extract<Proposal, { kind: 'cancel-order' }>
  onDone: () => void
  onConfirmed: AfterConfirm
}) {
  const queryClient = useQueryClient()
  const allowed = useScopes().includes(CANCEL_SCOPE)
  const order = useQuery({
    queryKey: ['orders', proposal.orderId],
    queryFn: () => call(client.GET('/api/v1/orders/{id}', { params: { path: { id: proposal.orderId } } })),
  })
  const customerId = Number(order.data?.customerId ?? 0)
  const customer = useQuery({
    queryKey: ['customers', customerId],
    queryFn: () => call(client.GET('/api/v1/customers/{id}', { params: { path: { id: customerId } } })),
    enabled: customerId > 0,
  })
  const cancel = useMutation({
    mutationFn: () => call(client.POST('/api/v1/orders/{id}/cancel', { params: { path: { id: proposal.orderId } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries()
      if (onConfirmed)
        return onConfirmed({
          said: 'Order cancelled.',
          happened: `Confirmed and done: order ${proposal.orderId} was cancelled and its stock put back.`,
        })
      toast.success('Order cancelled')
      onDone()
    },
  })

  if (order.isError)
    return <Callout tone="negative">The assistant named an order that could not be found. Nothing was changed.</Callout>
  if (!order.data) return <p className="text-ink-tertiary">Loading the order…</p>

  return (
    <Card title="Cancel an order">
      <dl className="space-y-1.5">
        <Row label="Customer">{customer.data?.name ?? '…'}</Row>
        <Row label="Placed">{day(order.data.orderDate)}</Row>
        <Row label="Status">{words(order.data.status ?? '')}</Row>
        <Row label="Items">
          {(order.data.items ?? []).map((item) => `${item.quantity} × ${item.productName}`).join(', ') || '—'}
        </Row>
        <Row label="Total">{money(order.data.totalIncludingGst, { cents: true })}</Row>
      </dl>
      <p className="text-ink-secondary">The order is cancelled and its stock is put back.</p>
      {cancel.isError ? <Callout tone="negative">{`${cancel.error.message} Nothing was changed.`}</Callout> : null}
      {allowed ? null : (
        <Callout tone="caution">This needs the {CANCEL_SCOPE} scope, which your sign-in does not have.</Callout>
      )}
      <div className="flex gap-2">
        <Button type="button" variant="accent" disabled={!allowed || cancel.isPending} onClick={() => cancel.mutate()}>
          Confirm cancelling
        </Button>
        <Button type="button" variant="ghost" onClick={onDone}>
          Cancel
        </Button>
      </div>
    </Card>
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
  const allowed = useScopes().includes(proposal.scope)
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
        onChange={(name, value) => setValues((current) => ({ ...current, [name]: value }))}
      />
      <p className="font-mono text-2xs text-ink-tertiary">
        {proposal.method} {proposal.path}
      </p>
      {request.isError ? <Callout tone="negative">{`${request.error.message} Nothing was changed.`}</Callout> : null}
      {allowed ? null : (
        <Callout tone="caution">This needs the {proposal.scope} scope, which your sign-in does not have.</Callout>
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
