# Design System

The visual identity and interface rules for Melamu Holdings OS.
- **Tokens:** `MelamuHoldings.Angular/src/styles.scss`.
- **Shared components:** `src/app/shared/ui/`.
- **Chart colours:** `CHART` in `shared/ui/chart-theme.ts` mirrors the tokens, because canvas charts cannot read CSS variables.

The audit behind these decisions is in [`UI_AUDIT.md`](UI_AUDIT.md).

![Executive overview](images/executive-overview.jpg)

## Intent

A calm, institutional workspace for running a business's money:
- hierarchy carried by type, spacing and alignment rather than colour;
- figures that line up;
- tables for comparisons and charts only for trends;
- every element earns its place.

**Avoided:** gradients, glows, glassmorphism, oversized KPI cards, decorative charts, colour used for decoration, and placeholder data.

**Reference products:** Mercury, Ramp and Stripe were studied from screenshots supplied with the brief, for their qualities rather than their layouts. The live sites were not browsed. Nothing is copied: the mark, palette and layouts are Melamu Holdings' own.

## Identity

<img src="images/brand-mark.svg" width="64" alt="MH monogram">

- **Mark:** an M and an H that share their middle stem, joined by a brass bridge, the H's crossbar. It is drawn on a navy tile with rounded corners on a 32-unit grid. Strokes are 2.6 units; the M's V meets the bridge at mid-height.
  - Drawn once, in `BrandMarkComponent` (`<app-brand-mark>`) and `public/favicon.svg`.
  - `tone="on-dark"` lightens the tile so it stays visible on the navy sidebar.
  - It reads at 16 px, the favicon size.
- **Wordmark:** `MELAMU HOLDINGS` in IBM Plex Sans semibold, uppercase, tracked +0.14em. The secondary label `OPERATING SYSTEM` is 10 px with +0.2em tracking.
- **Where it appears:** sidebar, phone top bar, sign-in page, favicon, `theme-color`.
- **Status:** a proposed digital product identity created for this application. It is not a registered or previously approved corporate logo.

## Colour

| Token | Value | Use |
|---|---|---|
| `--navy-900` (`--primary`, `--sidebar-bg`) | `#102036` | Brand, sidebar, primary buttons, selected tabs and toggles |
| `--navy-800` (`--primary-hover`) | `#1c304d` | Hover on primary; the mark's tile on dark surfaces |
| `--brass-500` | `#c9a35a` | **Only** the mark's bridge, the active-navigation marker, and focus on the sidebar |
| `--surface-canvas` | `#f6f4ef` | Page background (warm off-white) |
| `--surface` | `#ffffff` | Cards, tables, inputs |
| `--surface-sunken` / `--surface-hover` | `#f9f8f5` / `#f4f2ed` | Table headers and detail rows / row hover |
| `--border` / `--border-subtle` | `#dcd7cd` / `#ebe7df` | Controls / dividers and card edges |
| `--text-primary` | `#172131` | Headings, values |
| `--text-secondary` | `#4b5565` | Labels, descriptions |
| `--text-muted` | `#646c79` | Notes and hints (4.8:1 on canvas, 5.4:1 on white) |
| `--link` / `--focus` | `#1d5c8c` / `#2f6aa3` | Links / focus ring |
| `--positive` / `--negative` | `#1d7a4c` / `#b4322a` | Money in and gains / money out and losses |
| `--warning` / `--info` | `#93560a` / `#1d5c8c` | Needs attention / neutral information |
| `--*-bg` | tints of each status | Backgrounds of tags and notices |

**Rules**
- Every text colour passes WCAG AA on `--surface` and `--surface-canvas`.
- Status colour is never the only signal. It always comes with a sign (`+`/`−`), a word ("Warning", "Not assigned") or an icon.
- Values that are neither gains nor losses (win rate, profit factor, average R, drawdown, balances) are set in `--text-primary`.
- There is **no dark theme in this release.** Components use only semantic tokens, so a dark theme is a token-level change.

## Typography

- **IBM Plex Sans** is used everywhere. `font-variant-numeric: tabular-nums` is set on `body`, so all figures align in columns.
- **IBM Plex Mono** (`.mono`) is used only for identifiers and codes: audit action names, CSV column names.

| Role | Size / weight |
|---|---|
| Page title (`h1`) | 24 px / 600 |
| Card and section title (`h2`) | 15 px / 600 |
| Body, table cells | 14 px and 13.5 px / 400 |
| Labels, notes | 12.5 px and 11.5 px |
| Section label above a title | 11.5 px / 600, uppercase, +0.08em |
| Metric values | 22 px / 600; lead metric 30 px |

## Space, layout and shape

- **Spacing:** a 4 px scale (`--space-1` … `--space-10`). Cards pad 20 px; table cells pad 11 × 16 px.
- **Content width:** pages are centred at a maximum of 1240 px (`--content-max`); Import uses 900 px.
- **Sidebar:** 248 px, fixed, navy. Below 900 px it becomes a white top bar with a drawer.
- **Radii:** 4, 6 and 10 px. Shadows are a single 1 px hairline (`--shadow-sm`) on surfaces, with `--shadow-lg` only for the drawer.

## Components

| Component | Where | Use |
|---|---|---|
| `<app-brand-mark>` | `shared/ui/brand-mark.component.ts` | The mark. Inputs: `size`, `tone` |
| `<app-icon>` | `shared/ui/icon.component.ts` | The product's own stroke icon set (24-unit grid, 1.6 stroke, round ends). Decorative (`aria-hidden`); the surrounding control carries the label |
| `<app-page-header>` | `shared/ui/page-header.component.ts` | Every page: `section` label, `title`, `description`, with action buttons projected to the right |
| `<app-load-error>` | `shared/load-error.component.ts` | An error state with Retry |
| `.metric-strip` / `.metric` | `styles.scss` | Summary figures on one surface. `.metric-lead` makes the headline figure larger and gives it a full row on phones. Cells are flex items, so a short row stretches instead of leaving a gap |
| `.card`, `.card-header`, `.table-card` | `styles.scss` | Surfaces. A table card has no padding; the table owns its edges |
| `.data-table` | `styles.scss` | Sunken header row, hairline rows, hover. `.num` right-aligns figures. The first link in a row is set as the row's title |
| `.toolbar` | `styles.scss` | Filters inside the top of a table card |
| `.tabs` / `.tab` | `styles.scss` | Switching one view (`role="tablist"`, `aria-selected`) |
| `.btn`, `.btn-primary`, `.btn-danger`, `.btn-sm`, `.btn-link` | `styles.scss` | One primary action per area |
| `.field`, `.form-grid`, `.form-actions` | `styles.scss` | Every input has a visible `<label for>` |
| `.tag-positive/negative/warning/info` | `styles.scss` | Status pills |
| `.notice-error/success/warning` | `styles.scss` | Inline messages |
| `.loading-state`, `.empty-state` | `styles.scss` | Loading, and empty with the next action |
| `.bar-track` / `.bar-fill` | `styles.scss` | Proportional bars in table cells. They diverge from a centre line when values can be negative |
| `baseChartOptions()` | `shared/ui/chart-theme.ts` | Chart.js defaults: no legend for a single series, emphasised zero line, at most 8 unrotated date ticks, navy tooltip |

## Usage conventions

- **Money:**
  - `{{ amount | money: currency }}` renders `ZAR 1,350.00`; `: true` adds `+` to credits.
  - Currencies are shown side by side and are never added together.
  - Trading P&L has no currency yet and is labelled as such: "Figures are in the trading account's currency, which the journal does not record yet". It is never presented as business cash.
- **Signed figures** use a true minus sign (`−`) and a `+` for gains.
- **Status vocabulary:** one treatment per meaning, defined once in `finance.models.ts` (`PAYOUT_STATUS`, `ALLOCATION_STATUS`) and reused everywhere.

  | Meaning | Tag | Used for |
  |---|---|---|
  | Not yet money / not yet acted on | neutral `tag` | Payout *Requested*, allocation *Cancelled*, policy *Not in use*, Proposed (later phases) |
  | Committed, nothing moved yet | `tag-info` | Payout *Approved*, allocation *Confirmed* |
  | Part done, needs attention | `tag-warning` | Allocation *Partially settled*, Pending (later phases), *Not assigned* (Unallocated money), fee entries |
  | Done and reconciled | `tag-positive` | Payout *Received*, allocation *Reconciled*, line *Settled*, policy *Active*, Completed (later phases) |
  | Refused or overdue | `tag-negative` | Payout *Rejected*, Overdue (later phases) |

  Expected amounts (requested or approved payouts) always carry the words "not cash".
- **Progress pages** (a payout) show the steps as a vertical timeline beside a single "Next step" panel that offers only the actions the current status allows.
- **Navigation lists only working destinations.** The roadmap lives in `PLAN.md`, not in the sidebar.
- **Irreversible actions ask first.** Deleting a trade and closing an account each need a second click; a reversal needs a written reason. Native `confirm()` is not used.
- **Every data view has three states:** loading, empty (with the next action) and error (with Retry). Independent panels load and fail independently: the overview still shows trading results if the finance data is refused.
- **Charts:** a chart only for a trend over time. Straight segments with no smoothing, and labels set as "8 Oct" rather than ISO dates.
- **Accessibility:**
  - one `h1` per page;
  - visible labels on every input;
  - `aria-current="page"` on the active link;
  - a visible focus ring on every focusable element (blue on light surfaces, brass on the sidebar);
  - icon-only buttons carry screen-reader text;
  - motion off under `prefers-reduced-motion`.
- **Responsive:**
  - no horizontal page scroll at 375 px;
  - wide tables scroll inside their card;
  - the breakdown bar column hides on phones.
- **Component styles stay under the 4 kB budget.** Anything shared goes in `styles.scss`.
