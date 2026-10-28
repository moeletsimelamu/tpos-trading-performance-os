# Testing

How the automated tests are organised, how to run them, and the latest recorded results.
The strategy for later phases is in `../PLAN.md` §9.

## Test suites

| Suite | Location | What it covers | Needs |
|---|---|---|---|
| .NET unit | `tests/MelamuHoldings.UnitTests` | `Money`/`Currency` rules (minor units, rounding modes, currency mismatch, equality), ledger rules (each transaction type's entries and zero-sum, currency and closed-account checks, exchange rate, reversal, dates, closing at zero, system fund), trading analytics (summary, expectancy, drawdown, equity curve, strategy breakdown), behaviour detection, CSV parsing, trade DTO validation, owner provisioning, login, JWT options | Nothing |
| API integration | `tests/MelamuHoldings.IntegrationTests` | The real API via `WebApplicationFactory`: login and ProblemDetails, rate limiting, missing signing key, anonymous access (401), trade validation (400), cross-user isolation, CSV upload into behaviour analytics, JSON contract (`rr`). Finance:
<ul>
<li>a full flow with hand-checked account, fund and cash totals;</li>
<li>idempotent retries, including 8 concurrent requests posting once;</li>
<li>reversal once only;</li>
<li>400 vs 422 cases;</li>
<li>one opening balance per account and fund;</li>
<li>closing an account;</li>
<li>filtering and paging;</li>
<li>as-of balances;</li>
<li>audit events;</li>
<li>401, and 403 without the Owner role.</li>
</ul> | SQL Server |
| Angular unit | `MelamuHoldings.Angular/src/**/*.spec.ts` (Vitest via `@angular/build:unit-test`) | Routing (landing on the overview, old-URL redirects, login redirect, page titles), shell navigation (only live destinations, each with an icon; brand mark; drawer toggle), auth session and guard, HTTP error messages, and the performance, trades, login and import pages: loading, empty and error states, Retry, filters, two-step and failed delete, double submit, inline upload errors. Performance: the lead metric, the date range and the breakdown tabs. Executive overview: attention checks (no accounts, unallocated money in every currency, below-zero and unstated-ownership accounts, active accounts only, critical alerts only), currencies never summed, trading P&L labelled as not cash, the trading panel surviving a finance 403, headline amounts. Finance:
<ul>
<li>money formatting;</li>
<li>the record form: request body, idempotency key kept on retry and renewed on change, double submit, amount validation, negative opening balance, same- vs cross-currency destinations, entry preview, implied rate;</li>
<li>accounts: totals, close at zero only, 409 shown;</li>
<li>ledger: URL filters, signed entries, reversal with key, reversal eligibility.</li>
</ul> | Node 22 |

### Integration test database

- Each test class creates its own database named `MelamuHoldings_Test_<guid>`, applies the EF migrations, seeds two test users, and drops the database afterwards.
- The server comes from the `MELAMU_TEST_SQLSERVER` environment variable. Locally it defaults to `Server=.\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True;`.
- Tests run in the `Testing` environment. That environment skips `appsettings.Development.json` and user-secrets, so a misconfigured test cannot reach the development database.

## Running

```bash
dotnet test MelamuHoldings.slnx
```

```bash
npm --prefix MelamuHoldings.Angular test -- --watch=false
```

CI (`.github/workflows/ci.yml`) runs:
- **Backend:** a Release build with warnings as errors, a vulnerable-package gate, and both .NET suites against a SQL Server 2022 container.
- **Frontend:** `npm ci`, a production-dependency audit, `ng build`, and the Angular specs.

## Results — 2026-10-28 (branch `journal-j5-overview`)

| Command | Result |
|---|---|
| `dotnet build MelamuHoldings.slnx -c Release -warnaserror` | 0 warnings, 0 errors |
| `dotnet test` (Release): unit | 148 passed, 0 failed |
| `dotnet test` (Release): integration, applying all eight migrations | 74 passed, 0 failed. The J5 tests (including concurrent import decisions) passed 5 runs in a row. |
| `ng test --watch=false` | 129 passed, 0 failed (17 files) |
| `ng build` (production) | Succeeded; initial bundle 308.17 kB; no budget warnings |
| `dotnet ef migrations has-pending-model-changes` | No changes |

**What the new tests cover**
- **Unit:**
  - **File types:** PNG, JPEG, WebP and PDF are recognised by their bytes; SVG, HTML, GIF, executables and other RIFF files are refused.
  - **File names:** stored names lose folders and unsafe characters and take the detected extension; empty and oversized files are refused.
  - **Matching:** a row matches a journaled trade only when nothing recorded disagrees, and not on the day alone. A row duplicates only an imported trade with the same values.
  - **Linking:** fills only empty fields, keeps notes and status, and cannot be decided twice.
  - **Calendar:** totals count completed trades only, with drafts and open trades counted apart.
- **Integration:**
  - **Calendar:** 2 completed trades (+120, −45) and a draft with a P&L total +75; a draft-only day is listed with 0 completed. Month 13 is refused, and another trader's month is empty.
  - **Overview:** lists drafts and trades awaiting review, newest first.
  - **Attachments:**
    - **Uploading and serving:** an image is stored as `setup chart.png` (the path is stripped) and served inline with `nosniff`. A PDF named `.png` is stored and served as a PDF download.
    - **Access:** another trader gets 404 on read, delete and upload; anonymous gets 401.
    - **Refused:** SVG and HTML-as-PNG (422), a 5 MB + 1 byte file (422), an empty form (400) and an 11th file (422).
    - **Cleanup:** deleting a trade removes its files from disk.
  - **Import:**
    - **First upload:** the matching row is held back, the new row is imported, and the identical re-upload skips both.
    - **Isolation:** another trader cannot see or link the match.
    - **Linking:** fills exit, P&L and session but not the recorded entry price, keeps the notes and the Open status, and a second link is refused.
    - **Other decisions:** import separately creates an imported trade, and dismiss removes the row from the list.
    - **Weak match:** a draft with neither entry time nor price is not matched.
    - **Concurrency:** 4 concurrent "import separately" requests create exactly one trade.
- **Angular:**
  - **Calendar:** the month grid starts on Monday. Cells give totals, a dashed unfinished day, links with the date, labels for screen readers, and a compact amount format.
  - **Journal page:** counts, the import reminder, the "All N" link and month navigation; it still loads when matches fail.
  - **Trade log:** date and status from the address; strategy, session and account filters; P&L sorting with empty values last; 25 per page, back to the first page on a filter change.
  - **Attachments:** image previews; client-side refusal of SVG and oversized files; upload with caption; delete after confirmation.
  - **Folded answers:** unanswered questions are folded behind a toggle.
  - **Import page:** skipped and matched counts, the side-by-side comparison, the link message naming filled fields, and only import or leave out when the trade was deleted.

**Found and fixed while testing:** the attachment folder was read from configuration while the app was being built, before test (and host) overrides applied. The integration tests showed files landing in the API project folder instead of the test folder. It is now read when first needed, and the stray files were removed.

### Manual browser verification (J5)
Run against `MelamuHoldings_Scratch` after applying `AddAttachmentsAndImportMatches`:
- **Journal page:**
  - **Counts:** 0 drafts, 0 open, 20 awaiting review, 2 reviewed.
  - **October calendar:** 22 completed trades, +2,460.00. That matches Performance (21 trades, +1,960.00) plus the J3 trade (+500.00).
  - **28 October:** +875.00 (375 + 500).
- **Day link:** clicking 12 October opened the trade log at `?date=2026-10-12` with "Trades on Monday 12 October 2026" and 6 trades summing to +270.00, as the calendar showed. Sorting by P&L ordered them +400 … −200 with `aria-sort="descending"`.
- **Attachments:** trade 22 got a 320 × 200 PNG (created in the browser) with the caption "Retest entry", shown as a thumbnail. An HTML file named `harmless.png` was refused: "Only PNG, JPEG, WebP images and PDF files can be attached. (HTTP 422)".
- **Folded answers:** the trade's journal showed only the answered sections, with "Show 17 unanswered questions".
- **Import:** a 3-row CSV was uploaded: one row identical to imported trade 2, one matching hand-journaled trade 22, and one new CL trade.
  - **Result:** 1 imported, 1 already imported, 1 matching the journal, shown side by side with trade 22.
  - **Linking:** "Filled in: duration, strategy". The database showed trade 22 still Reviewed with its review notes, now with duration 30 and strategy ORB. There is still one ES trade on 5 October, CL was imported as trade 23, and the match is Linked.
  - **Calendar after:** 23 trades, +2,710.00.
- **Phone width (375 px):** the journal page, trade log, trade page and import page have no page-level horizontal scroll.
- **Fixed during the walkthrough:** calendar cells cut off amounts like "+100.0" at phone width. They now show a compact amount there (+100, +1.2k), and the full amount stays in each day's label.

## Results — 2026-10-28 (branch `journal-j4-strategies`)

| Command | Result |
|---|---|
| `dotnet build MelamuHoldings.slnx -c Release -warnaserror` | 0 warnings, 0 errors |
| `dotnet test` (Release): unit | 130 passed, 0 failed |
| `dotnet test` (Release): integration, applying all seven migrations | 67 passed, 0 failed. The strategy tests (including the concurrent-create race) passed 5 runs in a row. |
| `ng test --watch=false` | 115 passed, 0 failed (16 files) |
| `ng build` (production) | Succeeded; initial bundle 307.82 kB; no budget warnings |
| `dotnet ef migrations has-pending-model-changes` | No changes |

**What the new tests cover**
- **Unit:**
  - Rules are edited in place, retired when left out, and restored at the end.
  - Repeated, empty, foreign or too many rules are refused.
  - A checklist copies the active rules, so rewording a rule later does not change it.
  - Only a mandatory rule not met is a violation, and compliance is unknown until every rule is answered.
  - Answers are appended, never overwritten, and an unchanged answer adds nothing.
  - N/A is refused on mandatory rules, and rules not on the checklist are refused.
  - The adherence report counts closed trades only, with a complete checklist as the denominator, and flags groups under 20 trades. It has no rate, rather than 0%, when nothing is scored.
- **Integration:**
  - Strategies are private to each trader, names are unique per trader (409), and a strategy named on a trade is created for that trader only.
  - Five concurrent trades naming a new strategy create it once.
  - Removed rules are retired and can be restored; invalid strategies are refused (400/422).
  - A trade's checklist keeps its original wording after the rule is edited, records a change as sequence 2, and is left alone when a save sends no answers.
  - Answers that do not fit the strategy (N/A on a mandatory rule, another strategy's rule, no strategy, an unknown answer) are refused, and no trade is saved.
  - The adherence report: 5 closed trades, 3 complete checklists, 1 unfinished, 1 without, 66.67 %, insufficient data, the rule most often not met; another trader sees none of it.
  - Times come back marked UTC.
- **Angular:**
  - The strategies list shows the rate with its denominator, the result comparison and the per-strategy rate, and "—" (not 0 %) with nothing scored.
  - The editor sends rules in order, refuses empty or repeated rules, keeps rule ids, reports retired rules, and restores one.
  - The trade form shows the named strategy's rules, offers N/A only on optional ones, sends only answers that were touched, and keeps an existing checklist's wording.
  - The entry page shows violations and changes.

**Migration backfill, tested by hand** on a throwaway database (`MelamuHoldings_J4Backfill`, dropped afterwards). It was migrated to J3, seeded with two traders and five old global strategies, then migrated to J4:
- one strategy used by both traders was split, and trader 2's trades moved to their copy;
- an unused strategy was removed;
- `ICT` and `ict ` were merged, with their trade moved;
- a 120-character name was cut to 100;
- blank descriptions became NULL.

The first attempt failed on NULL descriptions before the column allowed them. The migration rolled back, and the order was fixed. `Down` and `Up` were then run again successfully.

### Manual browser verification (J4)
Run against `MelamuHoldings_Scratch` after applying `AddStrategyRulesAndChecklists` (its 3 strategies went to its one trader):
- **Strategies page:** listed ORB, Pullback and London Breakout with "No rules yet". Adherence showed "—" and "22 without a checklist · 0 unfinished, of 22 closed trades".
- **ORB rules:** gave ORB two mandatory rules and one optional rule, saved, and reloaded; they persisted.
- **Trade 1:** the edit form showed the three rules, with N/A offered only on the optional one. It was answered Met / Not met / N/A, and the entry page showed "1 mandatory rule not met". Changing the risk answer to Met showed "Followed every mandatory rule" and "Changed after first answer: Risk within 1% of the account: Not met → Met".
- **Rule edits:** reworded the stop rule and removed the news rule (it was retired, with Restore offered). Trade 1 still showed its original wording and the N/A news answer. Trade 3's form showed only the two current rules.
- **Trade 3:** answered with the stop rule not met. The Strategies page then showed **50 % (1 of 2)**, Followed +400.00 at 2.00R and Broke −400.00 at −2.00R, both labelled insufficient data. The stop rule was listed as not met 1 of 2.
- **Phone width (375 px):** the strategies list, strategy page, trade form and trade page have no page-level horizontal scroll.
- **Fixed during the walkthrough:**
  - **Hidden answer radios:** they sat outside their labels, so focusing one scrolled the page away. They now cover their label.
  - **"Keep it":** "1 rule was retired" now reads "keep it".
  - **Local times:** times read from the database lost their UTC marker, so the browser showed them two hours early. The audit log and the transaction "Recorded" times had the same fault, which predates J4. An EF convention now marks every stored time UTC on read; no migration is needed.

## Results — 2026-10-28 (branch `journal-j3-templates`)

| Command | Result |
|---|---|
| `dotnet build MelamuHoldings.slnx -c Release -warnaserror` | 0 warnings, 0 errors |
| `dotnet test` (Release): unit | 122 passed, 0 failed |
| `dotnet test` (Release): integration, applying all six migrations | 61 passed, 0 failed |
| `ng test --watch=false` | 105 passed, 0 failed (15 files) |
| `ng build` (production) | Succeeded; initial bundle 307.44 kB; no budget warnings |
| `dotnet ef migrations has-pending-model-changes` | No changes |

**What the new tests cover**
- **Unit:** the standard template is valid and has the brief's sections; duplicate or malformed ids and choice fields without options are refused; answers are checked against field types and options; empty answers are dropped, not stored; changing questions adds a version and renaming does not; an entry keeps its version's questions and answers; the default cannot be archived and an archived template cannot become default.
- **Integration:** every trader gets the standard template once, as default; editing a template adds a version while existing entries stay on theirs; review is refused until required questions are answered; duplicate, archive, restore and make-default follow the rules; invalid templates return 400; another user cannot see or use a template or version.
- **Angular:** answers carry to another version only where the id and type match, and the rest are named; checklist and multi-select answers; the editor's request body (ids from labels, options one per line, existing ids kept), its missing-field messages and version message; the template list, duplicate and archived toggle; the trade form sends the default template's answers, carries answers between templates, keeps an older version until moved, and still saves when templates fail to load; the entry page shows the version and holds back the review while required questions are unanswered. The trade form specs now stub navigation, which removed 6 unhandled router rejections from the test output.

### Manual browser verification (J3)
Run against `MelamuHoldings_Scratch` after applying `AddJournalTemplates`:
- Opening Journal templates created **Standard trade journal v1** as default.
- **Duplicate** opened the copy in the editor. Renaming it saved without a new version ("The questions did not change"); changing a question's label saved **version 2**.
- New trade: the default template was preselected. Saved as closed with the key lesson, two checklist ticks and a confidence rating, but without "Was the trade valid?". The entry page showed "Standard trade journal · version 1", every answer (dashes for the rest), and held back the review: "Answer the required journal questions first".
- Edited the standard template (changed one label, removed "Setup identified") → version 2. The trade still showed **version 1 (latest is 2)**, including the removed question.
- On the trade's edit form, **Use version 2** moved the entry with its answers; after answering "valid", the review was allowed and the trade marked **Reviewed**.
- Archived the copy: it left the list, and returned with Restore under "Show archived". The default row offers no Archive.
- At phone width (375 px), the template list and editor have no page-level horizontal scroll; the list's table scrolls inside its card.
- Fixed during the walkthrough: "1 trade … keep" now reads "keeps", and labels ending in "?" no longer gain a full stop in notices; the template name column has a minimum width.

## Results — 2026-10-28 (branch `journal-j1-j2`)

| Command | Result |
|---|---|
| `dotnet build MelamuHoldings.slnx -c Release -warnaserror` | 0 warnings, 0 errors |
| `dotnet test` (Release): unit | 115 passed, 0 failed |
| `dotnet test` (Release): integration, applying all five migrations | 55 passed, 0 failed. The concurrency tests passed 15 runs in a row. |
| `ng test --watch=false` | 90 passed, 0 failed (14 files) |
| `ng build` (production) | Succeeded; initial bundle 307.06 kB; no budget warnings |
| `dotnet ef migrations has-pending-model-changes` | No changes |

**What the new tests cover**
- **Unit:** required fields per status, refusing a status until its fields exist, review only after close and with notes, R / duration / day calculated only from recorded values (no risk → no R, not 0R), the completed-trade projection, analytics ignoring a draft with a P&L in it, average R over trades that have one, status validation on save. The existing analytics and behaviour characterisation tests pass unchanged.
- **Integration:** a draft saves with nulls and leaves the summary unchanged; closing it is refused without the exit, then counts with R 2.5 and a 45-minute duration; review needs a closed trade and `Reviewed` cannot be set as a plain status; 5 concurrent same-key saves create one trade; the original closed-trade contract still works; another user cannot read or review a draft.
- **Angular:** a draft sends nulls with an idempotency key; a closed save names what is missing instead of sending; the key is kept on retry; editing a reviewed trade sends no status; the entry page shows dashes for unrecorded values and marks a trade reviewed; delete needs confirmation; status and result filters.

**Race fixed on this branch:** a same-key retry of an allocation confirmation or a reversal breaks two unique indexes, and SQL Server may report either. Both paths now look for the key before reporting a conflict.

### Manual browser verification (J2)
Run against `MelamuHoldings_Scratch` after applying `AddTradeJournalFields`. The trade is labelled "SCRATCH":
- New trade → saved a **draft** with only instrument, session and strategy. The entry page shows dashes and "Not counted in performance".
- Edited it: "Save as closed" without an exit was refused with "Still needed for this status: exit price, realised P&L". Added exit and P&L → **Closed**, R 2.50 (375 ÷ 150), 45 minutes, USD.
- Wrote the review → **Reviewed**, with the notes shown and dated.
- Performance went from 20 to **21 closed trades** and from +1,585.00 to **+1,960.00**; expectancy +93.33 (1,960 ÷ 21).
- The trade log shows status tabs (Closed 20, Reviewed 1) and the New trade button.
- At phone width (375 px), the new trade form, the entry page and the trade log have no horizontal scroll.

## Results — 2026-10-28 (branch `phase-5-payouts`)

| Command | Result |
|---|---|
| `dotnet build MelamuHoldings.slnx -c Release -warnaserror` | 0 warnings, 0 errors |
| `dotnet test` (Release): unit | 106 passed, 0 failed |
| `dotnet test` (Release): integration, against local SQL Server Express 2019, applying all four migrations | 49 passed, 0 failed. The four concurrency tests passed 12 runs in a row after the replay-race fix below. |
| `ng test --watch=false` | 82 passed, 0 failed (13 files) |
| `ng build` (production) | Succeeded; initial bundle 307.06 kB; no budget warnings |
| `dotnet ef migrations has-pending-model-changes` | No changes |

**What the new tests cover**
- **Unit:** largest-remainder splits (the PLAN §3.4 examples, ties, 715 amounts conserved to the cent), policy rules, the payout lifecycle (order, approved ≤ requested, received ≤ approved, rejection, undo), cross-currency effective rate, allocation snapshots, derived status and reopening on reversal, over-settlement and insufficient-funds checks, fee entries on income and exchange (and their reversal), quoted-rate validation, and a regression test for culture-dependent validation.
- **Integration:** the PLAN §8 milestone end to end (request → approve → receive → preview → confirm → earmark and transfer → partially settled → reconciled, balances checked by hand, same-key replay, new-key 409, policy v2 leaving v1 allocations untouched, 1,000.01 and 0.03 splits); 8 concurrent confirms allocate once; 6 concurrent same-key receipts post once; receipt cannot be reversed from the ledger or undone while allocated; reversing a settlement reopens its line; exchange settlement with fee and both rates; exchange with a receiving-side fee; policy validation and activation; audit events; 401/403 on the new endpoints.
- **Angular:** receipt prefill, key kept on retry, effective-rate hint, preview and confirm with the previewed policy, settlement bodies per method (earmark, transfer destinations, exchange with fee and quoted rate), cancel only before settlement, policy draft total must be 100 %, expected payouts labelled not cash, overview attention items for payouts and allocations, exchange fee in the record form.

**Race found by CI (first push of Phase 5):** concurrent requests with the same `Idempotency-Key` could pass the key check before the first one committed, then fail the state check after it had ("this payout is already received"), returning 422 instead of replaying. Rejected requests now re-check the key and replay the winner (`LedgerPosting`, `AllocationService.ConfirmAsync`). A new test sends 6 same-key confirmations and 6 same-key settlements at once.

**Bug found by the tests:** `[Range(typeof(decimal), "0.01", …)]` parses its limits with the server's culture, and this machine uses a comma decimal separator, so every request with a fee failed with a 500. Replaced by `PositiveAmountAttribute`, which parses invariantly; a unit test runs it under en-US, de-DE and af-ZA.

### Manual browser verification (Phase 5)

Run against `MelamuHoldings_Scratch` after applying `AddPayoutsAndAllocation`, every record labelled "scratch test":
- Recorded a USD 1,350.00 request: listed as **Expected · not cash**.
- Approved it, then received USD 1,340.00 into Payouts USD with a USD 10.00 fee. The ledger shows income +1,350.00 and a **Fee** entry −10.00.
- The preview split 1,340.00 as 536 / 402 / 268 / 134 (sums exactly); confirmed as allocation #1.
- Settled Operating Capital by **exchange**: USD 536 → ZAR 9,782.00 into Operating ZAR, USD 5.00 fee, quoted rate 18.30. Status became **Partially settled** and the cancel option disappeared.
- Executive overview at that point: cash USD 1,149.00 (350 + 1,340 − 536 − 5) and ZAR 28,032.50 (18,250.50 + 9,782), with "Allocation #1 not fully settled — USD 804.00 of USD 1,340.00" under Needs attention. Both match the hand calculation.
- Earmarked the other three lines: **Reconciled**, USD 0.00 outstanding.
- The policy draft at 95 % shows the shortfall and keeps Save disabled.
- Phone width (375 px): payouts, payout detail, allocations, allocation detail and policy pages all have `scrollWidth` 375.

## Results — 2026-10-27 (branch `ui-brand-redesign`)

The frontend changed only; the API, the domain and the database did not. The .NET suites were not re-run for this branch, so their latest results are the Phase 4 results below. CI runs them again on push.

| Command | Result |
|---|---|
| `ng test --watch=false` | 68 passed, 0 failed (12 files) |
| `ng build` (production) | Succeeded; initial bundle 306.81 kB; no budget warnings (every component style under 4 kB) |
| `npm audit --omit=dev --audit-level=high` | 0 vulnerabilities |

### Manual browser verification (redesign)

Run against `MelamuHoldings_Scratch` (the Phase 4 scratch ledger and 20 sample trades) at 1366 × 860 and 375 × 812.

- **Every route renders with real API data:** overview, performance, trades, import, behaviour, accounts, funds, transactions, record transaction and audit log.
- **Sign-in:** signing out shows the branded sign-in page, and signing in lands on `/overview`.
- **Overview figures match the Phase 4 hand calculation:**
  - USD 350.00 and ZAR 18,250.50 cash, shown separately;
  - Unallocated USD 350.00 and ZAR 13,250.50, flagged "Not assigned";
  - "Earmark money" opens the record form with Earmark selected.
- **Performance:** shows the same figures as before the redesign: +1,585.00, 50.0% (10 won · 9 lost · 1 even), +79.25, 1.87, 0.46R, −800.00. The strategy breakdown (ORB +1,860.00, London Breakout +725.00, Pullback −1,000.00) sums to the total.
- **Phone width (375 px):** every page has `scrollWidth` 375, so there is no horizontal scroll. No metric value is clipped, and the drawer opens and closes.
  - **Fixed during the check:** a screen-reader-only table header widened the page by 85 px; `.visually-hidden` is now pinned to its containing block.
- **Keyboard:** Tab reaches the sidebar links with a visible brass focus ring. The favicon is served as the new `favicon.svg`.
- **Not verified:**
  - contrast was calculated from the token values, not measured with a tool;
  - no screen-reader pass was done;
  - Firefox and Safari were not checked; only the built-in Chromium pane was used.

## Results — 2026-10-27 (branch `phase-4-ledger`)

| Command | Result |
|---|---|
| `dotnet build MelamuHoldings.slnx -c Release -warnaserror` | 0 warnings, 0 errors |
| `dotnet test` (Release): unit | 75 passed, 0 failed |
| `dotnet test` (Release): integration, against local SQL Server Express 2019, applying all three migrations | 33 passed, 0 failed. Stable across 4 consecutive runs; no test databases left behind. |
| `ng test --watch=false` | 58 passed, 0 failed (11 files) |
| `ng build` (production) | Succeeded; initial bundle 310.28 kB; no budget warnings |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages |
| `npm audit --omit=dev --audit-level=high` | 0 vulnerabilities |
| `dotnet ef migrations has-pending-model-changes` | No changes |

### Manual browser verification (Phase 4)

Run against `MelamuHoldings_Scratch` after applying `AddFinanceLedger`, with every record labelled "scratch test".

Recorded:
- **Through the UI:** an account (Payouts USD); income of USD 1,350, with the account preselected from the URL and the preview showing "Payouts USD · Unallocated, USD +1,350.00"; a currency exchange of USD 1,000 → ZAR 18,250.50, where the "to" list offered only ZAR accounts and the rate hint read 18.2505; and an earmark of ZAR 5,000 from Unallocated to Business Continuity.
- **Through the API:** two ZAR accounts, a ZAR 2,000 transfer to Reserve, and a deliberately wrong ZAR 3,000 expense.
- **Reversed through the ledger page:** the wrong expense, with a reason. #6 reverses #5, and #5 shows "Reversed by #6".

Checked:
- **Accounts page, all matching the hand calculation:**
  - Payouts USD 350.00.
  - Operating ZAR 16,250.50 (Unallocated 13,250.50 + Business Continuity 3,000.00).
  - Reserve ZAR 2,000.00.
  - Cash totals USD 350.00 and ZAR 18,250.50.
- **Funds page:** Unallocated USD 350.00 and ZAR 13,250.50; Business Continuity ZAR 5,000.00.
- **Audit log:** all nine changes in order.
- **Phone width (375 px):** no horizontal page scroll; the ledger table scrolls inside its card; the form uses one column.
- **Fixed during the check:** links used the browser's default dark blue, and `.btn-link` buttons had a light default background.

## Results — Phase 3 (branch `phase-3-foundation`)

| Command | Result |
|---|---|
| `dotnet build MelamuHoldings.slnx -c Release -warnaserror` | 0 warnings, 0 errors |
| `dotnet test` (Release): unit | 58 passed, 0 failed |
| `dotnet test` (Release): integration, against local SQL Server Express 2019, applying both migrations | 15 passed, 0 failed |
| `ng test --watch=false` | 37 passed, 0 failed (8 files) |
| `ng build` (production) | Succeeded; initial bundle 296.85 kB; no component-style budget warnings |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages |
| `npm audit --omit=dev --audit-level=high` | 0 vulnerabilities |
| `dotnet ef migrations has-pending-model-changes` | No changes |

### Manual browser verification (Phase 3)

Done against `MelamuHoldings_Scratch` (20 sample trades), after applying the new migration:

- **Login:** rebranded page with the title "Sign in · Melamu Holdings OS". Signing in lands on `/trading/performance`.
- **Desktop (1366 px):**
  - The grouped sidebar shows four live links, and ten modules marked Planned.
  - The six KPIs sit on one row.
  - Expectancy reads +79.25 (1,585 ÷ 20), confirming the Phase 2 formula fix.
- **Old URLs:** `/upload` redirects to `/trading/import`, with `aria-current="page"` on the active link. Trades shows its RR values; Behaviour shows 3 alerts.
- **Phone (375 px):**
  - No horizontal scroll.
  - Two KPI columns and one chart column.
  - The menu button opens the navigation drawer (`aria-expanded="true"`).

## Results — Phase 2 (branch `phase-2-stabilise`)

| Command | Result |
|---|---|
| `dotnet build MelamuHoldings.slnx -c Release -warnaserror` | 0 warnings, 0 errors |
| `dotnet test` (Release): unit | 39 passed, 0 failed |
| `dotnet test` (Release): integration, against local SQL Server Express 2019 | 15 passed, 0 failed |
| `ng test --watch=false` | 24 passed, 0 failed (6 files) |
| `ng build` (production) | Succeeded; initial bundle 285.76 kB (warning budget 500 kB) |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages |
| `npm audit --omit=dev --audit-level=high` | 0 vulnerabilities |
| GitHub Actions run | Passed on `develop` (runs 40508109510, 40513060976) and on PR #2 (run 40514984634) |

### Manual browser verification

Done against a scratch database (`MelamuHoldings_Scratch`) holding 20 sample trades, each marked `SAMPLE DATA`:

- **Dashboard:** KPIs match hand calculation (total P&L +1,585.00; 10W/9L/1B) and the charts render.
- **Trades:** the RR column shows values.
- **Behaviour:** flags the planted revenge trade, overtrading day and three-loss streak.
- **Upload:** history lists the import.
- **API stopped:** the dashboard shows "The API could not be reached" with Retry, and Retry recovers once the API is back.
- **Sign out:** clears the session and shows the login page without the sidebar.
- **Signed-out access:** `/trades` redirects to `/login`.

## Known gaps

- There are no end-to-end browser tests yet. The §8 scenario is automated at the API level and was walked through by hand in the browser. The manual walkthrough above stands in until the Phase 5 financial workflow gets an automated end-to-end test.
- Profit factor with zero losses is pinned at its current (incorrect) value by `Profit_factor_with_no_losses_currently_returns_gross_profit` until Phase 8.
