# Testing

How the automated tests are organised, how to run them, and the latest recorded results.
The strategy for later phases is in `../PLAN.md` §9.

## Test suites

| Suite | Location | What it covers | Needs |
|---|---|---|---|
| .NET unit | `tests/MelamuHoldings.UnitTests` | `Money`/`Currency` rules (minor units, rounding modes, currency mismatch, equality), trading analytics (summary, expectancy, drawdown, equity curve, strategy breakdown), behaviour detection, CSV parsing, trade DTO validation, owner provisioning, login, JWT options | Nothing |
| API integration | `tests/MelamuHoldings.IntegrationTests` | The real API via `WebApplicationFactory`: login and ProblemDetails, rate limiting, missing signing key, anonymous access (401), trade validation (400), cross-user isolation, CSV upload into behaviour analytics, JSON contract (`rr`) | SQL Server |
| Angular unit | `MelamuHoldings.Angular/src/**/*.spec.ts` (Vitest via `@angular/build:unit-test`) | Routing (old-URL redirects, login redirect, page titles), shell navigation (built pages are links; planned modules are labelled, non-interactive items; drawer toggle), auth session and guard, HTTP error messages, and the performance, trades, login and import pages: loading, empty and error states, Retry, filters, failed delete, double submit, inline upload errors | Node 22 |

### Integration test database

- Each test class creates its own database named `MelamuHoldings_Test_<guid>`, applies the EF migrations, seeds two test users, and drops the database afterwards.
- The server comes from the `MELAMU_TEST_SQLSERVER` environment variable. Locally it defaults to `Server=.\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True;`.
- Tests run in the `Testing` environment. That environment skips `appsettings.Development.json` and user-secrets, so a misconfigured test cannot reach the development database.

## Running

```bash
dotnet test MelamuHoldings.slnx
```

```bash
npm --prefix MelamuHoldings.Angular test -- --watch=false
```

CI (`.github/workflows/ci.yml`) runs:
- **Backend:** a Release build with warnings as errors, a vulnerable-package gate, and both .NET suites against a SQL Server 2022 container.
- **Frontend:** `npm ci`, a production-dependency audit, `ng build`, and the Angular specs.

## Results — 2026-10-27 (branch `phase-3-foundation`)

| Command | Result |
|---|---|
| `dotnet build MelamuHoldings.slnx -c Release -warnaserror` | 0 warnings, 0 errors |
| `dotnet test` (Release): unit | 58 passed, 0 failed |
| `dotnet test` (Release): integration, against local SQL Server Express 2019, applying both migrations | 15 passed, 0 failed |
| `ng test --watch=false` | 37 passed, 0 failed (8 files) |
| `ng build` (production) | Succeeded; initial bundle 296.85 kB; no component-style budget warnings |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages |
| `npm audit --omit=dev --audit-level=high` | 0 vulnerabilities |
| `dotnet ef migrations has-pending-model-changes` | No changes |

### Manual browser verification (Phase 3)

Done against `MelamuHoldings_Scratch` (20 sample trades), after applying the new migration:

- **Login:** rebranded page with the title "Sign in · Melamu Holdings OS". Signing in lands on `/trading/performance`.
- **Desktop (1366 px):**
  - The grouped sidebar shows four live links, and ten modules marked Planned.
  - The six KPIs sit on one row.
  - Expectancy reads +79.25 (1,585 ÷ 20), confirming the Phase 2 formula fix.
- **Old URLs:** `/upload` redirects to `/trading/import`, with `aria-current="page"` on the active link. Trades shows its RR values; Behaviour shows 3 alerts.
- **Phone (375 px):**
  - No horizontal scroll.
  - Two KPI columns and one chart column.
  - The menu button opens the navigation drawer (`aria-expanded="true"`).

## Results — Phase 2 (branch `phase-2-stabilise`)

| Command | Result |
|---|---|
| `dotnet build MelamuHoldings.slnx -c Release -warnaserror` | 0 warnings, 0 errors |
| `dotnet test` (Release): unit | 39 passed, 0 failed |
| `dotnet test` (Release): integration, against local SQL Server Express 2019 | 15 passed, 0 failed |
| `ng test --watch=false` | 24 passed, 0 failed (6 files) |
| `ng build` (production) | Succeeded; initial bundle 285.76 kB (warning budget 500 kB) |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages |
| `npm audit --omit=dev --audit-level=high` | 0 vulnerabilities |
| GitHub Actions run | Passed on `develop` (runs 40508109510, 40513060976) and on PR #2 (run 40514984634) |

### Manual browser verification

Done against a scratch database (`MelamuHoldings_Scratch`) holding 20 sample trades, each marked `SAMPLE DATA`:

- **Dashboard:** KPIs match hand calculation (total P&L +1,585.00; 10W/9L/1B) and the charts render.
- **Trades:** the RR column shows values.
- **Behaviour:** flags the planted revenge trade, overtrading day and three-loss streak.
- **Upload:** history lists the import.
- **API stopped:** the dashboard shows "The API could not be reached" with Retry, and Retry recovers once the API is back.
- **Sign out:** clears the session and shows the login page without the sidebar.
- **Signed-out access:** `/trades` redirects to `/login`.

## Known gaps

- There are no end-to-end browser tests yet. The manual walkthrough above stands in until the Phase 5 financial workflow gets an automated end-to-end test.
- Profit factor with zero losses is pinned at its current (incorrect) value by `Profit_factor_with_no_losses_currently_returns_gross_profit` until Phase 8.
