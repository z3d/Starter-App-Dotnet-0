import { useState, type ReactNode } from "react";
import { api, type Problem, type Schemas } from "../lib/api";
import { claims, resourceScopes, signInWithScopes } from "../lib/auth";

// A derived project removes this folder with the sample domain (docs/DERIVATION-PRUNING.md).

interface Outcome { method: string; path: string; status: number; body: unknown }

const suffix = crypto.randomUUID().replaceAll("-", "").slice(0, 8);

function Step({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="mb-8">
      <h2 className="mb-2 text-lg font-semibold">{title}</h2>
      {children}
    </section>
  );
}

function Result({ step, outcome }: { step: string; outcome: Outcome | null }) {
  return (
    <pre data-testid={`${step}-result`} className="mt-3 min-h-10 whitespace-pre-wrap">
      {outcome ? `${outcome.method} ${outcome.path}\n${outcome.status}\n\n${JSON.stringify(outcome.body, null, 2)}` : "Nothing sent yet."}
    </pre>
  );
}

const Button = ({ children, ...props }: React.ButtonHTMLAttributes<HTMLButtonElement>) => (
  <button type="button" {...props} className="rounded bg-neutral-900 px-3 py-1.5 text-sm text-white hover:bg-neutral-700 disabled:opacity-40 dark:bg-neutral-100 dark:text-neutral-900 dark:hover:bg-neutral-300">
    {children}
  </button>
);

const Field = ({ label, ...props }: React.InputHTMLAttributes<HTMLInputElement> & { label: string }) => (
  <label className="flex flex-col gap-1 text-sm">
    <span className="text-neutral-600 dark:text-neutral-400">{label}</span>
    <input {...props} className="rounded border border-neutral-300 bg-white px-2 py-1 dark:border-neutral-700 dark:bg-neutral-900" />
  </label>
);

const outcome = (method: string, path: string, response: Response, data: unknown, error: unknown): Outcome =>
  ({ method, path, status: response.status, body: data ?? (error as Problem | undefined) ?? null });

export function Walkthrough() {
  const token = claims();
  const granted = String(token?.scope ?? "").split(" ");
  const [scopes, setScopes] = useState<string[]>(resourceScopes.filter((s) => granted.includes(s)));

  const [productName, setProductName] = useState(`Demo Widget ${suffix.toUpperCase()}`);
  const [price, setPrice] = useState("199.99");
  const [stock, setStock] = useState("10");
  const [product, setProduct] = useState<Schemas["ProductDto"] | null>(null);
  const [productOutcome, setProductOutcome] = useState<Outcome | null>(null);

  const [customerName, setCustomerName] = useState("Demo Customer");
  const [email, setEmail] = useState(`demo.${suffix}@example.test`);
  const [customer, setCustomer] = useState<Schemas["CustomerDto"] | null>(null);
  const [customerOutcome, setCustomerOutcome] = useState<Outcome | null>(null);

  const [quantity, setQuantity] = useState("1");
  const [order, setOrder] = useState<Schemas["OrderDto"] | null>(null);
  const [orderOutcome, setOrderOutcome] = useState<Outcome | null>(null);

  const createProduct = async () => {
    const body: Schemas["CreateProductCommand"] = { name: productName, description: "Created from the walkthrough", price: Number(price), currency: "AUD", stock: Number(stock) };
    const { data, error, response } = await api.POST("/api/v1/products", { body });
    setProductOutcome(outcome("POST", "/api/v1/products", response, data, error));
    setProduct(data ?? null);
  };

  const createCustomer = async () => {
    const body: Schemas["CreateCustomerCommand"] = { name: customerName, email };
    const { data, error, response } = await api.POST("/api/v1/customers", { body });
    setCustomerOutcome(outcome("POST", "/api/v1/customers", response, data, error));
    setCustomer(data ?? null);
  };

  const placeOrder = async () => {
    if (!product || !customer) return;
    const body: Schemas["CreateOrderCommand"] = { customerId: customer.id, items: [{ productId: product.id, quantity: Number(quantity) }] };
    const { data, error, response } = await api.POST("/api/v1/orders", { body });
    setOrderOutcome(outcome("POST", "/api/v1/orders", response, data, error));
    setOrder(data ?? null);
  };

  const confirmOrder = async () => {
    if (!order) return;
    const path = `/api/v1/orders/${order.id}/status`;
    const { error, response } = await api.PUT("/api/v1/orders/{id}/status", { params: { path: { id: order.id } }, body: { orderId: order.id, status: "Confirmed" } });
    if (error !== undefined || !response.ok) {
      setOrderOutcome(outcome("PUT", path, response, undefined, error));
      return;
    }
    const read = await api.GET("/api/v1/orders/{id}", { params: { path: { id: order.id } } });
    setOrderOutcome(outcome("GET", `/api/v1/orders/${order.id}`, read.response, read.data, read.error));
    setOrder(read.data ?? order);
  };

  return (
    <>
      <Step title="Who you are">
        <p className="mb-2 text-sm text-neutral-600 dark:text-neutral-400">
          The claims the API verifies on every call: the signature, issuer, audience and expiry it checks itself, and the scopes and
          authentication methods it decides each write from.
        </p>
        <pre data-testid="claims">{JSON.stringify({ sub: token?.sub, tid: token?.tid, aud: token?.aud, scope: token?.scope, amr: token?.amr, exp: token?.exp ? new Date(Number(token.exp) * 1000).toISOString() : undefined }, null, 2)}</pre>
        <fieldset className="mt-3 flex flex-wrap gap-3 text-sm">
          <legend className="mb-1 text-neutral-600 dark:text-neutral-400">Scopes to ask for next time</legend>
          {resourceScopes.map((scope) => (
            <label key={scope} className="flex items-center gap-1">
              <input type="checkbox" checked={scopes.includes(scope)} onChange={(e) => setScopes(e.target.checked ? [...scopes, scope] : scopes.filter((s) => s !== scope))} />
              {scope}
            </label>
          ))}
        </fieldset>
        <div className="mt-3">
          <Button onClick={() => signInWithScopes(scopes)}>Sign in again with these scopes</Button>
        </div>
      </Step>

      <Step title="A product">
        <p className="mb-2 text-sm text-neutral-600 dark:text-neutral-400">A synchronous write: validate, one EF Core insert, 201 with the new id. Needs products:write and mfa.</p>
        <div className="grid grid-cols-3 gap-3">
          <Field label="Name" data-testid="product-name" value={productName} onChange={(e) => setProductName(e.target.value)} />
          <Field label="Price (AUD)" value={price} onChange={(e) => setPrice(e.target.value)} />
          <Field label="Stock" value={stock} onChange={(e) => setStock(e.target.value)} />
        </div>
        <div className="mt-3"><Button onClick={() => void createProduct()}>Create the product</Button></div>
        <Result step="product" outcome={productOutcome} />
      </Step>

      <Step title="A customer">
        <p className="mb-2 text-sm text-neutral-600 dark:text-neutral-400">The other synchronous write; the email is the natural key, so the page randomises it per load.</p>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Name" value={customerName} onChange={(e) => setCustomerName(e.target.value)} />
          <Field label="Email" value={email} onChange={(e) => setEmail(e.target.value)} />
        </div>
        <div className="mt-3"><Button onClick={() => void createCustomer()}>Create the customer</Button></div>
        <Result step="customer" outcome={customerOutcome} />
      </Step>

      <Step title="An order">
        <p className="mb-2 text-sm text-neutral-600 dark:text-neutral-400">
          One commit writes the order and its OrderCreated event; the outbox publishes it to the Service Bus topic and the Functions
          consume it. Confirming it raises OrderStatusChanged the same way. Needs orders:write: sign in again without it to see the
          API refuse the order from the token alone.
        </p>
        <div className="grid grid-cols-3 gap-3">
          <Field label="Quantity" value={quantity} onChange={(e) => setQuantity(e.target.value)} />
        </div>
        <div className="mt-3 flex gap-3">
          <Button onClick={() => void placeOrder()} disabled={!product || !customer}>Place the order</Button>
          <Button onClick={() => void confirmOrder()} disabled={!order}>Confirm the order</Button>
        </div>
        <Result step="order" outcome={orderOutcome} />
      </Step>
    </>
  );
}
