import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Proposal } from '@/lib/assistant'
import { ProposalCard } from './assistant-panel'

const ORDER = '0193a000-0000-7000-8000-0000000000a1'

const state = vi.hoisted(() => ({
  scope: 'customers:read customers:write orders:read orders:write assistant:use',
  sent: [] as unknown[],
}))

vi.mock('@tanstack/react-router', () => ({ useNavigate: () => vi.fn() }))
vi.mock('@/lib/session', () => ({
  useSession: () => ({
    scopes: String(state.scope ?? '')
      .split(' ')
      .filter(Boolean),
  }),
}))
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }))
vi.mock('@/lib/assistant', () => ({
  pageFor: () => undefined,
  send: (proposal: unknown) => {
    state.sent.push(proposal)
    return Promise.resolve({ id: 'made-1' })
  },
}))

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function renderCard(proposal: Proposal, onConfirmed = vi.fn(), onDone = vi.fn()) {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ProposalCard proposal={proposal} onDone={onDone} onConfirmed={onConfirmed} />
    </QueryClientProvider>,
  )
  return { onConfirmed, onDone }
}

const request: Extract<Proposal, { kind: 'request' }> = {
  kind: 'request',
  title: 'Add Grace Hopper as a customer',
  method: 'POST',
  path: '/api/v1/customers',
  summary: 'Create a new customer',
  scope: 'customers:write',
  pathParams: {},
  body: { name: 'Grace Hopper' },
  fields: [
    { name: 'name', required: true, kind: 'text', options: [] },
    { name: 'email', required: true, kind: 'text', options: [] },
  ],
}

describe('the request card', () => {
  beforeEach(() => {
    state.scope = 'customers:read customers:write orders:read orders:write assistant:use'
    state.sent = []
  })

  it('asks for what the model left out, and sends the request only on confirm', async () => {
    const { onConfirmed } = renderCard(request)

    const card = screen.getByTestId('assistant-proposal')
    expect(card).toHaveTextContent('Add Grace Hopper as a customer')
    expect(card).toHaveTextContent('POST /api/v1/customers')
    expect(card).toHaveTextContent('Still needed: email')
    expect(screen.getByRole('button', { name: 'Confirm' })).toBeDisabled()

    fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'grace@example.com' } })
    fireEvent.click(screen.getByRole('button', { name: 'Confirm' }))

    await waitFor(() =>
      expect(state.sent).toEqual([{ ...request, body: { name: 'Grace Hopper', email: 'grace@example.com' } }]),
    )
    expect(onConfirmed).toHaveBeenCalledWith(expect.objectContaining({ said: 'Add Grace Hopper as a customer: done.' }))
  })

  it('cannot be confirmed by a token without the scope the route needs, and says which', () => {
    state.scope = 'customers:read assistant:use'
    renderCard({ ...request, body: { name: 'Grace Hopper', email: 'grace@example.com' } })

    expect(screen.getByRole('button', { name: 'Confirm' })).toBeDisabled()
    expect(screen.getByText(/needs the customers:write scope/)).toBeInTheDocument()
    expect(state.sent).toEqual([])
  })
})

describe('the cancel-order card', () => {
  const calls: string[] = []

  beforeEach(() => {
    state.scope = 'customers:read orders:read orders:write assistant:use'
    calls.length = 0
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request
      const url = new URL(request.url)
      calls.push(`${request.method} ${url.pathname}`)
      if (request.method === 'GET' && url.pathname === `/api/v1/orders/${ORDER}`)
        return json({
          id: ORDER,
          customerId: 7,
          orderDate: '2026-11-02T03:00:00Z',
          status: 'Confirmed',
          totalIncludingGst: 55,
          items: [{ productName: 'Widget', quantity: 2 }],
        })
      if (url.pathname === '/api/v1/customers/7') return json({ id: 7, name: 'Ada Lovelace' })
      if (request.method === 'POST' && url.pathname === `/api/v1/orders/${ORDER}/cancel`)
        return json({ id: ORDER, status: 'Cancelled' })
      return json({ title: 'Not found' }, 404)
    })
  })
  afterEach(() => vi.restoreAllMocks())

  it('shows the order from the records and cancels it only on confirm', async () => {
    const { onConfirmed } = renderCard({ kind: 'cancel-order', orderId: ORDER })

    const card = await screen.findByTestId('assistant-proposal')
    await waitFor(() => expect(card).toHaveTextContent('Ada Lovelace'))
    expect(card).toHaveTextContent('2 × Widget')
    expect(card).toHaveTextContent('Confirmed')
    expect(calls).not.toContain(`POST /api/v1/orders/${ORDER}/cancel`)

    fireEvent.click(screen.getByRole('button', { name: 'Confirm cancelling' }))

    await waitFor(() => expect(calls).toContain(`POST /api/v1/orders/${ORDER}/cancel`))
    await waitFor(() => expect(onConfirmed).toHaveBeenCalledWith(expect.objectContaining({ said: 'Order cancelled.' })))
  })

  it('cannot be confirmed without orders:write', async () => {
    state.scope = 'orders:read assistant:use'
    renderCard({ kind: 'cancel-order', orderId: ORDER })

    await screen.findByTestId('assistant-proposal')

    expect(screen.getByRole('button', { name: 'Confirm cancelling' })).toBeDisabled()
  })

  it('says so and changes nothing when the order cannot be found', async () => {
    renderCard({ kind: 'cancel-order', orderId: '0193a000-0000-7000-8000-0000000000ff' })

    expect(await screen.findByText(/could not be found\. Nothing was changed/)).toBeInTheDocument()
    expect(calls.filter((call) => call.startsWith('POST'))).toEqual([])
  })
})
