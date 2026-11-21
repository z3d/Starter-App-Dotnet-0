import type { AssistantArea } from '@/lib/assistant'

// The work people do here, as the product knows it: where each job starts and what usually follows each step. The
// product offers these, the same every time; the assistant's model only works out which job a person's words mean.
export interface Step {
  label: string
  method: 'POST' | 'PUT' | 'DELETE'
  path: string
  // Path parameters, each taken from something an earlier step in the conversation made or named.
  params?: Record<string, ContextKey>
  // Body fields filled in from the same.
  body?: Record<string, ContextKey>
}

export type ContextKey = string
export type Context = Partial<Record<ContextKey, string>>

export interface Start {
  label: string
  // A job with a first step the product can open straight away, or words for the assistant to work out.
  step?: Step
}

const steps = {
  addCustomer: { label: 'Add a customer', method: 'POST', path: '/api/v1/customers' },
  addProduct: { label: 'Add a product', method: 'POST', path: '/api/v1/products' },
} satisfies Record<string, Step>

export const starts: Record<AssistantArea, Start[]> = {
  app: [
    { label: 'A new customer', step: steps.addCustomer },
    { label: 'A new product', step: steps.addProduct },
    { label: 'A customer wants to cancel an order' },
    { label: 'Show me our products' },
  ],
}

const key = (method: string, path: string) => `${method} ${path}`

// What usually follows each step, in the order it is done.
const after: Record<string, Step[]> = {
  [key('POST', steps.addCustomer.path)]: [{ ...steps.addCustomer, label: 'Add another customer' }],
  [key('POST', steps.addProduct.path)]: [{ ...steps.addProduct, label: 'Add another product' }],
}

// What a confirmed step made, kept by the kind of record so a later step can name it.
const made: Record<string, ContextKey> = {
  [key('POST', steps.addCustomer.path)]: 'customerId',
  [key('POST', steps.addProduct.path)]: 'productId',
}

const carried: [string, ContextKey][] = []

const ID = /^([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|[1-9][0-9]*)$/i

// What the conversation knows after a step: the record it made and the records it named.
export function learn(
  context: Context,
  done: { method: string; path: string; pathParams: Record<string, string>; body: Record<string, unknown> | null },
  id: string | undefined,
): Context {
  const next = { ...context }
  for (const [field, as] of carried) {
    const value = done.body?.[field] ?? done.pathParams[field]
    if ((typeof value === 'string' || typeof value === 'number') && ID.test(String(value))) next[as] = String(value)
  }
  const kind = made[key(done.method, done.path)]
  if (kind && id != null) next[kind] = String(id)
  return next
}

// The steps to offer after one is confirmed: those the table lists whose records the conversation can name.
export function nextSteps(done: { method: string; path: string }, context: Context): Step[] {
  return (after[key(done.method, done.path)] ?? []).filter((step) =>
    Object.values(step.params ?? {}).every((needed) => context[needed]),
  )
}

export function filled(step: Step, context: Context) {
  const from = (keys: Record<string, ContextKey> | undefined) =>
    Object.fromEntries(
      Object.entries(keys ?? {})
        .map(([name, needed]) => [name, context[needed]] as const)
        .filter((entry): entry is readonly [string, string] => typeof entry[1] === 'string'),
    )
  return { pathParams: from(step.params), body: from(step.body) }
}
