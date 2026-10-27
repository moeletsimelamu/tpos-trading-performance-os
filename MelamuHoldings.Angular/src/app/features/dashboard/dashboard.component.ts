import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { BaseChartDirective, provideCharts, withDefaultRegisterables } from 'ng2-charts';
import { ChartConfiguration, ChartData } from 'chart.js';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { forkJoin } from 'rxjs';
import { ApiService } from '../../services/api.service';
import { AnalyticsSummary, TimePerformance } from '../../models/analytics.model';
import { LoadErrorComponent } from '../../shared/load-error.component';
import { describeHttpError } from '../../shared/http-error';

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [DecimalPipe, RouterLink, BaseChartDirective, LoadErrorComponent],
  providers: [provideCharts(withDefaultRegisterables())],
  template: `
    <div class="dashboard">
      <header class="page-header">
        <div>
          <h1>Dashboard</h1>
          <p class="text-muted mono">Performance overview · All trades</p>
        </div>
      </header>

      @switch (state()) {
        @case ('loading') {
          <div class="loading-state">
            <div class="spinner"></div>
            <span class="loading-text">Loading performance data...</span>
          </div>
        }
        @case ('error') {
          <app-load-error title="Could not load performance data" [message]="error()" (retry)="load()" />
        }
        @case ('ready') {
          @if (summary(); as summary) {
            @if (summary.totalTrades === 0) {
              <div class="card empty-card">
                <div class="empty-title">No trades recorded yet</div>
                <p class="text-muted">Performance metrics are calculated from your journal. <a routerLink="/upload">Import a CSV</a> to get started.</p>
              </div>
            } @else {
              <!-- KPI Cards -->
              <section class="kpi-grid">
                <div class="card kpi-card">
                  <span class="kpi-label">Total P&L</span>
                  <span class="kpi-value mono" [class.text-green]="summary.totalPnL >= 0" [class.text-red]="summary.totalPnL < 0">
                    {{ summary.totalPnL >= 0 ? '+' : '' }}{{ summary.totalPnL | number:'1.2-2' }}
                  </span>
                  <span class="kpi-sub">{{ summary.totalTrades }} trades</span>
                </div>

                <div class="card kpi-card">
                  <span class="kpi-label">Win Rate</span>
                  <span class="kpi-value mono" [class.text-green]="summary.winRate >= 50" [class.text-red]="summary.winRate < 50">
                    {{ summary.winRate | number:'1.1-1' }}%
                  </span>
                  <span class="kpi-sub">{{ summary.winCount }}W · {{ summary.lossCount }}L · {{ summary.breakevenCount }}B</span>
                </div>

                <div class="card kpi-card">
                  <span class="kpi-label">Expectancy</span>
                  <span class="kpi-value mono" [class.text-green]="summary.expectancy >= 0" [class.text-red]="summary.expectancy < 0">
                    {{ summary.expectancy >= 0 ? '+' : '' }}{{ summary.expectancy | number:'1.2-2' }}
                  </span>
                  <span class="kpi-sub">per trade avg</span>
                </div>

                <div class="card kpi-card">
                  <span class="kpi-label">Profit Factor</span>
                  <span class="kpi-value mono" [class.text-green]="summary.profitFactor >= 1" [class.text-red]="summary.profitFactor < 1">
                    {{ summary.profitFactor | number:'1.2-2' }}
                  </span>
                  <span class="kpi-sub">gross profit / loss</span>
                </div>

                <div class="card kpi-card">
                  <span class="kpi-label">Average RR</span>
                  <span class="kpi-value mono text-amber">
                    {{ summary.averageRR | number:'1.2-2' }}R
                  </span>
                  <span class="kpi-sub">reward-to-risk</span>
                </div>

                <div class="card kpi-card">
                  <span class="kpi-label">Max Drawdown</span>
                  <span class="kpi-value mono text-red">
                    -{{ summary.maxDrawdown | number:'1.2-2' }}
                  </span>
                  <span class="kpi-sub">peak-to-trough</span>
                </div>
              </section>

              <!-- Row 1: Equity Curve + Strategy Breakdown -->
              <section class="charts-grid">
                <div class="card chart-card chart-wide">
                  <div class="chart-header">
                    <span class="chart-title">Equity Curve</span>
                    <span class="tag tag-green">Cumulative P&L</span>
                  </div>
                  @if (equityChartData(); as data) {
                    <div class="chart-body">
                      <canvas baseChart [data]="data" [options]="chartOptions" type="line"></canvas>
                    </div>
                  }
                </div>

                <div class="card chart-card">
                  <div class="chart-header">
                    <span class="chart-title">Strategy Breakdown</span>
                    <span class="tag tag-amber">By P&L</span>
                  </div>
                  @if (strategyChartData(); as data) {
                    <div class="chart-body">
                      <canvas baseChart [data]="data" [options]="chartOptions" type="bar"></canvas>
                    </div>
                  }
                </div>
              </section>

              <!-- Row 2: Day + Session Performance -->
              <section class="charts-grid charts-row-2">
                <div class="card chart-card">
                  <div class="chart-header">
                    <span class="chart-title">Performance by Day</span>
                    <span class="tag tag-blue">P&L</span>
                  </div>
                  @if (dayChartData(); as data) {
                    <div class="chart-body">
                      <canvas baseChart [data]="data" [options]="chartOptions" type="bar"></canvas>
                    </div>
                  }
                </div>

                <div class="card chart-card">
                  <div class="chart-header">
                    <span class="chart-title">Performance by Session</span>
                    <span class="tag tag-blue">P&L</span>
                  </div>
                  @if (sessionChartData(); as data) {
                    <div class="chart-body">
                      <canvas baseChart [data]="data" [options]="chartOptions" type="bar"></canvas>
                    </div>
                  }
                </div>
              </section>
            }
          }
        }
      }
    </div>
  `,
  styles: [`
    .dashboard { max-width: 1400px; }

    .page-header {
      margin-bottom: 2rem;
      h1 {
        font-family: var(--font-display);
        font-size: 1.75rem;
        font-weight: 800;
        color: var(--text-primary);
      }
    }

    .kpi-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
      gap: 1rem;
      margin-bottom: 1.5rem;
    }

    .kpi-card { display: flex; flex-direction: column; gap: 0.25rem; }

    .kpi-label {
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--text-secondary);
      font-weight: 600;
    }

    .kpi-value { font-size: 1.6rem; font-weight: 500; line-height: 1.2; }

    .kpi-sub {
      font-size: 11px;
      color: var(--text-muted);
      font-family: var(--font-mono);
    }

    .charts-grid {
      display: grid;
      grid-template-columns: 2fr 1fr;
      gap: 1rem;
    }

    .charts-row-2 { margin-top: 1rem; }

    .chart-card { display: flex; flex-direction: column; }

    .chart-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 1.25rem;
    }

    .chart-title { font-weight: 700; font-size: 14px; color: var(--text-primary); }

    .chart-body { position: relative; height: 260px; }

    .empty-card {
      .empty-title { font-weight: 700; font-size: 15px; margin-bottom: 0.35rem; }
      a { color: var(--accent-amber); }
    }
  `]
})
export class DashboardComponent {
  private api = inject(ApiService);
  private destroyRef = inject(DestroyRef);

  readonly state = signal<LoadState>('loading');
  readonly error = signal('');
  readonly summary = signal<AnalyticsSummary | null>(null);
  readonly equityChartData = signal<ChartData<'line'> | null>(null);
  readonly strategyChartData = signal<ChartData<'bar'> | null>(null);
  readonly dayChartData = signal<ChartData<'bar'> | null>(null);
  readonly sessionChartData = signal<ChartData<'bar'> | null>(null);

  readonly chartOptions: ChartConfiguration['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { display: false } },
    scales: {
      x: { ticks: { color: '#8888a0', font: { family: 'DM Mono', size: 10 } }, grid: { color: '#1e1e2a' } },
      y: { ticks: { color: '#8888a0', font: { family: 'DM Mono', size: 10 } }, grid: { color: '#1e1e2a' } }
    }
  };

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');

    forkJoin({
      summary: this.api.getSummary(),
      equity: this.api.getEquityCurve(),
      strategies: this.api.getStrategyPerformance(),
      days: this.api.getPerformanceByDay(),
      sessions: this.api.getPerformanceBySession()
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ summary, equity, strategies, days, sessions }) => {
          this.summary.set(summary);
          this.equityChartData.set({
            labels: equity.map(p => p.date),
            datasets: [{
              data: equity.map(p => p.cumulativePnL),
              borderColor: '#10b981',
              backgroundColor: 'rgba(16,185,129,0.08)',
              fill: true,
              tension: 0.4,
              pointRadius: 3,
              pointBackgroundColor: '#10b981'
            }]
          });
          this.strategyChartData.set(pnlBarChart(
            strategies.map(s => ({ label: s.strategyName, totalPnL: s.totalPnL })), 'rgba(16,185,129,0.7)'));
          this.dayChartData.set(pnlBarChart(days, 'rgba(16,185,129,0.7)'));
          this.sessionChartData.set(pnlBarChart(sessions, 'rgba(59,130,246,0.7)'));
          this.state.set('ready');
        },
        error: err => {
          this.error.set(describeHttpError(err));
          this.state.set('error');
        }
      });
  }
}

function pnlBarChart(rows: Pick<TimePerformance, 'label' | 'totalPnL'>[], positiveColour: string): ChartData<'bar'> {
  return {
    labels: rows.map(r => r.label),
    datasets: [{
      data: rows.map(r => r.totalPnL),
      backgroundColor: rows.map(r => r.totalPnL >= 0 ? positiveColour : 'rgba(239,68,68,0.7)'),
      borderRadius: 4
    }]
  };
}
