# Architecture

How the code is organised today (after Phase 3). Plans for modules that aren't built yet are in [`../PLAN.md`](../PLAN.md); the original audit is in [`ARCHITECTURE_REVIEW.md`](ARCHITECTURE_REVIEW.md).

## Backend (.NET 10)

```
MelamuHoldings.Domain/          no dependencies
  Common/       DomainException
  Identity/     User
  Trading/      Trade, Strategy, UploadLog, TradeDirection, UploadStatus
  Finance/      Money, Currency            (used by the finance modules from Phase 4)
MelamuHoldings.Application/     depends on Domain
  Common/       IRepository<T>
  Identity/     AuthService, OwnerAccountService, JwtService, JwtOptions, auth DTOs, IUserRepository
  Trading/      TradeService, AnalyticsService, BehaviourAnalyticsService, UploadService,
                CsvParserService, trade DTOs, repository interfaces
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
- **Modules depend in one direction.** Trading records reference their `User`; Identity knows nothing about trading. Finance (Phase 4+) will depend on nothing in Trading, except optional references from payouts to prop accounts (Phase 8).
- **Namespaces follow folders.** For example, `MelamuHoldings.Application.Trading`.
- **Money rules live in Domain** (`Money`, `Currency`) and are unit-tested without a database. See [`FINANCIAL_DOMAIN.md`](FINANCIAL_DOMAIN.md).
- **One `AppDbContext`.** Trading tables are in `dbo`; finance tables will use the `finance` schema.
- **Errors are ProblemDetails.** Business-rule violations will be `DomainException` → HTTP 422.

## Frontend (Angular 21, standalone, zoneless, signals)

```
src/app/
  core/
    auth/       AuthService (signal user), authGuard, authInterceptor, LoginComponent
    http/       errorInterceptor (401 → sign out)
    layout/     ShellComponent, NAVIGATION (module list with status), PageTitleStrategy
  shared/       describeHttpError, LoadErrorComponent
  features/
    trading/    trading.routes.ts (mounted at /trading), TradingApiService, models,
                performance/, trades/, import/, behaviour/
```

**Rules**

- **Each feature module has its own lazy routes and API service.** New modules go under `features/<module>/` with a `<module>.routes.ts`.
- **Component state uses signals.** The app has no zone.js, so plain fields don't trigger a re-render.
- **Old URLs (`/dashboard`, `/trades`, `/upload`, `/behaviour`) redirect** to their `/trading/...` homes.
- **The landing page is `/trading/performance`** until the Executive Overview exists (Phase 5).
- **Browser tab titles** come from route `title`s plus the product name.
- **Design rules and tokens** are in [`DESIGN.md`](DESIGN.md).

## Data

- **SQL Server Express.** The development database is `MelamuHoldings`.
- **Schema changes go through EF Core migrations only.** See [`DATABASE_MIGRATION.md`](DATABASE_MIGRATION.md) for the procedure and the migration log.
