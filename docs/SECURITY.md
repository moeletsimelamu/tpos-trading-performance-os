# Security

The current access model and how secrets are handled. Remaining risks are listed at the end.

## Access model

- **One operator.** There is no public sign-up. Accounts are created on the server with:

  ```bash
  dotnet run --project MelamuHoldings.API -- create-owner
  ```

  It prompts for email, display name and password. The password has a 12-character minimum and is masked at a terminal. It is stored as a BCrypt hash.
- **Authentication:** an HS256 JWT valid for `Jwt:ExpiryHours` (default 8). The frontend keeps it in `localStorage`. That is acceptable for a single-operator local deployment; revisit before any public deployment.
- **Authorisation:**
  - Every trading endpoint requires a valid token and is scoped to the caller's user id. Another user's trade returns 404, and an integration test covers this.
  - Every finance endpoint (`/api/funds`, `/api/financial-accounts`, `/api/transactions`, `/api/balances`, `/api/audit-events`, `/api/currencies`, `/api/payouts`, `/api/allocations`, `/api/allocation-policies`) requires the **`Owner` policy**. Accounts created with `create-owner` get the `Owner` role in their token. A valid token without it gets 403, and a test covers this.
  - Sessions started before Phase 4 hold tokens without the role, so they must sign in again; the UI says so.
- **Financial integrity:**
  - There is no endpoint that edits or deletes a transaction or sets a balance.
  - Retries are idempotent (`Idempotency-Key`), and concurrent changes to one payout or allocation are rejected (`rowversion`).
  - Allocation is a recorded decision, not an automatic action: nothing moves money until the operator records a settlement.
  - Every finance change writes an append-only audit event in the same database transaction.
  - Finance foreign keys never cascade from users.
  - No bank or brokerage credentials are stored.
- **Login throttling:** `Auth:LoginAttemptsPerMinute` (default 5) per client IP. Excess attempts get HTTP 429.

## Secrets

| Setting | Where it lives | Never in |
|---|---|---|
| `Jwt:Key` (at least 32 bytes) | user-secrets (development), `Jwt__Key` environment variable (deployment) | `appsettings*.json`, git |
| `ConnectionStrings:DefaultConnection` | `appsettings.Development.json` (trusted local connection, no password), user-secrets or the `ConnectionStrings__DefaultConnection` environment variable when a password is involved | git, when it contains a password |

- The API **refuses to start** if `Jwt:Key` is missing or too short.
- Before Phase 2 a placeholder key was committed. It has been removed and replaced, but it remains in git history, so it must never be reused.

To set a key on a new machine:

```bash
dotnet user-secrets set "Jwt:Key" "<at least 32 random characters>" --project MelamuHoldings.API
```

## Errors and logging

- Errors are returned as RFC 7807 ProblemDetails. Stack traces appear only in the Development environment.
- EF Core SQL command logging is at `Warning`, so query text and parameters are not written to the console during normal use.

## Dependency hygiene

- CI fails on any vulnerable NuGet package (including transitive ones) and on high-severity advisories in production npm dependencies.
- `System.Security.Cryptography.Pkcs` is pinned to a patched version until `Microsoft.Data.SqlClient` depends on one.
- **Known accepted advisory:** `undici` inside `@angular/build`. It is build tooling only and not shipped to browsers.

## Open items

- HTTPS and HSTS enforcement, CORS origins, and secret management for an actual deployment are documented in Phase 10.
- The audit log is append-only at the API level. A database administrator could still alter the table directly. Tamper evidence (for example hash-chaining events) is not implemented.
