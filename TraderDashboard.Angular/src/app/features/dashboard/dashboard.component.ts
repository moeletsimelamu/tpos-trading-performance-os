import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration, ChartData } from 'chart.js';
import { ApiService } from '../../services/api.service';
import { AnalyticsSummary, EquityPoint, StrategyPerformance } from '../../models/analytics.model';
import { NavigationService } from '../../services/navigation.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, BaseChartDirective],
  template: `
    <div class="dashboard">
      <header class="page-header">
        <div>
          <h1>Dashboard</h1>
          <p class="text-muted mono">Performance overview · All trades</p>
        </div>
      </header>

      <!-- KPI Cards -->
      <section class="kpi-grid" *ngIf="summary">
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

      <!-- Charts Row -->
      <section class="charts-grid">
        <div class="card chart-card chart-wide">
          <div class="chart-header">
            <span class="chart-title">Equity Curve</span>
            <span class="tag tag-green">Cumulative P&L</span>
          </div>
          <div class="chart-body" *ngIf="equityChartData">
            <canvas baseChart
              [data]="equityChartData"
              [options]="equityChartOptions"
              type="line">
            </canvas>
          </div>
        </div>

        <div class="card chart-card">
          <div class="chart-header">
            <span class="chart-title">Strategy Breakdown</span>
            <span class="tag tag-amber">By P&L</span>
          </div>
          <div class="chart-body" *ngIf="strategyChartData">
            <canvas baseChart
              [data]="strategyChartData"
              [options]="barChartOptions"
              type="bar">
            </canvas>
          </div>
        </div>
      </section>
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

    .kpi-card {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }

    .kpi-label {
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--text-secondary);
      font-weight: 600;
    }

    .kpi-value {
      font-size: 1.6rem;
      font-weight: 500;
      line-height: 1.2;
    }

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

    .chart-card { display: flex; flex-direction: column; }
    .chart-wide { grid-column: span 1; }

    .chart-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 1.25rem;
    }

    .chart-title {
      font-weight: 700;
      font-size: 14px;
      color: var(--text-primary);
    }

    .chart-body { position: relative; height: 260px; }
  `]
})
export class DashboardComponent implements OnInit {
  summary: AnalyticsSummary | null = null;
  equityChartData: ChartData<'line'> | null = null;
  strategyChartData: ChartData<'bar'> | null = null;

  equityChartOptions: ChartConfiguration['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { display: false } },
    scales: {
      x: { ticks: { color: '#8888a0', font: { family: 'DM Mono', size: 10 } }, grid: { color: '#1e1e2a' } },
      y: { ticks: { color: '#8888a0', font: { family: 'DM Mono', size: 10 } }, grid: { color: '#1e1e2a' } }
    }
  };

  barChartOptions: ChartConfiguration['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { display: false } },
    scales: {
      x: { ticks: { color: '#8888a0', font: { family: 'DM Mono', size: 10 } }, grid: { color: '#1e1e2a' } },
      y: { ticks: { color: '#8888a0', font: { family: 'DM Mono', size: 10 } }, grid: { color: '#1e1e2a' } }
    }
  };

  constructor(private api: ApiService, private nav: NavigationService) {}

  ngOnInit(): void {
    this.nav.onNavigateTo('dashboard', () => this.loadData());
  }

  loadData(): void {
    this.api.getSummary().subscribe(s => this.summary = s);

    this.api.getEquityCurve().subscribe(points => {
      this.equityChartData = {
        labels: points.map(p => p.date),
        datasets: [{
          data: points.map(p => p.cumulativePnL),
          borderColor: '#10b981',
          backgroundColor: 'rgba(16,185,129,0.08)',
          fill: true,
          tension: 0.4,
          pointRadius: 3,
          pointBackgroundColor: '#10b981'
        }]
      };
    });

    this.api.getStrategyPerformance().subscribe(strategies => {
      this.strategyChartData = {
        labels: strategies.map(s => s.strategyName),
        datasets: [{
          data: strategies.map(s => s.totalPnL),
          backgroundColor: strategies.map(s =>
            s.totalPnL >= 0 ? 'rgba(16,185,129,0.7)' : 'rgba(239,68,68,0.7)'
          ),
          borderRadius: 4
        }]
      };
    });
  }
}