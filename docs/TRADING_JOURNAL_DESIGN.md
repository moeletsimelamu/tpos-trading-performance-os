# Trading Journal — Design

The journal makes manual trade journaling a first-class workflow: a **New trade** action, drafts, configurable templates, strategy checklists, post-trade review, and optional AI-assisted reflection grounded in recorded evidence. CSV import stays, as a bulk and migration path.

This document is phase J1 of [`../PLAN.md`](../PLAN.md) §7a. It records:
- the audit of what exists;
- the decisions for every journal phase;
- the exact scope of J2, which is built alongside it.

J1 to J8 are built (2026-10-29). What the brief asks for and what is built are compared in [`JOURNAL_REVIEW.md`](JOURNAL_REVIEW.md), with the remaining gaps.

## 1. Audit of the existing trading module (2026-10-28)

| Area | What exists | Consequence for the journal |
|---|---|---|
| **`Trade`** (`Domain/Trading/Trade.cs`) | Anemic entity, user-owned. Holds date, entry time, instrument, direction, entry and exit price, risk, P&L, R, session, day of week, duration (minutes), override flag, deviation notes and notes. Every execution field is non-nullable. | Cannot represent a draft or an open trade without inventing values. Extend it: add a status and make execution fields nullable. |
| **`Strategy`** | A global name and description, created on the fly from CSV or `StrategyName`. No owner and no rules. | Fine for J2. J4 gives strategies an owner, rules and checklists (the `Strategies.UserId` backfill planned in PLAN §4). |
| **`UploadLog`** | One row per CSV import; trades link to it. | `UploadLogId` already tells imported trades from manual ones; no extra "source" column is needed. |
| **`TradeService` + `TradesController`** | CRUD under `/api/trades`, scoped to the caller. `CreateTradeDto` validates a *closed* trade only. | Keep the routes and the closed-trade contract for compatibility. Add status-aware validation, a review action and status filtering. |
| **`AnalyticsService`, `BehaviourAnalyticsService`** | Read every trade of the user and compute every figure. | They must count **completed** trades only (closed or reviewed); otherwise a draft would move the numbers. One shared filter, no second calculation path. |
| **CSV import** (`UploadService`) | Creates closed trades with every field set. | Unchanged. Imported trades are `Closed`. |
| **Frontend** | Trades table (search, filters, two-step delete), Import, Performance, Behaviour. There is no trade form at all. | Add the New trade flow, a journal entry page and status filters. |
| **AI** | None in this repository. DocuMind has an OpenAI-compatible client pattern. | J7 adds a provider abstraction here. The journal never depends on it. |
| **Tests** | Unit tests for analytics, behaviour and CSV; integration tests for trades (validation, isolation, CSV round trip). | Extend; the existing analytics characterisation tests guard against regressions. |

## 2. Workflows

1. **Before or at entry:** New trade → choose the template (J3) → fill what is known → **Save draft**, or **Save as open** once the position is live.
2. **During the trade:** reopen the entry; add the stop, target and notes.
3. **After exit:** record the exit, P&L and fees → **Save as closed**. It now counts in analytics.
4. **Review:** read the entry, write the post-trade review → **Mark reviewed**. In J3–J4 the template's review section and checklist answers become part of this step.
5. **Bulk history:** CSV import creates closed trades. A row already imported is skipped, and a row matching a trade journaled by hand waits for the trader to link it, import it separately or leave it out (J5, §7).

## 3. Trade lifecycle (J2)

```
Draft ──▶ Open ──▶ Closed ──▶ Reviewed
  └────────────────▶ Closed
```

| Status | Meaning | Required to save | Counts in analytics |
|---|---|---|---|
| `Draft` | Being written; anything may be missing | Date and instrument | No |
| `Open` | Position entered, not yet exited | Draft fields + direction, entry time, entry price | No |
| `Closed` | Exited | Open fields + exit price and realised P&L | **Yes** |
| `Reviewed` | Closed, and the trader has written the post-trade review | Closed fields + review notes | **Yes** |

**Rules**
- **Nothing is invented.** Missing execution data stays empty (`null`), never `0`.
- **Calculated, not invented:**
  - **R-multiple:** when the trader records a risk amount and P&L but no R, R = P&L ÷ risk, to 2 places. That is the same definition the imported journals use.
  - **Duration:** calculated from entry and exit time when both are known.
  - **Day of week:** taken from the trade date.
- **Edits keep the status.** A status can move back (for example Closed → Open), but only with the fields that status needs. Marking reviewed is a separate action that requires a closed trade.
- **Imported trades are `Closed`.** Existing rows are migrated to `Closed` and analytics are unchanged.
- **P&L is the net realised P&L** as the platform reports it, which is the same meaning as imports. Fees are recorded separately for information and are **not** subtracted again.
- **Currency.** A manual trade can state the currency of its P&L, risk and fees. Imports leave it empty, because the CSV has no currency column. Analytics still add P&L across trades as before; the known gap stays documented until prop-firm accounts (Phase 8) give every trade an account currency.

**Deletion policy:**
- Any trade can be deleted with a two-step confirmation, as today.
- A draft is the normal thing to delete.
- Archiving trades was considered for J5 and not built: the journal page lists what needs finishing, so nothing needed hiding. Deletion with confirmation stays the only way to remove a trade.
- Trading records are not financial records. A trade never posts to the ledger, so deleting one cannot change a balance.

**Duplicate protection:** `POST /api/trades` accepts an `Idempotency-Key`. A unique (user, key) index means a retried save returns the trade already created instead of a second copy.

## 4. Domain model changes

**J2 (built now): extend `Trade`; no parallel journal-entry model.**

| Column | Change |
|---|---|
| `Status` | New, `Draft`/`Open`/`Closed`/`Reviewed`, stored as text, default `Closed` |
| `Direction`, `EntryTime`, `EntryPrice`, `ExitPrice`, `RiskAmount`, `PnL`, `RR`, `TradeDuration` | Become nullable. Existing values are kept. |
| `StopLoss`, `TakeProfit` | New, `decimal(18,5)`, optional |
| `PositionSize` | New, `decimal(18,4)`, optional |
| `Fees` | New, `decimal(18,2)`, optional, informational |
| `ExitDate`, `ExitTime` | New, optional (a trade may span midnight) |
| `Currency` | New, `char(3)`, optional |
| `TradingAccount` | New, free text, optional, until Phase 8 adds prop-firm accounts |
| `ReviewNotes`, `ReviewedAt` | New: the trader's own post-trade review |
| `UpdatedAt` | New |
| `IdempotencyKey` | New, unique per user when present |

Analytics read completed trades through one projection (`CompletedTrade`). That projection is the only place that decides what counts. Average R ignores trades without an R; all imported trades have one, so current figures do not change.

**J3 (built):**
- **`JournalTemplate`:** id, owner, name, description, `IsDefault`, `Status` (Active/Archived), current version. A filtered unique index allows one default per owner.
- **`JournalTemplateVersion`:** id, template id, version number, created at, and a `Schema` JSON document (sections and fields). Versions are immutable; (template, version) is unique.
- **`TradeJournalEntry`:** one per trade (unique), template version id, `Answers` JSON keyed by field id, created and updated times. Deleted with its trade; a version in use cannot be deleted.

**J4 (built):**
- **`Strategy`:** gains `UserId` (required; unique name per trader, case-insensitive), `Status` (Active/Archived) and `UpdatedAt`. Name is limited to 100 characters and description to 500. The migration backfill:
  - gives each strategy to the lowest-numbered trader whose trades use it;
  - gives every other trader using it their own copy, with their trades moved to it;
  - removes strategies that no trade uses (they held only a name);
  - trims names and merges any that then repeat for one trader.
- **`StrategyRule`:** text (200), mandatory flag, order, created and retired times. A rule left out when the strategy is saved is retired, never deleted, and can be restored.
- **`TradeChecklist`:** one per trade and strategy. It is created on the first answer and copies the strategy's active rules as **`ChecklistItem`** rows (rule id, text and mandatory flag as they were). Later rule edits never change it.
- **`ChecklistResponse`:** item, sequence, answer (Met / NotMet / NotApplicable), answered at. Answers are appended, never overwritten: the latest per item counts, and every earlier one is history. A unique (item, sequence) index stops two requests writing the same change.
- **Not built:** a strategy's default journal template (deferred; it does not affect adherence). Change history for template answers was added after J8 (§8a).

## 5. Template architecture (J3, built)

- **A validated schema, not a form builder.** A template version is a list of sections, each holding fields. A field has an id (stable across versions), label, type, required flag, help text and options.
- **Supported types (built):** short text, long text, number, date/time, single select, multi select, checkbox (yes/no), checklist (a list of items, each ticked or not), rating (1–5) and URL. Ids are lowercase letters, digits and hyphens. Select, multi-select and checklist fields need options.
- **Not template fields:** attachments are kept with the trade, not as answers (J5, §7); the price and risk block is the fixed part of the trade form (the `Trade` columns), not a field type.
- **The core execution fields stay columns on `Trade`,** not template answers. That way analytics never depend on how a template is configured. Templates decide *which* of them are shown and add their own fields around them.
- **Versioning:**
  - Editing a template creates a new version.
  - An entry stores the id of the version it was written against, and renders with that version's definitions.
  - A field removed in a later version still shows on old entries, labelled from the old definition.
  - Nothing is reinterpreted or lost.
  - Renaming a template or changing its description does not create a version; changing sections or fields does.
  - Moving an entry to a newer version is the trader's choice, on the edit form. Answers move where the field id still exists with the same type; the others are named on screen before saving, and the old version keeps them.
- **Answers:** validated against the version's field types and options; empty answers are dropped rather than stored. Required questions are not needed to save a draft, open or closed trade, but must be answered before the trade can be marked reviewed.
- **Archived templates** are hidden from new trades; entries written with them keep rendering. The default template cannot be archived, and an archived template cannot be made default.
- **Default template:** "Standard trade journal", seeded per user the first time templates are listed if the user has no default. Identification and execution (the brief's A and D) are the trade form's own sections, so the template holds the rest: market context and setup, a seven-item pre-trade checklist, psychology and behaviour, post-trade review ("Was the trade valid according to the strategy?" required) and review and learning ("Key lesson" required). Every part is editable, and an edit creates version 2.

## 6. Strategy and checklist (J4, built)

- **Mandatory rules versus observations.** Only an unmet **mandatory** rule is a violation. Optional items are informational, and an unchecked box is never treated as a breach.
- **Adherence rate** = trades with every mandatory rule met ÷ trades with a completed checklist. The denominator is always shown, and trades without a checklist are excluded and counted separately.
- **Reporting** compares compliant and non-compliant results with their sample sizes. Under 20 trades in a group, the comparison carries an "insufficient data" label. Correlation is never presented as cause.
- **As built:**
  - **Not applicable** is allowed only on optional rules; a mandatory rule was either met or not.
  - **Which trades count:** adherence counts closed and reviewed trades only, the same set as every other figure. A trade is scored on the checklist of its current strategy. Trades with no checklist and trades with an unfinished one are counted separately, and never in the rate.
  - **Changing a trade's strategy:** the trade gets a new checklist; the earlier one is kept but no longer counts.
  - **The report (`GET /api/analytics/adherence?strategyId=`)** gives the rate with its denominator, and for each group its count, win rate, net P&L and average R. It also lists the five mandatory rules most often not met, with how many times each was answered, and a per-strategy breakdown.
  - **Where to find it:** the Strategies page shows the overall report; each strategy's page shows its own.

## 7. Journal interface (J5, built)

- **Journal page (`/trading/journal`):**
  - **Counts:** drafts, open, awaiting review (closed, not reviewed) and reviewed; each links to the trade log filtered by that status.
  - **Lists:** the eight latest of each, plus recent activity.
  - **Import reminder:** a notice when imported rows are waiting for a decision.
  - **Data:** `GET /api/journal/overview`.
- **Calendar:** a month grid, Monday first (`GET /api/journal/calendar?year=&month=`).
  - **Totals:** each day shows the number of completed trades and net P&L, counted from closed and reviewed trades through `CompletedTrade`, the same projection analytics use.
  - **Unfinished trades:** drafts and open trades are counted beside the totals and never in them. A day with only drafts or open trades is dashed; a day with no trades is plain.
  - **Links:** each day with trades opens the trade log for that date.
  - **Phone width:** cells show a compact amount (+100, +1.2k).
- **Trade log:**
  - **Filters:** search; status tabs; direction, strategy, session, account and result filters; and a date taken from the calendar link.
  - **Sorting:** by date, instrument, P&L or R (`aria-sort`); trades without the value (a draft has no P&L) sort last.
  - **Paging:** 25 per page.
  - **Scope:** filtering, sorting and paging run in the browser over the trader's trades. That is enough for one trader's journal; server paging can follow if it grows large.
- **Journal entry:** the review section, the strategy checklist, and template answers. Unanswered questions are folded behind "Show N unanswered questions".
- **Attachments (`/api/trades/{id}/attachments`):**
  - **Accepted:** PNG, JPEG, WebP and PDF, recognised by their first bytes. The file name and the browser's content type are ignored, and SVG, HTML and anything else are refused.
  - **Limits:** 5 MB and 10 files per trade.
  - **Storage:** files are stored under random names in `Attachments:Path` (default `App_Data/attachments` under the API's content root, outside any web root, ignored by git). The shown name is cleaned of folders and unsafe characters and takes the detected extension.
  - **Serving:** only to the trade's owner (another trader gets 404, an anonymous caller 401), with `X-Content-Type-Options: nosniff` and `Cache-Control: private, no-store`. Images are shown inline and PDFs are downloaded.
  - **Deletion:** deleting a trade removes its files.
  - **In the browser:** files are fetched with the session token, so thumbnails use object URLs that are released when the page closes.
- **Import reconciliation:**
  - **Already imported:** a CSV row identical to an already imported trade (date, instrument, direction, entry time, entry and exit price, P&L), or to a row already decided on, is skipped and counted.
  - **Matches a journaled trade:** a row on the same day and instrument as a trade journaled by hand is held back as an `ImportMatch`, as long as nothing the trader recorded disagrees on direction, entry time (to the minute) or entry price, and the trade has at least an entry time or entry price.
  - **The trader decides:**
    - **Link:** fills only the trade's empty fields (direction, times, prices, risk, P&L, R, duration, session, strategy). It never changes notes, status or recorded values, and a trade can be linked once.
    - **Import separately:** creates the trade as a normal import.
    - **Don't import.**
  - **Safeguards:**
    - **Concurrency:** a `rowversion` makes concurrent decisions on one row fail cleanly with a 409, so only one trade is created.
    - **Deleted trade:** deleting the journaled trade keeps the row (the link becomes empty), and it can still be imported or left out.

## 7a. Analytics filters (J6, built)

- **One calculation path.**
  - **Where trades come from:** `AnalyticsScope` is the only place that decides which of a trader's trades analytics look at. Summary, equity curve, the strategy, day and session breakdowns, behaviour and adherence all read their trades from it.
  - **What a filter does:** it only removes trades. `CompletedTrade` then decides which count, and the calculations are unchanged.
  - **How the tests check it:** a filtered result must equal the same calculation over a journal that holds only the kept trades, and a strategy filter must equal that strategy's breakdown row.
- **Filters** (the same query parameters on every `/api/analytics/*` endpoint):
  - **Dates:** `from` and `to` (trade date).
  - **Strategy:** `strategyId`.
  - **Instrument:** `instrument` (added in J8), compared case-insensitively.
  - **Session and account:** `session` and `account`, compared case-insensitively.
  - **Rule adherence:** `adherence`, taken from the checklist of the trade's current strategy. `Followed` means a complete checklist with every mandatory rule met; `Broke` means a complete checklist with one not met; `NotScored` means no checklist, or an unfinished one.
  - **Journal answers:** `answerField` + `answerValue`. "Emotion" and "rating" are questions in the trader's own templates, so they are not fixed fields. Any question with fixed answers can be filtered on, matched by its field id (stable across template versions):
    - **Choice:** equals the value (case-insensitive).
    - **Several choices:** contains it.
    - **Checklist:** that item is ticked.
    - **Rating:** equals the number.
    - **Yes / no:** equals true or false.
    - **Missing answers:** trades without an answer to that question are left out, never guessed.
- **Validation:** `from` after `to`, a question without an answer (or the reverse), or an unknown adherence value is refused with 400.
- **Options (`GET /api/analytics/filter-options`):**
  - **Strategies:** the trader's strategies, archived ones included.
  - **Sessions and accounts:** taken from closed trades.
  - **Questions:** every question with fixed answers from any version of any template. Each keeps its latest wording, and options from all versions are offered.
- **Sample sizes:**
  - **Small samples:** the summary carries `insufficientData` (fewer than 20 trades, the same threshold as the adherence comparison), and the page says so.
  - **Rule adherence on Performance:** the page shows the compliant versus non-compliant comparison for the same filters, with each group's count.
- **In the app:**
  - **Pages:** Performance and Behaviour share one filter bar.
  - **The address:** the filter is kept in the address (the same names as the API), so a filtered view survives a reload and can be bookmarked; malformed values are ignored.
  - **Journal answers:** a question applies only once an answer is chosen.
  - **Phone width:** the filters fold behind "Filters (n applied)", so the figures come first.
  - **Behaviour:** the page says patterns are looked for only among the filtered trades.

## 8. AI assistant (J7, built)

- **A provider abstraction in Application,** with an OpenAI-compatible HTTP implementation in Infrastructure. Configuration covers base URL, model and key, all optional. With no configuration, every AI control is hidden and the journal works unchanged.
- **Bounded context:** the selected trade, its template answers, its strategy rules, and at most N recent comparable trades (by strategy and session), each with an id and date. Never the whole database.
- **Prompt safety:**
  - Journal text is passed as quoted data under a system instruction that says so.
  - The model has no tools and no data access beyond what the request contains.
- **Answer format:** recorded facts (with trade ids), calculated metrics (computed by the app, not the model), and interpretations, in separate labelled parts. "Not enough data" is an expected answer, not a failure.
- **Drafted reviews are suggestions.** The trader edits and confirms before anything is saved, and a draft never overwrites the trader's text.
- **Boundaries:** no diagnosis of mental-health conditions, no trading signals, no autonomous actions. Behaviour patterns are described as recorded evidence with stated uncertainty.

**As built:**
- **Configuration (`AiReview`):**
  - **Required to switch it on:** `BaseUrl` and `Model`. Anything OpenAI-compatible works: a hosted provider, or a local server such as Ollama or LM Studio.
  - **Optional:** `ApiKey` (from user-secrets or `AiReview__ApiKey`, never appsettings; it is never returned by the API); `TimeoutSeconds` (60); `MaxComparableTrades` (10); `JsonMode` (true, which sends `response_format: json_object`); and `DraftsPerMinute` (6 per user, enforced by a rate limiter after authentication).
  - **Without `BaseUrl` and `Model`:** `GET /api/ai-review/status` says it is off, the trade page shows no AI control, and `POST /api/trades/{id}/ai-review` returns 503.
- **Which trades:** drafting needs a closed or reviewed trade (otherwise 422) belonging to the caller (otherwise 404). The panel is offered only while a closed trade's review is being written.
- **Bounded context:**
  - **Sent:** the trade's own fields and notes; its journal answers, with the question wording of their version; its checklist (rule wording, mandatory flag, answer); at most `MaxComparableTrades` recent completed trades with the same strategy and session (id, date, P&L and R only); and the app's metrics.
  - **Not sent:** anything else, including other traders' records.
- **Not enough data:** with no notes, override reason, journal answers or checklist answers, the model is **not called**. The reply is "not enough data", saying what is missing.
- **Prompt safety:**
  - **Data block:** the context is JSON inside `<journal_data>`…`</journal_data>`. JSON encoding escapes `<` and `>`, so text in a note cannot close the block.
  - **System prompt:** it says the block is data, never instructions; that the model has no tools and cannot change records; and that there are no signals, no diagnoses, no own calculations, and no patterns from fewer than 5 comparable trades.
  - **The call:** one system and one user message, with no tools.
- **The reply is checked:**
  - **Facts:** a fact must cite only trade ids that were sent, or it is dropped with a warning; at most 10 facts and 6 interpretations.
  - **Lengths:** the suggested review is cut to the review's 4,000-character limit.
  - **Figures:** they always come from the app, never from the model. They are formatted invariantly, because this machine's comma decimals had produced "2,50R".
  - **Unusable replies:** an unreadable reply or a provider failure is a 502 with a plain message, and nothing is saved.
- **Nothing is saved by the assistant.**
  - **Using a draft:** "Use as my review" fills an empty review box; with text already there it becomes "Add below my notes". The trader then edits and saves through the existing **Mark reviewed** action.
  - **On the page:** facts (with links to each trade), "Calculated by the app" figures and an "AI interpretation · may be wrong" part are shown separately, with the model's name.
- **Privacy:**
  - **Leaving the machine:** with a hosted provider, the context above leaves the machine. With a local server it does not.
  - **Logs:** prompts and replies are not logged; HttpClient logs only the URL and timing.
  - **Error messages:** provider error bodies are never echoed.
- **Verified against a stub, not a real model:** the browser check used a local stub server that answers in the same protocol (built from the data it receives). It shows the wiring, the escaping and the boundaries work, not the quality of a real model's drafts.

## 8a. Change history (built after J8, 2026-10-29)

The brief asks that a completed checklist is not changed without a record and that corrections keep audit information. The strategy checklist already kept every answer (J4); every other part of a trade was overwritten on save. Now:

- **`TradeRevision`:** one append-only row per save that changed something: trade, time, kind, the trade's status after the change, and each changed value with its label, before and after (JSON). There is no update path. Revisions are deleted only with their trade, so deleting a trade remains permanent (§3).
- **What is recorded:**
  - **Created:** a trade journaled by hand.
  - **Edited:** every changed trade field, journal answer (and a move to another template version) and strategy checklist answer, in one revision per save.
  - **Reviewed:** the review text, and the status the first time. A later correction keeps the earlier wording.
  - **Filled in from a CSV import:** the empty fields a link filled.
  - **File added or removed:** the file name.
- **Written with the change:** each service stages the revision and it is saved in the same `SaveChanges` as the change, so a change cannot be saved without its record. A save that changes nothing records nothing.
- **Values as the trader sees them:** invariant culture, numbers without trailing zeros (so 21100 and 21100.00000 are the same value), yes/no for checkboxes, lists joined, rule answers as Met / Not met / Not applicable. Empty is null.
- **On the trade page:** a History section, folded and loaded only when opened, lists revisions newest first as What / Before / After. Trades saved before the history existed (and CSV imports, which have no creation record) say that earlier changes were not recorded.
- **Not recorded:** the creation of CSV-imported trades (the upload log has it), and changes made before the migration.

## 8b. Questions about the journal (built after J8, 2026-10-29)

The brief asks for questions across entries ("Which rules have I struggled to follow?", "Summarise my last trading session", "Compare my recent rule adherence with my previous period") on the journal and performance screens, not in an isolated chatbot.

- **Where:** an "Ask about these trades" panel at the foot of Performance and Behaviour, shown only when AI review is on. It uses the page's filter, so the question covers exactly the trades in view, and a new filter clears an old answer. Suggested questions from the brief fill the box; nothing is asked until the trader presses Ask.
- **Endpoint:** `POST /api/ai-review/ask` with `{ question }` (3–500 characters) and the analytics filter in the query string. It shares the draft's rate limit, is 503 when AI is off, and saves nothing.
- **Bounded context (`AiReview:MaxJournalTrades`, default 30):**
  - **Sent:** the question, and the most recent closed trades in view (oldest first): date, day, entry time, instrument, direction, session, strategy, account, P&L, R, duration, status, override and reason, and notes and review (each cut to 300 characters). Each trade also carries its journal answers by question wording (cut to 200) and its checklist result with the mandatory rules not met.
  - **Not sent:** drafts and open trades, trades outside the filter, other traders' records, attachments, and anything older than the trades sent.
- **The app's figures, shown under "Calculated by the app":**
  - **Totals:** trades sent (of how many in view, and their dates), win rate, net P&L (noting mixed or unstated currencies) and average R.
  - **Rules:** rule adherence overall and for winning and losing trades, and the most-missed mandatory rules.
  - **Impulsive-trading evidence the brief names:** the most trades in one day, and trades taken straight after a loss the same day.
  - **Previous period:** adherence, win rate and net P&L for the same number of closed trades before these in the view.
  - **Sample size:** fewer than 20 trades is labelled insufficient data.
- **The model's rules:**
  - **Shared with the draft:** the safety rules are the same constant, so neither prompt can drift.
  - **Answering:** answer only from the trades and figures sent, and say plainly when they cannot answer. Name trade ids and dates.
  - **Patterns:** none from fewer than 5 trades. A pattern is a possibility, stated with how many trades support it.
  - **Revenge trading:** look at recorded emotions, same-day trades after a loss, violations, override reasons and trades per day, explain the evidence and its uncertainty, and never declare it.
- **The reply is checked:** facts must cite trades that were sent, or they are dropped with a warning. The answer is cut to 1,500 characters and the other items to 400; figures are always the app's. With no closed trades in view the model is not called, and the answer is "not enough data".
- **Verified against the same local stub as the draft,** not a real model.

## 9. Data integrity and security

- Trades, templates, entries and attachments are scoped to their owner. Another user's id returns 404, as trades do today.
- Execution values are decimals, and the currency is recorded where it is known.
- Journal content and AI prompts are not written to logs.
- No trading record writes to the financial ledger. A link between a payout and its trading account (Phase 8) will be an explicit, visible reference.

## 10. Implementation phases and testing

| Phase | Scope | Tests |
|---|---|---|
| **J1** ✅ | This document | — |
| **J2** ✅ | Statuses, nullable execution fields, new columns, migration; status-aware create and update; review action; status filter; idempotent create; analytics count completed trades only; New trade, journal entry and edit pages; status filter on the trade log | Unit: per-status validation, R and duration calculation, completed-trade projection. Integration: a draft has missing fields and is excluded from analytics; closing it counts it; review needs a closed trade; the idempotent create; isolation still holds; existing tests unchanged. Angular: form per status, draft save, idempotency key, entry page. |
| **J3** ✅ | Templates and immutable versions; default template; create, edit, duplicate, archive, restore, make default; editor with live preview; answers on the trade form and entry page; moving an entry to the latest version; required answers before review | Unit: default template valid, schema rules, answer types, empty answers dropped, versioning, entries keep their version, default and archive rules. Integration: one default per trader, editing leaves old entries on their version, review refused with required questions unanswered, duplicate/archive/restore/default, invalid templates refused (400), owner isolation. Angular: answer carrying, renderer, editor body and ids, version message, form journal body, entry answers and blocked review. |
| **J4** ✅ | Owned strategies with the backfill; rules (edit in place, retire, restore); checklists that copy the rules and keep every answer; checklist on the trade form and entry page; adherence report on the Strategies pages | Unit: rule editing, retiring and restoring; rule validation; the checklist copy; violations and compliance; appended history; N/A rules; adherence counts, rate and sample flags. Integration: per-trader ownership and names, concurrent first use of a new name, retire and restore, checklist history, rejected answers saving nothing, the adherence report, owner isolation, UTC times. Angular: list and report, editor bodies and messages, form checklist, entry history. The backfill was tested by hand on a throwaway database with shared, unused, duplicate and over-long strategies. |
| **J5** ✅ | Journal page and calendar; trade log filters, sorting and paging; folded journal answers; attachments; import reconciliation | Unit: type detection, file names and size limits, match and duplicate rules, linking fills only empty fields, calendar totals. Integration: calendar and overview, attachment types, size, count limit and access, files removed with the trade, re-import skipped, link / import separately / dismiss, concurrent decisions create one trade. Angular: month grid, calendar cells and links, journal page, log filters, sorting and paging, attachments, import decisions, folded answers. |
| **J6** ✅ | One filter for every analytics figure (dates, strategy, session, account, adherence, journal answers), filter options, sample-size flag, filter bar on Performance and Behaviour, adherence comparison on Performance | Unit: filtered summaries equal the same calculation over only the kept trades; each filter; adherence from the current strategy's checklist; answers by shape and field id; options across versions; validation; sample flag. Integration: dates, session and account narrow every endpoint alike; a strategy filter equals its breakdown row; adherence and answer filters (and adherence under a filter); invalid filters 400; options private. Angular: the address round trip, the filter bar (answer only applies with a question), filters sent with every figure, no-match state and clearing, Behaviour filtered. |
| **J7** ✅ | Optional AI-drafted review: provider abstraction (OpenAI-compatible HTTP), bounded context, not-enough-data path, reply checks, rate limit, panel on the trade page | Integration: off by default (status off, 503); status never shows the key; not enough data without calling the model; only the caller's closed trades; context bounded to N same-strategy-and-session trades; injected text stays escaped data and nothing is saved; facts citing unknown trades dropped; app figures (invariant formatting); 502 on unusable replies; per-user rate limit. Provider: request shape (no tools, JSON mode, Content-Length), no key, failures without echoing bodies. Angular: hidden when off or after review, parts kept apart, box filled only on request and appended below the trader's words, not-enough-data and failure states. |
| **History** ✅ | Change history for trades, journal answers, checklists, import links and files (§8a) | Unit: values compared at any stored precision, invariant formatting, empty as null, journal answers as text. Integration: an edit records each changed field, answer and rule with before and after; an unchanged save records nothing; review and a corrected review; files added and removed; history private (404) and deleted with the trade; an import link records only what it filled. Angular: loaded only when opened, newest first with before and after, the no-creation-record note, reload while open, plain-word titles. |
| **Questions** ✅ | Questions about the trades in view (§8b): endpoint, bounded context, app figures, panel on Performance and Behaviour | Integration: only the most recent closed trades in view are sent (not drafts, other instruments, other traders, or older trades); the app's figures (win rate, net P&L, adherence overall and for wins and losses, most-missed rule, busiest day, trades after a same-day loss, previous period); a fact citing an unsent trade dropped; interpretations and prompts whole; injected text escaped; nothing saved; no call with no closed trades; bad question or filter 400; 503 when off; both prompts share the safety rules. Unit: checklist answers as text. Angular: hidden when off, scope sentence, suggested question, filter sent, parts kept apart, cleared on a new filter. |
| **J8** ✅ | Verification and documentation | Full regression, run three times; package audits; idempotent migration script; the brief traced in `JOURNAL_REVIEW.md`; an end-to-end browser flow across J2–J7; small gaps closed (instrument filter, rule-adherence column, reflection prompts, New trade from a template) |
