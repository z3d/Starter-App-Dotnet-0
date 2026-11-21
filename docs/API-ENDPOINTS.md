# API Endpoints Documentation

This project uses .NET 10 Minimal APIs with endpoint-definition auto-discovery. All business endpoints are under `/api/v1` and require an OIDC/JWT bearer token the API validates itself; health and OpenAPI/Scalar endpoints remain public.

## Minimal API Structure

```
src/StarterApp.Api/
├── Endpoints/
│   ├── CustomerEndpoints.cs
│   ├── OrderEndpoints.cs
│   ├── ProductEndpoints.cs
│   ├── IEndpointDefinition.cs
│   └── EndpointExtensions.cs
└── Program.cs
```

Endpoint definitions dispatch through the custom mediator. Command handlers use EF Core; query handlers use Dapper.

## Authentication

Protected `/api/v1` routes require an OIDC bearer token with audience `starterapp-api`:

```http
Authorization: Bearer <access-token>
X-Correlation-ID: demo-correlation-id
```

The API validates issuer, audience, signature, and expiry itself against `Identity:Authority`. Locally, mint a token from the dev Keycloak realm (client `starterapp-dev`, user `dev-user` — well-known dev credentials, see `dev/StarterApp.AppHost/Realms/`); request the resource scopes explicitly, they are optional client scopes:

```bash
curl -s "$KEYCLOAK/realms/starterapp/protocol/openid-connect/token" \
  -d grant_type=password -d client_id=starterapp-dev \
  -d client_secret=local-dev-client-secret-not-a-secret \
  -d username=dev-user -d password=dev-password \
  -d 'scope=products:read products:write customers:read customers:write orders:read orders:write settings:read settings:write'
```

Write routes require the matching `*:write` scope and an `amr` claim containing `mfa` (the dev realm's users carry it as an attribute, all but `password-only`; a real IdP earns it). A browser client that lacks it is told so with a step-up challenge; `web/apps/web/src/lib/auth.ts` answers it.

## Product Catalog

Base path: `/api/v1/products`

| Method | Endpoint | Scope | MFA | Response |
|--------|----------|-------|-----|----------|
| `GET` | `/api/v1/products?page=1&pageSize=50` | `products:read` | No | `200 OK` with `PagedResponse<ProductReadModel>` |
| `GET` | `/api/v1/products/{id}` | `products:read` | No | `200 OK` or `404 Not Found` |
| `POST` | `/api/v1/products` | `products:write` | Yes | `201 Created` |
| `PUT` | `/api/v1/products/{id}` | `products:write` | Yes | `204 No Content` |
| `DELETE` | `/api/v1/products/{id}` | `products:write` | Yes | `204 No Content`, `404`, or `409` |

Create/update body:

```json
{
  "name": "Gaming Laptop",
  "description": "High-performance gaming laptop with RTX graphics",
  "price": 1299.99,
  "currency": "AUD",
  "stock": 50
}
```

## Customer Management

Base path: `/api/v1/customers`

| Method | Endpoint | Scope | MFA | Response |
|--------|----------|-------|-----|----------|
| `GET` | `/api/v1/customers?page=1&pageSize=50` | `customers:read` | No | `200 OK` with `PagedResponse<CustomerReadModel>` |
| `GET` | `/api/v1/customers/{id}` | `customers:read` | No | `200 OK` or `404 Not Found` |
| `POST` | `/api/v1/customers` | `customers:write` | Yes | `201 Created` |
| `PUT` | `/api/v1/customers/{id}` | `customers:write` | Yes | `204 No Content` |
| `DELETE` | `/api/v1/customers/{id}` | `customers:write` | Yes | `204 No Content`, `404`, or `409` |

Create/update body:

```json
{
  "name": "John Doe",
  "email": "john.doe@example.com"
}
```

## Tenant Settings

Base path: `/api/v1/settings`. One record per tenant (the token's `tid`), read by everyone in it and changed only by an administrator: in the dev realm `settings:write` is granted only to holders of the `admin` realm role (`dev-user` has it, `password-only` does not), and a token without it is refused before the handler runs.

| Method | Endpoint | Scope | MFA | Response |
|--------|----------|-------|-----|----------|
| `GET` | `/api/v1/settings/appearance` | `settings:read` | No | `200 OK` with `{ "palette": "sky" \| null, "updatedBy", "lastUpdated" }`; `palette` is `null` until an administrator chooses one |
| `PUT` | `/api/v1/settings/appearance` | `settings:write` | Yes | `200 OK` with `TenantSettingsDto`, or `400` when `palette` is not a lower-case name of letters, digits and hyphens (at most 32) |

The API accepts any well-formed name; the web app shows the default theme for one it does not declare (`web/src/palettes.ts`). Who changed it and when are on the record (`updated_by`, `last_updated`) and in the payload archive.

## Assistant

Base path: `/api/v1/assistant`. Every route needs the `assistant:use` scope, which a token request asks for beside the others. Off by default: it is on only where `Assistant:Enabled` is true and `Assistant:ApiKey` and `Assistant:Model` are set. The routes are mapped either way. The API holds the model key and makes one model call a turn; the browser holds the conversation, runs the reads the model asks for through the other routes as the person, and shows a proposal as a card that changes nothing until the person confirms it through the route it names.

| Method | Endpoint | Scope | MFA | Response |
|--------|----------|-------|-----|----------|
| `GET` | `/api/v1/assistant` | `assistant:use` | No | `200 OK` with `{ "enabled", "primary" }`; `primary` is true only where it is on and `Assistant:PrimaryInterface` is true |
| `GET` | `/api/v1/assistant/routes?area=app` | `assistant:use` | No | `200 OK` with the write routes the token's scopes admit, each with its scope and the fields its card asks for; `400` for an unknown area |
| `POST` | `/api/v1/assistant/turns` | `assistant:use` | Yes | `200 OK` with the model's blocks and stop reason, a `read` or `propose` block carrying the listed route it names; `400` for a malformed conversation or an unknown area, `502` when the model does not answer, `503` when the assistant is off |

## Order Management

Base path: `/api/v1/orders`

| Method | Endpoint | Scope | MFA | Response |
|--------|----------|-------|-----|----------|
| `GET` | `/api/v1/orders/{id}` | `orders:read` | No | `200 OK` or `404 Not Found` |
| `GET` | `/api/v1/orders/customer/{customerId}?page=1&pageSize=50` | `orders:read` | No | `200 OK` with `PagedResponse<OrderReadModel>` |
| `GET` | `/api/v1/orders/status/{status}?page=1&pageSize=50` | `orders:read` | No | `200 OK` with `PagedResponse<OrderReadModel>` |
| `POST` | `/api/v1/orders` | `orders:write` | Yes | `201 Created`, `400`, `404`, `409`, or `422` |
| `PUT` | `/api/v1/orders/{id}/status` | `orders:write` | Yes | `200 OK`, `400`, `404`, or `409` |
| `POST` | `/api/v1/orders/{id}/cancel` | `orders:write` | Yes | `200 OK`, `404`, or `409` |

Send an `Idempotency-Key` header (1–255 visible ASCII characters, e.g. a UUID) to make a retry safe.
A repeat with the same key and the same body returns the first order with `201` and reserves no more
stock; the same key with a different body is refused with `422`. Keys are scoped to the caller, and
without the header every request creates an order.

Create order body:

```json
{
  "customerId": 1,
  "items": [
    {
      "productId": 1,
      "quantity": 2
    }
  ]
}
```

Update status body:

```json
{
  "orderId": "0194fd1e-6f72-7c81-9c70-19040a79dd9b",
  "status": "Confirmed"
}
```

Valid statuses are `Pending`, `Confirmed`, `Processing`, `Shipped`, `Delivered`, and `Cancelled`. The aggregate enforces valid transitions.

## Operational Endpoints

| Method | Endpoint | Description | Response |
|--------|----------|-------------|----------|
| `GET` | `/health` | Aggregate health status | `200 OK` or `503 Service Unavailable` |
| `GET` | `/health/ready` | Readiness probe including database connectivity | `200 OK` or `503 Service Unavailable` |
| `GET` | `/health/live` | Liveness probe for container restarts | `200 OK` |
| `GET` | `/alive` | Liveness alias | `200 OK` |

In Development, OpenAPI and Scalar are exposed through `MapOpenApi()` and `MapScalarApiReference()`.

## Response Shapes

List endpoints return:

```json
{
  "data": [],
  "hasMore": false
}
```

Validation failures return Problem Details with an `errors` extension grouped by property. Business rule conflicts, invalid state transitions, unique-key collisions, and stale concurrency writes map to `409 Conflict`.

## Payload Capture

The payload capture middleware archives bounded HTTP request/response payloads and full Service Bus payloads to Blob storage:

- Archive: `archive/{yyyy-MM-dd}/{HH}/{mm}/{correlationId}.jsonl`
- Audit: `audit/{yyyy-MM-dd}/{HH}/{mm}/payload-audit.jsonl`
- Entity index: `entity-index/{entityType}/{entityId}/{yyyy-MM-dd}/{HH}/{mm}/{correlationId}.jsonl`

Operational logs remain redacted. Production-like orchestration sets `PayloadCapture:RequireArchiveStore=true` and `PayloadCapture:FailureMode=FailClosed`.

## Example Curl

```bash
curl -X POST "http://localhost:8080/api/v1/products" \
  -H "Content-Type: application/json" \
  -H "X-Correlation-ID: local-demo-1" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -d '{
    "name": "Gaming Laptop",
    "description": "High-performance gaming laptop with RTX graphics",
    "price": 1299.99,
    "currency": "AUD",
    "stock": 50
  }'
```
