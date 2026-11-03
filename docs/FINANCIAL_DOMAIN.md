# Financial Domain

The rules for money in Melamu Holdings OS. This file records what is **implemented**: accounts, funds and the ledger (Phase 4); payouts, allocation policies and allocations (Phase 5); the Executive Overview (Phase 6); and obligations, continuity, investments, budgets and planned purchases (Phase 7, reviewed in [`PHASE_7_REVIEW.md`](PHASE_7_REVIEW.md)). Later-phase designs are in [`../PLAN.md`](../PLAN.md) §3.6 and move here as they are built.

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
| What fits | `Money.Of` also rejects an amount larger than the `decimal(19,4)` columns can hold (more than 999,999,999,999,999.99), with a 422 rather than a database error. |
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
- **Money:** money left in Unallocated, accounts below zero, funds below zero in any currency, and funds below their minimum (Phase 7.1).
- **Obligations (Phase 7.1):** overdue ones, those due within 14 days, and payments whose expense was reversed, on a paid one-off too (not on a cancelled obligation, which cannot be changed). The warning links to the page with the closed ones showing.
- **Continuity (Phase 7.2):** below its target, or not measured because no monthly essential expense is entered. The cash strip also shows the coverage in months.
- **Investments (Phase 7.3):** an active brokerage account holding a recorded balance with no valuation, or with transactions dated after its latest valuation.
- **Budgets and purchases (Phase 7.4):** a budget line exceeded this month; an open purchase that would breach its fund's minimum or that the fund cannot pay for (a warning once committed, a note while proposed); and a purchase whose expense was reversed, which links to the page with the closed ones showing.
- **Setup:** no accounts, and ownership not stated.
- **Trading:** critical trading-behaviour alerts.

## Fund minimums and targets (Phase 7.1: implemented)

Code: `Domain/Finance/Fund.cs` (`SetThresholds`), set through `PUT /api/funds/{id}`. Tests: `ObligationTests.cs` (unit), `ObligationEndpointTests.cs` (integration).

| Rule | Behaviour |
|---|---|
| One currency | A fund's minimum and target are in one stated currency (`ThresholdCurrency`). A fund can hold several currencies; only its balance in that currency is compared, and nothing is converted. |
| Valid levels | Minimum ≥ 0, target > 0, target ≥ minimum, whole minor units. Giving neither clears both and the currency. |
| Not on Unallocated | The system fund has none: money there is waiting to be allocated. |
| Below minimum | Balance in the currency < minimum. Shown on the Funds page and as a warning. A fund below zero gets the below-zero warning instead, not both. |
| Progress | Balance ÷ target, shown between 0 % and 100 %. |
| Audited | Thresholds are part of the fund's audit snapshot (before and after). |

## Obligations (Phase 7.1: implemented)

Code: `Domain/Finance/Obligation.cs`, `Application/Finance/ObligationService.cs`, `/api/obligations`.

| Rule | Behaviour |
|---|---|
| What it is | A name, an amount in one currency, an optional category and notes, and how often it is due: once, monthly, quarterly or yearly. **It never moves money and never creates a ledger entry.** |
| Due dates | The n-th due date is the first one plus n periods (months, quarters or years), so a monthly obligation first due on the 31st is due on the 28th in February and the 31st again in March. The next due date is the one after the last recorded payment. A first due date after 2100 is refused. |
| Paying | A payment is a **link** to an Expense transaction already in the ledger: same currency, not reversed, and not linked to another payment (unique index) or to a planned purchase. The amount actually paid is recorded, even if it differs from the obligation's amount. A one-off is Completed by its payment. |
| Undoing | Only the latest payment can be undone. The link is removed and the due date goes back; the expense stays in the ledger. |
| A later reversal | If an expense is reversed after it was linked, the payment is marked "expense reversed" and a warning asks for it to be reviewed. Nothing is changed automatically. |
| Changing | Name, amount, category and notes can change. The currency never changes, and the schedule (how often and the first date) is fixed once a payment is recorded. Cancelled and completed obligations cannot be changed. Two active obligations cannot share a name. |
| Standing | Against today: **Overdue** (due date passed), **due soon** (within 14 days), **upcoming**, or **completed** or **cancelled**. |
| Coming due | The Obligations page totals active obligations due within 30 days per currency, overdue ones included, and never across currencies. |
| Audited | Created, updated, paid, payment undone and cancelled are each an audit event. |

## Continuity (Phase 7.2: implemented)

Code: `Domain/Finance/ContinuitySettings.cs` (the target, coverage, shortfall and progress), `Application/Finance/ContinuityService.cs` (what is counted), `/api/continuity`. Decisions: PLAN.md D6–D8.
Tests: `ContinuitySettingsTests.cs` and `Phase7FormulaTests.cs` (unit), `ContinuityEndpointTests.cs` (integration).

| Rule | Behaviour |
|---|---|
| Settings | One record: the continuity fund (Business Continuity by default), the months to cover (default 24, 1 to 120), the currency (ZAR, D7), the monthly essential expense (entered by the owner, D8; none until it is), and the restoration fund. |
| Target | Months × monthly essential expense. Without the expense there is no target, coverage or shortfall: the page says "Not measured" rather than showing a zero. |
| Counted | The continuity fund's balance in ZAR in **bank and cash** accounts, from the ledger. |
| Shown, not counted | The fund's money in another currency (never converted), in brokerage or other accounts, and what confirmed allocations still owe it but have not earmarked or transferred. Each is listed with its reason. |
| Coverage | Counted ÷ monthly expense, rounded **down** to one decimal, so it never overstates. Shortfall: target − counted, never below zero. |
| Earmarked or separated | A counted amount is **separated** when its account holds no other fund's money, and **earmarked** when the account is shared with other funds. Both count; the page shows the split. |
| Restoration (D6) | A separate fund, Trading Restoration, added by the migration. It is measured against that fund's own target (set on the Funds page, in the target's currency), in bank and cash accounts, with the same not-counted rules. Continuity and restoration must be different funds, and neither can be Unallocated, so no money counts twice. |
| Nothing moves | The settings and the report never create a ledger entry. Money reaches the funds through allocations, earmarks and transfers. |
| Audited | Each change of settings is a `continuity.updated` event with before and after. |

## Unrestricted cash (Phase 7.6: implemented)

Code: `Domain/Finance/UnrestrictedCash.cs`, `Application/Finance/UnrestrictedCashService.cs`, `/api/unrestricted-cash`. Decision: PLAN.md D9.
Tests: `Phase7FormulaTests.cs` (unit), `UnrestrictedCashEndpointTests.cs` (integration).

Cash that is free to use, per currency:

`Unrestricted = Held − Operating minimum − Owed to other funds − Payouts not allocated − Committed purchases`

| Line | What it is |
|---|---|
| **Held** | Unallocated and Operating Capital, in that currency, in **company bank and cash** accounts. |
| **Operating minimum** | Operating Capital's minimum, when it is in that currency. It comes off the total, so Unallocated money covers a fund below its minimum first. |
| **Owed to other funds** | What confirmed allocations still owe funds other than Operating Capital, when the payout's money is in a counted account. A line owed to Operating Capital does not come off, because that money stays unrestricted once settled. Settlements whose transaction was reversed do not count as settled. |
| **Payouts not allocated** | The credited amount of received payouts with no live allocation, in full, when received into a counted account. A cancelled allocation puts the payout back here. |
| **Committed purchases** | Committed purchases paid from Operating Capital. Proposed ones reserve nothing, and a purchase from another fund has no claim on this money. |

| Rule | Behaviour |
|---|---|
| Below zero | The figure can go below zero, when more is spoken for than is held, and it is shown that way. |
| Obligations | Active obligations due within 30 days, overdue ones included, are shown beside the figure with what would be left if all were paid. They are **not deducted**: an expense lowers the balance as soon as it is recorded, and the obligation moves on only when that expense is linked, so one bill could come off twice. |
| Shown, not counted | The two funds' money in brokerage and other accounts, personal accounts, and accounts with ownership not stated, each with the reason. |
| Which fund | Operating Capital is found by its seeded id, so renaming it changes nothing. |
| Currencies | Each stands alone. Nothing is converted. |
| Nothing moves | The figure is worked out when asked for. It is never stored, and it never blocks or moves anything. |

**A known limit:** payouts and allocations are counted in the currency they were credited in. If a payout is exchanged before it is allocated, its deduction stays in the old currency, so that figure reads too low and the new currency's too high, until it is allocated.

## Investments (Phase 7.3: implemented)

Code: `Domain/Finance/InvestmentValuation.cs` (the unrealised gain or loss, and which group an entry falls in), `Application/Finance/InvestmentService.cs`, `/api/investments`.
Tests: `InvestmentValuationTests.cs` and `Phase7FormulaTests.cs` (unit), `InvestmentEndpointTests.cs` (integration).

An investment account is a financial account of type **Brokerage**. Money reaches it through the ledger's own transactions; the owner records what the broker says it is worth.

| Rule | Behaviour |
|---|---|
| Valuation | The observed value of one brokerage account on a date, in the account's currency, ≥ 0, in whole minor units, with an optional note. Never in the future (one day of slack, as in the ledger), not on a closed account, and one per account per day (unique index; a second is 409). **It is not a ledger movement, not income and not cash**: it never changes a balance. |
| Correcting | A valuation is an observation, not a ledger record, so a wrong one is deleted and recorded again. Recording and deleting are both audited (`valuation.recorded`, `valuation.deleted`). |
| Net contributions | From the ledger: the principal of opening balances, transfers and currency exchanges into the account, less those out. A reversal counts as the type it reverses, so a reversed contribution cancels. |
| Recorded income and costs | Income recorded in the account (for example dividends), and expenses and fees (for example platform fees). |
| Recorded balance | Net contributions + recorded income + recorded costs: the ledger balance of the account. |
| As of the valuation | Every ledger figure is taken **up to the latest valuation's date**. Transactions dated after it are counted apart ("N transactions since this valuation") and are in neither the gain nor the loss. |
| Unrealised gain or loss | Latest valuation − recorded balance on its date: the change in market value the ledger does not hold. Always labelled **unrealised**; it is not cash until sold and withdrawn. |
| Gain on contributions | Latest valuation − net contributions: the unrealised gain plus recorded income and costs. |
| Totals | Per currency, over accounts that have a valuation; never across currencies and never converted. Valuations in a total may be of different dates. |
| Not cash | Brokerage balances were never in the cash totals (Phase 4), and valuations do not change that. |

## Budgets (Phase 7.4: implemented)

Code: `Domain/Finance/BudgetLine.cs` (remaining, used % and exceeded), `Application/Finance/BudgetService.cs`, `/api/budgets`. Tests: `BudgetAndPurchaseTests.cs` and `Phase7FormulaTests.cs` (unit), `BudgetAndPurchaseEndpointTests.cs` (integration).

| Rule | Behaviour |
|---|---|
| Budget line | An amount for one category in one calendar month from 2000 to 2100, in one currency, in whole minor units. One line per month, category and currency (unique index; a second is 409). The month and currency of a line never change. |
| Actual | The sum of **Expense** transactions dated in that month, in that currency, with that category, by transaction date. |
| Matching | Categories are free text on the ledger, so they match whatever their case or surrounding spaces. |
| Reversed expenses | Count for nothing, in the month of the original, whenever the reversal was dated. |
| Not counted | Fees recorded inside an income or an exchange are not expenses and are in no budget. Actual covers expenses in every account type, including brokerage. |
| Outside the budget | Expenses with no line for their category and currency are listed apart, with no category shown as such, so the month's spending is all accounted for. A rand budget does not cover dollars, and nothing is converted. |
| Figures | Remaining = budget − actual, below zero when exceeded. Used % is actual ÷ budget, rounded down to one decimal and not capped. Totals are per currency. |
| Never blocks | A budget never posts to the ledger and never stops an expense. A line can be changed or deleted, and each change is audited (`budget.created`, `budget.updated`, `budget.deleted`). |

## Planned purchases (Phase 7.4: implemented)

Code: `Domain/Finance/PlannedPurchase.cs`, `Domain/Finance/Affordability.cs`, `Application/Finance/PlannedPurchaseService.cs`, `/api/planned-purchases`. Tests: `BudgetAndPurchaseTests.cs` and `Phase7FormulaTests.cs` (unit), `BudgetAndPurchaseEndpointTests.cs` (integration).

| Rule | Behaviour |
|---|---|
| Stages | **Proposed → Committed → Purchased**, or **Cancelled** from either open stage. Committed can go back to Proposed. Only a proposal can be edited. PLAN.md §3.6 sketched a further "Paid" stage; the linked expense already records payment, so there is none. |
| What it is | A name, an amount in one currency, the fund it is paid from (not Unallocated), and an optional category, target date and notes. **It never moves money and never creates a ledger entry.** Two open purchases cannot share a name. |
| Purchased | A **link** to an Expense already in the ledger: same currency, not reversed, not linked to another purchase (unique index) or to an obligation's payment. What it actually cost is recorded, even if it differs from the plan. The expense does not have to be from the same fund. |
| Undoing | Removes the link and returns the purchase to Committed. The expense stays in the ledger. |
| A later reversal | The purchase is marked "expense reversed" and a warning asks for it to be reviewed. Nothing is changed automatically. |
| Audited | Proposed, updated, committed, commitment withdrawn, cancelled, bought and undone are each an audit event (`purchase.*`). |

**Can it be afforded?** Worked out for each open purchase from recorded balances, today:

`After = Available − Committed elsewhere − Purchase`

- **Available:** the purchase's fund, in the purchase's currency, in **bank and cash** accounts. Brokerage money is not spendable.
- **Committed elsewhere:** that fund's other Committed purchases in that currency. Proposals reserve nothing, and a commitment never reserves against itself. Once a purchase is bought, its reservation ends and the expense has already reduced the balance.

| Answer | When |
|---|---|
| **Affordable** | The fund has a minimum in this currency and After ≥ minimum (exactly at the minimum is allowed). |
| **Breaches the minimum** | After < minimum. The shortfall is minimum − After. |
| **No minimum to compare** | No minimum in this currency, and After ≥ 0. A minimum in another currency is shown and never converted. |
| **Not enough money** | No minimum in this currency, and After < 0. |

The check **warns and never blocks**: a purchase can be committed even when it would breach the minimum, because the decision is the owner's. Active obligations due within 30 days in the same currency, overdue ones included, are shown beside the answer and **not deducted**, because obligations are not tied to a fund.

## Storage

- Finance tables are in the SQL schema `finance`: `Funds`, `FinancialAccounts`, `LedgerTransactions`, `LedgerEntries`, `AuditEvents`, `Payouts`, `AllocationPolicies`, `AllocationPolicyLines`, `Allocations`, `AllocationLines`, `AllocationSettlements`, `Obligations`, `ObligationPayments`, `ContinuitySettings`, `InvestmentValuations`, `BudgetLines`, `PlannedPurchases`.
- Trading tables stay in `dbo`.
- Amounts: `decimal(19,4)`. Exchange rates: `decimal(18,8)`. Percentages: `decimal(9,6)`. Currency codes: `char(3)`. Enums are stored as their names.
- `Payouts` and `Allocations` carry a `rowversion` for optimistic concurrency.
- Every foreign key from finance tables uses `Restrict`. Deleting a login can never cascade into financial history.
- Unique indexes: idempotency keys (ledger transactions, payouts, allocations), reversed transaction, fund name, account name, policy version, one active policy, one live allocation per payout, one settlement per ledger transaction, one payout per income transaction.
