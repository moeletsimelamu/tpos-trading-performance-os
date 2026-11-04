# Phase 8: verification review (P8.5)

Phase 8 (trading integration), checked requirement by requirement against `PLAN.md`: the Phase 8 row of §7, the stages in §7c, the entity in §3.6, the rules in §3 and decisions D10–D12, D15 and D16. Each row says whether it is built, where, and how it was verified. Differences and gaps are listed at the end.

Status key:
- **Built:** done and verified.
- **Partly:** a working version exists, with the stated gap.

How prop firm accounts work is in [`PROP_ACCOUNTS.md`](PROP_ACCOUNTS.md). Test results and browser checks are in [`TESTING.md`](TESTING.md).

## Regression (2026-11-04)

| Check | Result |
|---|---|
| `dotnet build -c Release -warnaserror` | 0 warnings, 0 errors |
| .NET unit / integration tests | 385 / 156 passed, the integration suite on two consecutive runs |
| Angular tests (`ng test`) | 252 passed (32 files) |
| Production build (`ng build`) | Succeeded, 340.13 kB initial, no budget warnings |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages in any project |
| `npm audit` | 0 vulnerabilities |
| `dotnet ef migrations has-pending-model-changes` | No changes |
| `dotnet ef migrations script --idempotent` | Generated without error |
| Migrations applied | 20 of 20 on both `MelamuHoldings_Scratch` and dev `MelamuHoldings` (read from their history tables). P8.5 adds none. |
| CI | Passed on `develop` at `575438f` on 2026-11-04 (run 41802036533, backend and frontend), the first CI run on Phase 8. |

A first regression run during this review was invalid: a running API preview locked the output files, so the build failed and the tests ran against old binaries, showing 147 integration tests instead of 156. Those results were discarded and the run repeated with the API stopped.

## Exit criterion

Phase 8's exit criterion is "existing analytics unchanged except the documented definition fixes; tests prove it". **Met.**

- **Code:** against `main`, the only analytics code changed is `AnalyticsService` (the two definitions in P8.4) and `AnalyticsScope` (the account filter reads a linked account's name, P8.2). `CompletedTrade`, `BehaviourAnalyticsService`, `AdherenceService` and `JournalAnswerAnalytics` are untouched.
- **Tests:** `AnalyticsDefinitionTests` was written and run before the change. Five tests pin every other figure, worked out by hand, and passed both before and after it. Eight tests of the new definitions failed before and pass after.

## Requirement trace

### Scope of Phase 8 (§7)

| Requirement | Status | Where | Verified by |
|---|---|---|---|
| Prop firm accounts with configurable rules | Built | `PropFirmAccount`, versioned `PropAccountRuleSet`, `/trading/prop-accounts` | `PropAccountEndpointTests`, unit tests, browser (P8.1) |
| Status history | Built | Append-only `PropAccountEvent` | `An_account_goes_through_its_phases_on_one_record_with_every_change_kept` |
| Trades linked to accounts | Built | `Trade.PropFirmAccountId`, trade form, whole-file import | `PropAccountStandingEndpointTests`, browser (P8.2) |
| Fees and resets as expenses | Built | `PropAccountFee` links an existing expense | `PropAccountLedgerEndpointTests`, browser (P8.3) |
| Payouts linked to accounts | Built | `Payout.PropFirmAccountId` | `PropAccountLedgerEndpointTests`, browser (P8.3, and on the Payouts page in P8.5) |
| Per-user strategies with CRUD | Built, with one difference | J4: `StrategiesController` lists, reads, creates and updates. Strategies are archived and restored instead of deleted, so history stays readable | `Strategies_belong_to_one_trader_and_names_are_unique_per_trader`, `Removed_rules_are_retired_and_can_be_restored_and_invalid_strategies_are_refused` |
| CSV duplicate detection | Built, extended here | J5: a re-imported row is skipped. P8.5: so is the same row twice in one file | `An_import_never_duplicates_a_journaled_trade_and_links_only_what_is_missing`, `The_same_row_twice_in_one_file_is_imported_once`, browser |
| Profit factor fix | Built | P8.4: undefined with no losses | `AnalyticsDefinitionTests`, `AnalyticsDefinitionEndpointTests`, browser |
| Drawdown fix | Built | P8.4: the curve's drawdown is an amount | Same tests. The API was read directly, because no page shows this figure |
| Date filters | Built | J6: `from` and `to` on every `/api/analytics/*` endpoint | `Date_session_and_account_filters_narrow_every_figure_alike`, browser |

### Decisions

| Decision | Status | Verified by |
|---|---|---|
| D10: one record per attempt, each phase and reset restarts the balance | Built | `A_trade_counts_on_the_day_it_closed_and_each_phase_or_reset_starts_again` |
| D11: prop firm accounts only, other trades keep free text | Built | `A_whole_file_can_be_imported_onto_an_account_and_without_one_nothing_changes` |
| D12: calculate and warn, never block, labelled as the app's figures | Built | `PropAccountStandingTests`, overview warnings in `overview.component.spec.ts`, wording checked in the browser |
| D15: drawn cards only where a card is recorded, masked, no logos | Built | `finance-pages.spec.ts`, browser at desktop and 375 px |
| D16: links to existing records, moving no money, owner only, unconverted | Built | `PropAccountLedgerEndpointTests`, `PropAccountEconomicsTests`, `FinanceEndpointTests`, browser |

### Decision question (§1a)

| Question | Status | Where |
|---|---|---|
| Which trading accounts consume fees without producing payouts? | Built | "Fees against payouts" on the Prop accounts page, per currency and unconverted. Only linked fees and received payouts count, and the report says how many received payouts are not linked |

### Integrity and security

| Rule | Status | Verified by |
|---|---|---|
| The trading module never writes ledger entries | Built | `Prop_accounts_post_nothing_to_the_ledger`. Fee and payout links leave balances unchanged in `PropAccountLedgerEndpointTests` |
| Finance does not depend on Trading | Built, after a fix | No file under `Application/Finance`, `Domain/Finance` or `API/Controllers/Finance` imports Trading. See "Found and fixed" |
| A trader reaches only their own accounts | Built | `Accounts_belong_to_one_trader`, `Another_traders_account_is_out_of_reach_and_missing_from_their_report` |
| Money linked to accounts is owner only | Built | `FinanceEndpointTests`, with a token that has no Owner role |
| No full card numbers, expiry, security code or cardholder | Built | `Card_details_that_should_never_be_kept_are_refused` |
| Projected payouts never count as received | Built | `A_payout_on_its_way_is_never_money_received` |
| CSV import is atomic | Built, after a fix | `A_failure_part_way_leaves_no_trades_strategies_or_matches_and_the_file_can_be_imported_again` |

## Is CSV import atomic? (W11)

**It was not.** The trades looked as if they were saved together, but saving a new strategy also saved everything added so far. In a file naming several new strategies, the rows before a failure stayed in the journal, the upload log stayed at Pending forever, and importing the file again met those rows as duplicates. A test showed this on the old code by making a file's last row fail: two earlier trades remained.

**Now:** everything from one file happens in one database transaction. If anything fails, no trade, match or new strategy remains, the upload log reads Failed, and the same file can be uploaded again.

**What atomic does not mean here:** a file with some bad rows still imports its good ones and lists the bad ones by row number. That is the documented behaviour, and it is safe because importing again skips what is already in.

## Differences from the plan

1. **Two links between finance and trading, not one.** §3 named a payout's reference to an account as the single integration point, while §3.6 also asked for fees linked back to an account. Both are built, as links only. §3 now says so.
2. **Strategies are archived, not deleted.** "CRUD" in the Phase 8 row is met with archive and restore, as J4 decided, so old trades keep their strategy's name.
3. **Profit factor is also undefined with no trades, or when every trade broke even.** The plan only named "no losses". Those cases have no losses either.
4. **Cards were added to Phase 8 at the owner's request** (D15), and are not in the plan's row.

## Found and fixed during P8.5

Each of the import problems was reproduced by a test on the old code first. All nine of those tests failed before the fixes and pass now.

| Problem | Effect | Fix |
|---|---|---|
| Import not atomic | A failure part way left some trades and strategies, and a stuck log | One transaction; the log reads Failed |
| A value too large for its column failed the whole file | `RR` of 1,000,000 gave a server error, and could leave earlier rows behind | Reported as that row's error; the other rows import |
| Unreadable entry times became midnight (W11) | `half past nine`, `25:00` and a blank time were saved as 00:00, which feeds session and timing analysis | Reported as that row's error. **A blank time is now refused too** |
| The same row twice in one file was imported twice | A re-import counted both as duplicates, so the second copy could never be noticed | Imported once, with the repeat counted as already imported |
| The same row twice matching a hand-entered trade asked twice | Two decisions for one row | Asked once |
| P8.3's ledger service sat in Finance and read Trading | It broke the "Finance does not depend on Trading" rule, introduced by P8.3 | Moved to `Application/Trading`, with its controller. No behaviour changed |

## Gaps and recommendations

1. **CI.** *Closed after P8.5 (2026-11-04).* `develop` was pushed and CI passed at `575438f`. Phase 8 is not on `main` until its pull request is merged.
2. **Two uploads of one file at the same moment could both import it.** Duplicates are found by reading existing trades, and no database rule prevents them. This comes from reading the code and has not been tested. It needs two uploads within moments of each other, and there is one operator.
3. **Dates and times in a CSV are read in the server's language settings.** `2026-11-03` is safe everywhere, but `01/02/2026` could mean January or February depending on the server. Only ISO dates are safe to rely on.
4. **Two genuinely identical fills in one file count as one.** Same date, instrument, direction, time, prices and P&L. The result shows it as already imported, so it can be seen, and the trade can be added by hand.
5. **A failed upload's log keeps no reason.** The person uploading sees the error once, while the history only says Failed.
6. **Fees are not compared with the rules.** A linked fee is not checked against the fee in the account's rules, and a monthly fee never linked does not show as missing.
7. **Mixed currencies in analytics.** Performance adds P&L in each trade's own currency, which imported trades do not record. This predates Phase 8, and the page says so.
8. **Results by phase** need a date filter, as a consequence of D10.
9. **Not checked in a browser:** receiving a linked payout, a failed import's message on the page, and Owner-only hiding, because every scratch user is an owner. Integration or Angular tests cover each of them.
10. **Repository screenshots still show the old logo.** Retaking them belongs to the Phase 10 design pass.
