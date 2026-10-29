# Financial Domain

The rules for money in Melamu Holdings OS. This file records what is **implemented**: accounts, funds and the ledger (Phase 4); payouts, allocation policies and allocations (Phase 5). Later-phase designs (obligations, continuity, investments) are in [`../PLAN.md`](../PLAN.md) §3.6 and move here as they are built.

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
| Rounding is explicit | Calculated amounts go through `Money.Round(amount, currency, mode)`, so the rounding mode is visible where it happens. The allocation calculator works in whole minor units and distributes the leftover units by largest remainder (see "Allocation"). |
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
| Income (`income`) | `+X` to (account, fund); optionally a **fee** entry `−F` to the same (account, fund) | `X > 0`. Records money that **has arrived**: a requested payout is not income. `X` is the gross amount; a fee deducted before the money arrived is `0 < F < X`, so the account nets to what was credited. |
| Expense (`expense`) | `−X` from (account, fund) | `X > 0` |
| Transfer (`transfer`) | `−X` (A, fund₁), `+X` (B, fund₂) | Two different accounts in the **same** currency. Sums to zero. |
| Earmark (`reallocation`, type `FundReallocation`) | `−X` (A, fund₁), `+X` (A, fund₂) | Same account, two different funds. Sums to zero. **No money leaves the account.** |
| Currency exchange (`exchange`) | `−S` (A, fund₁) in A's currency, `+R` (B, fund₂) in B's currency; optionally a **fee** entry `−F` on A or B | Accounts in **different** currencies; both amounts `> 0`. A fee charged on top is in the currency of the account it was charged to (A if sending, B if receiving). Rates: see "Exchange rates". |
| Reversal (`{id}/reverse`) | The exact negation of every entry of the original | Never dated before the original. A transaction can be reversed only once (unique index), and a reversal cannot itself be reversed. The original stays in the ledger, marked "reversed by". |

**Rules for every type:**
- The date is set, and at most one day ahead of today (allowing for time zones); future-dated entries are rejected.
- A description is required (up to 200 characters). Reference and category are optional.
- Accounts must be active.

**There is no way to edit or delete a transaction, or to set a balance.** A mistake is corrected by reversing it and recording the right transaction. The one exception to reversing from the ledger is a payout's receipt, which is undone from the payout (see "Payouts").

### Fees

A fee is never netted away. It is a ledger entry with role `Fee` inside the transaction that incurred it, so reports can sum fees separately and a reversal undoes the fee with the rest. Two places record fees today: a fee deducted from a payout before it arrived (on the income), and a conversion fee charged on top of an exchange (on the exchange). A fee charged separately on a transfer is recorded as its own expense.

### Exchange rates

Every rate stored says where it came from, by the field it is in:

| Field | Source | Used for |
|---|---|---|
| `ExchangeRate` on an exchange | **Effective**: calculated from the two recorded amounts, received ÷ sent, 8 places. The amounts come from the transaction evidence, so the rate is evidence too. | Display. Balances use the amounts, never the rate. |
| `QuotedRate` on an exchange (optional) | **Quoted**: the rate the provider printed, as entered by the operator. | Reference only, beside the effective rate, so a spread between them is visible. Never used in a calculation. |
| `EffectiveRate` on a payout credited in another currency | **Effective**: (credited + fee) ÷ approved amount. | Display. |

The system never fetches market rates, never applies today's rate to a past transaction, and never adds amounts in different currencies. Valuing a foreign balance in a reporting currency is a separate, later feature (Phase 9) and will be labelled as a valuation, not a transaction.

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

Balances may go negative (for example, an expense from a fund that holds less). This is shown as recorded rather than blocked; the Executive overview warns about an account or a fund below zero (Phase 6).

### Safe retries (idempotency)

Every money-moving request carries an `Idempotency-Key` header (1–64 characters from `A–Z a–z 0–9 - _`). The frontend generates one per intended transaction.

- **Same key, same body:** the original transaction is returned (HTTP 200, `Idempotent-Replayed: true`). Nothing new is posted.
- **Same key, different body:** HTTP 409.
- **Concurrent retries with one key:** the database's unique index lets exactly one through, and the others receive the winner. A test sends 8 requests at once.
- **No key:** the request still posts exactly once, but cannot be retried safely.
- **A retry that arrives while the first attempt is committing** may fail its own state check ("already received", "already allocated"). Before returning that error, the request looks for its key again and, if the first attempt has now committed, replays it.

### Audit trail

Every account, fund and transaction change writes an append-only `AuditEvent` in the same database transaction:
- who made the change;
- when;
- the action;
- a summary;
- a JSON snapshot, with before/after values for edits.

There is no API to change or delete audit events.

## Payouts (Phase 5: implemented)

Code: `MelamuHoldings.Domain/Finance/Payout.cs`, `Application/Finance/PayoutService.cs`. Tests: `PayoutAllocationTests.cs` (unit), `PayoutAllocationEndpointTests.cs` (integration).

```
Requested ──approve──▶ Approved ──receive──▶ Received
    │                      │                    │
    └──── reject ──────────┴──▶ Rejected        └─ undo receipt ─▶ Approved
```

| Step | What is recorded | Rules |
|---|---|---|
| Request | Payer, currency the payout is denominated in, amount, date, optional reference and notes | Amount `> 0`. **Nothing reaches the ledger.** |
| Approve | Approved amount (defaults to requested) and date | Only from Requested. Never more than requested; may be less. |
| Reject | Reason and date | Only from Requested or Approved. |
| Receive | Account, **amount credited** (in the account's currency), optional fee deducted on the way, date, reference | Only from Approved. Posts one Income transaction to (account, **Unallocated**): gross = credited + fee, with the fee as a fee entry. In the payout's own currency, gross may not exceed the approved amount. In another currency, the effective rate is recorded (see "Exchange rates"). |
| Undo receipt | Reason and date | Only while no allocation uses the money (cancel it first). Reverses the income transaction and returns the payout to Approved. Reversing the receipt directly from the ledger is refused with a pointer here, so the payout can never claim money the ledger no longer holds. |

Dates run in order (approval not before request, receipt not before approval) and none may be in the future. Requests and receipts take an `Idempotency-Key`; approval and rejection are idempotent by state (repeating the same approval returns the payout unchanged). Two different receipts of one payout cannot both succeed: the payout row carries a `rowversion`, and the loser receives 409.

**Payouts and cash.** Requested and approved payouts are shown as *expected* and labelled "not cash" wherever they appear. Only the credited amount of a received payout is cash, and only that amount can be allocated.

## Allocation (Phase 5: implemented)

Code: `MelamuHoldings.Domain/Finance/AllocationPolicy.cs`, `Allocation.cs`, `Application/Finance/AllocationService.cs`.

### Policies

A policy is a numbered **version** of (fund, percentage) lines.
- Percentages are `> 0`, at most 100, with at most 4 decimal places, and add up to **exactly 100**.
- Each fund appears once; the Unallocated fund cannot be in a policy.
- **A version is never edited.** Changing the split creates a new version; exactly one version is active (a filtered unique index enforces it).
- Version 1, **40 / 30 / 20 / 10** across Operating Capital, Business Continuity, Wealth Fund and Owner's Distribution, is seeded as configuration and is active out of the box. Replace it by creating and activating a new version.

### Calculating a split

For an amount of `U` minor units (cents) and percentages `p₁ … pₙ` in policy order:

1. `rawᵢ = U × pᵢ ÷ 100`
2. `shareᵢ = floor(rawᵢ)`; the spare units are `U − Σ shareᵢ` (always fewer than `n`).
3. Give one spare unit at a time to the lines with the **largest remainder** `rawᵢ − shareᵢ`; equal remainders go to the earlier line.
4. Check `Σ shareᵢ = U` exactly; a mismatch is an error, never a silent fix.

| Amount | 40 % | 30 % | 20 % | 10 % |
|---|---|---|---|---|
| 1,350.00 | 540.00 | 405.00 | 270.00 | 135.00 |
| 1,000.01 | **400.01** | 300.00 | 200.00 | 100.00 |
| 0.03 | 0.01 | **0.01** | **0.01** | 0.00 |
| 999,999.99 | **399,999.99** | 300,000.00 | 200,000.00 | 100,000.00 |

(The last row: remainders .6, .7, .8, .9 of a cent, so the three spare cents go to the 30 %, 20 % and 10 % lines and the 40 % line keeps its floor.)

### Lifecycle

```
preview (stores nothing) ──confirm──▶ Confirmed ──settle some──▶ Partially settled ──settle all──▶ Reconciled
                                           │
                                           └── cancel (only while nothing is settled) ──▶ Cancelled
```

- **Preview** splits the payout's credited amount by the active (or a chosen) policy. Nothing is stored.
- **Confirm** stores the allocation with an immutable **snapshot** of each line's fund name, percentage and amount, the policy version, and the account holding the money. Later policy changes or fund renames never alter it. Only a received payout can be allocated, and **at most once** (a filtered unique index on the payout, ignoring cancelled allocations). Confirmation takes an `Idempotency-Key`: a retry returns the original allocation; a second allocation with a new key is 409.
- **Settle** a line by recording the real movement, out of the **Unallocated fund of the receiving account**:

  | Method | Ledger transaction | Use when |
  |---|---|---|
  | Earmark | Fund reallocation inside the receiving account | The money stays where it is, assigned to the fund |
  | Transfer | Transfer to another account, same currency, into the line's fund | The money physically moves |
  | Exchange | Currency exchange into an account in another currency, into the line's fund, with optional fee and quoted rate | The money is converted on the way |

  A settlement can be partial. It is refused if it exceeds the line's outstanding amount, or if the receiving account's Unallocated balance cannot cover it (plus a fee charged to that account). The ledger transaction, the settlement link and the audit event are saved in one database transaction, under the request's `Idempotency-Key`; concurrent settlements of one allocation are serialised by its `rowversion`.
- **Status is derived, never set**: settled = Σ settlements whose ledger transaction has not been reversed. Reversing a settlement transaction from the ledger therefore reopens its line (Reconciled → Partially settled, or back to Confirmed).
- **Cancel** needs a reason and is allowed only while nothing is settled (reverse settlements first). The payout can then be allocated again.

The settlement amount is in the allocation's currency (the payout's credited currency). For an exchange, the line is settled by the amount **sent**; what arrives in the other currency is recorded in that account's fund, and a conversion fee does not count towards the line.

### Errors

| Situation | HTTP |
|---|---|
| Malformed request: missing field, unsupported currency code, bad page size or idempotency key | 400 |
| Business rule broken: sub-cent amount, zero or negative amount, cross-currency transfer, closed account, unknown account or fund, future date, second opening balance, payout step out of order, policy not adding up to 100 %, over-settling a line, not enough unallocated money, reversing a payout receipt from the ledger | 422 |
| Unknown transaction or account in the URL | 404 |
| Duplicate name, reused idempotency key with a different body, already reversed, payout already allocated, a concurrent change to the same payout or allocation | 409 |

All errors are returned as ProblemDetails.

## Executive overview: period figures (Phase 6: implemented)

Code: `Application/Finance/OverviewService.cs`, `GET /api/overview?from&to` (this month to date by default; at most 10 years; `from` after `to` is 422). Tests: `OverviewEndpointTests.cs`.

**Scope.** Every figure is summed from ledger entries in **Bank and Cash** accounts, the same scope as the cash totals, per currency. Brokerage and Other accounts are outside it.

**Lines.** Each entry falls in exactly one line, decided by its transaction's type:

| Line | Entries | Ledger list it matches |
|---|---|---|
| Cash at the start | Every entry dated before `from` | (balances as of the day before) |
| Income received | Income entries in the period; shown with their gross (principal) and the fees deducted (fee entries) | `type=Income` |
| Expenses | Expense entries | `type=Expense` |
| Currency exchanges | Exchange entries, conversion fees included | `type=CurrencyExchange` |
| Transfers | Transfer entries; between two bank or cash accounts they cancel out, so only money moved to or from Brokerage and Other accounts remains | `type=Transfer` |
| Reversals (corrections) | Reversal entries, on the reversal's own date | `type=Reversal` |
| **Net cash movement** | The sum of the five lines above: what came in less what went out. **Not profit.** | |
| Opening balances recorded | Opening-balance entries in the period: money that existed before it was recorded, so not a movement | `type=OpeningBalance` |
| Cash at the end | Cash at the start + net cash movement + opening balances recorded, which equals the cash totals as of `to` | |

Earmarks move money between funds inside one account, so they net to zero here and have no line. Every ledger list is filtered by the same dates and `cash=true` (transactions with an entry in a Bank or Cash account), so a figure and its list always contain the same transactions.

**By month.** The same income, expenses and net cash movement for each calendar month in the period (clipped at its ends), per currency, with a link per figure; only the most recent 24 months are returned.

**Payouts received.** Payouts with status Received and a receipt date in the period, summed by credited currency. A receipt that was undone returns the payout to Approved, so it no longer counts.

**Warnings.** "Needs attention" on the same page is derived from records as of today, whatever the period:
- **Payouts and allocations:** received payouts not allocated, allocations not fully settled, and payouts on the way (not cash).
- **Money:** money left in Unallocated, accounts below zero, and funds below zero in any currency.
- **Setup:** no accounts, and ownership not stated.
- **Trading:** critical trading-behaviour alerts.

## Storage

- Finance tables are in the SQL schema `finance`: `Funds`, `FinancialAccounts`, `LedgerTransactions`, `LedgerEntries`, `AuditEvents`, `Payouts`, `AllocationPolicies`, `AllocationPolicyLines`, `Allocations`, `AllocationLines`, `AllocationSettlements`.
- Trading tables stay in `dbo`.
- Amounts: `decimal(19,4)`. Exchange rates: `decimal(18,8)`. Percentages: `decimal(9,6)`. Currency codes: `char(3)`. Enums are stored as their names.
- `Payouts` and `Allocations` carry a `rowversion` for optimistic concurrency.
- Every foreign key from finance tables uses `Restrict`. Deleting a login can never cascade into financial history.
- Unique indexes: idempotency keys (ledger transactions, payouts, allocations), reversed transaction, fund name, account name, policy version, one active policy, one live allocation per payout, one settlement per ledger transaction, one payout per income transaction.
