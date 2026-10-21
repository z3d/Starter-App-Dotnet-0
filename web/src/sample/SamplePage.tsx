import { useState } from 'react'
import { useAuth } from 'react-oidc-context'
import { Button } from '@/components/ui/button'
import { Checkbox, Field, Input } from '@/components/ui/field'
import { HeroFigure, SplitBar, Statement } from '@/components/ui/figures'
import { Card, EmptyState, PageHeader } from '@/components/ui/primitives'
import { ApiError, call, client } from '@/lib/api'
import { money } from '@/lib/format'
import { claimsOf, resourceScopes, signInWithScopes } from '@/lib/auth'
import type { Full, Schemas } from '@/lib/types'

// A derived project removes this folder with the sample domain (docs/DERIVATION-PRUNING.md).
// It drives the Customer/Product/Order sample and shows what the API answers, so the identity
// seam (scopes, the step-up challenge, 201s and problem details) can be seen end to end.

interface Outcome {
  method: string
  path: string
  status: number
  body: unknown
}

const suffix = crypto.randomUUID().replaceAll('-', '').slice(0, 8)

function Result({ step, outcome }: { step: string; outcome: Outcome | null }) {
  return (
    <pre
      data-testid={`${step}-result`}
      className="mt-3 min-h-10 rounded-md bg-surface-sunken p-3 font-mono text-xs whitespace-pre-wrap text-ink-secondary"
    >
      {outcome
        ? `${outcome.method} ${outcome.path}\n${outcome.status}\n\n${JSON.stringify(outcome.body, null, 2)}`
        : 'Nothing sent yet.'}
    </pre>
  )
}

// Runs one typed call and keeps what the API answered, success or problem, for the page to show.
async function send<T>(method: string, path: string, run: () => Promise<T>): Promise<[Outcome, T | null]> {
  try {
    const data = await run()
    return [{ method, path, status: method === 'POST' ? 201 : 200, body: data }, data]
  } catch (error) {
    if (error instanceof ApiError) return [{ method, path, status: error.status, body: error.problem ?? null }, null]
    throw error
  }
}

export function SamplePage() {
  const auth = useAuth()
  const profile = claimsOf(auth.user?.access_token)
  const granted = String(profile.scope ?? '').split(' ')
  const [scopes, setScopes] = useState<string[]>(resourceScopes.filter((s) => granted.includes(s)))

  const [productName, setProductName] = useState(`Demo Widget ${suffix.toUpperCase()}`)
  const [price, setPrice] = useState('199.99')
  const [stock, setStock] = useState('10')
  const [product, setProduct] = useState<Full<Schemas['ProductDto']> | null>(null)
  const [productOutcome, setProductOutcome] = useState<Outcome | null>(null)

  const [customerName, setCustomerName] = useState('Demo Customer')
  const [email, setEmail] = useState(`demo.${suffix}@example.test`)
  const [customer, setCustomer] = useState<Full<Schemas['CustomerDto']> | null>(null)
  const [customerOutcome, setCustomerOutcome] = useState<Outcome | null>(null)

  const [quantity, setQuantity] = useState('1')
  const [order, setOrder] = useState<Full<Schemas['OrderDto']> | null>(null)
  const [orderOutcome, setOrderOutcome] = useState<Outcome | null>(null)

  const createProduct = async () => {
    const body: Schemas['CreateProductCommand'] = {
      name: productName,
      description: 'Created from the sample page',
      price: Number(price),
      currency: 'AUD',
      stock: Number(stock),
    }
    const [outcome, data] = await send('POST', '/api/v1/products', () =>
      call(client.POST('/api/v1/products', { body })),
    )
    setProductOutcome(outcome)
    setProduct(data)
  }

  const createCustomer = async () => {
    const body: Schemas['CreateCustomerCommand'] = { name: customerName, email }
    const [outcome, data] = await send('POST', '/api/v1/customers', () =>
      call(client.POST('/api/v1/customers', { body })),
    )
    setCustomerOutcome(outcome)
    setCustomer(data)
  }

  const placeOrder = async () => {
    if (!product || !customer) return
    const body: Schemas['CreateOrderCommand'] = {
      customerId: customer.id,
      items: [{ productId: product.id, quantity: Number(quantity) }],
    }
    const [outcome, data] = await send('POST', '/api/v1/orders', () => call(client.POST('/api/v1/orders', { body })))
    setOrderOutcome(outcome)
    setOrder(data)
  }

  const confirmOrder = async () => {
    if (!order) return
    const [put] = await send('PUT', `/api/v1/orders/${order.id}/status`, () =>
      call(
        client.PUT('/api/v1/orders/{id}/status', {
          params: { path: { id: order.id } },
          body: { orderId: order.id, status: 'Confirmed' },
        }),
      ),
    )
    if (put.status >= 400) {
      setOrderOutcome(put)
      return
    }
    const [read, data] = await send('GET', `/api/v1/orders/${order.id}`, () =>
      call(client.GET('/api/v1/orders/{id}', { params: { path: { id: order.id } } })),
    )
    setOrderOutcome(read)
    setOrder(data ?? order)
  }

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Sample"
        title="The identity seam, end to end"
        description="A product, a customer and an order through the API, with what it answers. The token decides each write."
      />

      <Card className="p-5">
        <h2 className="font-display text-xl">Who you are</h2>
        <p className="mt-1 text-sm text-ink-secondary">
          The claims the API verifies on every call: the signature, issuer, audience and expiry it checks itself, and
          the scopes and authentication methods it decides each write from.
        </p>
        <pre
          data-testid="claims"
          className="mt-3 rounded-md bg-surface-sunken p-3 font-mono text-xs whitespace-pre-wrap"
        >
          {JSON.stringify(
            {
              sub: profile.sub,
              tid: profile.tid,
              aud: profile.aud,
              scope: profile.scope,
              amr: profile.amr,
              exp: profile.exp ? new Date(Number(profile.exp) * 1000).toISOString() : undefined,
            },
            null,
            2,
          )}
        </pre>
        <fieldset className="mt-3 flex flex-wrap gap-4 text-sm">
          <legend className="mb-1 text-xs text-ink-secondary">Scopes to ask for next time</legend>
          {resourceScopes.map((scope) => (
            <Checkbox
              key={scope}
              label={scope}
              checked={scopes.includes(scope)}
              onChange={(e) => setScopes(e.target.checked ? [...scopes, scope] : scopes.filter((s) => s !== scope))}
            />
          ))}
        </fieldset>
        <Button className="mt-3" onClick={() => signInWithScopes(scopes)}>
          Sign in again with these scopes
        </Button>
      </Card>

      <Card className="p-5">
        <h2 className="font-display text-xl">A product</h2>
        <p className="mt-1 text-sm text-ink-secondary">
          A synchronous write: validate, one EF Core insert, 201 with the new id. Needs products:write and mfa.
        </p>
        <div className="mt-3 grid gap-3 sm:grid-cols-3">
          <Field label="Name">
            <Input data-testid="product-name" value={productName} onChange={(e) => setProductName(e.target.value)} />
          </Field>
          <Field label="Price (AUD)">
            <Input value={price} onChange={(e) => setPrice(e.target.value)} inputMode="decimal" />
          </Field>
          <Field label="Stock">
            <Input value={stock} onChange={(e) => setStock(e.target.value)} inputMode="numeric" />
          </Field>
        </div>
        <Button className="mt-3" variant="accent" onClick={() => void createProduct()}>
          Create the product
        </Button>
        <Result step="product" outcome={productOutcome} />
      </Card>

      <Card className="p-5">
        <h2 className="font-display text-xl">A customer</h2>
        <p className="mt-1 text-sm text-ink-secondary">
          The other synchronous write; the email is the natural key, so the page randomises it per load.
        </p>
        <div className="mt-3 grid gap-3 sm:grid-cols-2">
          <Field label="Name">
            <Input value={customerName} onChange={(e) => setCustomerName(e.target.value)} />
          </Field>
          <Field label="Email">
            <Input value={email} onChange={(e) => setEmail(e.target.value)} inputMode="email" />
          </Field>
        </div>
        <Button className="mt-3" variant="accent" onClick={() => void createCustomer()}>
          Create the customer
        </Button>
        <Result step="customer" outcome={customerOutcome} />
      </Card>

      <Card className="p-5">
        <h2 className="font-display text-xl">An order</h2>
        <p className="mt-1 text-sm text-ink-secondary">
          One commit writes the order and its OrderCreated event; the outbox publishes it to the Service Bus topic and
          the Functions consume it. Confirming it raises OrderStatusChanged the same way. Needs orders:write: sign in
          again without it to see the API refuse the order from the token alone.
        </p>
        <div className="mt-3 grid gap-3 sm:grid-cols-3">
          <Field label="Quantity">
            <Input value={quantity} onChange={(e) => setQuantity(e.target.value)} inputMode="numeric" />
          </Field>
        </div>
        <div className="mt-3 flex gap-2">
          <Button variant="accent" onClick={() => void placeOrder()} disabled={!product || !customer}>
            Place the order
          </Button>
          <Button onClick={() => void confirmOrder()} disabled={!order}>
            Confirm the order
          </Button>
        </div>
        <div className="mt-3">
          {order ? (
            <Statement data-testid="order-statement">
              <HeroFigure
                label={`Order total, ${order.status.toLowerCase()}`}
                value={money(order.totalIncludingGst, { cents: true })}
                hint={`${String(order.items.length)} line${order.items.length === 1 ? '' : 's'}, GST included.`}
              />
              <div className="mt-6">
                <SplitBar
                  dark
                  label="The order total"
                  segments={[
                    {
                      label: 'Excluding GST',
                      value: Number(order.totalExcludingGst),
                      display: money(order.totalExcludingGst, { cents: true }),
                      tone: 'accent',
                    },
                    {
                      label: 'GST',
                      value: Number(order.totalGstAmount),
                      display: money(order.totalGstAmount, { cents: true }),
                      tone: 'info',
                    },
                  ]}
                />
              </div>
            </Statement>
          ) : (
            <EmptyState compact title="No order yet" description="Place one to see its total and the GST in it." />
          )}
        </div>
        <Result step="order" outcome={orderOutcome} />
      </Card>
    </div>
  )
}
