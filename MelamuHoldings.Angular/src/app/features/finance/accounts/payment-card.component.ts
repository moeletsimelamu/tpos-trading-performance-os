import { Component, computed, input } from '@angular/core';
import { CardFinish, PaymentCard } from '../finance.models';

// A bank card as drawn by this app from what the owner recorded. It shows only the last four digits: the rest of the
// number, expiry and security code are masked because they are never stored. It is not the bank's own card artwork.
@Component({
  selector: 'app-payment-card',
  standalone: true,
  host: { '[attr.data-finish]': 'finish()', '[class.cancelled]': "card().status === 'Cancelled'" },
  template: `
    <div class="face" role="img" [attr.aria-label]="label()">
      <svg class="pattern" viewBox="0 0 320 200" preserveAspectRatio="xMidYMid slice" aria-hidden="true" focusable="false">
        @for (line of lines; track $index) { <path [attr.d]="line" /> }
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
      --card-bg: linear-gradient(135deg, #1b2c47, #102036); --card-ink: #f4f1ea; --card-muted: rgba(244, 241, 234, .72); --card-line: rgba(255, 255, 255, .13);
      display: block; width: 100%; max-width: 320px; container-type: inline-size;
    }
    :host([data-finish='Graphite']) { --card-bg: linear-gradient(135deg, #3a3d44, #1f2126); }
    :host([data-finish='Plum']) { --card-bg: linear-gradient(135deg, #43305c, #271a38); }
    :host([data-finish='Teal']) { --card-bg: linear-gradient(135deg, #17666a, #0c4044); }
    :host([data-finish='Gold']) { --card-bg: linear-gradient(135deg, #8f7433, #65501f); --card-line: rgba(255, 255, 255, .17); --card-muted: rgba(255, 255, 255, .82); --card-ink: #fff; }
    :host([data-finish='Silver']) { --card-bg: linear-gradient(135deg, #dfe1e5, #b9bcc3); --card-ink: #17191c; --card-muted: rgba(23, 25, 28, .72); --card-line: rgba(255, 255, 255, .42); }
    :host(.cancelled) { filter: grayscale(1); opacity: .6; }
    .face {
      position: relative; aspect-ratio: 1.586; overflow: hidden; border-radius: 12px; background: var(--card-bg); color: var(--card-ink);
      box-shadow: 0 8px 20px rgba(16, 32, 54, .22); display: flex; flex-direction: column; justify-content: space-between; padding: 6.2cqw 6.5cqw;
    }
    .pattern { position: absolute; inset: 0; width: 100%; height: 100%; fill: none; stroke: var(--card-line); stroke-width: .7; }
    .top, .middle, .number, .bottom { position: relative; }
    .top, .bottom { display: flex; justify-content: space-between; align-items: center; gap: 8px; }
    .status { display: inline-flex; align-items: center; gap: 6px; font-size: max(10px, 3.7cqw); color: var(--card-muted); }
    .dot { width: 7px; height: 7px; border-radius: 50%; background: #3fbf8f; }
    :host(.cancelled) .dot { background: #999; }
    .provider { font-size: max(12px, 5cqw); font-weight: 600; text-align: right; overflow-wrap: anywhere; }
    .middle { display: flex; align-items: center; gap: 3cqw; }
    .chip { width: 13.5cqw; min-width: 30px; }
    .chip-body { fill: #d9c79a; stroke: rgba(0, 0, 0, .25); }
    .chip-lines { fill: none; stroke: rgba(70, 55, 20, .62); stroke-width: 1; }
    .contactless { width: 6cqw; min-width: 13px; fill: none; stroke: var(--card-muted); stroke-width: 1.8; stroke-linecap: round; }
    .number { font-family: var(--font-mono); font-size: max(13px, 5.4cqw); letter-spacing: .04em; white-space: nowrap; }
    .masked { font-family: var(--font-mono); font-size: max(9px, 3.1cqw); color: var(--card-muted); white-space: nowrap; }
    .brand { display: inline-flex; flex-direction: column; align-items: flex-end; line-height: 1.15; }
    .network { font-size: max(11px, 4.4cqw); font-weight: 700; letter-spacing: .02em; }
    .kind { font-size: max(9px, 3.1cqw); text-transform: uppercase; letter-spacing: .1em; color: var(--card-muted); }
  `]
})
export class PaymentCardComponent {
  readonly card = input.required<PaymentCard>();
  readonly provider = input.required<string>();

  // Guilloche-style lines: decoration only, generated rather than copied from any card
  readonly lines = Array.from({ length: 22 }, (_, i) => {
    const y = 16 + i * 8;
    return `M0 ${y} C 70 ${y - 26 + i}, 120 ${y + 34 - i}, 180 ${y + 2} S 280 ${y - 34 + i}, 320 ${y + 8}`;
  });

  readonly finish = computed<CardFinish>(() => this.card().finish ?? 'Navy');
  readonly network = computed(() => {
    const n = this.card().network;
    return n === 'Visa' ? 'VISA' : n === 'Mastercard' ? 'Mastercard' : '';
  });
  readonly label = computed(() => {
    const c = this.card();
    return `${c.status} ${c.kind.toLowerCase()} card from ${this.provider()}${c.lastFour ? `, ending ${c.lastFour}` : ''}`;
  });
}
