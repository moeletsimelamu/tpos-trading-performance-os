# Existing Features — Inventory

Phase 1 audit of the trading analytics application before it becomes Melamu Holdings OS.
Audit date: 2026-10-27. Branch inspected: `develop` (`d9f1aa5`), plus the uncommitted working tree.

## How to read this table

**Verification status**

- **Code-verified**: I read the full code path from controller to service, repository and database mapping, and found no defect that stops it working.
- **Partial**: the code path exists, but a defect, a gap, or a missing UI limits it.
- **Unverified at runtime**: the backend and the Angular app were built (committed `develop` builds; see "Baseline" in `ARCHITECTURE_REVIEW.md`), but nothing was run against a live API, database or browser. Every row carries this caveat until Phase 2 verifies it.
- **Absent**: the README brief or the commit history describes it, but there is no code for it.

**Planned action**: Preserve / Improve / Replace / Retire / Build.

## Inventory

| Feature | Existing implementation (from source) | Verification status | Planned action |
|---|---|---|---|
| Account registration | `POST /api/auth/register` (`AuthController`, `AuthService`). BCrypt hash, unique email index, 8-character minimum password. Angular `RegisterComponent`. | Code-verified. Open to anyone who can reach the API. | **Replace** with an owner-provisioned account. Open self-registration is inappropriate for a system holding company finances. |
| Login / JWT | `POST /api/auth/login` issues an HS256 JWT valid for 7 days. Claims: user id, email, name. Validates issuer, audience, lifetime and key. | Code-verified. The signing key is committed in `appsettings.json`. | **Improve**: move the secret out of source control and rotate it, shorten token lifetime, add an Owner role claim. |
| Route protection (frontend) | `authGuard` decodes the JWT `exp` client-side. `authInterceptor` adds the bearer header. `errorInterceptor` logs out on 401. | Code-verified | **Preserve** |
| Endpoint protection (backend) | `[Authorize]` on Trades, Analytics and Upload controllers. Every query is scoped by the `UserId` claim. | Code-verified | **Preserve**; add role-based policies for finance endpoints. |
| Trade journal — list | `GET /api/trades`, Angular `TradesComponent`: client-side search, direction filter, result filter. | Partial. The **RR column is likely blank**: the API serialises `RR` as `rr`, but the Angular model reads `rR`. No error state; the spinner stays forever if the request fails. No pagination. | **Improve** |
| Trade journal — create / edit | `POST` / `PUT /api/trades` exist in the API. | Partial. **No UI** to create or edit a single trade; only CSV import adds trades. An invalid `Direction` throws inside `Enum.Parse`, so the API returns HTTP 500. No input validation. | **Improve**: add validation and a trade form. |
| Trade journal — delete | `DELETE /api/trades/{id}`, with a `confirm()` dialog in the UI. | Code-verified. Hard delete. | **Preserve**. Trades are journal records, not financial records, so hard delete is acceptable. |
| CSV journal import | `POST /api/upload` (10 MB limit, `.csv` only). CsvHelper parsing, per-row validation, `UploadLog` history, `GET /api/upload/logs`. | Partial. Re-uploading a file **duplicates every trade**. The import is not wrapped in one transaction. An unparseable `EntryTime` silently becomes 00:00, which distorts revenge-trading detection. Date parsing depends on the server culture. | **Improve**: duplicate detection, atomic import, strict time parsing. |
| Strategy management | `Strategy` entity, created automatically from a free-text name on trade create or import. | Partial. **There is no strategy management**: no CRUD, no rules, no description editing. The `Strategies` table has no `UserId`, so strategy names are shared across all users. | **Improve**: make strategies per user, add CRUD; strategy rules come later. |
| Performance analytics | `AnalyticsService`: total P&L, win rate, expectancy, average RR, profit factor, max drawdown, and win/loss/breakeven counts. Endpoints `/api/analytics/summary`, `equity-curve`, `by-strategy`, `by-day`, `by-session`. | Partial. Profit factor returns **gross profit in currency** when there are no losses (it should be undefined or ∞). Equity-curve drawdown % is measured against peak cumulative P&L, not account equity. All calculations load every trade into memory. No date filtering. | **Preserve and improve**: fix the definitions, add date ranges, add unit tests. |
| Dashboard (trading) | `DashboardComponent`: 6 KPI cards, equity curve, strategy, day and session bar charts (Chart.js via ng2-charts). | Partial. No error or empty state; it spins forever if the API fails. Values have no currency label. | **Preserve** as the "Trading Performance" page. Retitle it so it is not confused with the Executive Overview. |
| Behavioural analytics | `BehaviourAnalyticsService`: revenge trading (re-entry within 20 min of a loss), overtrading (more than 5 trades a day), loss streaks (3 or more), deviation count and P&L of manual overrides. Angular `BehaviourComponent` with an alert feed. | Code-verified logic. Thresholds are hard-coded constants. The `BehaviourAlerts` table exists but is **never written**; alerts are recomputed on every request. | **Preserve**; make thresholds configurable later; decide whether to persist alerts or drop the table. |
| Rule compliance | Only the `IsManualOverride` flag and `DeviationNotes` text on each trade. | Partial. There are no rule definitions and no rule-check evaluation. | **Preserve** the flag; full rule compliance is a later trading-module enhancement. |
| Psychological coaching | — | **Absent**. Nothing in the code beyond the deviation insight sentence on the behaviour page. | Not planned for the first release. Do not claim it exists. |
| Prop firm accounts | — | **Absent** | **Build** (Phase 8) |
| Upload history | Table on the upload page. | Code-verified | **Preserve** |
| Swagger UI | Swashbuckle 6.5.0, Development only. | Code-verified | **Improve**: keep it or move to built-in OpenApi when convenient; add JWT auth to the UI. |
| SSR server | `src/server.ts` (Express) plus `@angular/ssr`, `@angular/platform-server`, `express`. | **Not wired**: `angular.json` has no `server` or `ssr` option, so this file is never built. | **Retire** (dead code and unused dependencies) |
| `app.ts` | A second `AppComponent` declaration. | **Dead**: `main.ts` bootstraps `app.component.ts`. | **Retire** |
| `NavigationService` | Re-runs a callback on navigation. | Imported by the behaviour page but never injected. Unused. | **Retire** (after verifying change detection; see the zoneless note in `ARCHITECTURE_REVIEW.md`). |
| Automated tests | — | **Absent**. No .NET test project, no `*.spec.ts`, and `angular.json` has no `test` target, so `npm test` cannot run. | **Build** (Phase 2 onwards) |
| CI | `.github/` directory exists but is empty. | **Absent** | **Build** (Phase 2) |
| README | None on either branch, even though commit `be3dc6c` says "professional README". | Verified absent (`git diff main develop`) | **Build** (Phase 10, Melamu Holdings OS README) |

## Data the existing database holds

From `20260601103317_InitialCreate`, the only migration:

| Table | Purpose | Notes |
|---|---|---|
| `Users` | Login accounts | Unique email. Holds the existing user's account. |
| `Trades` | Journal entries | FK to `Users` (cascade), `Strategies` (set null), `UploadLogs`. P&L `decimal(18,2)`, prices `decimal(18,5)`. |
| `Strategies` | Strategy names | No owner column (shared across users). |
| `UploadLogs` | CSV import history | FK to `Users` (cascade). |
| `BehaviourAlerts` | Intended alert history | Never written by the application. |

None of these tables needs a destructive change for the Melamu Holdings OS financial modules. See `PLAN.md` § Database migration strategy.
