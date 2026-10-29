import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FinanceApiService } from '../finance/finance-api.service';
import { CashLine, MonthMovement, Overview, TransactionType } from '../finance/finance.models';
import { MoneyPipe } from '../../shared/money.pipe';
import { LoadErrorComponent } from '../../shared/load-error.component';
import { describeHttpError } from '../../shared/http-error';

export interface PeriodPreset { key: string; label: string; from: string; to: string; }

const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const DATE = /^\d{4}-\d{2}-\d{2}$/;

// The periods offered, from today's calendar date. Each ends today except last month.
export function periodPresets(today: Date): PeriodPreset[] {
  const y = today.getFullYear();
  const m = today.getMonth();
  const quarter = Math.floor(m / 3) * 3;
  const year = new Date(y - 1, m, today.getDate() + 1);
  return [
    { key: 'month', label: 'This month', from: iso(new Date(y, m, 1)), to: iso(today) },
    { key: 'last-month', label: 'Last month', from: iso(new Date(y, m - 1, 1)), to: iso(new Date(y, m, 0)) },
    { key: 'quarter', label: 'This quarter', from: iso(new Date(y, quarter, 1)), to: iso(today) },
    { key: 'year', label: 'Year to date', from: iso(new Date(y, 0, 1)), to: iso(today) },
    { key: '12-months', label: 'Last 12 months', from: iso(year), to: iso(today) }
  ];
}

// "Money in and out" on the Executive overview: cash movement in bank and cash accounts for a period, per currency,
// with every figure linked to the ledger list it is summed from. The period lives in the address.
@Component({
  selector: 'app-overview-period',
  standalone: true,
  imports: [FormsModule, RouterLink, MoneyPipe, LoadErrorComponent],
  template: `
    <section class="period" aria-labelledby="period-heading">
      <div class="period-head">
        <div>
          <h2 id="period-heading" class="section-heading">Money in and out</h2>
          <p class="small secondary">{{ label() }}. Bank and cash accounts only. Net cash movement is what came in less what went out, not profit.</p>
        </div>
        <div class="presets" role="group" aria-label="Period">
          @for (p of presets; track p.key) {
            <button type="button" class="btn btn-sm" [class.btn-primary]="p.key === activePreset()" [attr.aria-pressed]="p.key === activePreset()" (click)="choose(p.from, p.to)">{{ p.label }}</button>
          }
        </div>
      </div>
      <form class="custom" (ngSubmit)="choose(draftFrom, draftTo)" aria-label="Custom period">
        <div class="field"><label for="p-from">From</label><input id="p-from" type="date" name="from" [(ngModel)]="draftFrom" [max]="today" /></div>
        <div class="field"><label for="p-to">To</label><input id="p-to" type="date" name="to" [(ngModel)]="draftTo" [max]="today" /></div>
        <button type="submit" class="btn btn-sm" [disabled]="!draftFrom || !draftTo || draftFrom > draftTo">Show period</button>
      </form>

      @switch (state()) {
        @case ('loading') { <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading the period...</span></div> }
        @case ('error') { <app-load-error title="Could not load the period" [message]="error()" (retry)="load()" /> }
        @case ('ready') {
          @if (overview(); as o) {
            @if (o.currencies.length === 0) {
              <div class="card empty-state small"><p>No money recorded in bank or cash accounts up to {{ o.to }}.</p></div>
            } @else {
              <div class="metric-strip">
                @for (c of o.currencies; track c.currency) {
                  <div class="metric">
                    <span class="metric-label">Net cash movement · {{ c.currency }}</span>
                    <span class="metric-value" [class.text-positive]="c.netCashMovement > 0" [class.text-negative]="c.netCashMovement < 0">{{ c.netCashMovement | money: c.currency : true }}</span>
                    <span class="metric-note">In {{ line(c.movements, 'Income').amount | money: c.currency }} · out {{ abs(line(c.movements, 'Expense').amount) | money: c.currency }} in expenses</span>
                  </div>
                }
                <div class="metric">
                  <span class="metric-label">Payouts received</span>
                  <span class="metric-value">{{ payoutCount() }}</span>
                  <span class="metric-note">
                    @for (p of o.payoutsReceived; track p.currency; let last = $last) { {{ p.credited | money: p.currency }}{{ last ? '' : ' · ' }} }
                    @empty { None in this period }
                  </span>
                </div>
              </div>

              <div class="currencies">
                @for (c of o.currencies; track c.currency) {
                  <section class="card table-card" [attr.aria-label]="'Cash movement in ' + c.currency">
                    <div class="card-header"><h3>Cash movement · {{ c.currency }}</h3></div>
                    <table class="data-table">
                      <tbody>
                        <tr class="edge"><th scope="row">Cash at the start</th><td class="num">{{ c.cashAtStart | money: c.currency }}</td></tr>
                        @for (l of c.movements; track l.type) {
                          <tr [class.muted]="l.transactions === 0">
                            <th scope="row">
                              {{ l.label }}
                              @if (l.type === 'Income' && l.fees) { <div class="small secondary">{{ l.gross! | money: c.currency }} gross, less {{ abs(l.fees) | money: c.currency }} in fees deducted</div> }
                              @if (l.type === 'Transfer' && l.transactions > 0) { <div class="small secondary">Between two bank or cash accounts they cancel out</div> }
                              @if (l.transactions > 0) { <div class="small"><a [routerLink]="['/finance/transactions']" [queryParams]="ledger(l.type)">{{ l.transactions }} {{ l.transactions === 1 ? 'transaction' : 'transactions' }}</a></div> }
                            </th>
                            <td class="num" [class.amount-in]="l.amount > 0" [class.amount-out]="l.amount < 0">{{ l.amount | money: c.currency : true }}</td>
                          </tr>
                        }
                        <tr class="total"><th scope="row">Net cash movement</th><td class="num">{{ c.netCashMovement | money: c.currency : true }}</td></tr>
                        @if (c.openingBalances.transactions > 0) {
                          <tr>
                            <th scope="row">{{ c.openingBalances.label }} <div class="small secondary">Balances that existed before they were recorded; not a movement</div>
                              <div class="small"><a [routerLink]="['/finance/transactions']" [queryParams]="ledger('OpeningBalance')">{{ c.openingBalances.transactions }} {{ c.openingBalances.transactions === 1 ? 'transaction' : 'transactions' }}</a></div></th>
                            <td class="num">{{ c.openingBalances.amount | money: c.currency : true }}</td>
                          </tr>
                        }
                        <tr class="edge"><th scope="row">Cash at the end</th><td class="num">{{ c.cashAtEnd | money: c.currency }}</td></tr>
                      </tbody>
                    </table>
                  </section>
                }
              </div>

              @if (monthCount() > 1) {
                <section class="card table-card months" aria-labelledby="months-heading">
                  <div class="card-header"><h3 id="months-heading">By month</h3>@if (o.monthsTruncated) { <span class="card-note">The most recent 24 months</span> }</div>
                  <div class="scroll">
                    <table class="data-table">
                      <thead><tr><th>Month</th><th>Currency</th><th class="num">Income</th><th class="num">Expenses</th><th class="num">Net cash movement</th></tr></thead>
                      <tbody>
                        @for (m of o.months; track m.start + m.currency) {
                          <tr [class.muted]="m.transactions === 0">
                            <td class="nowrap">{{ monthLabel(m) }}</td>
                            <td>{{ m.currency }}</td>
                            <td class="num">@if (m.income !== 0) { <a [routerLink]="['/finance/transactions']" [queryParams]="ledger('Income', m)">{{ m.income | money: m.currency }}</a> } @else { — }</td>
                            <td class="num">@if (m.expenses !== 0) { <a [routerLink]="['/finance/transactions']" [queryParams]="ledger('Expense', m)">{{ abs(m.expenses) | money: m.currency }}</a> } @else { — }</td>
                            <td class="num" [class.amount-in]="m.netCashMovement > 0" [class.amount-out]="m.netCashMovement < 0">{{ m.netCashMovement | money: m.currency : true }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                </section>
              }
            }
          }
        }
      }
    </section>
  `,
  styles: [`
    .period { margin-top: var(--space-6); }
    .period-head { display: flex; justify-content: space-between; align-items: flex-end; gap: var(--space-4); flex-wrap: wrap; margin-bottom: var(--space-3); }
    .presets { display: flex; flex-wrap: wrap; gap: var(--space-2); }
    .custom { display: flex; flex-wrap: wrap; gap: var(--space-3); align-items: flex-end; margin-bottom: var(--space-4); }
    .custom .field { margin: 0; }
    .currencies { display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: var(--space-5); margin-top: var(--space-5); }
    .currencies .card, .months { padding: 0; }
    h3 { font-size: var(--text-md); }
    th[scope="row"] { font-weight: 400; text-align: left; white-space: normal; }
    td.num { white-space: nowrap; vertical-align: top; }
    .edge th, .edge td { color: var(--text-secondary); }
    .total th, .total td { font-weight: 600; border-top: 2px solid var(--border); }
    .months { margin-top: var(--space-5); }
    .scroll { overflow-x: auto; }
    .nowrap { white-space: nowrap; }
    .empty-state.small { padding: var(--space-5); }
    @media (max-width: 400px) { .currencies { grid-template-columns: minmax(0, 1fr); } }
  `]
})
export class OverviewPeriodComponent {
  private api = inject(FinanceApiService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  readonly today = iso(new Date());
  readonly presets = periodPresets(new Date());
  readonly from = signal(this.presets[0].from);
  readonly to = signal(this.presets[0].to);
  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly error = signal('');
  readonly overview = signal<Overview | null>(null);
  readonly payoutCount = computed(() => (this.overview()?.payoutsReceived ?? []).reduce((n, p) => n + p.payouts, 0));
  readonly monthCount = computed(() => new Set((this.overview()?.months ?? []).map(m => m.start)).size);
  readonly label = computed(() => `${long(this.from())} to ${long(this.to())}`);
  // Two presets can cover the same dates (this month and this quarter in a quarter's first month); mark the first
  readonly activePreset = computed(() => this.presets.find(p => p.from === this.from() && p.to === this.to())?.key ?? null);
  draftFrom = this.from();
  draftTo = this.to();

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const from = params.get('from'), to = params.get('to');
      if (from && to && DATE.test(from) && DATE.test(to) && from <= to) { this.from.set(from); this.to.set(to); }
      this.draftFrom = this.from();
      this.draftTo = this.to();
      this.load();
    });
  }

  load(): void {
    this.state.set('loading');
    this.api.getOverview(this.from(), this.to()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: o => { this.overview.set(o); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  // The period goes in the address, which reloads the figures
  choose(from: string, to: string): void {
    if (!from || !to || from > to) return;
    this.router.navigate([], { relativeTo: this.route, queryParams: { from, to }, replaceUrl: true });
  }

  line(lines: CashLine[], type: TransactionType): CashLine {
    return lines.find(l => l.type === type)!;
  }

  // The ledger list a figure is summed from: same type, same dates, bank and cash accounts
  ledger(type: TransactionType, month?: MonthMovement): Record<string, string> {
    return { from: month?.start ?? this.from(), to: month?.end ?? this.to(), type, cash: 'true' };
  }

  monthLabel(m: MonthMovement): string {
    const [y, mo, d] = m.start.split('-').map(Number);
    const name = new Date(Date.UTC(y, mo - 1, 1)).toLocaleDateString('en-GB', { month: 'short', year: 'numeric', timeZone: 'UTC' });
    const last = new Date(Date.UTC(y, mo, 0)).getUTCDate();
    const endDay = Number(m.end.slice(8, 10));
    return d === 1 && endDay === last ? name : `${name} (${d}–${endDay})`;
  }

  abs(value: number): number {
    return Math.abs(value);
  }
}

// "2026-10-29" → "29 Oct 2026", as a calendar date
function long(isoDate: string): string {
  const [y, m, d] = isoDate.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d)).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
}
