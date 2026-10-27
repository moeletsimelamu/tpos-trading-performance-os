# Melamu Holdings OS

A unified operating system for managing trading operations, financial performance, capital allocation, wealth creation and business continuity.

> **Status: in transformation.** This repository started as a trading performance analytics app and is being evolved into Melamu Holdings OS in phases (see [`PLAN.md`](PLAN.md)).
>
> **What works today** is the trading module:
> - trade journal and CSV import;
> - performance analytics (P&L, win rate, expectancy, profit factor, drawdown, equity curve, breakdowns by strategy, day and session);
> - behaviour analytics (revenge trading, overtrading, loss streaks, rule deviations).
>
> The financial modules (accounts, payouts, capital allocation, continuity, wealth) are **planned, not built**.

## Stack

ASP.NET Core 10 Web API · EF Core 10 · SQL Server · Angular 21 (standalone components, signals, zoneless) · Chart.js · xUnit · Vitest

```
MelamuHoldings.Domain          entities and enums
MelamuHoldings.Application     services, DTOs, repository interfaces
MelamuHoldings.Infrastructure  EF Core DbContext, configurations, migrations, repositories
MelamuHoldings.API             controllers, auth, ProblemDetails, `create-owner` command
MelamuHoldings.Angular         frontend
tests/                         unit and API integration tests
```

## Running locally

You need:
- the .NET 10 SDK;
- Node 22;
- SQL Server Express (`.\SQLEXPRESS`) or another SQL Server, with the connection set in `appsettings.Development.json`;
- `dotnet-ef`.

1. Set a JWT signing key (once per machine):

   ```bash
   dotnet user-secrets set "Jwt:Key" "<at least 32 random characters>" --project MelamuHoldings.API
   ```

2. Create the database:

   ```bash
   dotnet ef database update --project MelamuHoldings.Infrastructure --startup-project MelamuHoldings.API
   ```

3. Create your login. There is no public sign-up.

   ```bash
   dotnet run --project MelamuHoldings.API -- create-owner
   ```

4. Start the API on http://localhost:5051:

   ```bash
   dotnet run --project MelamuHoldings.API --launch-profile http
   ```

5. Start the frontend on http://localhost:4200:

   ```bash
   npm --prefix MelamuHoldings.Angular start
   ```

## Tests

```bash
dotnet test MelamuHoldings.slnx
```

```bash
npm --prefix MelamuHoldings.Angular test -- --watch=false
```

The integration tests need SQL Server. Details and the latest results are in [`docs/TESTING.md`](docs/TESTING.md).

## Documentation

- [`PLAN.md`](PLAN.md): target architecture, financial domain model, phases and decisions
- [`docs/EXISTING_FEATURES.md`](docs/EXISTING_FEATURES.md): inventory of the original application
- [`docs/ARCHITECTURE_REVIEW.md`](docs/ARCHITECTURE_REVIEW.md): audit findings and Phase 2 outcome
- [`docs/SECURITY.md`](docs/SECURITY.md): access model and secrets
- [`docs/TESTING.md`](docs/TESTING.md): test suites and results
