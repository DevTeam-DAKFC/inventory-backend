# Backend quality gate validation

This note records the backend validation state for the final project quality gate.

## Implemented API surface

The locally verifiable API contract is the generated OpenAPI document served in Development at `/openapi/v1.json`. There is no committed `openapi.inventory-api.yaml` file in this repository.

Implemented endpoints:

- `GET /health`
- `GET /stock`
- `GET /stock/lookup`
- `GET /stock/{stockId}`

Swagger and Scalar documentation are available only in Development:

- `/openapi/v1.json`
- `/swagger/v1/swagger.json`
- `/swagger/index.html`
- `/scalar/v1`

## Response contracts

Stock endpoints return `StockResponse`, with nested `StockProductResponse` and `StockBranchResponse`.

Implemented stock validation and not-found errors use `ErrorResponse`:

```json
{
  "code": "error_code",
  "message": "Human-readable message."
}
```

Covered cases:

- `400 Bad Request` for invalid or missing GUID query/path values.
- `404 Not Found` when a requested stock record or product/branch stock combination does not exist.

There are currently no implemented endpoints that produce `401 Unauthorized`, `403 Forbidden`, or `409 Conflict`.

## Authentication and authorization

Authentication and role-based authorization are intentionally outside the current project scope. There are no protected endpoints and no admin-only endpoints in the current backend.

Quality-gate checklist items for bearer-token rejection, collaborator rejection, and role-based authorization are therefore `N/A`, not failed or pending.

## Database migration

The initial EF Core migration creates the planned SQL Server tables:

- `users`
- `branches`
- `products`
- `stocks`
- `inventory_movements`
- `notification_tokens`
- `import_batches`
- `import_batch_errors`

The migration is present in `src/Inventory.Api/Migrations/20260604082216_InitialCreate.cs`.

A clean live apply of the migration depends on the local SQL Server Docker container being available. If the container is not running, live migration validation is an environment limitation.
