import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { BaseChartDirective, provideCharts, withDefaultRegisterables } from 'ng2-charts';
import { ChartData } from 'chart.js';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, forkJoin, of } from 'rxjs';
import { TradingApiService } from '../trading-api.service';
import { AnalyticsSummary, EquityPoint } from '../models/analytics.model';
import { AdherenceReport } from '../models/strategy.model';
import { AnalyticsFilter, AnalyticsFilterOptions, filterFromQuery, filterToQuery, isFilterEmpty } from '../models/analytics-filter.model';
import { AnalyticsFilterBarComponent } from '../analytics/analytics-filter-bar.component';
import { AdherencePanelComponent } from '../strategies/adherence-panel.component';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';
import { CHART, baseChartOptions } from '../../../shared/ui/chart-theme';

type LoadState = 'loading' | 'ready' | 'error';
type BreakdownKey = 'strategy' | 'day' | 'session';

// One row of any breakdown: the strategy, weekday and session endpoints share these figures
export interface BreakdownRow { label: string; totalTrades: number; winRate: number; averageRR: number; totalPnL: number; }

const BREAKDOWNS: { key: BreakdownKey; label: string; column: string }[] = [
  { key: 'strategy', label: 'Strategy', column: 'Strategy' },
  { key: 'day', label: 'Day of week', column: 'Day' },
  { key: 'session', label: 'Session', column: 'Session' }
];

@Component({
  selector: 'app-trading-performance',
  standalone: true,
  imports: [DecimalPipe, RouterLink, BaseChartDirective, LoadErrorComponent, PageHeaderComponent, AnalyticsFilterBarComponent, AdherencePanelComponent],
  providers: [provideCharts(withDefaultRegisterables())],
  template: `
    <div class="page">
      <app-page-header section="Trading operations" title="Performance" [description]="description()" />

      @if (hasTrades()) {
        <app-analytics-filter-bar [options]="options()" [filter]="filter()" (filterChange)="applyFilter($event)" />
      }

      @switch (state()) {
        @case ('loading') {
          <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading performance data...</span></div>
        }
        @case ('error') {
          <app-load-error title="Could not load performance data" [message]="error()" (retry)="load()" />
        }
        @case ('ready') {
          @if (summary(); as s) {
            @if (s.totalTrades === 0 && filtered()) {
              <div class="card empty-state">
                <h2>No closed trades match these filters</h2>
                <p>Widen the date range or remove a filter. Drafts and open trades are never counted.</p>
                <div class="empty-actions"><button type="button" class="btn" (click)="applyFilter({})">Clear filters</button></div>
              </div>
            } @else if (s.totalTrades === 0) {
              <div class="card empty-state">
                <h2>No trades recorded yet</h2>
                <p>Performance is calculated from closed trades in the journal. Record a trade, or import your history.</p>
                <div class="empty-actions">
                  <a class="btn btn-primary" routerLink="/trading/trades/new">New trade</a>
                  <a class="btn" routerLink="/trading/import">Import trades</a>
                </div>
              </div>
            } @else {
              @if (s.insufficientData) {
                <p class="notice notice-warning sample" role="status">
                  {{ s.totalTrades }} {{ filtered() ? (s.totalTrades === 1 ? 'trade matches these filters' : 'trades match these filters') : (s.totalTrades === 1 ? 'trade recorded' : 'trades recorded') }}: fewer than {{ s.minimumSample }},
                  so these figures are insufficient data for drawing conclusions.
                </p>
              }
              <section class="metric-strip" aria-label="Summary">
                <div class="metric metric-lead">
                  <span class="metric-label">Total P&amp;L</span>
                  <span class="metric-value" [class.text-positive]="s.totalPnL > 0" [class.text-negative]="s.totalPnL < 0">{{ signed(s.totalPnL) }}</span>
                  <span class="metric-note">{{ s.totalTrades }} {{ s.totalTrades === 1 ? 'trade' : 'trades' }}</span>
                </div>
                <div class="metric">
                  <span class="metric-label">Win rate</span>
                  <span class="metric-value">{{ s.winRate | number: '1.1-1' }}%</span>
                  <span class="metric-note">{{ s.winCount }} won · {{ s.lossCount }} lost · {{ s.breakevenCount }} even</span>
                </div>
                <div class="metric">
                  <span class="metric-label">Expectancy</span>
                  <span class="metric-value" [class.text-positive]="s.expectancy > 0" [class.text-negative]="s.expectancy < 0">{{ signed(s.expectancy) }}</span>
                  <span class="metric-note">Average P&amp;L per trade</span>
                </div>
                <div class="metric">
                  <span class="metric-label">Profit factor</span>
                  <span class="metric-value">{{ s.profitFactor | number: '1.2-2' }}</span>
                  <span class="metric-note">Gross profit ÷ gross loss</span>
                </div>
                <div class="metric">
                  <span class="metric-label">Average R</span>
                  <span class="metric-value">{{ s.averageRR | number: '1.2-2' }}R</span>
                  <span class="metric-note">Mean reward-to-risk</span>
                </div>
                <div class="metric">
                  <span class="metric-label">Max drawdown</span>
                  <span class="metric-value">{{ s.maxDrawdown > 0 ? '−' : '' }}{{ s.maxDrawdown | number: '1.2-2' }}</span>
                  <span class="metric-note">Largest fall from a peak</span>
                </div>
              </section>

              <section class="card chart-card" aria-labelledby="equity-heading">
                <div class="card-header">
                  <h2 id="equity-heading">Equity curve</h2>
                  <span class="card-note">Cumulative P&amp;L at the close of each trading day</span>
                </div>
                @if (equityChart(); as data) {
                  <div class="chart-body">
                    <canvas baseChart [data]="data" [options]="chartOptions" type="line" aria-label="Equity curve" role="img"></canvas>
                  </div>
                }
              </section>

              <section class="card table-card breakdown" aria-labelledby="breakdown-heading">
                <div class="card-header">
                  <h2 id="breakdown-heading">Breakdown</h2>
                  <span class="card-note">Bars compare P&amp;L within the table</span>
                </div>
                <div class="tabs" role="tablist" aria-label="Break down by">
                  @for (b of breakdowns; track b.key) {
                    <button type="button" role="tab" class="tab" [id]="'tab-' + b.key" [attr.aria-selected]="breakdown() === b.key"
                      aria-controls="breakdown-panel" (click)="breakdown.set(b.key)">{{ b.label }}</button>
                  }
                </div>
                <div id="breakdown-panel" role="tabpanel" [attr.aria-labelledby]="'tab-' + breakdown()">
                  <table class="data-table">
                    <thead>
                      <tr><th>{{ column() }}</th><th class="num">Trades</th><th class="num">Win rate</th><th class="num">Average R</th><th class="num">P&amp;L</th><th class="bar-col"><span class="visually-hidden">Comparison</span></th></tr>
                    </thead>
                    <tbody>
                      @for (row of rows(); track row.label) {
                        <tr>
                          <td>{{ row.label }}</td>
                          <td class="num">{{ row.totalTrades }}</td>
                          <td class="num">{{ row.winRate | number: '1.1-1' }}%</td>
                          <td class="num">{{ row.averageRR | number: '1.2-2' }}R</td>
                          <td class="num" [class.text-positive]="row.totalPnL > 0" [class.text-negative]="row.totalPnL < 0">{{ signed(row.totalPnL) }}</td>
                          <td class="bar-col">
                            <div class="bar-track centred" aria-hidden="true">
                              <div class="bar-fill" [class.positive]="row.totalPnL >= 0" [class.negative]="row.totalPnL < 0"
                                [style.left.%]="barLeft(row)" [style.width.%]="barWidth(row)"></div>
                            </div>
                          </td>
                        </tr>
                      } @empty {
                        <tr><td colspan="6" class="muted">No trades to break down.</td></tr>
                      }
                    </tbody>
                  </table>
                </div>
              </section>

              @if (adherence(); as a) {
                <section class="adherence" aria-labelledby="adherence-heading">
                  <h2 id="adherence-heading" class="section-heading">Rule adherence</h2>
                  <app-adherence-panel [report]="a" />
                </section>
              }
            }
          }
        }
      }
    </div>
  `,
  styles: [`
    .chart-card, .breakdown, .adherence { margin-top: var(--space-5); }
    .section-heading { font-size: var(--text-lg); margin-bottom: var(--space-3); }
    .sample { margin-bottom: var(--space-4); }
    .chart-card { padding: 0; }
    .chart-body { position: relative; height: 300px; padding: var(--space-4) var(--space-5) var(--space-3); }
    .empty-actions { display: flex; gap: var(--space-2); justify-content: center; }
    .bar-col { width: 28%; min-width: 120px; vertical-align: middle; }
    .centred::after { content: ''; position: absolute; left: 50%; top: -3px; bottom: -3px; width: 1px; background: var(--border); }
    @media (max-width: 599px) { .bar-col { display: none; } .chart-body { height: 240px; padding: var(--space-3); } }
  `]
})
export class PerformanceComponent {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  readonly breakdowns = BREAKDOWNS;
  readonly chartOptions = baseChartOptions();

  readonly state = signal<LoadState>('loading');
  readonly error = signal('');
  readonly summary = signal<AnalyticsSummary | null>(null);
  readonly equity = signal<EquityPoint[]>([]);
  readonly tables = signal<Record<BreakdownKey, BreakdownRow[]>>({ strategy: [], day: [], session: [] });
  readonly breakdown = signal<BreakdownKey>('strategy');
  readonly adherence = signal<AdherenceReport | null>(null);

  // The filter lives in the address, so a filtered view survives a reload and can be bookmarked
  readonly filter = signal<AnalyticsFilter>(filterFromQuery(this.route.snapshot.queryParamMap));
  readonly options = signal<AnalyticsFilterOptions | null>(null);
  readonly filtered = computed(() => !isFilterEmpty(this.filter()));
  // The filter bar shows once there is anything to filter: options list strategies or sessions from closed trades
  readonly hasTrades = computed(() => this.filtered() || (this.summary()?.totalTrades ?? 0) > 0);

  readonly rows = computed(() => this.tables()[this.breakdown()]);
  readonly column = computed(() => BREAKDOWNS.find(b => b.key === this.breakdown())!.column);
  private readonly maxAbsPnL = computed(() => Math.max(0, ...this.rows().map(r => Math.abs(r.totalPnL))));

  readonly description = computed(() => {
    const s = this.summary();
    const points = this.equity();
    if (!s || s.totalTrades === 0) return 'Results calculated from the trading journal.';
    const range = points.length > 0 ? ` from ${formatDay(points[0].date, true)} to ${formatDay(points[points.length - 1].date, true)}` : '';
    const noun = s.totalTrades === 1 ? 'closed trade' : 'closed trades';
    const which = this.filtered() ? `${noun} matching the filters` : noun;
    return `${s.totalTrades} ${which}${range}; drafts and open trades are not counted. Figures are in each trade's own currency, which imported trades do not record.`;
  });

  readonly equityChart = computed<ChartData<'line'> | null>(() => {
    const points = this.equity();
    if (points.length === 0) return null;
    return {
      labels: points.map(p => formatDay(p.date)),
      datasets: [{
        label: 'Cumulative P&L',
        data: points.map(p => p.cumulativePnL),
        borderColor: CHART.line,
        backgroundColor: CHART.lineFill,
        borderWidth: 2,
        fill: 'origin',
        tension: 0,  // straight segments: no smoothing that implies moves between data points
        pointRadius: points.length > 40 ? 0 : 2.5,
        pointBackgroundColor: CHART.line,
        pointHoverRadius: 4
      }]
    };
  });

  constructor() {
    this.load();
    // The options only fill the filter lists; without them the dates still work
    this.api.getFilterOptions().pipe(catchError(() => of(null)), takeUntilDestroyed(this.destroyRef)).subscribe(o => this.options.set(o));
  }

  applyFilter(filter: AnalyticsFilter): void {
    this.filter.set(filter);
    this.router.navigate([], { relativeTo: this.route, queryParams: filterToQuery(filter), replaceUrl: true });
    this.load();
  }

  load(): void {
    this.state.set('loading');
    const filter = this.filter();

    forkJoin({
      summary: this.api.getSummary(filter),
      equity: this.api.getEquityCurve(filter),
      strategies: this.api.getStrategyPerformance(filter),
      days: this.api.getPerformanceByDay(filter),
      sessions: this.api.getPerformanceBySession(filter),
      // Adherence is a further view; the figures above still show if it fails
      adherence: this.api.getAdherence(filter).pipe(catchError(() => of(null)))
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ summary, equity, strategies, days, sessions, adherence }) => {
          this.summary.set(summary);
          this.equity.set(equity);
          this.adherence.set(adherence);
          this.tables.set({
            strategy: strategies.map(s => ({ ...s, label: s.strategyName })),
            day: days,
            session: sessions
          });
          this.state.set('ready');
        },
        error: err => {
          this.error.set(describeHttpError(err));
          this.state.set('error');
        }
      });
  }

  signed(value: number): string {
    const formatted = Math.abs(value).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    return value > 0 ? `+${formatted}` : value < 0 ? `−${formatted}` : formatted;
  }

  // Bars diverge from the centre line: gains to the right, losses to the left, scaled to the largest move
  barWidth(row: BreakdownRow): number {
    const max = this.maxAbsPnL();
    return max === 0 ? 0 : (Math.abs(row.totalPnL) / max) * 50;
  }

  barLeft(row: BreakdownRow): number {
    return row.totalPnL >= 0 ? 50 : 50 - this.barWidth(row);
  }
}

// "2026-10-08" → "8 Oct" (or "8 Oct 2026"); parsed as a calendar date so the time zone cannot shift the day
function formatDay(isoDate: string, withYear = false): string {
  const [year, month, day] = isoDate.slice(0, 10).split('-').map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: withYear ? 'numeric' : undefined, timeZone: 'UTC' });
}
