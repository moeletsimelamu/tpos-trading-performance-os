# Trader Performance Analytics Dashboard

A full-stack analytics platform for traders to upload trading journal CSV files and analyse performance metrics, behavioural patterns, and risk statistics. Built as a portfolio project demonstrating production-style software engineering across the full stack.

## Tech Stack

| Layer | Technology |

| Frontend | Angular 17 (Standalone Components) |
| Backend | ASP.NET Core Web API (.NET 8) |
| Database | SQL Server + Entity Framework Core |
| Auth | JWT Authentication (BCrypt password hashing) |
| Charts | Chart.js via ng2-charts |
| Architecture | Clean Architecture (Domain / Application / Infrastructure / API) |

## Features

### Trading Journal Upload
- Upload CSV trading journals
- Automatic validation and parsing
- Upload history and audit trail
- Auto-creates strategies from CSV data

### Performance Analytics
- **Win Rate** — wins / total trades
- **Expectancy** — (win rate × avg win) − (loss rate × avg loss)
- **Profit Factor** — gross profit / gross loss
- **Average RR** — mean reward-to-risk ratio
- **Equity Curve** — cumulative P&L over time with drawdown
- **Max Drawdown** — peak-to-trough analysis
- **Strategy Breakdown** — per-strategy win rate, RR, and P&L
- **Time Analysis** — performance by day of week and trading session

### Behavioural Analytics
- **Revenge Trading Detection** — flags trades entered within 20 minutes of a loss
- **Overtrading Detection** — flags days exceeding the daily trade limit
- **Consecutive Loss Streaks** — detects and alerts on loss run sequences
- **Strategy Deviation Tracking** — measures P&L impact of rule violations

### Trade Management
- View, filter, search, and delete trades
- Filter by direction, result, instrument, strategy, and session

### Authentication
- JWT-based stateless authentication
- BCrypt password hashing
- Route guards and HTTP interceptors

## Architecture
TraderDashboard/
├── TraderDashboard.Domain/          # Entities, Enums (no dependencies)
├── TraderDashboard.Application/     # Services, DTOs, Interfaces
├── TraderDashboard.Infrastructure/  # EF Core, Repositories, SQL Server
├── TraderDashboard.API/             # Controllers, Middleware, Program.cs
└── TraderDashboard.Angular/         # Angular SPA

Dependency direction: API → Application → Domain. Infrastructure depends on Domain and is wired at startup via Dependency Injection.

## Getting Started

### Prerequisites
- .NET 8 SDK
- Node.js 18+
- SQL Server (Express or full)
- Angular CLI (`npm install -g @angular/cli`)

### Backend Setup

```powershell
# Update connection string in appsettings.json
# Then run migrations
dotnet ef database update --project TraderDashboard.Infrastructure --startup-project TraderDashboard.API

# Run the API
dotnet run --project TraderDashboard.API
```

API runs at `http://localhost:5051`

### Frontend Setup

```powershell
cd TraderDashboard.Angular
npm install
ng serve
```

App runs at `http://localhost:4200`

### CSV Format
Your trading journal CSV must include these headers:

TradeDate, EntryTime, Instrument, Direction, EntryPrice, ExitPrice,
RiskAmount, PnL, RR, Strategy, Session, DayOfWeek, TradeDuration,
Notes, IsManualOverride, DeviationNotes

## API Endpoints

| Method | Endpoint | Description |

| POST | `/api/auth/register` | Create account |
| POST | `/api/auth/login` | Login, returns JWT |
| GET | `/api/trades` | List trades |
| POST | `/api/trades` | Create trade |
| PUT | `/api/trades/{id}` | Update trade |
| DELETE | `/api/trades/{id}` | Delete trade |
| POST | `/api/upload` | Upload CSV journal |
| GET | `/api/upload/logs` | Upload history |
| GET | `/api/analytics/summary` | KPI summary |
| GET | `/api/analytics/equity-curve` | Equity curve data |
| GET | `/api/analytics/by-strategy` | Strategy breakdown |
| GET | `/api/analytics/by-day` | Performance by day |
| GET | `/api/analytics/by-session` | Performance by session |
| GET | `/api/analytics/behaviour` | Behavioural analytics |

## Author

Built by Moeletsi Melamu as a portfolio project for software engineering and fintech applications.
