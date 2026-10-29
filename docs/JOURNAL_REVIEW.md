# Trading journal: verification review (J8)

The owner's brief *"Trading journal and AI-assisted performance coaching"* (2026-10-27), checked requirement by requirement against what J1–J7 built. Each row says whether it is built, where, and how it was verified. Gaps are listed honestly at the end, with what to do about them.

Status key:
- **Built:** done and verified.
- **Partly:** a working version exists, with the stated gap.
- **Not built:** deliberately deferred.

Verification is recorded in [`TESTING.md`](TESTING.md). Each stage has automated tests and a browser walkthrough, and the J8 end-to-end flow is the last section there. Design decisions are in [`TRADING_JOURNAL_DESIGN.md`](TRADING_JOURNAL_DESIGN.md).

## Regression (2026-10-29)

| Check | Result |
|---|---|
| `dotnet build -c Release -warnaserror` | 0 warnings, 0 errors |
| .NET unit / integration tests | 165 / 91 passed on three consecutive runs of the final code (and three before the J8 fixes) |
| Angular tests (`ng test`) | 147 passed (19 files) |
| Production build (`ng build`) | Succeeded, 308.17 kB initial, no budget warnings |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages in any project |
| `npm audit --omit=dev` | 0 vulnerabilities |
| `dotnet ef migrations has-pending-model-changes` | No changes |
| `dotnet ef migrations script --idempotent` | All 8 migrations, each guarded by a migrations-history check |

## Requirement trace

### 2–3. Audit, and manual journaling first

| Requirement | Status | Where | Verified by |
|---|---|---|---|
| Audit and `TRADING_JOURNAL_DESIGN.md` | Built | J1 | The document |
| Extend `Trade`, no parallel journal model or second analytics path | Built | `Trade` statuses; `CompletedTrade`; `AnalyticsScope` | Analytics characterisation tests unchanged since J2 |
| Obvious New trade action, no import needed | Built | New trade on the Journal page, trade log and templates page | J2 and J8 walkthroughs |
| Drafts, return to a draft, edit | Built | Draft / Open / Closed / Reviewed; the Journal page lists drafts | J2 tests; J8 flow (draft → closed → reviewed) |
| Entry and exit information; never invent missing data | Built | Nullable execution fields; R and duration only from recorded values | J2 unit tests (no risk → no R) |
| Screenshots and supporting files | Built | J5 attachments | J5 tests; J8 flow |
| Pre-trade checklist; post-trade review; mark reviewed | Built | Strategy checklist (J4); template sections (J3); review action | J3, J4 and J8 |
| Notes and **tags** | Partly | Notes, plus fixed-choice multi-select template questions | No free-form tags |
| Search and filter history | Built | Trade log: search, status, direction, strategy, session, account, result, date | J5 tests |
| Archive or delete by a documented policy | Partly | Delete with two-step confirmation (documented); trade archiving deliberately not built | Design doc §3 |
| Draft, open, closed and reviewed distinguished; incomplete never treated as complete | Built | Status tags; analytics count closed and reviewed only | J2 and J6 tests |

### 4. Configurable templates

| Requirement | Status | Where | Verified by |
|---|---|---|---|
| Create, name, describe, edit, duplicate, default, archive / restore, preview | Built | Templates page and editor with live preview | J3 tests and walkthrough |
| New trade from a chosen template | Built | Picker on the form; "New trade" link per active template (J8) | J8 spec and walkthrough |
| Validated schema, no hard-coded fields | Built | `JournalSchema` (sections, fields, ids, options) | J3 unit tests |
| Field types | Partly | Short and long text, number, date-time, single / multi select, checkbox, checklist, rating, URL | Images are trade attachments, not a template field type; price and risk are the trade form's fixed section |
| Versioning without reinterpreting old entries | Built | Immutable versions; entries keep theirs; moving one is explicit | J3 integration tests |
| Default template with the brief's sections, configurable | Built | Standard trade journal (identification and execution are the trade form) | J3 tests |
| Checklist answers with completion time, changes preserved | Built | The strategy checklist keeps every answer with its time (J4); template answers, including checklist questions, are kept in the trade's change history (after J8) | Change-history integration tests |
| Partial exits / multiple executions | Not built | One entry and one exit per trade | Documented limitation |
| Psychology fields configurable; no assumptions from the result | Built | Template questions; the AI prompt forbids result-based assumptions | J7 system prompt test |
| Distinguish a compliant loss from a violation | Built | Rule adherence per trade, independent of P&L | J4 adherence tests |

### 5. Journal interface

| Requirement | Status | Where | Verified by |
|---|---|---|---|
| Overview: recent, awaiting review, drafts, activity by date, quick New trade | Built | Journal page | J5 tests and walkthrough |
| Overview filters (strategy, account, session, outcome, review status) | Built | Counts link to the trade log, which has these filters | J5 |
| Trade log columns incl. rule adherence and review status | Built | Rule adherence column added in J8 | J8 integration and spec |
| Sorting and pagination | Built | Sorting by date, instrument, P&L and R; 25 per page, in the browser | J5 spec |
| Calendar; empty days distinct from incomplete ones | Built | Dashed days with only drafts or open trades | J5 tests |
| Readable trade document, progressive disclosure, AI where available | Built | Trade page; unanswered questions folded | J5 and J7 |
| Attachments: type and size checks, private storage, fallback preview | Built | Content-sniffed types, 5 MB, outside the web root, owner only, icon fallback | J5 tests |

### 6. Strategy and rules

| Requirement | Status | Where | Verified by |
|---|---|---|---|
| Structured strategy referenced by trades; active / archived | Built | Owned strategies (J4) | J4 tests |
| Setup, entry, exit, risk rules; checklist items | Built | Each rule is setup, entry, exit, risk or general; checklists copy the category and are grouped by it (after J8) | Strategy scope unit, integration and Angular tests |
| Relevant instruments or sessions | Built | Optional instruments and sessions per strategy; the trade form notes a trade outside them, without refusing it (after J8) | Strategy scope tests; walkthrough |
| Default template and checklist per strategy | Built | The checklist is the strategy's own rules; a default journal template (one of the trader's active ones) starts new trades on that strategy (after J8) | Strategy scope tests; walkthrough |
| Mandatory versus optional; unchecked is not a violation | Built | N/A only for optional rules | J4 tests |
| Violation context preserved | Built | Checklist items copy the rule wording; history kept | J4 tests |
| Adherence: counts, rate with denominator, compliant vs non-compliant, most missed | Built | Adherence report; Performance and Strategies pages | J4 and J6 tests |
| Repeated execution errors over time / adherence trends | Partly | Adherence by month or week on Performance; most-missed rules for any period through the date filter; recorded mistakes by the journal-answer breakdown (after J8). No line per rule over time. | Breakdown unit, integration and Angular tests |

### 7. AI assistant

| Requirement | Status | Where | Verified by |
|---|---|---|---|
| Optional; the journal works without it; no key needed for core features | Built | Off unless `AiReview` is configured | J7 disabled-mode test |
| Reuse the provider style; OpenAI-compatible, configurable | Built | `IAiReviewProvider` with an OpenAI-compatible HTTP client | J7 provider tests |
| Bounded context; no claim of unseen data | Built | Trade, answers, checklist, N comparable trades | J7 integration (context contents) |
| Facts, calculated metrics and interpretations kept separate | Built | Three parts; figures always from the app | J7 tests |
| Draft review: setup, outcome, compliance, deviations, questions, prompts | Built | Facts, figures, interpretations, and reflection prompts (J8) | J8 integration and spec |
| Confirmation before saving; never overwrite the trader's words | Built | The draft fills an empty box or is added below; saved only via Mark reviewed | J7 and J8 flows |
| Insufficient data is an answer | Built | Model not called without the trader's own evidence | J7 test |
| No diagnosis, no signals, no autonomous actions; encourage a pause, never revenge trading or more risk | Built | System prompt (the last two rules added in J8); no tools | J7 and J8 tests |
| Untrusted journal text cannot steer the model | Built | Escaped JSON data block | J7 test and stub walkthrough |
| Journal-wide questions (patterns, sessions, rule trends) and AI on performance screens | Built | "Ask about these trades" on Performance and Behaviour, scoped by the page's filter (after J8) | Journal-question integration and Angular tests; stub walkthrough |
| Real model quality | Not verified | Checked against a local stub server only | — |

### 8–9. Analytics and import

| Requirement | Status | Where | Verified by |
|---|---|---|---|
| One calculation path; manual and imported trades counted once | Built | `CompletedTrade` and `AnalyticsScope` | J6 "filters only narrow" tests |
| Filters: date, account, instrument, session, strategy, adherence, emotion, rating, mistakes | Built | J6, with instrument added in J8; emotion, rating and mistakes as journal-answer filters | J6 and J8 tests |
| Outcomes by emotion, rating vs outcome, most frequent mistakes, adherence trends | Built | "Journal answer" breakdown on Performance (any question with fixed answers, with a not-answered row and a Show drill-down) and adherence by month or week (after J8) | Breakdown unit, integration and Angular tests |
| Sample sizes and missing data communicated | Built | Under-20 labels; "not scored" counts | J4 and J6 |
| CSV import optional; reconciliation without duplicates or overwritten notes | Built | Skip duplicates; match → link / import / dismiss | J5 tests; J8 flow |

### 10. Integrity and security

| Requirement | Status | Where |
|---|---|---|
| Ownership on trades, templates, strategies, attachments, matches, AI | Built | Every lookup scoped to the user; 404 for others (tests per stage) |
| Validation, constraints, transactions, no duplicates | Built | Unique indexes, rowversions, idempotency keys, concurrency tests |
| Decimal arithmetic; currency preserved | Built | Decimal everywhere; trade currency stored when stated (imports have none) |
| No silent rewriting of records or reflections; audit of corrections | Built | Every saved change to a trade, its journal answers, checklist, review, import links and files is kept with before and after values (`TradeRevision`, after J8). Changes made before 2026-10-29 were not recorded. |
| No sensitive content or secrets in logs | Built | Prompts and replies not logged; provider error bodies not echoed; key never returned |

## Gaps and recommendations

1. **Change history for trade edits and journal answers.** *Closed after J8 (2026-10-29).* Every save that changes a trade records an append-only revision with each value before and after, shown in a History section on the trade page (`TRADING_JOURNAL_DESIGN.md` §8a). Changes made before then were not recorded.
2. **Journal-wide AI questions.** *Closed after J8 (2026-10-29).* Performance and Behaviour have "Ask about these trades": a question about the most recent closed trades matching the page's filter, answered from their notes, journal answers and checklists, with the app's figures (including the previous period and same-day trades after a loss) shown apart (`TRADING_JOURNAL_DESIGN.md` §8b). Like the draft, it was checked against a stub, not a real model.
3. **Outcome analysis by journal answer, and trends.** *Closed after J8 (2026-10-29).* Performance breaks results down by any journal question with fixed answers (emotion, rating, mistakes, checklist items), with a "Not answered" row and Show to narrow the page to an answer, and shows rule adherence by month or week with empty periods kept (`TRADING_JOURNAL_DESIGN.md` §7b). Not built: a line per rule over time.
4. **Richer strategies.** *Closed after J8 (2026-10-29).* Rules have categories (setup, entry, exit, risk, general), a strategy can list the instruments and sessions it is for, and it can name a default journal template (`TRADING_JOURNAL_DESIGN.md` §6).
5. **Smaller limitations, documented and not planned yet:**
   - **Not supported:** partial exits and multiple executions per trade; free-form tags; archiving trades.
   - **Paging:** the trade log pages in the browser rather than on the server.
   - **AI context:** attachments are not described to the model.
   - **AI quality:** never tested against a real model.

## Found and fixed during J8

- **Instrument filter:** analytics had no instrument filter (brief §8). Added to every analytics endpoint, the filter options and the filter bar.
- **Trade log:** it had no rule-adherence column (brief §5). Each listed trade now carries `ruleAdherence` (Followed, Broke, Unfinished), read in one query.
- **AI draft:** it had no "questions to consider" (brief §7). The reply now includes 2–4 reflection prompts. The system prompt also now forbids result-based assumptions, and says to suggest a pause and a review of the trader's rules when the record looks impulsive, never more trading or risk.
- **Templates page:** it had no way to start a trade from a chosen template. Each active template now has a "New trade" link.
- **Minus sign:** the adherence comparison showed negative R with a hyphen ("-2.00R") beside a true minus in P&L. Both now use "−".
