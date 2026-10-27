# Financial Domain

The rules for money in Melamu Holdings OS. This file records what is **implemented**. The design for payouts and capital allocation, which are not built yet, is in [`../PLAN.md`](../PLAN.md) §3.4–3.6; it moves here as each part is implemented.

## Scope and limits

Melamu Holdings OS is an operational management system, **not accounting software**.
- It records cash movements and earmarks so that every balance can be traced to its records.
- It does not produce statutory financial statements, accrual-based profit or tax figures.
- Net cash movement is never labelled as profit.
- The ledger is single-sided for money entering or leaving the business, and zero-sum for movements inside it (see "Ledger"). It is not full double-entry bookkeeping.

## Money and currency

Code: `MelamuHoldings.Domain/Finance/Currency.cs`, `Money.cs`. Tests: `tests/MelamuHoldings.UnitTests/Finance/MoneyTests.cs`.

| Rule | Behaviour |
|---|---|
| Currency is always explicit | Every `Money` carries a `Currency`. There is no default currency. |
| Only supported currencies | `ZAR`, `USD`, `EUR` and `GBP` (ISO 4217, 2 minor units each). An unknown or mistyped code is rejected rather than creating a new currency. |
| Whole minor units | `Money.Of` rejects amounts finer than the currency allows: `10.005 USD` is a 422, not silently rounded. |
| Rounding is explicit | Calculated amounts go through `Money.Round(amount, currency, mode)`, so the rounding mode is visible where it happens. The allocation engine (Phase 5) will truncate with `ToZero`, then distribute the leftover minor units by largest remainder (PLAN §3.4). |
| No implicit conversion | Adding, subtracting, summing or comparing different currencies throws `CurrencyMismatchException`. Converting requires a recorded **currency exchange** with its rate. |
| Value equality | `10.5 USD == 10.50 USD`, but `10.5 USD != 10.5 ZAR`. |
| Decimal arithmetic only | `decimal` throughout. No `float` or `double` touches money. |

## Accounts and funds (Phase 4: implemented)

| Concept | What it is | Rules |
|---|---|---|
| **Financial account** (`FinancialAccount`) | A real place money is held: Bank, Cash, Brokerage or Other. | One currency, fixed at creation. Ownership (`Company`, `Personal` or `Unspecified`) is stated by the owner, never inferred from the name. No credentials are stored. It closes only when its balance is exactly zero, and a closed account accepts no entries, reversals included. Names are unique. |
| **Fund** (`Fund`) | A purpose money is earmarked for, not a place. | Seeded: **Unallocated** (system; cannot be renamed), Operating Capital, Business Continuity, Wealth Fund, Owner's Distribution. More can be added. One account can hold money for several funds; one fund can span several accounts and currencies. |

Seeded funds are configuration. They hold no money until transactions are posted.

## Ledger (Phase 4: implemented)

Code: `MelamuHoldings.Domain/Finance/LedgerTransaction.cs`. Tests: `tests/MelamuHoldings.UnitTests/Finance/LedgerTransactionTests.cs` and `tests/MelamuHoldings.IntegrationTests/FinanceEndpointTests.cs`.

Each **transaction** is dated, described and immutable. Its **entries** post signed amounts to (account, fund) pairs, in the account's currency. Transactions are created only through one factory per type:

| Type (API endpoint) | Entries | Rules |
|---|---|---|
| Opening balance (`opening-balance`) | `+X` to (account, fund) | Not zero; may be negative (an overdrawn account). One per (account, fund), unless the earlier one was reversed. |
| Income (`income`) | `+X` to (account, fund) | `X > 0`. Records money that **has arrived**: a requested payout is not income. |
| Expense (`expense`) | `−X` from (account, fund) | `X > 0` |
| Transfer (`transfer`) | `−X` (A, fund₁), `+X` (B, fund₂) | Two different accounts in the **same** currency. Sums to zero. |
| Earmark (`reallocation`, type `FundReallocation`) | `−X` (A, fund₁), `+X` (A, fund₂) | Same account, two different funds. Sums to zero. **No money leaves the account.** |
| Currency exchange (`exchange`) | `−S` (A, fund₁) in A's currency, `+R` (B, fund₂) in B's currency | Accounts in **different** currencies; both amounts `> 0`. Rate stored as `R ÷ S`, rounded to 8 places. |
| Reversal (`{id}/reverse`) | The exact negation of every entry of the original | Never dated before the original. A transaction can be reversed only once (unique index), and a reversal cannot itself be reversed. The original stays in the ledger, marked "reversed by". |

**Rules for every type:**
- The date is set, and at most one day ahead of today (allowing for time zones); future-dated entries are rejected.
- A description is required (up to 200 characters). Reference and category are optional.
- Accounts must be active.

**There is no way to edit or delete a transaction, or to set a balance.** A mistake is corrected by reversing it and recording the right transaction.

### Balances

All balances are summed from entries. None is stored.

| Figure | Formula |
|---|---|
| Account balance | Σ entries for the account (one currency) |
| Account by fund | Σ entries for each (account, fund) |
| Fund balance | Σ entries for the fund, **per currency**. Different currencies are never added together. |
| Cash totals | Σ entries in Bank and Cash accounts, per currency. Brokerage balances are recorded but are not counted as spendable cash. |
| As of a date | The same sums restricted to transactions dated on or before that date |

Money that is earmarked but has not physically moved shows as a fund balance inside the original account (for example "Operating ZAR · Business Continuity"). Physically separated money shows in its own account. Both are visible side by side on the Accounts page.

Balances may go negative (for example, an expense from a fund that holds less). This is shown as recorded rather than blocked; warnings are Phase 6.

### Safe retries (idempotency)

Every money-moving request carries an `Idempotency-Key` header (1–64 characters from `A–Z a–z 0–9 - _`). The frontend generates one per intended transaction.

- **Same key, same body:** the original transaction is returned (HTTP 200, `Idempotent-Replayed: true`). Nothing new is posted.
- **Same key, different body:** HTTP 409.
- **Concurrent retries with one key:** the database's unique index lets exactly one through, and the others receive the winner. A test sends 8 requests at once.
- **No key:** the request still posts exactly once, but cannot be retried safely.

### Audit trail

Every account, fund and transaction change writes an append-only `AuditEvent` in the same database transaction:
- who made the change;
- when;
- the action;
- a summary;
- a JSON snapshot, with before/after values for edits.

There is no API to change or delete audit events.

### Errors

| Situation | HTTP |
|---|---|
| Malformed request: missing field, unsupported currency code, bad page size or idempotency key | 400 |
| Business rule broken: sub-cent amount, zero or negative amount, cross-currency transfer, closed account, unknown account or fund, future date, second opening balance | 422 |
| Unknown transaction or account in the URL | 404 |
| Duplicate name, reused idempotency key with a different body, already reversed | 409 |

All errors are returned as ProblemDetails.

## Storage

- Finance tables are in the SQL schema `finance`: `Funds`, `FinancialAccounts`, `LedgerTransactions`, `LedgerEntries`, `AuditEvents`.
- Trading tables stay in `dbo`.
- Amounts: `decimal(19,4)`. Exchange rates: `decimal(18,8)`. Currency codes: `char(3)`. Enums are stored as their names.
- Every foreign key from finance tables uses `Restrict`. Deleting a login can never cascade into financial history.
- Unique indexes: idempotency key, reversed transaction, fund name, account name.
