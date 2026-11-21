import { call, client } from '@/lib/api'
import type { components } from '@/lib/api.generated'
import { MAX_SVG_LENGTH, readSvg } from '@/lib/graphic'

export type AssistantBlock = components['schemas']['AssistantBlockDto']
export type AssistantMessage = components['schemas']['AssistantMessageDto']
type ToolResult = components['schemas']['AssistantToolResultDto']

export type Proposal = {
      kind: 'request'
      title: string
      method: 'POST' | 'PUT' | 'DELETE'
      path: string
      summary: string
      roles: string[]
      pathParams: Record<string, string>
      body: Record<string, unknown> | null
      // The route's body fields as the product lists them: what the card asks for, in this order, every time.
      fields: RouteField[]
}

export type RouteField = components['schemas']['AssistantFieldDto']
export type AssistantRoute = components['schemas']['AssistantRouteDto']

export interface AssistantTable {
  title: string
  columns: string[]
  rows: string[][]
  note: string | null
}

export interface AssistantGraphic {
  title: string
  svg: string
  data: AssistantTable
  note: string | null
}

export interface QuestionField {
  name: string
  label: string
  kind: 'choice' | 'date' | 'amount' | 'yesno' | 'text'
  options: { value: string; label: string; hint: string | null }[]
  optional: boolean
}

// What the assistant needs from the person, as a form to click through and not a sentence to answer.
export interface AssistantQuestion {
  question: string
  fields: QuestionField[]
}

export interface AssistantOutcome {
  question?: AssistantQuestion | null
  messages: AssistantMessage[]
  reply: string | null
  proposal: Proposal | null
  table?: AssistantTable | null
  graphic?: AssistantGraphic | null
}

const READ = 'read'
const PROPOSE = 'propose'
const SHOW_TABLE = 'show_table'
const SHOW_GRAPHIC = 'show_graphic'
const ASK_PERSON = 'ask_person'
const MAX_FIELDS = 5
const MAX_OPTIONS = 8
const KINDS = ['choice', 'date', 'amount', 'yesno', 'text'] as const
const MAX_COLUMNS = 8
const MAX_ROWS = 50
const MAX_READ_RECORDS = 20
const MAX_RESULT_LENGTH = 7500

// A find and its answer are one round; a request that needs more than this many is not one the assistant can resolve.
const MAX_ROUNDS = 6
const MAX_MATCHES = 10
const GAVE_UP =
  'I could not work that out in a few steps. Nothing was changed; try saying it another way, or do it by hand.'

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

function isDay(value: unknown): value is string {
  const parts = typeof value === 'string' ? /^(\d{4})-(\d{2})-(\d{2})$/.exec(value) : null
  if (!parts) return false
  const [year, month, dayOfMonth] = [Number(parts[1]), Number(parts[2]), Number(parts[3])]
  const date = new Date(year, month - 1, dayOfMonth)
  return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === dayOfMonth
}

// The generated type calls a tool's input an empty record; it is whatever the tool's schema allows.
const inputOf = (block: AssistantBlock) => (block.input ?? {}) as Record<string, unknown>

const isId = (value: unknown): value is string => typeof value === 'string' && UUID.test(value)

// What the model put in a proposal is checked here before a person sees it; the reason goes back to the model so it can correct itself.
export function readProposal(block: AssistantBlock): Proposal | string {
  const input = inputOf(block)
  if (block.name === PROPOSE) {
    const route = block.route
    if (!route || route.method === 'GET')
      return 'There is no such write route. Use a method and path exactly as listed in the routes.'
    const pathParams = paramsFor(route.path ?? '', input.pathParams)
    if (typeof pathParams === 'string') return pathParams
    if (typeof input.title !== 'string' || !input.title.trim()) return 'title must say in a few words what will happen.'
    if (input.body != null && (typeof input.body !== 'object' || Array.isArray(input.body)))
      return "body must be an object of the route's fields."
    return {
      kind: 'request',
      title: input.title.trim(),
      method: route.method as 'POST' | 'PUT' | 'DELETE',
      path: route.path ?? '',
      summary: route.summary ?? '',
      roles: route.roles ?? [],
      pathParams,
      body: (input.body as Record<string, unknown> | undefined) ?? null,
      fields: route.fields ?? [],
    }
  }
  return `There is no tool called ${block.name ?? 'that'}.`
}

// A table the model asks to show is checked like a proposal: a heading for each column and a value in every cell.
export function readTable(block: AssistantBlock): AssistantTable | string {
  const input = inputOf(block)
  const columns = input.columns
  const rows = input.rows
  if (typeof input.title !== 'string' || !input.title.trim()) return 'title must say what the table shows.'
  if (
    !Array.isArray(columns) ||
    columns.length === 0 ||
    columns.length > MAX_COLUMNS ||
    columns.some((c) => typeof c !== 'string')
  )
    return `columns must be one to ${MAX_COLUMNS} headings.`
  if (!Array.isArray(rows) || rows.length > MAX_ROWS) return `rows must be at most ${MAX_ROWS} rows.`
  if (rows.some((row) => !Array.isArray(row) || row.length !== columns.length))
    return 'every row must have one value for each column.'
  return {
    title: input.title.trim(),
    columns: columns as string[],
    rows: (rows as unknown[][]).map((row) => row.map((cell) => (cell == null ? '' : String(cell)))),
    note: typeof input.note === 'string' && input.note.trim() ? input.note.trim() : null,
  }
}

// A form the model asks to show is checked like a proposal: every field named, and a choice with something to choose from.
export function readQuestion(block: AssistantBlock): AssistantQuestion | string {
  const input = inputOf(block)
  if (typeof input.question !== 'string' || !input.question.trim()) return 'question must say what you need.'
  if (!Array.isArray(input.fields) || input.fields.length === 0 || input.fields.length > MAX_FIELDS)
    return `fields must be one to ${MAX_FIELDS} fields.`
  const fields: QuestionField[] = []
  for (const given of input.fields as Record<string, unknown>[]) {
    if (typeof given?.name !== 'string' || !given.name.trim()) return 'every field needs a name.'
    if (typeof given.label !== 'string' || !given.label.trim()) return 'every field needs a label.'
    const kind = KINDS.find((known) => known === given.kind)
    if (!kind) return `a field's kind is one of ${KINDS.join(', ')}.`
    if (fields.some((field) => field.name === given.name)) return 'each field needs a name of its own.'
    const options = (Array.isArray(given.options) ? (given.options as Record<string, unknown>[]) : [])
      .filter((option) => typeof option?.value === 'string' && typeof option.label === 'string' && option.label.trim())
      .map((option) => ({
        value: option.value as string,
        label: (option.label as string).trim(),
        hint: typeof option.hint === 'string' && option.hint.trim() ? option.hint.trim() : null,
      }))
    if (kind === 'choice' && (options.length < 2 || options.length > MAX_OPTIONS))
      return `a choice needs two to ${MAX_OPTIONS} options, each with a value and a label.`
    fields.push({
      name: given.name.trim(),
      label: given.label.trim(),
      kind,
      options: kind === 'choice' ? options : [],
      optional: given.optional === true,
    })
  }
  return { question: input.question.trim(), fields }
}

// A graph the model draws is shown only when it is a drawing the page can read, and always with the figures it was drawn from.
export function readGraphic(block: AssistantBlock): AssistantGraphic | string {
  const input = inputOf(block)
  if (typeof input.title !== 'string' || !input.title.trim()) return 'title must say what the graph shows.'
  if (typeof input.svg !== 'string' || input.svg.length > MAX_SVG_LENGTH)
    return `svg must be the drawing as text, at most ${MAX_SVG_LENGTH} characters.`
  if (!readSvg(input.svg))
    return 'svg must be one well-formed <svg> element with a viewBox, made of shapes and text only.'
  const figures = input.data && typeof input.data === 'object' ? (input.data as Record<string, unknown>) : {}
  const data = readTable({
    ...block,
    input: { title: input.title, columns: figures.columns, rows: figures.rows } as never,
  })
  if (typeof data === 'string') return `data: ${data}`
  return {
    title: input.title.trim(),
    svg: input.svg,
    data,
    note: typeof input.note === 'string' && input.note.trim() ? input.note.trim() : null,
  }
}

// A value for every name in braces in the path, each one text with no slash in it.
function paramsFor(path: string, given: unknown): Record<string, string> | string {
  const values = (given && typeof given === 'object' ? given : {}) as Record<string, unknown>
  const params: Record<string, string> = {}
  for (const [, name] of path.matchAll(/\{([^}]+)\}/g)) {
    const value = values[name!]
    if (
      (typeof value !== 'string' && typeof value !== 'number') ||
      String(value).includes('/') ||
      !String(value).trim()
    )
      return `pathParams must give a value for ${name}, taken from a record you read.`
    params[name!] = String(value)
  }
  return params
}

// What a read returns goes to the model whole, less what no task here needs: sign-in subjects and tax or bank identifiers.
const WITHHELD = /subject|taxfile|tfn|bsb|accountnumber|bankaccount/i

function forTheModel(value: unknown): unknown {
  if (Array.isArray(value)) return value.slice(0, MAX_READ_RECORDS).map(forTheModel)
  if (value && typeof value === 'object')
    return Object.fromEntries(
      Object.entries(value)
        .filter(([key]) => !WITHHELD.test(key))
        .map(([key, inner]) => [key, forTheModel(inner)]),
    )
  return value
}

async function runRead(block: AssistantBlock): Promise<ToolResult> {
  const toolUseId = block.id ?? ''
  const route = block.route
  if (!route || route.method !== 'GET')
    return {
      toolUseId,
      content: 'There is no such read route. Use a path exactly as listed under the reads.',
      isError: true,
    }
  const input = inputOf(block)
  const path = paramsFor(route.path ?? '', input.pathParams)
  if (typeof path === 'string') return { toolUseId, content: path, isError: true }
  const query = input.query && typeof input.query === 'object' && !Array.isArray(input.query) ? input.query : {}
  try {
    // The route is one the API listed for this person; its path is not known to the generated types until run time.
    const read = client.GET as unknown as (
      path: string,
      init: object,
    ) => Promise<{ data?: unknown; error?: unknown; response: Response }>
    const data = await call(
      read(route.path ?? '', { params: { path, query: { ...query, page: 1, pageSize: MAX_READ_RECORDS } } }),
    )
    const text = JSON.stringify(forTheModel(data))
    return {
      toolUseId,
      content:
        text.length > MAX_RESULT_LENGTH ? `${text.slice(0, MAX_RESULT_LENGTH)} (cut short; narrow the read)` : text,
      isError: false,
    }
  } catch (failure) {
    return { toolUseId, content: failure instanceof Error ? failure.message : 'The read failed.', isError: true }
  }
}

const isFind = (block: AssistantBlock) => block.name === READ

function textOf(blocks: AssistantBlock[]) {
  return blocks
    .filter((b) => b.type === 'text' && b.text)
    .map((b) => b.text)
    .join('\n')
    .trim()
}

// Asks the model, runs the finds it asks for, and stops at the first thing for the person: a question, or one action to confirm.
export type AssistantArea = 'staff'

export async function ask(messages: AssistantMessage[], area: AssistantArea = 'staff'): Promise<AssistantOutcome> {
  let conversation = messages
  for (let round = 0; round < MAX_ROUNDS; round++) {
    const turn = await call(client.POST('/api/v1/assistant/turns', { body: { messages: conversation, area } }))
    const blocks = turn.blocks ?? []
    conversation = [...conversation, { role: 'assistant', blocks }]
    const calls = blocks.filter((b) => b.type === 'tool_use')
    if (calls.length === 0) return { messages: conversation, reply: textOf(blocks) || GAVE_UP, proposal: null }

    const [shown] = calls
    if (calls.length === 1 && shown && shown.name === SHOW_TABLE) {
      const table = readTable(shown)
      if (typeof table !== 'string')
        return { messages: conversation, reply: textOf(blocks) || null, proposal: null, table }
    }
    if (calls.length === 1 && shown && shown.name === SHOW_GRAPHIC) {
      const graphic = readGraphic(shown)
      if (typeof graphic !== 'string')
        return { messages: conversation, reply: textOf(blocks) || null, proposal: null, graphic }
    }

    if (calls.length === 1 && shown && shown.name === ASK_PERSON) {
      const question = readQuestion(shown)
      if (typeof question !== 'string')
        return { messages: conversation, reply: textOf(blocks) || null, proposal: null, question }
    }

    const read = calls.map((block) => ({
      block,
      proposal: isFind(block)
        ? null
        : block.name === ASK_PERSON
          ? readQuestion(block)
          : block.name === SHOW_TABLE
            ? readTable(block)
            : block.name === SHOW_GRAPHIC
              ? readGraphic(block)
              : readProposal(block),
    }))
    const [only] = read
    if (read.length === 1 && only && only.proposal && typeof only.proposal !== 'string' && 'kind' in only.proposal)
      return { messages: conversation, reply: textOf(blocks) || null, proposal: only.proposal }

    const results = await Promise.all(
      read.map(({ block, proposal }): Promise<ToolResult> | ToolResult => {
        if (block.name === READ) return runRead(block)
        return {
          toolUseId: block.id ?? '',
          content:
            typeof proposal === 'string'
              ? proposal
              : 'Not shown: propose one action, on its own, once the finds are done.',
          isError: true,
        }
      }),
    )
    conversation = [...conversation, { role: 'tool', results }]
  }
  return { messages: conversation, reply: GAVE_UP, proposal: null }
}

const openCalls = (outcome: AssistantOutcome) => {
  const last = outcome.messages[outcome.messages.length - 1]
  return last?.role === 'assistant' ? (last.blocks ?? []).filter((block) => block.type === 'tool_use') : []
}

// What the person says next, added to the conversation so far. A turn that ended on a tool call has to have that call
// answered before anything else is said (the model's API refuses the conversation otherwise): a form is answered with
// what the person said, and a table or a graph as shown, followed by their words.
export function reply(outcome: AssistantOutcome, text: string): AssistantMessage[] {
  const open = openCalls(outcome)
  if (open.length === 0) return [...outcome.messages, { role: 'user', text }]
  const asked = open.some((block) => block.name === ASK_PERSON)
  const answered: AssistantMessage = {
    role: 'tool',
    results: open.map((block) => ({
      toolUseId: block.id ?? '',
      content: asked ? text : 'Shown to the person.',
      isError: false,
    })),
  }
  return asked ? [...outcome.messages, answered] : [...outcome.messages, answered, { role: 'user', text }]
}

// What happened when the person confirmed a proposal, told to the model so it can say so and lead on, or correct a
// request the system refused.
export function confirmed(outcome: AssistantOutcome, happened: string, refused = false): AssistantMessage[] {
  return [
    ...outcome.messages,
    {
      role: 'tool',
      results: openCalls(outcome).map((block) => ({ toolUseId: block.id ?? '', content: happened, isError: refused })),
    },
  ]
}

// Sends a confirmed request as the person, through the same client as every other call.
export function send(proposal: Extract<Proposal, { kind: 'request' }>) {
  const request = client[proposal.method] as unknown as (
    path: string,
    init: object,
  ) => Promise<{ data?: unknown; error?: unknown; response: Response }>
  return call(
    request(proposal.path, { params: { path: proposal.pathParams }, body: proposal.body ?? undefined }),
  ) as Promise<{
    id?: string
  } | null>
}

// Where a record made by a general request opens, when the route that made it has a page of its own in that area.
const PAGES = {
  staff: {},
} as const

export function pageFor(proposal: Extract<Proposal, { kind: 'request' }>, area: AssistantArea) {
  const pages: Record<string, string | undefined> = PAGES[area]
  return pages[`${proposal.method} ${proposal.path}`]
}
