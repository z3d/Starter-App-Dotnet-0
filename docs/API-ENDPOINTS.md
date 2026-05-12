# API Endpoints Documentation

This project uses .NET 10 Minimal APIs with endpoint-definition auto-discovery. All business endpoints are under `/api/v1` and require the trusted gateway identity contract; health and OpenAPI/Scalar endpoints remain public.

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

## Authentication Headers

Protected `/api/v1` routes require gateway-projected identity headers:

```http
X-Correlation-ID: demo-correlation-id
X-Authenticated-Subject: user-123
X-Authenticated-Principal-Type: User
X-Authenticated-Tenant-Id: tenant-1
X-Authenticated-Scopes: products:read products:write customers:read customers:write orders:read orders:write
X-Authenticated-Amr: mfa
```

Production-like environments also require a signed `X-Gateway-Assertion`. Local Development, Testing, and Docker can use `GatewayIdentity:Mode=UnsignedDevelopment`; the identity headers are still required.

Write routes require the matching `*:write` scope and `X-Authenticated-Amr` containing `mfa`.

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

## Order Management

Base path: `/api/v1/orders`

| Method | Endpoint | Scope | MFA | Response |
|--------|----------|-------|-----|----------|
| `GET` | `/api/v1/orders/{id}` | `orders:read` | No | `200 OK` or `404 Not Found` |
| `GET` | `/api/v1/orders/customer/{customerId}?page=1&pageSize=50` | `orders:read` | No | `200 OK` with `PagedResponse<OrderReadModel>` |
| `GET` | `/api/v1/orders/status/{status}?page=1&pageSize=50` | `orders:read` | No | `200 OK` with `PagedResponse<OrderReadModel>` |
| `POST` | `/api/v1/orders` | `orders:write` | Yes | `201 Created` |
| `PUT` | `/api/v1/orders/{id}/status` | `orders:write` | Yes | `200 OK`, `400`, `404`, or `409` |
| `POST` | `/api/v1/orders/{id}/cancel` | `orders:write` | Yes | `200 OK`, `404`, or `409` |

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
  -H "X-Authenticated-Subject: user-123" \
  -H "X-Authenticated-Principal-Type: User" \
  -H "X-Authenticated-Tenant-Id: tenant-1" \
  -H "X-Authenticated-Scopes: products:write" \
  -H "X-Authenticated-Amr: mfa" \
  -d '{
    "name": "Gaming Laptop",
    "description": "High-performance gaming laptop with RTX graphics",
    "price": 1299.99,
    "currency": "AUD",
    "stock": 50
  }'
```
