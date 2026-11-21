import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  type AssistantBlock,
  ask,
  confirmed,
  readGraphic,
  readProposal,
  readQuestion,
  readTable,
  reply,
} from './assistant'

const ORDER = '0193a000-0000-7000-8000-0000000000a1'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

const toolUse = (id: string, name: string, input: Record<string, unknown>) =>
  ({ type: 'tool_use', id, name, input }) as AssistantBlock

function stubApi(turns: unknown[]) {
  const sentTurns: {
    messages: { role: string; results?: { toolUseId: string; content: string; isError: boolean }[] }[]
  }[] = []
  const reads: string[] = []
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const request = input as Request
    const url = new URL(request.url)
    if (url.pathname === '/api/v1/assistant/turns') {
      sentTurns.push(await request.json())
      const next = turns.shift()
      return next ? json(next) : json({ title: 'No more turns' }, 500)
    }
    reads.push(`${url.pathname}${url.search}`)
    if (url.pathname === '/api/v1/customers' && url.searchParams.get('page') === '1')
      return json({
        data: [
          { id: 7, name: 'Ada Lovelace', email: 'ada@example.com' },
          { id: 8, name: 'Charles Babbage', email: 'charles@example.com' },
        ],
        hasMore: true,
      })
    if (url.pathname === '/api/v1/customers')
      return json({ data: [{ id: 9, name: 'Grace Hopper', email: 'ada.fan@example.org' }], hasMore: false })
    return json({ title: 'Not found' }, 404)
  })
  return { sentTurns, reads }
}

describe('ask', () => {
  afterEach(() => vi.restoreAllMocks())

  it('runs the find the model asks for as the person and stops at the action to confirm', async () => {
    const api = stubApi([
      {
        stopReason: 'tool_use',
        blocks: [
          { type: 'thinking', thinking: '', signature: 'sig-1' },
          toolUse('t1', 'find_customers', { search: 'Ada' }),
        ],
      },
      { stopReason: 'tool_use', blocks: [toolUse('t2', 'propose_cancel_order', { orderId: ORDER })] },
    ])

    const outcome = await ask([{ role: 'user', text: "Cancel Ada's last order" }])

    expect(outcome.proposal).toEqual({ kind: 'cancel-order', orderId: ORDER })
    expect(api.reads).toEqual(['/api/v1/customers?page=1&pageSize=100', '/api/v1/customers?page=2&pageSize=100'])
    const second = api.sentTurns[1]!.messages
    expect(second.map((m) => m.role)).toEqual(['user', 'assistant', 'tool'])
    const [found] = second[2]!.results!
    expect(JSON.parse(found!.content)).toEqual([
      { id: 7, name: 'Ada Lovelace' },
      { id: 9, name: 'Grace Hopper' },
    ])
    expect(found!.content).not.toContain('example.com')
  })

  it('sends a proposal with a made-up id back to the model instead of showing it', async () => {
    const api = stubApi([
      { stopReason: 'tool_use', blocks: [toolUse('t1', 'propose_cancel_order', { orderId: 'the last one' })] },
      { stopReason: 'end_turn', blocks: [{ type: 'text', text: 'Which order do you mean?' }] },
    ])

    const outcome = await ask([{ role: 'user', text: "Cancel Ada's order" }])

    expect(outcome.proposal).toBeNull()
    expect(outcome.reply).toBe('Which order do you mean?')
    expect(api.sentTurns[1]!.messages[2]!.results).toEqual([
      { toolUseId: 't1', content: 'orderId must be an order id returned by a read.', isError: true },
    ])
    expect(api.reads).toEqual([])
  })

  it('gives up without a proposal when the model keeps searching', async () => {
    const searching = { stopReason: 'tool_use', blocks: [toolUse('t', 'find_customers', { search: 'Ada' })] }
    const api = stubApi(Array.from({ length: 10 }, () => searching))

    const outcome = await ask([{ role: 'user', text: "Cancel Ada's order" }])

    expect(outcome.proposal).toBeNull()
    expect(outcome.reply).toContain('Nothing was changed')
    expect(api.sentTurns).toHaveLength(6)
  })
})

describe('readProposal', () => {
  it('refuses a tool it does not know', () => {
    expect(readProposal(toolUse('t', 'delete_customer', { id: 7 }))).toBe('There is no tool called delete_customer.')
  })

  it('turns a general proposal into a request only for a route the API listed, with every path value given', () => {
    const route = {
      method: 'PUT',
      path: '/api/v1/orders/{id}/status',
      summary: 'Update order status',
      scope: 'orders:write',
      fields: [{ name: 'status', required: false, kind: 'choice', options: ['Pending', 'Shipped'] }],
    }
    const input = {
      method: 'PUT',
      path: route.path,
      pathParams: { id: ORDER },
      body: { status: 'Shipped' },
      title: 'Mark the order shipped',
    }

    expect(readProposal({ ...toolUse('t', 'propose', input), route })).toEqual({
      kind: 'request',
      title: 'Mark the order shipped',
      method: 'PUT',
      path: '/api/v1/orders/{id}/status',
      summary: 'Update order status',
      scope: 'orders:write',
      pathParams: { id: ORDER },
      body: { status: 'Shipped' },
      fields: route.fields,
    })
    expect(readProposal(toolUse('t', 'propose', input))).toContain('no such write route')
    expect(readProposal({ ...toolUse('t', 'propose', { ...input, pathParams: {} }), route })).toContain('value for id')
    expect(
      readProposal({ ...toolUse('t', 'propose', { ...input, pathParams: { id: '../customers' } }), route }),
    ).toContain('value for id')
  })
})

describe('the general read', () => {
  afterEach(() => vi.restoreAllMocks())

  it('calls the listed route as the person and gives the model the record without sign-in or bank details', async () => {
    const seen: string[] = []
    const turns: unknown[] = [
      {
        stopReason: 'tool_use',
        blocks: [
          {
            ...toolUse('r1', 'read', { path: '/api/v1/orders/{id}', pathParams: { id: ORDER } }),
            route: { method: 'GET', path: '/api/v1/orders/{id}', summary: 'Get order by ID', scope: 'orders:read' },
          },
          toolUse('r2', 'read', { path: '/api/v1/secrets' }),
        ],
      },
      { stopReason: 'end_turn', blocks: [{ type: 'text', text: 'It has shipped.' }] },
    ]
    const sent: { messages: { results?: { toolUseId: string; content: string; isError: boolean }[] }[] }[] = []
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request
      const url = new URL(request.url)
      if (url.pathname === '/api/v1/assistant/turns') {
        sent.push(await request.json())
        return json(turns.shift())
      }
      seen.push(`${url.pathname}${url.search}`)
      return json({ id: ORDER, status: 'Shipped', ownerSubject: 'dev-user', payment: { bankAccountNumber: '123' } })
    })

    const outcome = await ask([{ role: 'user', text: 'Has that order shipped?' }])

    expect(outcome.reply).toBe('It has shipped.')
    expect(seen).toEqual([`/api/v1/orders/${ORDER}?page=1&pageSize=20`])
    const [read, refused] = sent[1]!.messages[2]!.results!
    expect(JSON.parse(read!.content)).toEqual({ id: ORDER, status: 'Shipped', payment: {} })
    expect(refused).toEqual({
      toolUseId: 'r2',
      content: 'There is no such read route. Use a path exactly as listed under the reads.',
      isError: true,
    })
  })
})

describe('a table', () => {
  afterEach(() => vi.restoreAllMocks())

  const table = { title: 'Products', columns: ['Name', 'Price'], rows: [['Widget', '$12.00']] }

  it('is read with a heading for every column and a value in every cell, and refused otherwise', () => {
    expect(readTable(toolUse('t', 'show_table', table))).toEqual({ ...table, note: null })
    expect(readTable(toolUse('t', 'show_table', { ...table, rows: [['Widget']] }))).toBe(
      'every row must have one value for each column.',
    )
    expect(readTable(toolUse('t', 'show_table', { ...table, columns: [] }))).toContain('columns must be')
    expect(readTable(toolUse('t', 'show_table', { ...table, rows: [[null, 7]], note: ' Cut short. ' }))).toEqual({
      ...table,
      rows: [['', '7']],
      note: 'Cut short.',
    })
  })

  it('ends the turn as something to show, with nothing to confirm', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async () =>
      json({
        stopReason: 'tool_use',
        blocks: [{ type: 'text', text: 'Here they are.' }, toolUse('t1', 'show_table', table)],
      }),
    )

    const outcome = await ask([{ role: 'user', text: 'Show me our products' }])

    expect(outcome.table).toEqual({ ...table, note: null })
    expect(outcome.proposal).toBeNull()
    expect(outcome.reply).toBe('Here they are.')
  })
})

describe('a graph', () => {
  const graph = {
    title: 'Orders by month',
    svg: '<svg viewBox="0 0 640 120"><rect x="0" y="10" width="200" height="20" fill="var(--accent)"/></svg>',
    data: { columns: ['Month', 'Orders'], rows: [['October', '4']] },
  }

  it('is shown only as a drawing the page can read, with the figures it was drawn from', () => {
    expect(readGraphic(toolUse('t', 'show_graphic', graph))).toEqual({
      title: 'Orders by month',
      svg: graph.svg,
      data: { title: 'Orders by month', columns: ['Month', 'Orders'], rows: [['October', '4']], note: null },
      note: null,
    })
    expect(readGraphic(toolUse('t', 'show_graphic', { ...graph, svg: '<p>a chart</p>' }))).toContain(
      'well-formed <svg>',
    )
    expect(readGraphic(toolUse('t', 'show_graphic', { ...graph, data: undefined }))).toContain('data:')
  })
})

describe('a reply', () => {
  it('answers a table or graph the turn ended on before the person speaks again, and adds nothing after a question', () => {
    const table = toolUse('t9', 'show_table', { title: 'Products', columns: ['Name'], rows: [['Widget']] })
    const afterATable = {
      messages: [
        { role: 'user', text: 'List them' },
        { role: 'assistant', blocks: [table] },
      ],
      reply: null,
      proposal: null,
    }
    const afterAQuestion = {
      messages: [
        { role: 'user', text: "Cancel Ada's order" },
        { role: 'assistant', blocks: [{ type: 'text', text: 'Which Ada?' }] },
      ],
      reply: 'Which Ada?',
      proposal: null,
    }

    expect(reply(afterATable, 'Now graph them').slice(2)).toEqual([
      { role: 'tool', results: [{ toolUseId: 't9', content: 'Shown to the person.', isError: false }] },
      { role: 'user', text: 'Now graph them' },
    ])
    expect(reply(afterAQuestion, 'Ada Lovelace').slice(2)).toEqual([{ role: 'user', text: 'Ada Lovelace' }])
  })
})

describe('a question to the person', () => {
  const field = { name: 'which', label: 'Which one', kind: 'choice' }
  const options = [
    { value: '7', label: 'Ada Lovelace', hint: 'ada@example.com' },
    { value: '9', label: 'Ada Byron' },
  ]

  it('is a form when every field is named and a choice has something to choose from', () => {
    const block = toolUse('q1', 'ask_person', {
      question: ' Which Ada? ',
      fields: [
        { ...field, options },
        { name: 'when', label: 'Day it was placed', kind: 'date', optional: true },
      ],
    })

    expect(readQuestion(block)).toEqual({
      question: 'Which Ada?',
      fields: [
        {
          name: 'which',
          label: 'Which one',
          kind: 'choice',
          optional: false,
          options: [
            { value: '7', label: 'Ada Lovelace', hint: 'ada@example.com' },
            { value: '9', label: 'Ada Byron', hint: null },
          ],
        },
        { name: 'when', label: 'Day it was placed', kind: 'date', optional: true, options: [] },
      ],
    })
  })

  it.each([
    ['no question', { fields: [{ ...field, options }] }],
    ['no fields', { question: 'Which?', fields: [] }],
    ['a choice with one option', { question: 'Which?', fields: [{ ...field, options: options.slice(0, 1) }] }],
    ['a kind it does not know', { question: 'Which?', fields: [{ ...field, kind: 'file' }] }],
    [
      'two fields of one name',
      {
        question: 'Which?',
        fields: [
          { ...field, options },
          { ...field, options },
        ],
      },
    ],
  ])('goes back to the model with a reason when it has %s', (_, input) => {
    expect(typeof readQuestion(toolUse('q1', 'ask_person', input))).toBe('string')
  })

  it('is answered with what the person chose and nothing after it, and a confirmed card with what happened', () => {
    const asked = toolUse('q1', 'ask_person', { question: 'Which?', fields: [{ ...field, options }] })
    const proposed = toolUse('p1', 'propose', {})
    const after = (block: AssistantBlock) => ({
      messages: [
        { role: 'user' as const, text: "Cancel Ada's order" },
        { role: 'assistant' as const, blocks: [block] },
      ],
      reply: null,
      proposal: null,
    })

    expect(reply(after(asked), '{"which":"7"}').slice(2)).toEqual([
      { role: 'tool', results: [{ toolUseId: 'q1', content: '{"which":"7"}', isError: false }] },
    ])
    expect(confirmed(after(proposed), 'The system refused it: already delivered.', true).slice(2)).toEqual([
      {
        role: 'tool',
        results: [{ toolUseId: 'p1', content: 'The system refused it: already delivered.', isError: true }],
      },
    ])
  })
})
