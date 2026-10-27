# Trading Journal — Design

The journal makes manual trade journaling a first-class workflow: a **New trade** action, drafts, configurable templates, strategy checklists, post-trade review, and optional AI-assisted reflection grounded in recorded evidence. CSV import stays, as a bulk and migration path.

This document is phase J1 of [`../PLAN.md`](../PLAN.md) §7a. It records:
- the audit of what exists;
- the decisions for every journal phase;
- the exact scope of J2, which is built alongside it.

Later sections describe J3–J7 as designed, not built.

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
5. **Bulk history:** CSV import creates closed trades. In J5 an import offers to link to a matching manual trade instead of duplicating it.

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
- Archiving instead of deleting comes with J5, when the journal view needs it.
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

**J3–J4 (designed):**
- **`JournalTemplate`:** id, owner, name, description, `IsDefault`, `Status` (Active/Archived), current version.
- **`JournalTemplateVersion`:** id, template id, version number, created at, and a `Fields` JSON document. Versions are immutable.
- **`TradeJournalEntry`:** trade id, template version id, `Answers` JSON keyed by field id, completed at, and change history (J4).
- **Strategy (J4):** gains `UserId` (backfilled), rules (text, mandatory or optional, order), status, and a default template.
- **`ChecklistResponse` (J4):** trade id, rule id, answer (met / not met / not applicable), answered at. A completed checklist keeps every later change as a new row, never an overwrite.

## 5. Template architecture (J3)

- **A validated schema, not a form builder.** A template version is a list of sections, each holding fields. A field has an id (stable across versions), label, type, required flag, help text and options.
- **Supported types:** short text, long text, number, date/time, single select, multi select, checkbox, rating (1–5), URL, attachment, and the structured price and risk block (which maps to the `Trade` columns above).
- **The core execution fields stay columns on `Trade`,** not template answers. That way analytics never depend on how a template is configured. Templates decide *which* of them are shown and add their own fields around them.
- **Versioning:**
  - Editing a template creates a new version.
  - An entry stores the id of the version it was written against, and renders with that version's definitions.
  - A field removed in a later version still shows on old entries, labelled from the old definition.
  - Nothing is reinterpreted or lost.
- **Default template:** seeded per user on first use. It contains the brief's sections A–G (identification, context and setup, pre-trade checklist, execution, psychology, post-trade review, review and learning), and every one of them is editable.

## 6. Strategy and checklist (J4)

- **Mandatory rules versus observations.** Only an unmet **mandatory** rule is a violation. Optional items are informational, and an unchecked box is never treated as a breach.
- **Adherence rate** = trades with every mandatory rule met ÷ trades with a completed checklist. The denominator is always shown, and trades without a checklist are excluded and counted separately.
- **Reporting** compares compliant and non-compliant results with their sample sizes. Under 20 trades in a group, the comparison carries an "insufficient data" label. Correlation is never presented as cause.

## 7. Journal interface (J5)

- **Journal overview:** drafts, open trades, closed but not reviewed, recent activity, and a New trade button.
- **Trade log:** search, filters (strategy, account, session, outcome, status), sorting and paging.
- **Calendar:** a month grid, with each day showing its trade count and net P&L. A day with no trades is shown differently from a day that only has drafts.
- **Journal entry:** a readable document with progressive disclosure, attachments (type- and size-checked, stored outside any public directory, served only to the owner), and the review section.

## 8. AI assistant (J7)

- **A provider abstraction in Application,** with an OpenAI-compatible HTTP implementation in Infrastructure. Configuration covers base URL, model and key, all optional. With no configuration, every AI control is hidden and the journal works unchanged.
- **Bounded context:** the selected trade, its template answers, its strategy rules, and at most N recent comparable trades (by strategy and session), each with an id and date. Never the whole database.
- **Prompt safety:**
  - Journal text is passed as quoted data under a system instruction that says so.
  - The model has no tools and no data access beyond what the request contains.
- **Answer format:** recorded facts (with trade ids), calculated metrics (computed by the app, not the model), and interpretations, in separate labelled parts. "Not enough data" is an expected answer, not a failure.
- **Drafted reviews are suggestions.** The trader edits and confirms before anything is saved, and a draft never overwrites the trader's text.
- **Boundaries:** no diagnosis of mental-health conditions, no trading signals, no autonomous actions. Behaviour patterns are described as recorded evidence with stated uncertainty.

## 9. Data integrity and security

- Trades, templates, entries and attachments are scoped to their owner. Another user's id returns 404, as trades do today.
- Execution values are decimals, and the currency is recorded where it is known.
- Journal content and AI prompts are not written to logs.
- No trading record writes to the financial ledger. A link between a payout and its trading account (Phase 8) will be an explicit, visible reference.

## 10. Implementation phases and testing

| Phase | Scope | Tests |
|---|---|---|
| **J1** ✅ | This document | — |
| **J2** | Statuses, nullable execution fields, new columns, migration; status-aware create and update; review action; status filter; idempotent create; analytics count completed trades only; New trade, journal entry and edit pages; status filter on the trade log | Unit: per-status validation, R and duration calculation, completed-trade projection. Integration: a draft has missing fields and is excluded from analytics; closing it counts it; review needs a closed trade; the idempotent create; isolation still holds; existing tests unchanged. Angular: form per status, draft save, idempotency key, entry page. |
| J3 | Templates and versions, default template, rendering answers | Schema validation; editing a template leaves old entries intact |
| J4 | Strategy rules, checklists, adherence | Mandatory versus optional; change history; denominators |
| J5 | Journal overview, calendar, attachments, import reconciliation | Attachment type, size and access; no duplicate on import |
| J6 | Analytics filters (adherence, emotion, rating), sample sizes | One calculation path; filters only narrow it |
| J7 | AI assistant | Disabled mode; enough versus insufficient context; draft never auto-saved; prompt-injection text treated as data |
| J8 | Verification and documentation | Full regression; browser walkthrough |
