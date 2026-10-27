# Melamu Holdings OS

A unified operating system for managing trading operations, financial performance, capital allocation, wealth creation and business continuity.

> **Status: in transformation.** This repository started as the *Trader Performance Analytics Dashboard* and is being evolved into Melamu Holdings OS in phases (see [`PLAN.md`](PLAN.md)).
>
> **What works today** is the trading module described below. The financial modules (accounts, payouts, capital allocation, continuity, wealth) are **planned, not built**.

## What works today: trading operations

### Trading journal
- Import trades from CSV, with row-level validation. Valid rows are kept and failed rows are reported with their row number.
- Upload history per user.
- Strategies are created automatically from the CSV `Strategy` column.
- View, search and delete trades. Search covers instrument, strategy, session and day; filters cover direction and result.

### Performance analytics
- **Win rate**: winning trades ÷ total trades.
- **Expectancy**: (win rate × average win) − (loss rate × average loss). Loss rate counts losing trades only, so this equals the mean P&L per trade; breakevens count as zero.
- **Profit factor**: gross profit ÷ gross loss. *Known issue:* with no losing trades it currently shows gross profit instead of "undefined"; the fix is scheduled for Phase 8.
- **Average RR**: mean reward-to-risk ratio.
- **Equity curve**: cumulative P&L by day.
- **Max drawdown**: the largest peak-to-trough fall in cumulative P&L.
- **Breakdowns**: by strategy, day of week and trading session.

### Behavioural analytics
- **Revenge trading**: a trade entered within 20 minutes of a losing trade on the same day.
- **Overtrading**: more than 5 trades in a day (Critical above 10).
- **Loss streaks**: 3 or more consecutive losses (Critical at 5).
- **Rule deviations**: the count and P&L of trades marked as manual overrides.

### Access
- **Single operator.** There is no public sign-up; the owner account is created from the command line.
- JWT authentication (8-hour tokens) with BCrypt password hashing and login rate limiting.
- Every query is scoped to the signed-in user.

## Tech stack

| Layer | Technology |
|---|---|
| Frontend | Angular 21: standalone components, signals, zoneless change detection |
| Backend | ASP.NET Core Web API (.NET 10), RFC 7807 ProblemDetails |
| Database | SQL Server with Entity Framework Core 10 migrations |
| Auth | JWT bearer tokens, BCrypt |
| Charts | Chart.js via ng2-charts |
| Tests | xUnit (unit and API integration against SQL Server), Vitest (Angular) |
| CI | GitHub Actions |

## Architecture

```
MelamuHoldings.Domain          entities and enums (no dependencies)
MelamuHoldings.Application     services, DTOs, repository interfaces
MelamuHoldings.Infrastructure  EF Core DbContext, configurations, migrations, repositories
MelamuHoldings.API             controllers, auth, error handling, `create-owner` command
MelamuHoldings.Angular         Angular SPA
tests/                         unit and API integration tests
```

Dependencies point inwards:
- API → Application → Domain.
- Infrastructure implements the Application interfaces and is wired up at startup through dependency injection.

## Running locally

You need:
- the .NET 10 SDK;
- Node 22;
- SQL Server Express (`.\SQLEXPRESS`) or another SQL Server, with the connection set in `MelamuHoldings.API/appsettings.Development.json`;
- `dotnet-ef`.

1. Set a JWT signing key (once per machine):

   ```bash
   dotnet user-secrets set "Jwt:Key" "<at least 32 random characters>" --project MelamuHoldings.API
   ```

2. Create the database:

   ```bash
   dotnet ef database update --project MelamuHoldings.Infrastructure --startup-project MelamuHoldings.API
   ```

3. Create your login:

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

### CSV format

The journal CSV needs a header row with these columns:

```
TradeDate, EntryTime, Instrument, Direction, EntryPrice, ExitPrice,
RiskAmount, PnL, RR, Strategy, Session, DayOfWeek, TradeDuration,
Notes, IsManualOverride, DeviationNotes
```

`Direction` is `Long` or `Short`, and entry and exit prices must be greater than zero.

## API endpoints

All endpoints except login require a bearer token. Errors are returned as ProblemDetails.

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/auth/login` | Sign in; returns a JWT |
| GET | `/api/trades` | List your trades |
| GET | `/api/trades/{id}` | Get one trade |
| POST | `/api/trades` | Create a trade (validated) |
| PUT | `/api/trades/{id}` | Update a trade |
| DELETE | `/api/trades/{id}` | Delete a trade |
| POST | `/api/upload` | Import a CSV journal (max 10 MB) |
| GET | `/api/upload/logs` | Upload history |
| GET | `/api/analytics/summary` | KPI summary |
| GET | `/api/analytics/equity-curve` | Daily equity curve |
| GET | `/api/analytics/by-strategy` | Strategy breakdown |
| GET | `/api/analytics/by-day` | Performance by day of week |
| GET | `/api/analytics/by-session` | Performance by session |
| GET | `/api/analytics/behaviour` | Behavioural alerts and metrics |

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

## Author

Built by Moeletsi Melamu.
