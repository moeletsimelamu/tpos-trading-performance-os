# Architecture

How the code is organised today (after Phase 5 and trading journal J1–J8). Plans for modules that aren't built yet are in [`../PLAN.md`](../PLAN.md); the original audit is in [`ARCHITECTURE_REVIEW.md`](ARCHITECTURE_REVIEW.md).

## Backend (.NET 10)

```
MelamuHoldings.Domain/          no dependencies
  Identity/     User
  Trading/      Trade (Draft → Open → Closed → Reviewed), Strategy (+ StrategyRule), TradeChecklist (+ items,
                append-only responses), TradeAttachment (+ AttachmentType), ImportMatch,
                TradeRevision (append-only change history), UploadLog
    Journal/    JournalTemplate (+ immutable versions), JournalSchema, TradeJournalEntry, DefaultJournalTemplate
  Common/       DomainException, Text (length rules)
  Finance/      Money, Currency, Fund, FinancialAccount, LedgerTransaction (+ LedgerEntry, fee role), AuditEvent,
                Payout, AllocationPolicy (+ AllocationCalculator), Allocation (+ lines, settlements)
MelamuHoldings.Application/     depends on Domain
  Common/       IRepository<T>, PagedResult<T>, NotFound/Conflict/UniqueConstraintViolation exceptions
  Identity/     AuthService, OwnerAccountService, JwtService, JwtOptions, Roles/Policies, auth DTOs, IUserRepository
  Trading/      TradeService (journal entries: draft, open, closed, review), AnalyticsScope (the one place
                analytics read trades; filters only narrow), CompletedTrade (which of them count),
                AnalyticsService, BehaviourAnalyticsService, AdherenceService, JournalAnswerAnalytics, JournalOverviewService,
                JournalTemplateService + TradeJournal, StrategyService + TradeChecklists,
                TradeHistory + TradeValues (change history), AttachmentService (+ IAttachmentStore), UploadService + ImportReconciliationService,
                AiReviewService and AiJournalQuestionService (+ IAiReviewProvider, optional), CsvParserService, DTOs, repository interfaces
  Finance/      LedgerPosting (the one path every ledger transaction takes: idempotency, save, audit),
                LedgerService (direct money movements), PayoutService, AllocationService (policies,
                preview, confirm, settle), FinanceSetupService (funds, accounts),
                FinanceReadModel (balances from entries), AuditLog, Idempotency, request/response contracts,
                IFinanceRepository
MelamuHoldings.Infrastructure/  depends on Application and Domain
  Persistence/  AppDbContext (stored times are read back as UTC), Configurations/<Module>/, Migrations/, Repositories/
  Attachments/  FileSystemAttachmentStore (files outside the web root, random names)
  Ai/           OpenAiCompatibleProvider (used only when AiReview is configured)
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
- **Finance writes are use cases, not CRUD.** Each money movement has its own endpoint and service method, and every ledger transaction is saved through `LedgerPosting`, together with whatever depends on it (a payout receipt, an allocation settlement) and its audit event, in one database transaction. No endpoint edits a transaction or sets a balance.
- **Concurrency.** Unique indexes stop duplicates (idempotency keys, one live allocation per payout). `rowversion` on payouts and allocations stops two requests changing the same one at once; the loser gets 409.
- **Finance data belongs to the business, not to a login.** Every user with the `Owner` role sees the same accounts and ledger. Trading data stays per user.
- **One `AppDbContext`.** Trading tables are in `dbo` and finance tables in the `finance` schema.
- **Errors are ProblemDetails.** `ProblemExceptionHandler` maps `DomainException` → 422, `NotFoundException` → 404, `ConflictException` → 409, `ServiceUnavailableException` → 503 (AI review off) and `UpstreamException` → 502 (AI provider failed).
- **Analytics read trades in one place.** Performance, behaviour and adherence all take their trades from `AnalyticsScope`, which applies the shared filter. Then `CompletedTrade` decides which count. No second calculation path exists.
- **The journal never depends on AI.** `IAiReviewProvider` is called only when `AiReview:BaseUrl` and `AiReview:Model` are set; nothing an AI reply contains is saved without the trader.
- **Changes to a trade are recorded with the change.** Services stage a `TradeRevision` (each value before and after) and it is saved in the same `SaveChanges`; there is no path that edits or deletes a revision except deleting the trade.
- **Files stay out of the database and the web root.** Attachment records are in SQL Server and the bytes are in `Attachments:Path`, served only through the API to the owner.

## Frontend (Angular 21, standalone, zoneless, signals)

```
src/app/
  core/
    auth/       AuthService (signal user), authGuard, authInterceptor, LoginComponent
    http/       errorInterceptor (401 → sign out)
    layout/     ShellComponent, NAVIGATION (the live destinations), PageTitleStrategy
  shared/       describeHttpError, LoadErrorComponent, MoneyPipe
    ui/         BrandMarkComponent, IconComponent, PageHeaderComponent, chart-theme
  features/
    overview/   OverviewComponent (mounted at /overview, the landing page); reads the
                finance and trading APIs, owns no endpoints
    trading/    trading.routes.ts (mounted at /trading), TradingApiService, models,
                journal/ (Journal page and calendar, trade form, trade page, templates, files, AI draft),
                strategies/ (rules, adherence), analytics/ (shared filter bar), performance/, trades/,
                import/ (with match decisions), behaviour/
    finance/    finance.routes.ts (mounted at /finance), FinanceApiService, models, amount.validator,
                payouts/ (list, detail with approve/receive/allocate), allocations/ (list, detail with
                settlement, policy versions), accounts/, funds/, transactions/ (ledger + record form), audit/
```

**Rules**

- **Each feature module has its own lazy routes and API service.** New modules go under `features/<module>/` with a `<module>.routes.ts`.
- **Component state uses signals.** The app has no zone.js, so plain fields don't trigger a re-render.
- **The browser never computes an authoritative balance.** The record form's entry preview and the overview's per-transaction headline amount are display only; every balance shown comes from the API.
- **Money-moving forms send an `Idempotency-Key`.** The key is kept for retries of the same values and renewed when the form changes.
- **Old URLs (`/dashboard`, `/trades`, `/upload`, `/behaviour`) redirect** to their `/trading/...` homes.
- **The landing page is `/overview`.** It uses only existing endpoints; each panel loads and fails on its own. Its "Needs attention" items are derived from records by `attentionItems()`, never stored.
- **Browser tab titles** come from route `title`s plus the product name.
- **Design rules, tokens and shared UI components** are in [`DESIGN_SYSTEM.md`](DESIGN_SYSTEM.md).

## Data

- **SQL Server Express.** The development database is `MelamuHoldings`.
- **Schema changes go through EF Core migrations only.** See [`DATABASE_MIGRATION.md`](DATABASE_MIGRATION.md) for the procedure and the migration log.
