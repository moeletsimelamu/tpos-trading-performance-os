import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { BehaviourSummary, BehaviourAlert } from '../../models/analytics.model';
import { NavigationService } from '../../services/navigation.service';

@Component({
  selector: 'app-behaviour',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="behaviour-page">
      <header class="page-header">
        <div>
          <h1>Behaviour Analytics</h1>
          <p class="text-muted mono">Patterns, discipline metrics, and risk flags</p>
        </div>
      </header>

      <ng-container *ngIf="summary">

        <!-- Metric Cards -->
        <section class="metrics-grid">
          <div class="card metric-card">
            <span class="metric-label">Revenge Trading</span>
            <span class="metric-value mono" [class.text-red]="summary.revengeTradingCount > 0" [class.text-green]="summary.revengeTradingCount === 0">
              {{ summary.revengeTradingCount }}
            </span>
            <span class="metric-sub">detected instances</span>
          </div>

          <div class="card metric-card">
            <span class="metric-label">Overtrading Days</span>
            <span class="metric-value mono" [class.text-red]="summary.overtradingDayCount > 0" [class.text-green]="summary.overtradingDayCount === 0">
              {{ summary.overtradingDayCount }}
            </span>
            <span class="metric-sub">days above limit</span>
          </div>

          <div class="card metric-card">
            <span class="metric-label">Max Loss Streak</span>
            <span class="metric-value mono" [class.text-red]="summary.maxConsecutiveLosses >= 3" [class.text-amber]="summary.maxConsecutiveLosses === 2">
              {{ summary.maxConsecutiveLosses }}
            </span>
            <span class="metric-sub">consecutive losses</span>
          </div>

          <div class="card metric-card">
            <span class="metric-label">Strategy Deviations</span>
            <span class="metric-value mono" [class.text-red]="summary.strategyDeviationCount > 0" [class.text-green]="summary.strategyDeviationCount === 0">
              {{ summary.strategyDeviationCount }}
            </span>
            <span class="metric-sub">manual overrides</span>
          </div>

          <div class="card metric-card">
            <span class="metric-label">Deviation P&L Impact</span>
            <span class="metric-value mono"
              [class.text-green]="summary.deviationPnLImpact > 0"
              [class.text-red]="summary.deviationPnLImpact < 0">
              {{ summary.deviationPnLImpact >= 0 ? '+' : '' }}{{ summary.deviationPnLImpact | number:'1.2-2' }}
            </span>
            <span class="metric-sub">P&L from deviations</span>
          </div>

          <div class="card metric-card">
            <span class="metric-label">Total Alerts</span>
            <span class="metric-value mono" [class.text-red]="summary.alerts.length > 0" [class.text-green]="summary.alerts.length === 0">
              {{ summary.alerts.length }}
            </span>
            <span class="metric-sub">behaviour flags</span>
          </div>
        </section>

        <!-- Deviation insight -->
        <div class="card insight-card" *ngIf="summary.strategyDeviationCount > 0">
          <div class="insight-icon">⚠</div>
          <div class="insight-body">
            <div class="insight-title">Strategy Deviation Insight</div>
            <div class="insight-text">
              You deviated from your strategy rules
              <strong>{{ summary.strategyDeviationCount }} time{{ summary.strategyDeviationCount > 1 ? 's' : '' }}</strong>.
              These trades resulted in a total P&L of
              <strong [class.text-green]="summary.deviationPnLImpact >= 0" [class.text-red]="summary.deviationPnLImpact < 0">
                {{ summary.deviationPnLImpact >= 0 ? '+' : '' }}{{ summary.deviationPnLImpact | number:'1.2-2' }}
              </strong>.
              {{ summary.deviationPnLImpact < 0 ? 'Deviating from your rules is costing you money.' : 'These deviations were profitable, but consistency builds long-term edge.' }}
            </div>
          </div>
        </div>

        <!-- Alert Feed -->
        <div class="card alerts-card">
          <div class="alerts-header">
            <span class="alerts-title">Alert Feed</span>
            <span class="tag tag-amber mono">{{ summary.alerts.length }} alerts</span>
          </div>

          <div class="alert-list" *ngIf="summary.alerts.length > 0">
            <div class="alert-item" *ngFor="let alert of summary.alerts"
              [class.alert-warning]="alert.severity === 'Warning'"
              [class.alert-critical]="alert.severity === 'Critical'">
              <div class="alert-left">
                <span class="alert-type">{{ formatAlertType(alert.alertType) }}</span>
                <span class="alert-desc">{{ alert.description }}</span>
              </div>
              <div class="alert-right">
                <span class="tag" [class.tag-amber]="alert.severity === 'Warning'" [class.tag-red]="alert.severity === 'Critical'">
                  {{ alert.severity }}
                </span>
                <span class="alert-date mono">{{ alert.detectedOnDate }}</span>
              </div>
            </div>
          </div>

          <p class="empty-state mono" *ngIf="summary.alerts.length === 0">
            ✓ No behaviour alerts detected. Clean trading record.
          </p>
        </div>

      </ng-container>

      <div class="loading mono" *ngIf="!summary">Loading behaviour data...</div>
    </div>
  `,
  styles: [`
    .behaviour-page { max-width: 1100px; }

    .page-header {
      margin-bottom: 1.5rem;
      h1 { font-family: var(--font-display); font-size: 1.75rem; font-weight: 800; }
    }

    .metrics-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
      gap: 1rem;
      margin-bottom: 1rem;
    }

    .metric-card {
      display: flex;
      flex-direction: column;
      gap: 0.2rem;
    }

    .metric-label {
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--text-secondary);
      font-weight: 600;
    }

    .metric-value {
      font-size: 2rem;
      font-weight: 500;
      line-height: 1.2;
    }

    .metric-sub {
      font-size: 11px;
      color: var(--text-muted);
      font-family: var(--font-mono);
    }

    .insight-card {
      display: flex;
      align-items: flex-start;
      gap: 1rem;
      margin-bottom: 1rem;
      border-left: 3px solid var(--accent-amber);
    }

    .insight-icon {
      font-size: 1.25rem;
      color: var(--accent-amber);
      flex-shrink: 0;
      margin-top: 2px;
    }

    .insight-title {
      font-weight: 700;
      font-size: 14px;
      margin-bottom: 0.35rem;
    }

    .insight-text {
      font-size: 13px;
      color: var(--text-secondary);
      line-height: 1.6;
      strong { color: var(--text-primary); }
    }

    .alerts-card { }

    .alerts-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 1.25rem;
    }

    .alerts-title { font-weight: 700; font-size: 15px; }

    .alert-list { display: flex; flex-direction: column; gap: 0.5rem; }

    .alert-item {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 0.85rem 1rem;
      border-radius: var(--radius);
      background: var(--bg-secondary);
      border: 1px solid var(--border-subtle);
      gap: 1rem;

      &.alert-warning { border-left: 3px solid var(--accent-amber); }
      &.alert-critical { border-left: 3px solid var(--accent-red); }
    }

    .alert-left {
      display: flex;
      flex-direction: column;
      gap: 0.2rem;
    }

    .alert-type {
      font-size: 12px;
      font-weight: 700;
      color: var(--text-primary);
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .alert-desc {
      font-size: 12px;
      color: var(--text-secondary);
      font-family: var(--font-mono);
    }

    .alert-right {
      display: flex;
      flex-direction: column;
      align-items: flex-end;
      gap: 0.3rem;
      flex-shrink: 0;
    }

    .alert-date {
      font-size: 11px;
      color: var(--text-muted);
    }

    .empty-state {
      color: var(--accent-green);
      font-size: 13px;
      padding: 0.5rem 0;
    }

    .loading {
      color: var(--text-muted);
      font-size: 13px;
      padding: 2rem 0;
    }
  `]
})
export class BehaviourComponent implements OnInit {
  summary: BehaviourSummary | null = null;

  constructor(private api: ApiService, private nav: NavigationService) {}

  ngOnInit(): void {
    this.nav.onNavigateTo('behaviour', () => this.loadData());
  }

loadData(): void {
  this.api.getBehaviour().subscribe(data => this.summary = data);
}

  formatAlertType(type: string): string {
    switch (type) {
      case 'RevengeTrading': return 'Revenge Trading';
      case 'Overtrading': return 'Overtrading';
      case 'ConsecutiveLosses': return 'Loss Streak';
      default: return type;
    }
  }
}