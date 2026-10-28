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
- **Trade attachments:**
  - **Accepted:** only PNG, JPEG, WebP and PDF, decided from the file's first bytes; the name and the claimed content type are ignored. SVG and HTML are refused, so an uploaded file cannot run script in the app's origin.
  - **Limits:** 5 MB per file (the request limit is set just above it) and 10 per trade.
  - **Storage:** under random 32-hex-character names in `Attachments:Path` (default `App_Data/attachments` under the API's content root, never a web root, ignored by git). A storage key outside that exact shape is rejected, so a key cannot reach another folder.
  - **Serving:** only through the API, to the trade's owner (404 for anyone else, 401 without a token), with `nosniff` and `private, no-store`. Images are served inline and PDFs as downloads.
  - **Backups:** database backups do not include these files. Back up the attachments folder alongside the database.
- **AI-drafted reviews (optional, off by default):**
  - **What is sent:** with a hosted provider, a draft sends that trade, its journal answers and checklist, and up to `MaxComparableTrades` comparable trades (id, date, P&L, R) to the provider. Use a local OpenAI-compatible server to keep journal text on the machine.
  - **Prompt injection:** journal text is sent as escaped JSON data under a system prompt that says it is data. The model has no tools, and its reply cannot change any record; nothing is saved without the trader.
  - **Checking the reply:** facts must cite trades that were sent. Figures come from the app. Provider error bodies are not echoed, and prompts and replies are not logged.
  - **Questions about the journal:** a question sends the most recent closed trades in view (up to `MaxJournalTrades`, default 30) with their notes, reviews and journal answers, shortened, plus their checklist results. The same data rules, escaping and reply checks apply.
  - **Rate limit:** drafts and questions share one per-user limit (`AiReview:DraftsPerMinute`, default 6) to bound cost and abuse.

## Secrets

| Setting | Where it lives | Never in |
|---|---|---|
| `Jwt:Key` (at least 32 bytes) | user-secrets (development), `Jwt__Key` environment variable (deployment) | `appsettings*.json`, git |
| `AiReview:ApiKey` (optional) | user-secrets (development), `AiReview__ApiKey` environment variable (deployment); never returned by the API | `appsettings*.json`, git |
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
