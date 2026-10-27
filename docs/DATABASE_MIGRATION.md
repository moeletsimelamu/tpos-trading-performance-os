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

The old `TraderDashboard` database (test data only, decision D5) is no longer used and has not been migrated.
