import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AssistantOutcome } from '@/lib/assistant'
import { AssistantHome } from './assistant-home'

const state = vi.hoisted(() => ({
  outcomes: [] as AssistantOutcome[],
  asked: [] as { messages: unknown; area: string }[],
}))

vi.mock('@/lib/assistant', () => ({
  reply: (outcome: { messages: unknown[] }, text: string) => [...outcome.messages, { role: 'user', text }],
  confirmed: (outcome: { messages: unknown[] }, happened: string, refused: boolean) => [
    ...outcome.messages,
    { role: 'tool', happened, refused },
  ],
  ask: (messages: unknown, area: string) => {
    state.asked.push({ messages, area })
    return Promise.resolve(state.outcomes.shift())
  },
}))
vi.mock('@/lib/assistant-routes', async (original) => ({
  ...(await original<typeof import('@/lib/assistant-routes')>()),
  useAssistantRoutes: () => ({
    data: [
      { method: 'POST', path: '/api/v1/customers', summary: 'Create a new customer', scope: 'customers:write' },
      { method: 'POST', path: '/api/v1/products', summary: 'Create a new product', scope: 'products:write' },
    ],
  }),
}))
vi.mock('@/components/assistant-panel', () => ({
  ProposalCard: ({
    proposal,
    area,
    onDone,
    onConfirmed,
  }: {
    proposal: { kind: string; title?: string; method?: string; path?: string; pathParams?: Record<string, string> }
    area: string
    onDone: () => void
    onConfirmed: (done: object) => void
  }) => (
    <section data-testid="assistant-proposal">
      {proposal.kind} for {area}: {proposal.title} {proposal.method} {proposal.path}
      <button type="button" onClick={onDone}>
        Cancel
      </button>
      <button
        type="button"
        onClick={() =>
          onConfirmed({
            said: `${proposal.title}: done.`,
            happened: 'Confirmed and done.',
            made: { method: proposal.method, path: proposal.path, pathParams: proposal.pathParams, body: {}, id: 41 },
          })
        }
      >
        Confirm
      </button>
    </section>
  ),
}))

const cancelOrder: AssistantOutcome = {
  messages: [],
  reply: null,
  proposal: { kind: 'cancel-order', orderId: '0193a000-0000-7000-8000-0000000000a1' },
}

function renderHome() {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <AssistantHome area="app" />
    </QueryClientProvider>,
  )
}

const say = (words: string) => {
  fireEvent.change(screen.getByLabelText('Ask the assistant'), { target: { value: words } })
  fireEvent.submit(screen.getByLabelText('Ask the assistant'))
}

describe('AssistantHome', () => {
  beforeEach(() => {
    state.outcomes = []
    state.asked = []
  })

  it('opens on one box and things that happen; one the product has a card for opens it without asking the model', async () => {
    state.outcomes = [{ messages: [], reply: 'Which customer?', proposal: null }]
    renderHome()

    expect(screen.getByRole('heading', { name: 'What do you need done?' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'A new customer' }))

    expect(await screen.findByTestId('assistant-proposal')).toHaveTextContent('Add a customer POST /api/v1/customers')
    expect(state.asked).toEqual([])

    fireEvent.click(screen.getByRole('button', { name: 'Start something new' }))
    fireEvent.click(screen.getByRole('button', { name: 'A customer wants to cancel an order' }))

    expect(await screen.findByTestId('assistant-reply')).toHaveTextContent('Which customer?')
    expect(state.asked).toEqual([
      { messages: [{ role: 'user', text: 'A customer wants to cancel an order' }], area: 'app' },
    ])
  })

  it('shows what the assistant needs as a form, and sends the answers by field with the words the person picked', async () => {
    const asked: AssistantOutcome = {
      messages: [{ role: 'user', text: "Cancel Ada's order" }],
      reply: null,
      proposal: null,
      question: {
        question: 'Which Ada do you mean?',
        fields: [
          {
            name: 'customer',
            label: 'Customer',
            kind: 'choice',
            optional: false,
            options: [
              { value: '7', label: 'Ada Lovelace', hint: null },
              { value: '9', label: 'Ada Byron', hint: null },
            ],
          },
          { name: 'placed', label: 'Day it was placed', kind: 'date', optional: false, options: [] },
        ],
      },
    }
    state.outcomes = [asked, cancelOrder]
    renderHome()
    say("Cancel Ada's order")

    const form = await screen.findByTestId('assistant-question')
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled()
    fireEvent.click(screen.getByLabelText('Ada Lovelace'))
    fireEvent.change(screen.getByLabelText('Day it was placed'), { target: { value: '2026-11-02' } })
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }))

    expect(await screen.findByTestId('assistant-proposal')).toHaveTextContent('cancel-order for app')
    expect(form).not.toBeInTheDocument()
    expect(screen.getByText('Customer: Ada Lovelace · Day it was placed: 2026-11-02')).toBeInTheDocument()
    expect(state.asked[1]?.messages).toEqual([
      { role: 'user', text: "Cancel Ada's order" },
      { role: 'user', text: '{"customer":"7","placed":"2026-11-02"}' },
    ])
  })

  it('keeps a confirmed card in the conversation and offers what usually comes next, without the model', async () => {
    renderHome()
    fireEvent.click(screen.getByRole('button', { name: 'A new product' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Confirm' }))

    expect(await screen.findByTestId('assistant-done')).toHaveTextContent('Add a product: done.')
    fireEvent.click(screen.getByRole('button', { name: 'Add another product' }))

    expect(await screen.findByTestId('assistant-proposal')).toHaveTextContent(
      'Add another product POST /api/v1/products',
    )
    expect(state.asked).toEqual([])
  })

  it('sets an open card aside when the person moves on, so only the newest card can be confirmed', async () => {
    state.outcomes = [cancelOrder, { messages: [], reply: 'You have three products.', proposal: null }]
    renderHome()

    say("Cancel Ada's last order")
    await screen.findByTestId('assistant-proposal')
    say('How many products do we have?')

    expect(await screen.findByText('Set aside: nothing was changed.')).toBeInTheDocument()
    expect(await screen.findByTestId('assistant-reply')).toHaveTextContent('You have three products.')
    expect(screen.queryByTestId('assistant-proposal')).not.toBeInTheDocument()
  })

  it('carries a question and its answer on as one conversation, and starts a new one after a card is closed', async () => {
    const question: AssistantOutcome = {
      messages: [
        { role: 'user', text: 'Cancel an order' },
        { role: 'assistant', blocks: [{ type: 'text', text: 'Whose order?' }] },
      ],
      reply: 'Whose order?',
      proposal: null,
    }
    state.outcomes = [question, cancelOrder, question]
    renderHome()

    say('Cancel an order')
    expect(await screen.findByTestId('assistant-reply')).toHaveTextContent('Whose order?')
    say('Ada Lovelace')
    await screen.findByTestId('assistant-proposal')
    expect(state.asked[1]!.messages).toEqual([...question.messages, { role: 'user', text: 'Ada Lovelace' }])

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByTestId('assistant-proposal')).not.toBeInTheDocument())
    say('Show me our products')

    await waitFor(() => expect(state.asked).toHaveLength(3))
    expect(state.asked[2]!.messages).toEqual([{ role: 'user', text: 'Show me our products' }])
  })

  it('shows a table the assistant asks to show, with its note', async () => {
    state.outcomes = [
      {
        messages: [],
        reply: null,
        proposal: null,
        table: { title: 'Products', columns: ['Name', 'Price'], rows: [['Widget', '$12.00']], note: 'One so far.' },
      },
    ]
    renderHome()

    say('Show me our products')

    const table = await screen.findByTestId('assistant-table')
    expect(table).toHaveTextContent('Products')
    expect(screen.getByRole('columnheader', { name: 'Price' })).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: 'Widget' })).toBeInTheDocument()
    expect(table).toHaveTextContent('One so far.')
  })

  it('shows a graph the assistant drew, with the figures behind it to open', async () => {
    state.outcomes = [
      {
        messages: [],
        reply: null,
        proposal: null,
        graphic: {
          title: 'Orders by month',
          svg: '<svg viewBox="0 0 640 120"><rect x="0" y="10" width="200" height="20" fill="var(--accent)"/></svg>',
          data: { title: 'Orders by month', columns: ['Month', 'Orders'], rows: [['October', '4']], note: null },
          note: null,
        },
      },
    ]
    renderHome()

    say('Graph our orders by month')

    const graph = await screen.findByTestId('assistant-graphic')
    expect(graph.querySelector('svg rect')).not.toBeNull()
    expect(graph).toHaveTextContent('The figures behind this graph')
    expect(screen.getByRole('cell', { name: 'October' })).toBeInTheDocument()
  })
})
