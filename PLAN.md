# Melamu Holdings OS — Transformation Plan

> A unified operating system for managing trading operations, financial performance, capital allocation, wealth creation, and business continuity.

This plan evolves the existing trading analytics application (see `docs/EXISTING_FEATURES.md` and `docs/ARCHITECTURE_REVIEW.md`) into Melamu Holdings OS. It is an incremental transformation: the .NET 10 / EF Core / SQL Server / Angular 21 stack and the layered solution stay. Trading analytics becomes the **Trading Operations** module. The financial modules are added beside it.

**Status:**
- Phase 1 (audit) is complete.
- Phase 2 (stabilise) is complete and merged to `main` (PR #2; outcome in `docs/ARCHITECTURE_REVIEW.md` §10a).
- Phase 3 (foundation) is complete on branch `phase-3-foundation` and awaiting review. The structure as built is in `docs/ARCHITECTURE.md`.
- Next is Phase 4 (accounts and ledger, including currency exchange).

---

## 1. Product principle and scope

**Trading generates business income. Melamu Holdings OS manages, allocates, tracks and reports on that income.**

The first usable release is the **payout-to-allocation milestone** (§7, Phase 5): configure funds and policy → record a payout actually received → preview the allocation → confirm it → record real transfers → reconcile → verify balances → review history → see the result on the Executive Overview. Every other module is delivered after that, incrementally. Each module appears in the navigation only with an honest status label (`Live`, `Partial`, `Planned`).

The system is an **operational management system, not accounting software**. It records cash movements and allocations faithfully, but it does not produce statutory financial statements, accrual-based profit, or tax figures. `docs/FINANCIAL_DOMAIN.md` will state these limits explicitly.

---

## 2. Target architecture

Keep the four .NET projects and the Angular app. Introduce **module folders** inside each layer instead of new projects: the codebase is small, and extra projects would add ceremony without adding isolation.

```
Domain/
  Trading/        Trade, Strategy, UploadLog, (later) PropFirmAccount …
  Finance/        Money, Currency, FinancialAccount, Fund, LedgerTransaction, LedgerEntry …
  Allocation/     AllocationPolicy, Allocation, AllocationLine, AllocationSettlement, AllocationCalculator
  Continuity/  Wealth/  Planning/   (later phases)
  Identity/       User
Application/
  <Module>/       use-case services, DTOs, validators, repository interfaces
Infrastructure/
  Persistence/    AppDbContext, Configurations/<Module>/, Migrations/
  Identity/       JWT issuance, password hashing
API/
  Controllers/<Module>/, ProblemDetails, auth policies
Angular src/app/
  core/           auth, http interceptors, error handling, layout shell
  shared/         money pipe, status badge, empty/error/loading states, table helpers
  features/
    overview/  trading/  finance/  allocation/  wealth/  continuity/  planning/  settings/
```

Rules:

- **Financial rules live in Domain** (`Money` arithmetic, allocation calculation and rounding, ledger invariants) and are unit-tested without a database. Controllers only translate HTTP. Angular never computes an authoritative balance.
- **Finance does not depend on Trading.** The single integration point is a `Payout`, which may optionally reference a prop firm account (Phase 8). The trading module never writes ledger entries.
- **One `AppDbContext`, separate SQL schemas** (`dbo` for the existing trading tables, `finance` for new ones). This gives clear boundaries without distributed transactions.
- **Use-case services, not generic CRUD**, for anything that moves money (`RecordIncome`, `RecordTransfer`, `ConfirmAllocation`, …). The generic `BaseRepository<T>` remains for trading data only.

### Product and solution naming

**Done in Phase 2.** Code identifiers were renamed in one controlled commit (`b23e3ce`): folders, `.csproj` files, namespaces, assembly names and `.slnx`, from `TraderDashboard.*` to `MelamuHoldings.*`.
- It was verified by a clean build and by `dotnet ef migrations has-pending-model-changes` (no changes); the EF migration id `20260601103317_InitialCreate` is unaffected.
- Development now uses a fresh `MelamuHoldings` database (decision D5). The old `TraderDashboard` database held only test trades and is left untouched.
- The **visible** identity (wordmark, navigation labels, favicon) changes in Phase 3.

---

## 3. Financial domain model

### 3.1 Core concepts and the distinctions they enforce

| Concept | What it is | What it is **not** |
|---|---|---|
| **Financial account** | A real place money is held: a bank account, cash, a brokerage account. Has a currency, an ownership flag (`Company` / `Personal` / `Unspecified`) and a status. Never stores credentials. | Not a fund. Not proof of legal ownership; ownership is what you configure, never inferred from the name. |
| **Fund** | A logical purpose for money: Operating Capital, Business Continuity, Wealth Fund, Owner's Distribution, plus a system fund **Unallocated**. Holds an optional minimum and target balance. | Not a bank account. One bank account can hold several funds. |
| **Ledger transaction** | An immutable, dated record of money moving, with type, description, reference, category and idempotency key. Consists of ledger entries. | Never edited or deleted after posting. Corrections are reversal transactions. |
| **Ledger entry** | One signed amount posted to an (account, fund) pair in one currency. | |
| **Payout** | A trading payout request: `Requested → Approved → Received` or `Rejected`. Only **receiving** it creates an income ledger transaction. | A request is not income. A prop-firm balance is not company cash. |
| **Allocation policy** | A versioned set of (fund, percentage) lines summing to exactly 100 %. Default 40 / 30 / 20 / 10. | Never edited once used; changing it creates a new version. |
| **Allocation** | The confirmed split of one received payout, with an **immutable snapshot** of fund names, percentages and amounts. | A preview is not an allocation, and an allocation is not a transfer. |
| **Allocation settlement** | Links a real transfer or earmark transaction to an allocation line. | |
| **Valuation snapshot** (Phase 7) | The observed value of an investment account on a date. | Not a ledger movement, not income, not cash. |

### 3.2 Ledger design

Each ledger entry posts a signed amount to **both** dimensions at once, a financial account and a fund. The two views come from the same records:

- **Account balance** (real money, per currency) = Σ entries for that account.
- **Fund balance** (earmarked money, per currency) = Σ entries for that fund.

Invariants that `Domain` enforces and tests cover:

| Transaction type | Entries | Rule |
|---|---|---|
| `OpeningBalance` | +X to (account, fund) | Once per (account, fund); dated |
| `Income` (including a received payout) | +X to (account, Unallocated or a named fund) | X > 0 |
| `Expense` | −X from (account, fund) | X > 0 |
| `Transfer` (physical, between bank accounts) | −X (A, fund₁), +X (B, fund₂) | Same currency; sums to zero |
| `FundReallocation` (earmark, same account) | −X (A, Unallocated), +X (A, Continuity) | Same account; sums to zero; **no bank movement** |
| `InvestmentContribution` / `InvestmentWithdrawal` | Transfer into or out of a brokerage-type account | Zero-sum; never income or gain |
| `OwnerDistribution` | −X from (account, Owner's Distribution) | Money leaving the company |
| `CurrencyExchange` | −X USD (A), +Y ZAR (B), with the rate stored | Two currencies; zero-sum is not required, the rate is recorded |
| `Reversal` | Exact negation of an earlier transaction, linked to it | The original is marked reversed, never deleted |

This is **single-sided for external flows and zero-sum for internal movements**, not full double-entry bookkeeping. That is a documented limitation. It is enough to reconcile every balance to its records, and it keeps "earmarked but not physically moved" distinct from "physically moved":

- An earmark that stays inside the operating account shows as fund balance with account = Operating Bank.
- A physical transfer shows the money in the reserve bank account.

The Business Continuity view (Phase 7) shows both.

### 3.3 Money and currency

- `Money` value object: `decimal Amount` plus ISO-4217 `Currency` (`char(3)`). Arithmetic across different currencies throws.
- Storage: `decimal(19,4)` for amounts and `decimal(9,6)` for percentages and rates. Amounts are rounded to the currency's minor unit (2 dp for ZAR and USD) at the **domain boundary**.
- Summaries are always grouped by currency. Converting between currencies happens only through a recorded `CurrencyExchange` transaction. There is no implicit FX and no live rates.

### 3.4 Allocation calculation and rounding policy

1. Only a payout in status `Received`, with a linked income transaction, can be allocated.
2. For each policy line, `raw = amount × pct / 100`. Round each line **down** to the minor unit.
3. Distribute the remaining minor units, one unit at a time, to the lines with the **largest truncated remainder**. Ties go by policy line order. This is the largest-remainder method.
4. Assert Σ lines = amount exactly. A violation is a domain exception, never a silent fix.

Examples (tests will assert these):

| Payout | Operating 40 % | Continuity 30 % | Wealth 20 % | Owner 10 % | Σ |
|---|---|---|---|---|---|
| $1,350.00 | 540.00 | 405.00 | 270.00 | 135.00 | 1,350.00 |
| $1,000.01 | **400.01** | 300.00 | 200.00 | 100.00 | 1,000.01 |
| $0.03 | 0.01 | **0.01** | **0.01** | 0.00 | 0.03 |

(`$0.03`: raw values are 0.012, 0.009, 0.006, 0.003. Truncated to 0.01, 0.00, 0.00, 0.00 with remainders 0.002, 0.009, 0.006, 0.003. The two spare cents go to Continuity and then Wealth.)

### 3.5 Allocation lifecycle

```
(preview: stateless POST /api/allocations/preview — nothing stored)
        │ confirm
        ▼
   Confirmed ──settle some lines──▶ PartiallySettled ──all lines settled──▶ Reconciled
        │                                   │
        └── cancel (only if no settlements) ┘──▶ Cancelled
```

- **Duplicate protection**:
  - A unique index on `Allocation.PayoutId` (filtered to exclude `Cancelled`).
  - Every money-moving request carries a client-generated **idempotency key**, unique-indexed on the ledger transaction. A retry returns the original result instead of a second posting.
  - `rowversion` on `Payout` and `Allocation`.
  - Confirmation and settlement run inside one database transaction.
- **Settlement**: recording a `Transfer` or `FundReallocation` can name the allocation line it settles. Over-settling a line is rejected. Line status and allocation status derive from the settlements.
- **Policy history**: `AllocationLine` stores fund id, fund name at the time, percentage and amount. Later policy changes never recalculate existing allocations.

### 3.6 Later-phase entities (designed now so the core does not need rework)

- `FinancialObligation`: name, amount, currency, due date, recurrence, status. Feeds warnings and continuity.
- `BudgetLine`: period, category, amount, currency. `PlannedPurchase`: `Proposed → Committed → Purchased (linked Expense) → Paid`, or `Cancelled`. A planned purchase never creates a ledger entry by itself.
- `ContinuityTarget`: essential-expense months (default 24), monthly essential expense (entered manually, or derived from expenses flagged essential), and a **separate** trading-restoration target amount.
  - Coverage in months = continuity fund balance ÷ monthly essential expense.
  - Remaining amount = target − recorded balance.
- `InvestmentAccount` (a financial account of type Brokerage) plus `ValuationSnapshot`. Unrealised gain/loss = latest valuation − net contributions, labelled **unrealised**.
- `PropFirmAccount`: firm, nickname, phase, size, currency, status history, and a configurable rule set (drawdown model, limits, target, minimum days, payout split, fees). Fees and resets are `Expense` transactions linked back to the account.
- **Warnings** (computed, never stored, never acted on automatically):
  - a fund below its minimum;
  - continuity below target;
  - an allocation not fully settled;
  - an obligation due within N days;
  - a budget line exceeded;
  - a planned purchase that would breach the operating minimum.

---

## 4. Database migration strategy

1. **Back up before every migration** against a database holding real data: `BACKUP DATABASE [MelamuHoldings] TO DISK = '…\MelamuHoldings_<date>.bak' WITH INIT, CHECKSUM`. Today the development database is empty apart from your login, so the first backups matter once real financial records exist. The restore procedure goes in `docs/DATABASE_MIGRATION.md`.
2. **Never delete or squash migrations.** `InitialCreate` stays.
3. **Additive migrations only** for the finance modules, in the `finance` schema. Finance foreign keys use `Restrict`; there is no cascade from `Users` into financial data.
4. Each migration is checked with `dotnet ef migrations script --idempotent` (SQL reviewed before it is applied), and the model is checked with `dotnet ef migrations has-pending-model-changes`.
5. Integration tests run every migration from empty against a throwaway SQL Server database (LocalDB or a dedicated test database on the local SQLEXPRESS instance). That proves the migrations are reproducible.
6. Changes to existing trading tables are deferred and small:
   - `Strategies.UserId` (nullable, then backfilled from the owners of referencing trades, then required);
   - `Trades.PropFirmAccountId` (nullable).
   Each change is its own migration with a written backfill.
7. The `BehaviourAlerts` table: retire it (drop) only after you confirm it holds nothing you want. The application never writes to it, so it should be empty.
8. **Sample data**: optional `--seed-sample` command that populates a separate database (for example `MelamuHoldings_Sample`), never the real one. Every sample record carries an `IsSample` marker, and the UI shows a "Sample data" banner when it is connected to that database.

Planned migrations (names are indicative):

1. `FinanceFoundation`: `finance.Funds`, `finance.FinancialAccounts`, `finance.LedgerTransactions`, `finance.LedgerEntries`
2. `PayoutsAndAllocation`: `finance.Payouts`, `finance.AllocationPolicies`, `finance.AllocationPolicyLines`, `finance.Allocations`, `finance.AllocationLines`, `finance.AllocationSettlements`
3. `AuditTrail`: `finance.AuditEvents`, an append-only who/what/when record for every finance write
4. Later: `Obligations`, `Budgets`, `Continuity`, `Investments`, `PropFirmAccounts`, `Planning`

---

## 5. API design

The existing routes stay unchanged: `/api/auth/*`, `/api/trades`, `/api/upload`, `/api/analytics/*`. New routes:

| Area | Endpoints (first cut) |
|---|---|
| Funds | `GET/POST /api/funds`, `PUT /api/funds/{id}` (name, minimum, target; not balance) |
| Accounts | `GET/POST /api/financial-accounts`, `GET /api/financial-accounts/{id}/balance`, `POST …/{id}/close` |
| Transactions | `GET /api/transactions?from&to&type&accountId&fundId&page` and `POST /api/transactions/{income\|expense\|transfer\|reallocation\|exchange\|opening-balance}`, `POST /api/transactions/{id}/reverse` |
| Payouts | `GET/POST /api/payouts`, `POST /api/payouts/{id}/approve`, `/receive` (creates income), `/reject` |
| Policies | `GET /api/allocation-policies`, `POST` (new version), `POST /{id}/activate` |
| Allocations | `POST /api/allocations/preview`, `POST /api/allocations` (confirm), `GET /api/allocations/{id}`, `POST /{id}/cancel` |
| Overview | `GET /api/overview?from&to` returns only metrics backed by stored records |

Cross-cutting:

- Request and response DTOs only; entities are never returned.
- FluentValidation, or hand-written validators, behind one filter.
- RFC 7807 ProblemDetails for all errors: 400 validation, 404, 409 duplicate or concurrency conflict, 422 domain rule violation.
- An `Idempotency-Key` header on every money-moving POST.
- An `Owner` authorisation policy on all finance controllers.
- Pagination on transaction lists.

---

## 6. Frontend plan

- **Shell**:
  - Split into an `AuthLayout` (login) and an `AppLayout` (sidebar), which fixes the sidebar showing on the login page.
  - Brand: "Melamu Holdings OS" wordmark, new favicon, `<title>`.
- **Navigation**, each item labelled with its real status:

  | Item | Contents |
  |---|---|
  | Executive Overview | |
  | Trading Operations | Performance (the current dashboard), Trades, Import, Behaviour, Prop Accounts *(planned)* |
  | Financial Management | Accounts, Transactions, Payouts |
  | Capital Allocation | Allocations, Policies |
  | Wealth Management *(planned)* | |
  | Business Continuity *(planned)* | |
  | Business Planning *(planned)* | |
  | Settings | Funds, Currencies, Profile |

  The old paths `/dashboard`, `/trades`, `/upload` and `/behaviour` redirect to their new homes.
- **State and change detection**: signals for component state, `HttpClient` wrapped in small per-module data services. This also resolves the zoneless refresh risk (`ARCHITECTURE_REVIEW` W5).
- **Shared components**:
  - `money` pipe that always shows the currency code;
  - status badge (`Proposed`, `Confirmed`, `Partially settled`, `Reconciled`, `Earmarked`, `Transferred`);
  - `<app-state>` for loading, empty and error states, with a retry action;
  - a submit button that disables while a request is in flight and carries the idempotency key.
- **Design direction**:
  - Serious internal tool: dense, readable tables; tabular numerals for figures; restrained colour (status only, not decoration).
  - Keep the existing dark palette as the base. Retire the glow effects and the amber-on-everything styling.
  - Charts only where a trend matters (equity curve, cash-flow by month).
  - `docs/DESIGN.md` will hold the tokens.
- **Testing**: add the Angular test target (Vitest via `@angular/build:unit-test`, the Angular 21 default) and component tests for the finance forms and the allocation preview and confirm flow.

---

## 7. Implementation phases

Each phase ends with a build, all tests green, a browser check of the touched pages, updated docs, and one or more focused commits. Nothing is pushed or deployed without your approval.

| Phase | Scope | Exit criteria |
|---|---|---|
| **2 — Stabilise** | Resolve the folder rename (decision D1) so the solution builds again: the working tree currently fails with 102 errors, while committed `develop` builds cleanly. Run the API against a scratch SQLEXPRESS database and check the existing pages in the browser. Pin a patched `System.Security.Cryptography.Pkcs` to clear the NU1903 high-severity advisory. Move the JWT key to user-secrets and rotate it. Disable open registration and add an owner-provisioning command. Add ProblemDetails and exception middleware. Validate trade DTOs. Fix the RR binding, the error states and zoneless refresh on existing pages. Remove dead code (`app.ts`, `server.ts`, SSR dependencies, `NavigationService`). Add an xUnit test project (characterisation tests for analytics and behaviour), the Angular test target, and a GitHub Actions CI workflow. | Clean build; tests pass in CI; existing four pages verified in the browser; no secret in the repository |
| **3 — Foundation** ✅ | Module folders (the `MelamuHoldings.*` rename was done in Phase 2). Drop the never-written `BehaviourAlerts` table. New layout and navigation with status labels. Brand identity. `Money`/`Currency` domain types. `finance` schema conventions (the schema itself is created by Phase 4's first migration). Backup procedure documented. | All existing features reachable and working under the new navigation; migrations apply from scratch and to the existing databases |
| **4 — Accounts and ledger** | Funds, financial accounts, ledger transactions and entries; income, expense, transfer, reallocation, opening balance, reversal, currency exchange. Balances by account and by fund. Audit events. Transaction list with filters and pagination. | Domain tests for every invariant in §3.2; API integration tests including 401, 400, 409 and idempotent retry; balances reconcile to entries |
| **5 — Payouts and allocation (first milestone)** | Payout lifecycle, versioned policies (default 40/30/20/10 seeded as configuration, not hard-coded), preview, confirm, settle via transfer or earmark, reconciliation, cancellation, duplicate protection. A minimal Executive Overview with cash by account, fund balances and outstanding allocations. | The §8 end-to-end scenario passes in automated API tests **and** in a manual browser walkthrough, with screenshots |
| **6 — Executive Overview** | Period filters; income and expenses by period; net cash movement (labelled as cash movement, not profit); recent payouts; drill-down links to records; warnings panel. | Every figure traceable to a record list |
| **7 — Continuity and wealth** | Obligations, continuity targets (24-month essential cover plus a separate restoration target), coverage months, earmarked versus physically separated view; investment accounts, contributions, valuation snapshots, unrealised gain/loss; budgets and planned purchases with budget-versus-actual. | Formulas documented in `FINANCIAL_DOMAIN.md` and unit-tested |
| **8 — Trading integration** | Prop firm accounts with configurable rules and status history; trades linked to accounts; fees and resets as expenses; payouts linked to accounts; per-user strategies with CRUD; CSV duplicate detection; analytics fixes (profit factor, drawdown, date filters). | Existing analytics unchanged except the documented definition fixes; tests prove it |
| **9 — Planning and reporting** | Objectives and milestones linked to financial targets; monthly cash-flow report; allocation history report. | Reports built only on stored data |
| **10 — Hardening** | Full regression run; security review; migration replay; UI and accessibility pass; README and screenshots; deployment documentation; `docs/REVIEW.md`. | Definition of Done (prompt §19 plus the supplementary §9) met, with known limitations listed |

---

## 8. First-milestone acceptance scenario

Automated as an API integration test, then repeated by hand in the browser:

1. Create funds: Operating Capital (minimum configurable), Business Continuity, Wealth Fund, Owner's Distribution.
2. Create accounts: Operating Bank (USD or ZAR; see decision D3) and Reserve Bank. Record opening balances.
3. Activate policy v1: 40 / 30 / 20 / 10.
4. Record payout $1,350.00 as `Requested`. The overview shows it as **pending**, not cash.
5. Mark it received into Operating Bank. Income of 1,350.00 is posted to Unallocated.
6. Preview the allocation: 540 / 405 / 270 / 135. Nothing is stored.
7. Confirm. The allocation is `Confirmed` and appears as outstanding on the overview.
8. Earmark 540 to Operating Capital inside Operating Bank. Physically transfer 405 to Reserve Bank / Continuity. The allocation is `PartiallySettled`.
9. Settle the remaining 270 and 135. The allocation is `Reconciled`.
10. Verify:
    - Operating Bank = opening + 1,350 − 405.
    - Reserve Bank = opening + 405.
    - Fund balances equal their allocated amounts plus opening balances.
    - Unallocated = 0.
11. Re-submit step 7 with the same idempotency key: the original allocation is returned. Submit with a new key: 409.
12. Change the policy to 50/25/15/10 and confirm a second payout. The first allocation still shows 40/30/20/10.
13. Repeat with $1,000.01 and $0.03 and check no cent is created or lost.

---

## 9. Testing strategy (summary; detail goes in `docs/TESTING.md`)

| Layer | Tooling | Focus |
|---|---|---|
| Domain unit | xUnit | Money, rounding, allocation calculator, ledger invariants, policy validation, settlement rules, existing analytics (characterisation) |
| Application | xUnit with the EF Core SQL Server provider against a test database | Use cases, transactions, idempotency, concurrency (two parallel confirms → one succeeds) |
| API integration | `WebApplicationFactory` | Auth (401/403), validation (400), conflict (409), ProblemDetails shape, the §8 scenario |
| Frontend | Angular unit-test builder (Vitest) | Forms, preview and confirm, double-submit prevention, empty/error/loading states |
| Manual / E2E | Browser walkthrough with screenshots | §8 scenario, existing trading pages |

Test counts and results are recorded as they actually are, never estimated.

---

## 10. Security and access model

- **One authorised operator** (Owner role) in the first release. The design does not block a later Viewer or Accountant role, but no multi-tenant machinery is built.
- Registration is disabled. The owner account is created by a CLI command (`dotnet run -- create-owner`) with the password read from the console, never from a file.
- Secrets: the JWT key and connection string come from user-secrets (development) or environment variables (deployment). The committed key is rotated.
- Finance endpoints require the `Owner` policy. Server-side validation covers every input. There are no endpoints that set a balance directly.
- An append-only `AuditEvents` record covers every finance write (who, when, what, before and after for configuration changes).
- No bank or brokerage credentials are stored, and no live integrations are built.

---

## 11. Portfolio relationships (documented, not built)

| Project | Possible future integration | Precondition |
|---|---|---|
| DocuMind | Extract amounts and dates from receipts, bank statements and prop-firm payout statements, producing **draft** transactions for review | Stable transaction API; a human confirms every draft |
| CloudOps Monitor | Health checks and uptime for the Melamu Holdings OS API once deployed | A deployment exists; the `/health` endpoint is added in Phase 10 |
| OpsHub | None needed. The pattern is shared engineering practice, not data. | — |

Melamu Holdings OS stays fully usable on its own. No integration code is written until its value is assessed.

---

## 12. Decisions (answered 2026-10-27)

| # | Decision | Outcome |
|---|---|---|
| D1 | Finish the `TPOS.*` rename, or go straight to the final names? | Went straight to `MelamuHoldings.*` in one commit. |
| D2 | Commit identity | Local identity set to `Moeletsi Melamu <moeletsimelamu03@gmail.com>`; existing history untouched. |
| D3 | Currencies | Payouts can arrive in a different currency from the bank accounts, so recording a `CurrencyExchange` (with its rate) is part of Phase 4, not later. |
| D4 | Branching | A feature branch per phase off `develop` (`phase-2-stabilise` first); nothing pushed without approval. |
| D5 | `TraderDashboard` data | Test data only. Development moved to a fresh `MelamuHoldings` database; `TraderDashboard` is untouched and can be dropped by the owner. |
