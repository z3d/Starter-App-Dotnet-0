# API Endpoints Documentation

This project uses .NET 10 Minimal APIs with a clean architecture pattern. All endpoints are organized using the endpoint definition pattern for better maintainability and testability.

## 🏗️ Architecture Overview

### Minimal API Structure
```
src/StarterApp.Api/
├── Endpoints/
│   ├── CustomerEndpoints.cs      # Customer management endpoints
│   ├── OrderEndpoints.cs         # Order processing endpoints  
│   ├── ProductEndpoints.cs       # Product catalog endpoints
│   ├── IEndpointDefinition.cs    # Endpoint definition contract
│   ├── EndpointExtensions.cs     # Auto-discovery extensions
│   └── Filters/
│       └── LoggingFilter.cs      # Request/response logging
└── Program.cs                    # Application configuration
```

### Key Benefits of Minimal APIs
- **Performance**: ~30% faster than traditional controllers
- **Source Generators**: Better AOT compilation support
- **Simpler**: Less boilerplate code
- **Modern**: Native .NET 10 integration
- **Flexible**: Endpoint-specific filters and behaviors

## 🔌 Available Endpoints

### Customer Management (`/api/customers`)

| Method | Endpoint | Description | Response |
|--------|----------|-------------|----------|
| `GET` | `/api/customers` | Get all customers | `200 OK` with customer list |
| `GET` | `/api/customers/{id}` | Get customer by ID | `200 OK` or `404 Not Found` |
| `POST` | `/api/customers` | Create new customer | `201 Created` with location header |
| `PUT` | `/api/customers/{id}` | Update customer | `204 No Content` or `404 Not Found` |
| `DELETE` | `/api/customers/{id}` | Delete customer | `204 No Content` or `404 Not Found` |

**Example Request**:
```http
POST /api/customers
Content-Type: application/json

{
  "name": "John Doe",
  "email": "john.doe@example.com"
}
```

### Order Management (`/api/orders`)

| Method | Endpoint | Description | Response |
|--------|----------|-------------|----------|
| `GET` | `/api/orders/{id}` | Get order with items | `200 OK` or `404 Not Found` |
| `GET` | `/api/orders/customer/{customerId}` | Get orders by customer | `200 OK` with order list |
| `GET` | `/api/orders/status/{status}` | Get orders by status | `200 OK` with order list |
| `POST` | `/api/orders` | Create new order | `201 Created` with location header |
| `PUT` | `/api/orders/{id}/status` | Update order status | `200 OK` or `404 Not Found` |
| `POST` | `/api/orders/{id}/cancel` | Cancel order | `200 OK` or `400 Bad Request` |

### Operational Endpoints

| Method | Endpoint | Description | Response |
|--------|----------|-------------|----------|
| `GET` | `/health` | Aggregate health status | `200 OK` or `503 Service Unavailable` |
| `GET` | `/health/ready` | Readiness probe including database connectivity | `200 OK` or `503 Service Unavailable` |
| `GET` | `/health/live` | Liveness probe for container restarts | `200 OK` or `503 Service Unavailable` |
| `GET` | `/alive` | Backward-compatible liveness alias | `200 OK` or `503 Service Unavailable` |

**Example Request**:
```http
POST /api/orders
Content-Type: application/json

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

### Product Catalog (`/api/products`)

| Method | Endpoint | Description | Response |
|--------|----------|-------------|----------|
| `GET` | `/api/products` | Get all products | `200 OK` with product list |
| `GET` | `/api/products/{id}` | Get product by ID | `200 OK` or `404 Not Found` |
| `POST` | `/api/products` | Create new product | `201 Created` with location header |
| `PUT` | `/api/products/{id}` | Update product | `204 No Content` or `404 Not Found` |
| `DELETE` | `/api/products/{id}` | Delete product | `204 No Content` or `404 Not Found` |

**Example Request**:
```http
POST /api/products
Content-Type: application/json

{
  "name": "Gaming Laptop",
  "description": "High-performance gaming laptop with RTX graphics",
  "priceAmount": 1299.99,
  "currency": "AUD",
  "stock": 50
}
```

## 🏷️ OpenAPI Tags

Endpoints are organized with OpenAPI tags for better Scalar API reference documentation:

- **Customers**: Customer management operations including CRUD functionality
- **Orders**: Order processing and management including creation, status updates, and cancellation  
- **Products**: Product catalog management with full CRUD operations for inventory

## 🔧 Endpoint Filters

### LoggingFilter
- **Purpose**: Logs request execution time and details
- **Applied**: Can be applied to any endpoint or endpoint group
- **Output**: Structured logs with timing information

### Payload Capture Middleware
- **Purpose**: Archives inbound and outbound payloads for support investigation while keeping operational logs redacted
- **Correlation**: Accepts and echoes `X-Correlation-ID`; generated IDs are also propagated through outbox and Service Bus messages
- **Archive Layout**: `archive/{yyyy-MM-dd}/{HH}/{mm}/{correlationId}.jsonl`
- **Audit Layout**: `audit/{yyyy-MM-dd}/{HH}/{mm}/payload-audit.jsonl`
- **Entity Index Layout**: `entity-index/{entityType}/{entityId}/{yyyy-MM-dd}/{HH}/{mm}/{correlationId}.jsonl` pointer rows for support lookups without duplicating full payloads
- **Bounds**: HTTP capture is limited by `PayloadCapture:MaxPayloadBytes` and `CapturedContentTypes`; archive/audit rows include truncation or skip metadata when capture is bounded
- **Failure Policy**: `PayloadCapture:FailureMode` controls fail-open vs fail-closed archive writes, and production-like orchestrations require a configured archive store
- **Retention**: `PayloadArchiveCleanupFunction` runs on `PayloadCapture__CleanupCron` and deletes archive, audit, and entity-index blobs older than `PayloadCapture:RetentionDays`

### Usage Example
```csharp
customers.MapPost("/", CreateCustomer)
    .AddEndpointFilter<LoggingFilter>();
```

## 📊 Response Formats

### Success Responses
- `200 OK`: Successful GET/PUT operations
- `201 Created`: Successful POST operations with location header
- `204 No Content`: Successful PUT/DELETE operations

### Error Responses
- `400 Bad Request`: Validation errors or business rule violations
- `404 Not Found`: Resource not found
- `409 Conflict`: Business rule conflict or state transition violation
- `429 Too Many Requests`: Rate limit exceeded
- `500 Internal Server Error`: Unexpected server errors

All error responses follow the Problem Details specification (RFC 7807).

## 🔍 Example Usage with curl

### Create a Customer
```bash
curl -X POST "https://localhost:7232/api/customers" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "John Doe",
    "email": "john.doe@example.com"
  }'
```

### Get Customer Orders
```bash
curl -X GET "https://localhost:7232/api/orders/customer/1"
```

### Update Order Status
```bash
curl -X PUT "https://localhost:7232/api/orders/1/status" \
  -H "Content-Type: application/json" \
  -d '{
    "orderId": 1,
    "status": "Shipped"
  }'
```

## 🧪 Testing

Integration tests automatically test all endpoints:
```bash
dotnet test
```

The tests use Testcontainers to spin up SQL Server instances, ensuring isolated and reliable testing.

## 📚 Further Reading

- [.NET Minimal APIs Documentation](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis)
- [Endpoint Filters](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/middleware)
- [OpenAPI in .NET 10](https://docs.microsoft.com/en-us/aspnet/core/web-api/openapi)
