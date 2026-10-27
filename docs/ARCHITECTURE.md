# Architecture

How the code is organised today (after Phase 3). Plans for modules that aren't built yet are in [`../PLAN.md`](../PLAN.md); the original audit is in [`ARCHITECTURE_REVIEW.md`](ARCHITECTURE_REVIEW.md).

## Backend (.NET 10)

```
MelamuHoldings.Domain/          no dependencies
  Common/       DomainException
  Identity/     User
  Trading/      Trade, Strategy, UploadLog, TradeDirection, UploadStatus
  Finance/      Money, Currency, Fund, FinancialAccount, LedgerTransaction (+ LedgerEntry), AuditEvent
MelamuHoldings.Application/     depends on Domain
  Common/       IRepository<T>, PagedResult<T>, NotFound/Conflict/UniqueConstraintViolation exceptions
  Identity/     AuthService, OwnerAccountService, JwtService, JwtOptions, Roles/Policies, auth DTOs, IUserRepository
  Trading/      TradeService, AnalyticsService, BehaviourAnalyticsService, UploadService,
                CsvParserService, trade DTOs, repository interfaces
  Finance/      LedgerService (every money movement), FinanceSetupService (funds, accounts),
                FinanceReadModel (balances from entries), AuditLog, request/response contracts,
                IFinanceRepository
MelamuHoldings.Infrastructure/  depends on Application and Domain
  Persistence/  AppDbContext, Configurations/<Module>/, Migrations/, Repositories/
MelamuHoldings.API/             composition root
  Controllers/<Module>/, Cli/ (create-owner), Program.cs
tests/
  MelamuHoldings.UnitTests/         domain and application logic, no database
  MelamuHoldings.IntegrationTests/  the real API against a throwaway SQL Server database
```

**Rules**

- **Layers point inwards.** API → Application → Domain. Infrastructure implements Application's interfaces and is wired up in `Program.cs`.
- **Modules depend in one direction.** Trading and finance records reference their `User`, but Identity knows nothing about either. Finance depends on nothing in Trading; the only planned link is an optional reference from payouts to prop accounts (Phase 8).
- **Namespaces follow folders.** For example, `MelamuHoldings.Application.Trading`.
- **Money rules live in Domain.** `Money`, `Currency` and the ledger factories are unit-tested without a database. See [`FINANCIAL_DOMAIN.md`](FINANCIAL_DOMAIN.md).
- **Finance writes are use cases, not CRUD.** Each money movement has its own endpoint and `LedgerService` method. No endpoint edits a transaction or sets a balance. A change and its audit event are saved in one database transaction.
- **Finance data belongs to the business, not to a login.** Every user with the `Owner` role sees the same accounts and ledger. Trading data stays per user.
- **One `AppDbContext`.** Trading tables are in `dbo` and finance tables in the `finance` schema.
- **Errors are ProblemDetails.** `ProblemExceptionHandler` maps `DomainException` → 422, `NotFoundException` → 404 and `ConflictException` → 409.

## Frontend (Angular 21, standalone, zoneless, signals)

```
src/app/
  core/
    auth/       AuthService (signal user), authGuard, authInterceptor, LoginComponent
    http/       errorInterceptor (401 → sign out)
    layout/     ShellComponent, NAVIGATION (module list with status), PageTitleStrategy
  shared/       describeHttpError, LoadErrorComponent, MoneyPipe
  features/
    trading/    trading.routes.ts (mounted at /trading), TradingApiService, models,
                performance/, trades/, import/, behaviour/
    finance/    finance.routes.ts (mounted at /finance), FinanceApiService, models,
                accounts/, funds/, transactions/ (ledger + record form), audit/
```

**Rules**

- **Each feature module has its own lazy routes and API service.** New modules go under `features/<module>/` with a `<module>.routes.ts`.
- **Component state uses signals.** The app has no zone.js, so plain fields don't trigger a re-render.
- **The browser never computes an authoritative balance.** The record form's entry preview is display only; every figure shown comes from the API.
- **Money-moving forms send an `Idempotency-Key`.** The key is kept for retries of the same values and renewed when the form changes.
- **Old URLs (`/dashboard`, `/trades`, `/upload`, `/behaviour`) redirect** to their `/trading/...` homes.
- **The landing page is `/trading/performance`** until the Executive Overview exists (Phase 5).
- **Browser tab titles** come from route `title`s plus the product name.
- **Design rules and tokens** are in [`DESIGN.md`](DESIGN.md).

## Data

- **SQL Server Express.** The development database is `MelamuHoldings`.
- **Schema changes go through EF Core migrations only.** See [`DATABASE_MIGRATION.md`](DATABASE_MIGRATION.md) for the procedure and the migration log.
