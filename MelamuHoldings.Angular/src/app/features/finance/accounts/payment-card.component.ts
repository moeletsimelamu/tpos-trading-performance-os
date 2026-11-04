import { Component, computed, input } from '@angular/core';
import { CardFinish, PaymentCard } from '../finance.models';

const range = (count: number) => Array.from({ length: count }, (_, i) => i);

function circle(cx: number, cy: number, r: number): string {
  return `M${cx - r} ${cy} a${r} ${r} 0 1 0 ${r * 2} 0 a${r} ${r} 0 1 0 ${-r * 2} 0`;
}

// Decoration only, generated here rather than copied from any card
const WAVES = range(22).map(i => {
  const y = 16 + i * 8;
  return `M0 ${y} C 70 ${y - 26 + i}, 120 ${y + 34 - i}, 180 ${y + 2} S 280 ${y - 34 + i}, 320 ${y + 8}`;
});
const ARCS = [...range(6).map(i => circle(215, 100, 32 + i * 24)), 'M34 0 C 8 60, 8 140, 34 200'];
const FACETS = range(7).flatMap(row => range(8).filter(col => (row + col) % 2 === 0).map(col => {
  const x = col * 46 + 10;
  const y = row * 34 - 6;
  return `M${x} ${y - 24} L${x + 30} ${y} L${x} ${y + 24} L${x - 30} ${y} Z`;
}));
const RAYS = ['M190 0 L320 92', 'M215 0 L320 75', 'M0 118 L118 200', 'M0 143 L84 200'];

const PATTERNS: Partial<Record<CardFinish, string[]>> = { BlackTeal: ARCS, Yellow: FACETS, Green: FACETS, BlackGold: RAYS };
const FOILED: CardFinish[] = ['Yellow', 'Green'];

// A bank card as drawn by this app from what the owner recorded. It shows only the last four digits: the rest of the
// number, expiry and security code are masked because they are never stored. It is not the bank's own card artwork,
// so it carries no bank or network logo: the colours and patterns are the app's, and the names are plain text.
@Component({
  selector: 'app-payment-card',
  standalone: true,
  host: { '[attr.data-finish]': 'finish()', '[class.cancelled]': "card().status === 'Cancelled'" },
  template: `
    <div class="face" role="img" [attr.aria-label]="label()">
      <svg class="pattern" viewBox="0 0 320 200" preserveAspectRatio="xMidYMid slice" aria-hidden="true" focusable="false">
        @for (line of lines(); track $index) { <path [attr.d]="line" /> }
      </svg>
      <div class="top">
        <span class="status"><span class="dot"></span>{{ card().status }}</span>
        <span class="provider">{{ provider() }}</span>
      </div>
      <div class="middle" aria-hidden="true">
        <svg class="chip" viewBox="0 0 44 34" focusable="false">
          <rect x=".5" y=".5" width="43" height="33" rx="6" class="chip-body" />
          <path d="M15 1v9l-6 4H1M15 33v-9l-6-4H1M29 1v9l6 4h8M29 33v-9l6-4h8M15 10h14M15 24h14" class="chip-lines" />
        </svg>
        <svg class="contactless" viewBox="0 0 22 26" focusable="false">
          <path d="M3 8c2.500 3 2.500 7 0 10M8 5c4 4.500 4 11.500 0 16M13 2c5.500 6 5.500 16 0 22" />
        </svg>
        @if (foiled()) { <span class="foil"></span> }
      </div>
      <div class="number" aria-hidden="true">•••• •••• •••• {{ card().lastFour ?? '••••' }}</div>
      <div class="bottom">
        <span class="masked" aria-hidden="true">EXP ••/•• &nbsp; CVC •••</span>
        <span class="brand">
          @if (network()) { <span class="network">{{ network() }}</span> }
          <span class="kind">{{ card().kind }}</span>
        </span>
      </div>
    </div>
  `,
  styles: [`
    :host {
      --card-bg: linear-gradient(135deg, #1b2c47, #102036); --card-ink: #f4f1ea; --card-muted: rgba(244, 241, 234, .72); --card-accent: var(--card-ink);
      --card-line: rgba(255, 255, 255, .13); --card-fill: none; --card-line-width: .7;
      display: block; width: 100%; max-width: 320px; container-type: inline-size;
    }
    :host([data-finish='Graphite']) { --card-bg: linear-gradient(135deg, #3a3d44, #1f2126); }
    :host([data-finish='Plum']) { --card-bg: linear-gradient(135deg, #43305c, #271a38); }
    :host([data-finish='Teal']) { --card-bg: linear-gradient(135deg, #17666a, #0c4044); }
    :host([data-finish='Gold']) { --card-bg: linear-gradient(135deg, #8f7433, #65501f); --card-line: rgba(255, 255, 255, .17); --card-muted: rgba(255, 255, 255, .82); --card-ink: #fff; }
    :host([data-finish='Silver']) { --card-bg: linear-gradient(135deg, #dfe1e5, #b9bcc3); --card-ink: #17191c; --card-muted: rgba(23, 25, 28, .72); --card-line: rgba(255, 255, 255, .42); }
    :host([data-finish='ForestGold']) { --card-bg: linear-gradient(135deg, #123233, #071a1b); --card-accent: #d9bd78; --card-line: rgba(217, 189, 120, .09); }
    :host([data-finish='BlackTeal']) { --card-bg: radial-gradient(circle at 67% 50%, #252b2f, #0a0d0f 62%); --card-accent: #45c4bb; --card-line: rgba(69, 196, 187, .24); --card-line-width: 1.2; }
    :host([data-finish='BlackGold']) { --card-bg: linear-gradient(135deg, #1b1c1f, #09090a); --card-accent: #d6ad57; --card-line: #c79f4c; --card-line-width: 1.4; }
    :host([data-finish='Yellow']) { --card-bg: linear-gradient(135deg, #fbcd2c, #efb300); --card-ink: #15181c; --card-muted: rgba(21, 24, 28, .76); --card-line: none; --card-fill: rgba(255, 255, 255, .13); }
    :host([data-finish='Green']) { --card-bg: linear-gradient(135deg, #5a8d48, #2d5a2c); --card-ink: #fff; --card-muted: rgba(255, 255, 255, .84); --card-line: none; --card-fill: rgba(255, 255, 255, .07); }
    :host(.cancelled) { filter: grayscale(1); opacity: .6; }
    .face {
      position: relative; aspect-ratio: 1.586; overflow: hidden; border-radius: 12px; background: var(--card-bg); color: var(--card-ink);
      box-shadow: 0 8px 20px rgba(16, 32, 54, .22); display: flex; flex-direction: column; justify-content: space-between; padding: 6.2cqw 6.5cqw;
    }
    .pattern { position: absolute; inset: 0; width: 100%; height: 100%; fill: var(--card-fill); stroke: var(--card-line); stroke-width: var(--card-line-width); }
    .top, .middle, .number, .bottom { position: relative; }
    .top, .bottom { display: flex; justify-content: space-between; align-items: center; gap: 8px; }
    .status { display: inline-flex; align-items: center; gap: 6px; font-size: max(10px, 3.7cqw); color: var(--card-muted); }
    .dot { width: 7px; height: 7px; border-radius: 50%; background: #3fbf8f; box-shadow: 0 0 0 1px rgba(0, 0, 0, .18); }
    :host(.cancelled) .dot { background: #999; }
    .provider { font-size: max(12px, 5cqw); font-weight: 600; text-align: right; overflow-wrap: anywhere; color: var(--card-accent); }
    .middle { display: flex; align-items: center; gap: 3cqw; }
    .chip { width: 13.5cqw; min-width: 30px; }
    .chip-body { fill: #d9c79a; stroke: rgba(0, 0, 0, .25); }
    .chip-lines { fill: none; stroke: rgba(70, 55, 20, .62); stroke-width: 1; }
    .contactless { width: 6cqw; min-width: 13px; fill: none; stroke: var(--card-muted); stroke-width: 1.8; stroke-linecap: round; }
    .foil { margin-left: auto; width: 19cqw; height: 12.5cqw; border-radius: 3px; background: linear-gradient(135deg, #f2f3f4, #a9adb2 45%, #e8e9ea 70%, #9ea2a7); }
    .number { font-family: var(--font-mono); font-size: max(13px, 5.4cqw); letter-spacing: .04em; white-space: nowrap; }
    .masked { font-family: var(--font-mono); font-size: max(9px, 3.1cqw); color: var(--card-muted); white-space: nowrap; }
    .brand { display: inline-flex; flex-direction: column; align-items: flex-end; line-height: 1.15; }
    .network { font-size: max(11px, 4.4cqw); font-weight: 700; letter-spacing: .02em; color: var(--card-accent); }
    .kind { font-size: max(9px, 3.1cqw); text-transform: uppercase; letter-spacing: .1em; color: var(--card-muted); }
  `]
})
export class PaymentCardComponent {
  readonly card = input.required<PaymentCard>();
  readonly provider = input.required<string>();

  readonly finish = computed<CardFinish>(() => this.card().finish ?? 'Navy');
  readonly lines = computed(() => PATTERNS[this.finish()] ?? WAVES);
  readonly foiled = computed(() => FOILED.includes(this.finish()));
  readonly network = computed(() => {
    const n = this.card().network;
    return n === 'Visa' ? 'VISA' : n === 'Mastercard' ? 'Mastercard' : '';
  });
  readonly label = computed(() => {
    const c = this.card();
    return `${c.status} ${c.kind.toLowerCase()} card from ${this.provider()}${c.lastFour ? `, ending ${c.lastFour}` : ''}`;
  });
}
