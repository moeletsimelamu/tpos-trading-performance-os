import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TradingApiService } from '../trading-api.service';
import { AnalyticsFilter } from '../models/analytics-filter.model';
import { AdherencePeriod, AdherenceTrend, TrendPeriod } from '../models/strategy.model';
import { describeHttpError } from '../../../shared/http-error';

// Rule adherence month by month (or week by week) for the trades in view. Empty periods stay in the table, so a
// break in trading shows as a break and not as a smooth line.
@Component({
  selector: 'app-adherence-trend',
  standalone: true,
  imports: [DecimalPipe],
  template: `
    <div class="trend-head">
      <h3 id="trend-heading">Over time</h3>
      <div class="segmented" role="group" aria-label="Group by">
        @for (p of periods; track p) {
          <button type="button" class="btn btn-sm" [class.btn-primary]="period() === p" [attr.aria-pressed]="period() === p" (click)="period.set(p)">{{ p === 'Month' ? 'By month' : 'By week' }}</button>
        }
      </div>
    </div>
    @if (error()) { <p class="notice notice-error" role="alert">{{ error() }}</p> }
    @if (trend(); as t) {
      @if (t.periods.length === 0) {
        <p class="muted">No closed trades to show over time.</p>
      } @else {
        <div class="scroll">
          <table class="data-table compact" aria-labelledby="trend-heading">
            <thead>
              <tr><th>{{ t.period === 'Month' ? 'Month' : 'Week of' }}</th><th class="num">Closed</th><th class="num">Scored</th><th class="num">Followed</th><th class="num">Rate</th><th class="bar-col"><span class="visually-hidden">Rate as a bar</span></th></tr>
            </thead>
            <tbody>
              @for (p of t.periods; track p.start) {
                <tr [class.muted]="p.scored === 0">
                  <th scope="row">{{ label(p, t.period) }}</th>
                  <td class="num">{{ p.completedTrades }}</td>
                  <td class="num">{{ p.scored }}</td>
                  <td class="num">{{ p.compliant }}</td>
                  <td class="num">{{ p.adherenceRate == null ? '—' : (p.adherenceRate | number: '1.0-1') + '%' }}</td>
                  <td class="bar-col"><div class="track" aria-hidden="true"><div class="fill" [class.thin]="p.insufficientData" [style.width.%]="p.adherenceRate ?? 0"></div></div></td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <p class="small muted">
          Rate = trades that followed every mandatory rule ÷ trades with a complete checklist (Scored).
          Fewer than {{ t.minimumSample }} scored trades in a period is too few to read a trend from; those bars are lighter.
          @if (t.truncated) { Only the most recent {{ t.periods.length }} periods are shown. }
        </p>
      }
    }
  `,
  styles: [`
    :host { display: block; margin-top: var(--space-5); }
    .trend-head { display: flex; justify-content: space-between; align-items: center; gap: var(--space-3); flex-wrap: wrap; margin-bottom: var(--space-3); }
    h3 { font-size: var(--text-md); }
    .segmented { display: flex; gap: var(--space-2); }
    .scroll { overflow-x: auto; }
    .bar-col { width: 30%; min-width: 80px; }
    .track { height: 8px; border-radius: 4px; background: var(--surface-sunken); overflow: hidden; }
    .fill { height: 100%; background: var(--positive); }
    .fill.thin { opacity: 0.45; }
  `]
})
export class AdherenceTrendComponent {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);

  readonly filter = input<AnalyticsFilter>({});
  readonly periods: TrendPeriod[] = ['Month', 'Week'];
  readonly period = signal<TrendPeriod>('Month');
  readonly trend = signal<AdherenceTrend | null>(null);
  readonly error = signal('');

  constructor() {
    effect(() => {
      const filter = this.filter();
      const period = this.period();
      untracked(() => this.load(filter, period));
    });
  }

  private load(filter: AnalyticsFilter, period: TrendPeriod): void {
    this.error.set('');
    this.api.getAdherenceTrend(filter, period).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: t => this.trend.set(t),
      error: err => { this.trend.set(null); this.error.set(`Could not load adherence over time: ${describeHttpError(err)}`); }
    });
  }

  // "Oct 2026", or "5 Oct 2026" for the Monday a week starts on; calendar dates, so no time zone can move them
  label(p: AdherencePeriod, period: TrendPeriod): string {
    const [y, m, d] = p.start.split('-').map(Number);
    const date = new Date(Date.UTC(y, m - 1, d));
    return date.toLocaleDateString('en-GB', period === 'Month'
      ? { month: 'short', year: 'numeric', timeZone: 'UTC' }
      : { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
  }
}
