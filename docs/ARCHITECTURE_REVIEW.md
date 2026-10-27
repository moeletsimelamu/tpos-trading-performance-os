# Architecture Review

Phase 1 audit of the trading analytics codebase. It records what the code actually does today, which is the foundation Melamu Holdings OS will be built on.
The feature-by-feature inventory is in `EXISTING_FEATURES.md`. The target design is in `../PLAN.md`.

## 1. Repository state at audit time

| Item | Finding |
|---|---|
| Location | `C:\Users\moele.PC_YA_MOELETSI\Documents\TraderAnalytics\tpos-trading-performance-os` |
| Remote | `origin` → `github.com/moeletsimelamu/tpos-trading-performance-os` (not contacted during the audit) |
| Branches | `main` at `be3dc6c`; `develop` at `d9f1aa5`, one commit ahead of `main`. The working tree is on `develop`. |
| History | 9 commits between 2026-06-01 and 2026-06-05, all authored `Junior Melamu <u22715895@up.ac.za>` (the global git identity). The repository has no local `user.name`/`user.email`. |
| **Uncommitted work** | Every `TraderDashboard.*` folder was renamed on disk to `TPOS.*`, and `TraderDashboard.slnx` was replaced by `TPOS.slnx`. Git sees 88 deletions plus 6 untracked folders. All 87 moved files are identical to their committed versions (§8). |
| **Consequence of that rename** | The `.csproj` files were **not** updated. `TPOS.API/TraderDashboard.API.csproj` still references `..\TraderDashboard.Application\…` and `..\TraderDashboard.Infrastructure\…`, and Application and Infrastructure reference `..\TraderDashboard.Domain\…`. Those paths no longer exist, so **the working tree does not build** (102 errors, §8). The committed `develop` tree builds cleanly. |

The prompt describes a `TraderDashboard.sln`. The repository actually uses the newer XML solution format (`.slnx`), and the folders are mid-rename to `TPOS.*`. Namespaces, assembly names and `.csproj` file names are all still `TraderDashboard.*`.

## 2. Technology versions (from project files and `node_modules`)

| Component | Version | Notes |
|---|---|---|
| .NET target | `net10.0` (all four projects) | SDK 10.0.403 installed locally |
| ASP.NET Core JWT bearer | 10.0.8 | |
| EF Core SqlServer, Design, Tools | 10.0.8 | |
| Swashbuckle.AspNetCore | 6.5.0 | Old relative to .NET 10; .NET 9+ ships `Microsoft.AspNetCore.OpenApi`. Works for now; not a blocker. |
| System.IdentityModel.Tokens.Jwt / Microsoft.IdentityModel.Tokens | 8.18.0 / 8.19.0 | Minor version skew; NuGet resolves the higher one. Align them during cleanup. |
| BCrypt.Net-Next | 4.2.0 | Referenced by Application **and** Infrastructure; only Application uses it. |
| CsvHelper | 33.1.0 | |
| Angular | 21.2.15 (installed) | Standalone components, functional guards and interceptors. |
| zone.js | **not installed** | The app therefore runs **zoneless** (the Angular 21 default). See §5. |
| Chart.js / ng2-charts | 4.5.x / 10.0.0 | |
| TypeScript | ~5.9.2 | |
| Database | SQL Server Express 2019 (`MSSQL15.SQLEXPRESS`), database `TraderDashboard` over a trusted connection | Installed locally |

## 3. Current architecture

```
TPOS.Angular  ──HTTP/JSON──▶  TPOS.API (controllers, Program.cs DI)
                                  │
                                  ▼
                          TPOS.Application (services, DTOs, repository interfaces, CSV parsing, JWT)
                                  │            ▲
                                  ▼            │ implements
                          TPOS.Domain     TPOS.Infrastructure (AppDbContext, EF configs, repositories, migrations)
```

- **Domain**: five anemic entities (`User`, `Trade`, `Strategy`, `UploadLog`, `BehaviourAlert`) and three enums. No behaviour, no invariants.
- **Application**: concrete service classes (`TradeService`, `AnalyticsService`, `BehaviourAnalyticsService`, `UploadService`, `CsvParserService`, `AuthService`, `JwtService`). Repository interfaces live here, which is a correct dependency direction. DTOs are partly in `DTOs/` and partly declared inside service files.
- **Infrastructure**: one `AppDbContext`, two `IEntityTypeConfiguration`s (User, Trade), a generic `BaseRepository<T>` and four specific repositories. Folder layout is inconsistent (`Infrastructure/Infrastructure/Repositories` vs `Infrastructure/Repositories`).
- **API**: four thin controllers. User id comes from the JWT `NameIdentifier` claim. No global exception handling, no ProblemDetails, no validation framework.
- **Angular**: one root component with a fixed sidebar, six lazy-loaded routes, one `ApiService` for every endpoint. Templates and styles are inline. Global design tokens live in `styles.scss` (dark theme, Syne and DM Mono from Google Fonts).

## 4. Strengths worth keeping

1. **Layer direction is correct.** Domain has no dependencies; Application owns the interfaces; Infrastructure implements them. The financial modules can be added inside the same structure without a rewrite.
2. **Per-user data scoping is consistent.** Every trade, analytics and upload query filters by the authenticated user id. `GetById`, `Update` and `Delete` check ownership before acting.
3. **Money already uses `decimal`** in C# and `decimal(18,2)` in SQL. There is no floating-point arithmetic in the backend.
4. **Passwords use BCrypt.** No plaintext storage.
5. **EF Core migrations exist from day one.** The schema can evolve incrementally.
6. **Analytics logic is pure and in-memory.** It is easy to unit test once extracted from the repository calls.
7. **The Angular app is modern**: standalone components, lazy routes, functional guards and interceptors, `takeUntilDestroyed`.

## 5. Weaknesses and defects

Ordered by impact on the transformation.

| # | Area | Finding | Impact |
|---|---|---|---|
| W1 | Build | The uncommitted folder rename broke every `ProjectReference` (§1). | Blocks everything until it is fixed. |
| W2 | Security | The JWT signing key is in `appsettings.json` and therefore **public on GitHub**. Any token signed with it must be considered forgeable. | Must be fixed (rotated and moved to user-secrets or environment) **before** any financial data is stored. |
| W3 | Security | Open self-registration: anyone who can reach the API can create an account. | Unacceptable for company finances. |
| W4 | Testing | No tests at all: no .NET test project, no spec files, no `test` target in `angular.json`. | There is no regression baseline; one must be created before refactoring. |
| W5 | Frontend runtime | `zone.js` is absent, so the app is zoneless. Components assign plain fields inside `subscribe()` callbacks, and in zoneless mode an HTTP response does not by itself trigger change detection. The unused `NavigationService` and the "navigation fix" commit suggest views were not refreshing. | **Needs browser verification.** The fix is signals (or `ChangeDetectorRef.markForCheck`), and that pattern should be settled before new pages copy the old one. |
| W6 | Frontend contract | C# `RR` serialises as `rr` under System.Text.Json camel-casing, but the Angular `Trade` model reads `rR`. The RR column in the trade table is very likely empty. | Needs runtime confirmation; fix in Phase 2. |
| W7 | Frontend UX | The dashboard, behaviour and trades pages have no error state. A failed request leaves a permanent spinner. | The new financial pages must not repeat this; existing pages need fixing. |
| W8 | Frontend layout | `AppComponent` always renders the sidebar, including on `/login` and `/register`. *Corrected in Phase 2:* the bootstrapped `app.component.ts` is the older shell with **no Sign out button at all**; the user/Sign out shell from commit `649076c` sat unused in `app.ts`. | Fixed in Phase 2. |
| W9 | Validation | No validation on trade DTOs. An invalid `Direction` makes `Enum.Parse` throw (HTTP 500). Negative risk or prices are accepted. | Fix in Phase 2 and establish the validation pattern the finance API will reuse. |
| W10 | Error handling | No global exception handler. Errors are returned as plain strings with inconsistent shapes. A concurrent duplicate registration surfaces as a 500 from the unique index. | Add ProblemDetails plus an exception-handling middleware in Phase 2. |
| W11 | Data integrity | CSV import duplicates trades on re-upload, is not atomic, and silently coerces bad times to 00:00. | Fix in Phase 2 or Phase 8. Not a blocker for finance. |
| W12 | Data model | `Strategies` has no `UserId`, so names are shared across users. Strategy "management" is only an auto-created tag. | Make strategies per-user in Phase 8. |
| W13 | Analytics correctness | Profit factor with zero losses returns a currency amount. Equity-curve drawdown % is computed against cumulative P&L, not equity. There are no date filters. | Fix with tests in Phase 2 or Phase 8. |
| W14 | Dead code | `app.ts` (never bootstrapped, but holding the intended user/Sign out shell; see W8), `server.ts` plus SSR dependencies, `NavigationService`, the `BehaviourAlerts` table (never written), and the `AnalyticsServicecs.cs` filename typo. | Clean up in Phase 2. |
| W15 | Architecture | `JwtService` reads `IConfiguration` directly from the Application layer. DTOs are scattered. There is no options pattern. | Minor. Address when touching auth. |
| W16 | Ops | No CI, no Docker, no production environment file (`apiUrl` is hard-coded to `http://localhost:5051/api`), no README on `develop`. | Phase 2 (CI) and Phase 10 (docs, deployment). |

## 6. Database design concerns

- **One migration, `InitialCreate`.** The schema is small and stable, so adding financial tables is purely additive.
- `Users → Trades`, `UploadLogs` and `BehaviourAlerts` all use **cascade delete**. That is acceptable for journal data. Financial tables must **not** cascade from `Users`: deleting a login must never delete company financial history. Finance foreign keys will use `Restrict`.
- Text columns are `nvarchar(max)` (Instrument, Session, DayOfWeek, Direction). This is harmless at this scale. New tables will use bounded lengths.
- `Trades.Direction` is stored as a string. `UploadLogs.Status` and `BehaviourAlerts.Severity` are stored as ints. New tables will store enums as strings for readable audit data.
- There are no concurrency tokens anywhere. Finance aggregates (allocations, payouts) need `rowversion`.
- The database name `TraderDashboard` comes from configuration. Keeping it avoids a data move; it can be renamed later with a backup and restore if wanted.

## 7. Security summary

| Control | Current state | Required for Melamu Holdings OS |
|---|---|---|
| Password storage | BCrypt ✔ | Keep |
| JWT signing key | Committed to the repository ✘ | User-secrets locally, environment variable in deployment; rotate the leaked key |
| Token lifetime | 7 days, stored in localStorage | Shorter lifetime (for example 8 h). localStorage is acceptable for a single-operator local app; revisit for public deployment. |
| Registration | Open ✘ | Disabled; owner account provisioned by a one-off command or seeded from secrets |
| Authorisation | Authenticated only | Add an `Owner` role/policy on all finance endpoints |
| CORS | `http://localhost:4200` only ✔ | Configurable per environment |
| HTTPS | `UseHttpsRedirection` with an HTTP dev profile on 5051 | Fine locally; enforce HTTPS and HSTS in deployment |
| Error leakage | Unhandled exceptions → default 500 | ProblemDetails, no stack traces outside Development |
| Rate limiting on login | None | Add the ASP.NET Core rate limiter on `/api/auth/login` |

## 8. Baseline (commands actually run on 2026-10-27)

| Command | Result |
|---|---|
| `git hash-object` of all 87 renamed `TPOS.*` files vs `HEAD:TraderDashboard.*` | **All 87 identical** after line-ending normalisation. The rename is a pure move. Only `TPOS.slnx` has new content (the project paths). |
| `git diff --stat main develop` | 4 files, +8 / −11 lines, all in Angular feature components. **Neither branch has a README**, even though commit `be3dc6c` mentions one. |
| `dotnet build TPOS.slnx` (working tree) | **FAILED: 102 errors, 7 warnings.** CS0246: Application types not found, because the `ProjectReference` paths point to deleted folders. |
| `dotnet build TraderDashboard.slnx` (committed `develop`, extracted with `git archive` to a scratch folder) | **Succeeded: 0 errors, 8 warnings.** All 8 are NU1902/NU1903: transitive `System.Security.Cryptography.Pkcs` 9.0.4 has known **high** (GHSA-846f-f764-76mx) and moderate (GHSA-2fmv-rv2c-59x7) vulnerabilities, reported in API and Infrastructure. |
| .NET tests | Nothing to run: no test projects. |
| `npx ng build` (production configuration) | **Succeeded.** Initial bundle 481.87 kB raw / 137.15 kB transfer (warning budget is 500 kB). 7 lazy chunks. No warnings. |
| `npx ng test` | **FAILED**: "Cannot determine project or target for command". There is no `test` target. |

**Not run in Phase 1** (deferred to the start of Phase 2):

- Starting the API against SQL Server Express.
- Exercising the endpoints.
- Browser verification of W5 (zoneless refresh), W6 (RR column) and W8 (sidebar on login).

Logging in needs either the owner's real password, which I must not enter, or a test account. The test account will be created in a separate scratch database, not in `TraderDashboard`.

## 9. Migration risks

| Risk | Mitigation |
|---|---|
| Losing the in-flight `TPOS.*` rename, or committing it broken | Fix the `ProjectReference` paths, build, and commit as one isolated "rename folders" commit before any feature work, using `git add -A` so git records renames rather than delete-and-add. |
| A namespace/assembly rename breaks EF migrations | Migrations are identified by their id string (`20260601103317_InitialCreate`), not their namespace. Rename the namespace and the snapshot's `[DbContext(typeof(...))]` together, then verify with `dotnet ef migrations list` and `has-pending-model-changes`. |
| Financial tables couple to trading tables | Use a separate SQL schema (`finance`) and separate entity configurations. The only cross-module links are optional foreign keys from finance to trading (payout → prop account), never the reverse. |
| Existing user's data (the `Users` row and trades) | Additive migrations only. Back up the database (`BACKUP DATABASE`) before applying any migration. The script is in `DATABASE_MIGRATION.md` (Phase 3). |
| Rebuilding the navigation hides existing pages | All four existing routes stay reachable under "Trading Operations", with redirects from the old paths. |
| Zoneless change detection copied into new pages | Settle the pattern (signals) in Phase 2, before new UI is written. |

## 10a. Phase 2 outcome (2026-10-27, branch `phase-2-stabilise`)

| # | Status | What happened |
|---|---|---|
| W1 | **Fixed** | Renamed straight to `MelamuHoldings.*` (decision D1) in one commit that git records as 85 renames. The solution builds again. |
| W2 | **Fixed** | `Jwt:Key` removed from `appsettings.json`. A new key is in user-secrets; startup fails if it is missing or shorter than 32 bytes. The old key remains in git history but no longer signs anything. |
| W3 | **Fixed** | Public registration removed from the API and UI. The owner account is created with `dotnet run -- create-owner`. Login is rate-limited (5 attempts/min/IP). |
| W4 | **Fixed** | 39 .NET unit tests, 15 API integration tests (real SQL Server), 24 Angular specs, and GitHub Actions CI. |
| W5 | **Confirmed and fixed** | Worse than suspected: in the browser **no data page ever rendered its data**. Requests returned 200 and component state held the data, but `Zone` was undefined and the DOM stayed on "Loading…". All pages now use signals. |
| W6 | **Confirmed and fixed** | The API returns `"rr"`; the model now reads `rr`. |
| W7 | **Fixed** | Every page has loading, error (with Retry) and empty states. Verified by stopping and restarting the API. |
| W8 | **Fixed** | The login route renders outside a new `ShellComponent`; Sign out works. |
| W9 | **Fixed** | `CreateTradeDto` validation returns 400 with field errors. |
| W10 | **Fixed** | ProblemDetails for all errors (exception handler, status pages, `Problem()`). |
| W11 | Open (Phase 8) | CSV duplicate detection and atomic import. |
| W12 | Open (Phase 8) | Per-user strategies. |
| W13 | **Partly fixed** | Expectancy counted breakevens as losses and has been corrected. Profit factor with zero losses still returns gross profit (a test pins it until the Phase 8 contract change). |
| W14 | **Fixed**, except the `BehaviourAlerts` table | Dead files and SSR packages removed. The table stays until a Phase 3 migration drops it. |
| W15 | **Fixed** | `JwtService` uses `IOptions<JwtOptions>`. |
| W16 | Partly | CI added. README is a short setup guide; the full README is Phase 10. |

**Defects found during Phase 2:**

- `GET /api/trades/{id}` and the create response never returned the strategy name. The trade was loaded with `FindAsync`, without its `Strategy`.
- The login rate limit ignored late configuration.
- npm 10.9.2 crashes (`edgesOut`) while resolving this project's peer dependencies, so the lockfile was regenerated with npm 11. `npm ci` on npm 10 accepts it.
- `ng2-charts`' open `@angular/cdk >=21` peer range resolves cdk 22 on a fresh install, so `@angular/cdk` is now pinned to 21.x.

**Remaining advisory:** `undici` (high) inside `@angular/build`. It is dev tooling only and not shipped. npm's only offered fix is a downgrade to Angular 20. The CI audit gate covers production dependencies.

## 10. Leave untouched initially

- The trade, analytics and behaviour **calculation logic**. Wrap it in characterisation tests first; correct definitions (W13) only with tests in place.
- The `Trades`, `UploadLogs` and `Users` table shapes.
- The visual design tokens in `styles.scss`, until the Phase 3 design pass. The new identity should evolve them, not discard them blindly.
