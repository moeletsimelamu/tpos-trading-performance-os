# Database Migrations

How the schema changes safely. The strategy behind these rules is in [`../PLAN.md`](../PLAN.md) §4.

## Rules

1. **Never delete, squash or edit an applied migration.** Schema history only grows.
2. **Review the SQL before applying.** Generate the idempotent script and read it. EF's "may result in the loss of data" warning must be explained in the migration's commit message.
3. **Back up first** whenever the target database holds data you care about, and verify the backup before applying.
4. **Never run a destructive migration against production** without a verified backup and the owner's go-ahead.
5. Integration tests apply every migration to a throwaway database on each run, so a migration that does not apply from scratch fails CI.

## Procedure

Run from the repository root. Replace `<Name>` with a PascalCase description of the change.

**1. Create the migration** (always under `Persistence/Migrations`):

```bash
dotnet ef migrations add <Name> --project MelamuHoldings.Infrastructure --startup-project MelamuHoldings.API --output-dir Persistence/Migrations
```

**2. Review it.** Read the generated `.cs` file, then the SQL:

```bash
dotnet ef migrations script --idempotent --project MelamuHoldings.Infrastructure --startup-project MelamuHoldings.API
```

**3. Back up and verify.** This writes to SQL Server's default backup folder (`...\MSSQL15.SQLEXPRESS\MSSQL\Backup` for this machine):

```bash
sqlcmd -S ".\SQLEXPRESS" -E -C -b -Q "BACKUP DATABASE [MelamuHoldings] TO DISK = N'MelamuHoldings_<yyyymmdd>_pre-<Name>.bak' WITH INIT, CHECKSUM; RESTORE VERIFYONLY FROM DISK = N'MelamuHoldings_<yyyymmdd>_pre-<Name>.bak' WITH CHECKSUM;"
```

**4. Apply:**

```bash
dotnet ef database update --project MelamuHoldings.Infrastructure --startup-project MelamuHoldings.API
```

**5. Confirm the model and the database agree:**

```bash
dotnet ef migrations has-pending-model-changes --project MelamuHoldings.Infrastructure --startup-project MelamuHoldings.API
```

## Restoring a backup

Stop the API first, so it doesn't hold connections open, then run:

```bash
sqlcmd -S ".\SQLEXPRESS" -E -C -b -Q "ALTER DATABASE [MelamuHoldings] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [MelamuHoldings] FROM DISK = N'MelamuHoldings_<yyyymmdd>_pre-<Name>.bak' WITH REPLACE; ALTER DATABASE [MelamuHoldings] SET MULTI_USER;"
```

To undo only the latest migration, with no backup involved, apply the previous one by name. EF runs that migration's `Down`, and you can then delete the unapplied migration files:

```bash
dotnet ef database update <PreviousMigrationName> --project MelamuHoldings.Infrastructure --startup-project MelamuHoldings.API
```

## Migration log

| Migration | Applied | What it does | Data impact | Backup taken |
|---|---|---|---|---|
| `20260601103317_InitialCreate` | 2026-06-01 (original app) | Users, Trades, Strategies, UploadLogs, BehaviourAlerts | — | — |
| `20261027135646_RemoveUnusedBehaviourAlerts` | 2026-10-27, dev `MelamuHoldings` and `MelamuHoldings_Scratch` | Drops `BehaviourAlerts`. The application never wrote to it; alerts are computed on each request. `Down` recreates it. | None. The table was empty in every database. | `MelamuHoldings_20261027_pre-RemoveUnusedBehaviourAlerts.bak`, verified with `RESTORE VERIFYONLY` |
| `20261027144346_AddFinanceLedger` | 2026-10-27, dev `MelamuHoldings` and `MelamuHoldings_Scratch` | Creates the `finance` schema: `Funds`, `FinancialAccounts`, `LedgerTransactions`, `LedgerEntries`, `AuditEvents`, with Restrict foreign keys and unique indexes (idempotency key, reversed transaction, names). Seeds the five default funds. `Down` drops the five tables. | Additive; existing tables untouched. | `MelamuHoldings_20261027_pre-AddFinanceLedger.bak`, verified with `RESTORE VERIFYONLY` |
| `20261027185922_AddPayoutsAndAllocation` | 2026-10-27, dev `MelamuHoldings` and `MelamuHoldings_Scratch` | Creates `Payouts`, `AllocationPolicies` (+ lines), `Allocations` (+ lines, settlements) in `finance`, with rowversions and unique indexes (idempotency keys, one active policy, one live allocation per payout). Seeds policy v1 (40/30/20/10). Adds `LedgerEntries.Role` and `LedgerTransactions.QuotedRate`. `Down` drops them. | Additive. Existing ledger entries get `Role = 'Principal'` (the migration's column default); nothing else changes. | `MelamuHoldings_20261027_pre-AddPayoutsAndAllocation.bak` (dev), verified with `RESTORE VERIFYONLY`. The scratch database holds test data only and was not backed up. |
| `20261027234637_AddTradeJournalFields` | 2026-10-28, dev `MelamuHoldings` and `MelamuHoldings_Scratch` | Trade journal: adds `Status` (default `Closed`), stop, target, size, fees, exit date and time, currency, trading account, review notes and time, updated time and an idempotency key (unique per user) to `Trades`; makes direction, entry time, prices, risk, P&L, R and duration nullable. | Additive for data. Every existing trade becomes `Closed`, and its values are kept. `Down` restores NOT NULL columns, so it fails while drafts or open trades exist: delete or complete them first. | `MelamuHoldings_20261028_pre-AddTradeJournalFields.bak` (dev), verified with `RESTORE VERIFYONLY`. Scratch: test data only, not backed up. |
| `20261028071219_AddJournalTemplates` | 2026-10-28, dev `MelamuHoldings` and `MelamuHoldings_Scratch` | Journal templates: creates `JournalTemplates` (one default per user, filtered unique index), `JournalTemplateVersions` (unique template + version, Restrict delete) and `TradeJournalEntries` (one per trade, deleted with its trade). `Down` drops the three tables. | Additive; existing tables untouched. The standard template is created per user by the application on first use, not by the migration. | `MelamuHoldings_20261028_pre-AddJournalTemplates.bak` (dev), verified with `RESTORE VERIFYONLY`. Scratch: test data only, not backed up. |
| `20261028074550_AddStrategyRulesAndChecklists` | 2026-10-28, dev `MelamuHoldings` and `MelamuHoldings_Scratch` | Strategies become owned: adds `Strategies.UserId` (backfilled, then required; unique with `Name`), `Status` (default `Active`) and `UpdatedAt`. `Name` becomes `nvarchar(100)` and `Description` nullable `nvarchar(500)`. Creates `StrategyRules`, `TradeChecklists`, `ChecklistItems` and `ChecklistResponses`. `Down` drops the new tables and columns. | The backfill gives each strategy to the lowest-numbered trader whose trades use it and copies it for any other trader, moving their trades. Strategies with no trades are removed. Names are trimmed (and cut to 100 characters); names that then repeat for one trader are merged. Blank descriptions become NULL. Tested by hand on a throwaway database with shared, unused, case-duplicate and over-long strategies, then rolled down and up again. `Down` keeps the per-trader copies. Dev had no strategies; scratch kept its 3 (one owner). | `MelamuHoldings_20261028_pre-AddStrategyRulesAndChecklists.bak` (dev), verified with `RESTORE VERIFYONLY`. Scratch: test data only, not backed up. |
| `20261028171324_AddAttachmentsAndImportMatches` | 2026-10-28, dev `MelamuHoldings` and `MelamuHoldings_Scratch` | Creates `TradeAttachments` (one row per file, unique storage key, deleted with its trade) and `ImportMatches` (CSV rows held back for a decision: row JSON, status, rowversion; the matched trade becomes NULL if deleted; one `Linked` match per trade by filtered unique index). Adds `DuplicateRows` and `MatchedRows` to `UploadLogs` (default 0). `Down` drops them. | Additive; existing rows untouched. Attachment files live outside the database (see `docs/SECURITY.md`). | `MelamuHoldings_20261028_pre-AddAttachmentsAndImportMatches.bak` (dev), verified with `RESTORE VERIFYONLY`. Scratch: test data only, not backed up. |
| `20261028224044_AddTradeRevisions` | 2026-10-29, dev `MelamuHoldings` and `MelamuHoldings_Scratch` | Creates `TradeRevisions`: one append-only row per saved change to a trade (time, kind, status after, and the changed values as JSON), indexed by trade and time, deleted with its trade. `Down` drops the table. | Additive; existing tables untouched. Trades saved before this migration have no history; the trade page says so. | `MelamuHoldings_20261029_pre-AddTradeRevisions.bak` (dev), verified with `RESTORE VERIFYONLY`. Scratch: test data only, not backed up. |
| `20261029081329_AddStrategyScopeAndRuleCategories` | 2026-10-29, dev `MelamuHoldings` and `MelamuHoldings_Scratch` | Adds `Category` (`nvarchar(20)`) to `StrategyRules` and `ChecklistItems`; `Instruments` and `Sessions` (JSON arrays) and `DefaultTemplateId` (nullable, Restrict foreign key to `JournalTemplates`, indexed) to `Strategies`. `Down` drops them. | Existing rules and checklist items become `General`, and existing strategies get `[]` (anywhere) and no default template. EF generated an empty-string default for the categories, which would not have read back as a category; it was changed to `General` by hand and checked on scratch (3 rules, 7 items, 3 strategies). | `MelamuHoldings_20261029_pre-AddStrategyScopeAndRuleCategories.bak` (dev), verified with `RESTORE VERIFYONLY`. Scratch: test data only, not backed up. |

The old `TraderDashboard` database (test data only, decision D5) is no longer used and has not been migrated.
