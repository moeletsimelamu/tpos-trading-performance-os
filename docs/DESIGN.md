# Design

The visual rules for Melamu Holdings OS. The tokens live in `MelamuHoldings.Angular/src/styles.scss`. Canvas charts can't read CSS variables, so `CHART` in `performance.component.ts` mirrors them.

## Intent

A serious internal tool for managing business money:
- dense, readable tables;
- figures that line up;
- colour that carries meaning;
- nothing decorative.

Explicitly avoided: gradients, glows, glassmorphism, oversized summary cards, decorative charts, and invented data to fill space.

## Tokens

| Token | Value | Use |
|---|---|---|
| `--bg-primary` | `#0f1115` | Page background |
| `--bg-secondary` | `#14171c` | Sidebar, inputs, top bar |
| `--bg-card` | `#181b21` | Cards, active nav item |
| `--bg-card-hover` | `#1e2229` | Table row hover |
| `--border` / `--border-subtle` | `#2b3039` / `#22262d` | Controls / dividers and card edges |
| `--text-primary` | `#e9ebef` | Headings, values |
| `--text-secondary` | `#a3abb8` | Labels, secondary text |
| `--text-muted` | `#8a929e` | Hints, placeholders, planned items (passes WCAG AA on `--bg-primary` and `--bg-card`) |
| `--accent-amber` | `#d4a72c` | **Brand and current location only**: logo, active nav marker, primary button, focus ring |
| `--accent-green` / `--accent-red` | `#3fb27f` / `#e5534b` | Gain / loss, success / error. Never decoration. |
| `--accent-blue` | `#5b9bf0` | Neutral informational tags |
| `--font-display` | IBM Plex Sans | All UI text |
| `--font-mono` | IBM Plex Mono with tabular digits (`.mono`) | Figures, dates, codes |
| `--radius` / `--radius-lg` | `6px` / `8px` | Controls / cards |
| `--focus-ring` | 2px gold ring | Every keyboard-focusable element (`:focus-visible`) |

The token names (`--accent-amber`, `--bg-card` and so on) are kept from the original app so existing pages took the new values without edits. They describe history, not hue. Rename them only together with every usage.

## Rules

- **Status honesty.** Navigation shows planned modules as non-link items with a "Planned" badge and their phase. A page exists only when it works.
- **Money display.** Show the currency code beside every monetary figure. Don't colour a figure green or red unless its sign means gain or loss. *Gap:* trading P&L figures don't have a currency yet (the journal has none); prop-account currencies arrive in Phase 8.
- **Charts.** Only where a trend or comparison matters. P&L bars are green or red by sign. Lines are drawn straight, never smoothed. There are no legends for single series.
- **States.** Every data view has loading, empty (with the next action) and error (with Retry) states. Use `<app-load-error>`.
- **Responsive.** At 900 px and above, a fixed 240 px sidebar. Below that, a top bar with a drawer. Content must not scroll horizontally at 375 px; tables scroll inside their card.
- **Accessibility.**
  - Each page has one `h1`.
  - Inputs have `<label for>`.
  - Icon-only controls have screen-reader text (`.visually-hidden`).
  - The active nav link sets `aria-current="page"`.
  - Motion is reduced under `prefers-reduced-motion`.
- **Component styles stay under the 4 kB budget.** Shared helpers go in `styles.scss`.
