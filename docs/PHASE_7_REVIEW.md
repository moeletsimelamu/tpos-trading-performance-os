# Phase 7: verification review (P7.5)

Phase 7 (continuity and wealth), checked requirement by requirement against `PLAN.md`: the Phase 7 row of §7, the stages in §7b, the operating model in §1a, the entities in §3.6 and decisions D6–D8. Each row says whether it is built, where, and how it was verified. Gaps are listed at the end, with what to do about them.

Status key:
- **Built:** done and verified.
- **Partly:** a working version exists, with the stated gap.
- **Not built:** deliberately deferred, or waiting for a decision.

The rules as built are in [`FINANCIAL_DOMAIN.md`](FINANCIAL_DOMAIN.md). Test results and browser checks are in [`TESTING.md`](TESTING.md).

## Regression (2026-11-03)

| Check | Result |
|---|---|
| `dotnet build -c Release -warnaserror` | 0 warnings, 0 errors |
| .NET unit / integration tests | 238 / 120 passed, the integration suite on three consecutive runs |
| Angular tests (`ng test`) | 191 passed (28 files), on two consecutive runs |
| Production build (`ng build`) | Succeeded, 311.01 kB initial, no budget warnings |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages in any project |
| `npm audit --omit=dev` | 0 vulnerabilities |
| `dotnet ef migrations has-pending-model-changes` | No changes |
| `dotnet ef migrations script --idempotent` | All 14 migrations, each guarded by a migrations-history check |
| Migrations applied | 14 of 14 on both `MelamuHoldings_Scratch` and dev `MelamuHoldings` (read from their history tables). P7.5 adds none. |
| CI | Passed on `develop` at `7c15487`, the first push of Phase 7 (backend and frontend). That run also includes P7.6. |

## Exit criterion

Phase 7's exit criterion is "formulas documented in `FINANCIAL_DOMAIN.md` and unit-tested". **Before P7.5 this was only partly true.** Every formula was documented and tested, but the affordability check, coverage, shortfall, budget figures and unrealised gain were written inside application services, so only integration tests reached them, and §2 says financial rules live in Domain.

P7.5 moved them to Domain with no change in behaviour, and every integration test passes unchanged:

| Formula | Domain code | Unit tests (`Phase7FormulaTests`) |
|---|---|---|
| After = available − committed elsewhere − purchase; the four answers; the shortfall | `Affordability.Evaluate` | Exactly at the minimum, a cent below, commitments reserving money, a shortfall measured to the minimum, exactly zero left, a fund already below zero |
| Target = months × monthly essential expense | `ContinuitySettings.Target` | Nothing to measure without the expense |
| Coverage, rounded down, never below zero | `ContinuitySettings.CoverageMonths` | 1.25 reads 1.2; a fund below zero covers 0 months |
| Shortfall, never below zero | `ContinuitySettings.Shortfall` | Target met or passed; a fund below zero |
| Restoration progress, 0 to 100 | `ContinuitySettings.Progress` | Rounded down and capped at both ends |
| Remaining, used %, exceeded | `BudgetLine.Remaining`, `UsedPercent`, `IsExceeded` | Exactly on budget is not over; a cent over is, though it still reads 100% |
| Unrealised; gain on contributions | `InvestmentValuation.Unrealised`, `GainOnContributions` | A gain and a loss, by hand |
| Which group a brokerage entry falls in | `InvestmentValuation.FlowOf` | Each type and role |
| Due dates, fund levels | `Obligation.DueAfter`, `Fund.SetThresholds` | Already unit-tested (P7.1) |

Two calculations stay outside Domain. The earmarked or separated split (`ContinuityService.Measure`) reads the account positions the read model returns, and is covered by integration tests. A fund's standing against its minimum and target (`fundStanding`) is computed in Angular for display, and is covered by Angular specs. Neither produces a balance.

## Requirement trace

### Scope of Phase 7 (§7)

| Requirement | Status | Where | Verified by |
|---|---|---|---|
| Obligations | Built | `/finance/obligations`, `Obligation` | P7.1 tests; P7.1 and P7.5 browser checks |
| Continuity: 24-month essential cover and a separate restoration target | Built | `/finance/continuity`, `ContinuitySettings`; months are configurable, default 24 | P7.2 tests and browser check |
| Coverage in months | Built | Continuity page and the overview's cash strip | Unit, integration and browser |
| Earmarked versus physically separated | Built | Continuity page | Integration; direct ledger query in P7.5 |
| Investment accounts, contributions, valuations, unrealised gain or loss | Built | `/finance/investments`, `InvestmentValuation` | P7.3 tests and browser check |
| Budgets with budget versus actual | Built | `/finance/budgets`, `BudgetLine` | P7.4 tests; P7.4 and P7.5 browser checks |
| Planned purchases | Built | `/finance/purchases`, `PlannedPurchase` | P7.4 tests; P7.4 and P7.5 browser checks |

### Continuity rules (§1a, D6–D8)

| Requirement | Status | Where | Verified by |
|---|---|---|---|
| Two separate, configurable targets | Built | Months × expense; the restoration fund's own target | P7.2 tests |
| Never count the same money twice | Built | Continuity and restoration must be different funds, and neither can be Unallocated | `ContinuitySettings` unit test |
| A planned contribution, an outstanding allocation or a trading-account balance is not continuity cash | Built | Only bank and cash money in the fund is counted; outstanding allocations, brokerage money and other currencies are listed with the reason. Trading accounts have no ledger balance at all. | Integration |
| Shortfalls are shown as shortfalls | Built | Continuity page and an overview warning | Integration and Angular |
| New Trading Restoration fund (D6) | Built | Added by the `AddContinuitySettings` migration | Seeded-funds test |
| ZAR, other currencies shown apart and unconverted (D7) | Built | "Not counted" list | Integration; browser (USD 402 listed) |
| Monthly essential expense entered by the owner (D8) | Built | Settings form; "Not measured" until entered | Integration and Angular |

### Investments (§3.1, §3.6)

| Requirement | Status | Where | Verified by |
|---|---|---|---|
| A valuation is not a ledger movement, not income, not cash | Built | It never posts; balances are unchanged | Integration |
| Net contributions from the ledger | Built | Opening balances, transfers and exchanges, in less out; a reversal counts as the type it reverses | Unit and integration |
| Unrealised gain or loss, labelled unrealised | Built | Shows both figures; see "Differences from the plan" | Unit, integration and Angular |

### Budgets and purchases (§3.6, §7b)

| Requirement | Status | Where | Verified by |
|---|---|---|---|
| Budget per month and category, against expense categories | Built | `BudgetService` | Integration: case and spaces ignored, a reversed expense counts for nothing, currencies apart |
| Spending outside the budget is visible | Built | "Spent outside the budget" | Integration; browser |
| Proposed → Committed → Purchased with a linked expense, or Cancelled | Built | `PlannedPurchase` | Unit and integration; every transition used in the browser by P7.5 |
| A planned purchase never creates a ledger entry | Built | It only links an existing expense | Integration |
| "Can it be afforded without breaching the operating minimum" | Built | The answer and the figures behind it, for each open purchase | Unit, integration, Angular and browser |
| One expense pays for one thing | Built | Unique indexes, plus checks in both directions between purchases and obligations | Integration |

### Warnings (§3.6)

| Warning | Status | Verified by |
|---|---|---|
| A fund below its minimum | Built | Angular; browser |
| Continuity below target | Built | Angular; browser |
| An allocation not fully settled | Built (Phase 5) | Angular |
| An obligation due within N days (14), or overdue | Built | Angular; browser |
| A budget line exceeded | Built | Angular; browser |
| A planned purchase that would breach the minimum | Built, for any fund with a minimum | Angular; browser |
| Computed, never stored, never acted on automatically | Built | No warning table exists; nothing moves money or changes a status from a warning |

Added beyond the plan: a payment or purchase whose expense was later reversed, a brokerage account not valued or moved since its valuation, and continuity not measured.

### Decision questions (§1a)

| Question | Status | Where |
|---|---|---|
| Can a purchase be afforded without breaching the operating minimum? | Built | Planned purchases |
| How much of a payout is still unallocated? | Built | Payout and allocation pages; the overview's "not allocated" and "not fully settled" warnings |
| Which confirmed allocations are not yet transferred? | Built | Capital allocation page; the overview warning |
| How much unrestricted cash is there? | Built (after P7.5) | Overview, per currency, with the working on `/finance/unrestricted-cash` (D9) |
| How close is continuity to its target? | Built | Continuity page; the overview's cash strip and warning |
| What is coming due? | Built | Obligations page (30 days per currency); the overview warning (14 days) |
| Which trading accounts consume fees without producing payouts? | Not built | Needs prop-firm accounts, which are Phase 8 |

### Integrity and security

| Requirement | Status | Verified by |
|---|---|---|
| Owner only | Built: all Phase 7 controllers inherit the Owner policy from `FinanceControllerBase` | P7.5 added all five new areas to the 401 and 403 tests, which listed only earlier areas |
| Every change audited | Built: `obligation.*`, `continuity.updated`, `valuation.*`, `budget.*`, `purchase.*` | Integration; read back from the audit log in P7.5 |
| Nothing moves money by itself | Built: none of the five services posts to the ledger | Integration; `Affordability` has no side effects |
| Currencies never added or converted | Built: all totals are per currency | Integration and Angular |
| Migrations additive, backed up first | Built | `DATABASE_MIGRATION.md`; each backup passed `RESTORE VERIFYONLY` |

## Differences from the plan

Each was a decision made while building, recorded here so that §3.6 and the code agree. §3.6 now states what was built.

1. **No "Paid" stage on planned purchases.** §3.6 sketched `Purchased → Paid`. The purchase is recorded by linking an expense, and that expense is the payment, so a further stage would record nothing new.
2. **Unrealised gain or loss.** §3.6 said latest valuation − net contributions. That would count dividends and fees as market movement. The app shows valuation − recorded balance as the unrealised figure, and §3.6's figure separately as "gain on contributions".
3. **No `InvestmentContribution` or `InvestmentWithdrawal` transaction types.** §3.2 listed them. Money reaches a brokerage account by the ledger's own transfers, exchanges and opening balances, and the account's type says what they are.
4. **Navigation.** §6 planned a Wealth group. Continuity, Investments, Budgets and Purchases sit under Finance, because all of them are read from the ledger. The nav label is "Purchases", since a shell test forbids "Planned" there; the page title is "Planned purchases".
5. **The purchase warning applies to any fund with a minimum**, not only the operating fund.

## Found and fixed during P7.5

- **A paid one-off obligation hid its reversal.** The overview asked only for active obligations, and a one-off is closed by its payment. Reversing that expense raised no warning, contrary to `FINANCIAL_DOMAIN.md`. The overview now reads paid ones too. A cancelled obligation is left out, since it cannot be changed and nothing could clear the warning.
- **Warnings linked to a page that hid the item.** "Review the payment" and "Review the purchase" opened lists showing open items only, so a bought purchase or a paid obligation was not on the page. Those links now open with the closed ones showing (`?closed=true`).
- **Server errors on bad input.** These gave a 500 and now give a 422 with a reason:
  - **An amount too large to store:** on every money request, ledger included (`Money.Of`).
  - **A budget month of `9999-12`:** the calendar ran out. Budget months are now limited to 2000–2100.
  - **An obligation first due in 9999:** it saved, then would fail when its next date was worked out. First due dates are now limited to 2100.
- **Committed tag.** A committed purchase used the warning colour, while `DESIGN_SYSTEM.md` gives "committed, nothing moved yet" the info colour, as an approved payout has. It now matches.
- **Audit amounts.** Some summaries followed the server's culture (`ZAR 800,00` on this machine) beside others that did not (`ZAR 500.00`). All now read the same. Events already recorded keep their wording.
- **Duplicate warnings.** The list tracked warnings by title, and two budget lines of one category in different currencies can share one. It now tracks by position.
- **Access tests.** They did not include the Phase 7 endpoints. The code was already right; the tests now prove it.

## Gaps and recommendations

1. **Unrestricted cash.** *Closed after P7.5 (2026-11-03).* The owner defined it (PLAN.md D9) and it is built; the rules are in `FINANCIAL_DOMAIN.md`. The regression table above is P7.5's; the latest results are in `TESTING.md`.
2. **Obligations are shown beside the affordability answer, not deducted.** They are not tied to a fund, so a purchase can read "Affordable" with a larger bill listed beside it. If that proves too easy to overlook, an obligation could name the fund that pays it, and the check could then deduct it.
3. **Budgets match on free-text categories.** "Rent" and "Rental" are different categories. The difference is visible, because the second appears outside the budget, but nothing prevents it. A category list would. Budgets also do not carry over: each month's lines are entered again.
4. **A committed purchase from the continuity fund still counts as continuity cash** until it is bought, because a commitment moves nothing. The purchase page shows the reservation; the continuity page does not.
5. **Two targets can exist for the continuity fund.** The continuity target is months × expense, and the Funds page lets the same fund have its own target. They are not compared. The restoration fund has no such conflict, because its target is the fund's own.
6. **Smaller limitations, documented and not planned yet:**
   - **One expense, one purchase:** a purchase paid in two instalments cannot be linked to both.
   - **Undoing payments:** only an obligation's latest payment can be undone, so a reversal on an older one means undoing the later ones first.
   - **"Today" is the server's UTC date:** for the first two hours of a day in South Africa, "overdue" and "this month" still use the previous date.
   - **Concurrent edits:** budgets and purchases have no concurrency token, so with two tabs open the last save wins. There is one operator.
   - **Negative money:** the money format uses a hyphen (`ZAR -100.00`), while `DESIGN_SYSTEM.md` asks for a true minus sign. This predates Phase 7 and belongs in the Phase 10 interface pass.
   - **Screen readers:** the budget progress bar has a label but no role. Also for Phase 10.
