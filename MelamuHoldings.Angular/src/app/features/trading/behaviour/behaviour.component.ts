import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TradingApiService } from '../trading-api.service';
import { BehaviourSummary } from '../models/analytics.model';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';
import { IconComponent } from '../../../shared/ui/icon.component';

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-behaviour',
  standalone: true,
  imports: [DecimalPipe, LoadErrorComponent, PageHeaderComponent, IconComponent],
  template: `
    <div class="page">
      <app-page-header section="Trading operations" title="Behaviour"
        description="Patterns detected in the journal: trades after losses, busy days, loss streaks and rule overrides." />

      @switch (state()) {
        @case ('loading') {
          <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading behaviour data...</span></div>
        }
        @case ('error') {
          <app-load-error title="Could not load behaviour data" [message]="error()" (retry)="load()" />
        }
        @case ('ready') {
          @if (summary(); as summary) {
            <section class="metric-strip" aria-label="Behaviour summary">
              <div class="metric">
                <span class="metric-label">Revenge trading</span>
                <span class="metric-value">{{ summary.revengeTradingCount }}</span>
                <span class="metric-note">Trades within 20 min of a loss</span>
              </div>
              <div class="metric">
                <span class="metric-label">Overtrading days</span>
                <span class="metric-value">{{ summary.overtradingDayCount }}</span>
                <span class="metric-note">More than 5 trades in a day</span>
              </div>
              <div class="metric">
                <span class="metric-label">Longest loss streak</span>
                <span class="metric-value">{{ summary.maxConsecutiveLosses }}</span>
                <span class="metric-note">Consecutive losing trades</span>
              </div>
              <div class="metric">
                <span class="metric-label">Rule overrides</span>
                <span class="metric-value">{{ summary.strategyDeviationCount }}</span>
                <span class="metric-note">Trades marked as manual overrides</span>
              </div>
              <div class="metric">
                <span class="metric-label">Override P&amp;L</span>
                <span class="metric-value" [class.text-positive]="summary.deviationPnLImpact > 0" [class.text-negative]="summary.deviationPnLImpact < 0">
                  {{ summary.deviationPnLImpact > 0 ? '+' : '' }}{{ summary.deviationPnLImpact | number:'1.2-2' }}
                </span>
                <span class="metric-note">Combined P&amp;L of overrides</span>
              </div>
            </section>

            @if (summary.strategyDeviationCount > 0) {
              <div class="notice notice-warning insight">
                <app-icon name="info" [size]="18" />
                <p>
                  <strong>{{ summary.strategyDeviationCount }} {{ summary.strategyDeviationCount === 1 ? 'trade was' : 'trades were' }} taken as manual overrides</strong>
                  of the strategy's rules, with a combined P&amp;L of
                  {{ summary.deviationPnLImpact > 0 ? '+' : '' }}{{ summary.deviationPnLImpact | number:'1.2-2' }}.
                  Review them to see whether the override was justified.
                </p>
              </div>
            }

            <section class="card table-card alerts" aria-labelledby="alerts-heading">
              <div class="card-header">
                <h2 id="alerts-heading">Alerts</h2>
                <span class="card-note">{{ summary.alerts.length }} detected</span>
              </div>
              @if (summary.alerts.length > 0) {
                <ul class="alert-list">
                  @for (alert of summary.alerts; track $index) {
                    <li class="alert-item">
                      <app-icon name="alert" [size]="18" [class.critical]="alert.severity === 'Critical'" />
                      <div class="alert-body">
                        <p class="alert-type">{{ formatAlertType(alert.alertType) }}</p>
                        <p class="alert-desc">{{ alert.description }}</p>
                      </div>
                      <div class="alert-meta">
                        <span class="tag" [class.tag-warning]="alert.severity === 'Warning'" [class.tag-negative]="alert.severity === 'Critical'">{{ alert.severity }}</span>
                        <span class="small muted">{{ alert.detectedOnDate }}</span>
                      </div>
                    </li>
                  }
                </ul>
              } @else {
                <p class="empty-state small">No behaviour alerts detected in the journal.</p>
              }
            </section>
          }
        }
      }
    </div>
  `,
  styles: [`
    .insight { margin: var(--space-5) 0 0; app-icon { margin-top: 1px; } }
    .alerts { margin-top: var(--space-5); }
    .alert-list { list-style: none; }
    .alert-item {
      display: flex; gap: var(--space-3); align-items: flex-start; padding: var(--space-3) var(--space-5);
      border-bottom: 1px solid var(--border-subtle);
      &:last-child { border-bottom: none; }
      app-icon { color: var(--warning); margin-top: 2px; }
      app-icon.critical { color: var(--negative); }
    }
    .alert-body { flex: 1; min-width: 0; }
    .alert-type { font-weight: 500; }
    .alert-desc { font-size: var(--text-sm); color: var(--text-secondary); }
    .alert-meta { display: flex; flex-direction: column; align-items: flex-end; gap: 2px; flex-shrink: 0; }
    .empty-state.small { padding: var(--space-6); }
  `]
})
export class BehaviourComponent {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);

  readonly state = signal<LoadState>('loading');
  readonly error = signal('');
  readonly summary = signal<BehaviourSummary | null>(null);

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.api.getBehaviour()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: data => {
          this.summary.set(data);
          this.state.set('ready');
        },
        error: err => {
          this.error.set(describeHttpError(err));
          this.state.set('error');
        }
      });
  }

  formatAlertType(type: string): string {
    switch (type) {
      case 'RevengeTrading': return 'Revenge trading';
      case 'Overtrading': return 'Overtrading';
      case 'ConsecutiveLosses': return 'Loss streak';
      default: return type;
    }
  }
}
