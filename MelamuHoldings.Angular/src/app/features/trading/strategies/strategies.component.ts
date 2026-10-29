import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { TradingApiService } from '../trading-api.service';
import { AdherenceReport, Strategy, StrategyAdherence } from '../models/strategy.model';
import { AdherencePanelComponent } from './adherence-panel.component';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';
import { IconComponent } from '../../../shared/ui/icon.component';

@Component({
  selector: 'app-strategies',
  standalone: true,
  imports: [DecimalPipe, RouterLink, AdherencePanelComponent, LoadErrorComponent, PageHeaderComponent, IconComponent],
  template: `
    <div class="page">
      <app-page-header section="Trading operations" title="Strategies"
        description="Your strategies and their rules. Mandatory rules decide adherence; optional ones are reminders. Answer the checklist on each trade.">
        <a class="btn btn-primary" routerLink="/trading/strategies/new"><app-icon name="plus" [size]="16" />New strategy</a>
      </app-page-header>

      @switch (state()) {
        @case ('loading') { <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading strategies...</span></div> }
        @case ('error') { <app-load-error title="Could not load strategies" [message]="error()" (retry)="load()" /> }
        @case ('ready') {
          @if (report(); as r) { <app-adherence-panel [report]="r" /> }

          <div class="card table-card list">
            <div class="toolbar">
              <label class="inline"><input type="checkbox" [checked]="showArchived()" (change)="toggleArchived()" /> Show archived</label>
            </div>
            @if (strategies().length === 0) {
              <p class="empty">No strategies yet. Create one with its rules, or name a strategy on a trade and add rules to it here.</p>
            } @else {
              <table class="data-table">
                <thead><tr><th>Strategy</th><th>Rules</th><th class="num">Trades</th><th class="num">Adherence</th><th>Status</th></tr></thead>
                <tbody>
                  @for (s of strategies(); track s.id) {
                    <tr [class.archived]="s.status === 'Archived'">
                      <td class="name">
                        <a [routerLink]="['/trading/strategies', s.id]">{{ s.name }}</a>
                        @if (s.description) { <div class="small muted">{{ s.description }}</div> }
                      </td>
                      <td class="small nowrap">
                        @if (s.rules.length === 0) { <span class="muted">No rules yet</span> }
                        @else { {{ mandatory(s) }} mandatory · {{ s.rules.length - mandatory(s) }} optional }
                      </td>
                      <td class="num">{{ s.tradeCount }}</td>
                      <td class="num nowrap">
                        @if (adherence(s.id); as a) {
                          @if (a.adherenceRate != null) { {{ a.adherenceRate | number: '1.0-1' }}% <span class="small muted">({{ a.compliant }}/{{ a.withChecklist }})</span> }
                          @else { <span class="muted">No checklists</span> }
                        } @else { <span class="muted">—</span> }
                      </td>
                      <td>
                        @if (s.status === 'Archived') { <span class="tag">Archived</span> } @else { <span class="tag tag-info">Active</span> }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </div>
        }
      }
    </div>
  `,
  styles: [`
    .list { margin-top: var(--space-5); }
    .inline { display: flex; gap: var(--space-2); align-items: center; font-size: 13.5px; }
    .archived td { color: var(--text-muted); }
    .name { min-width: 200px; }
    .empty { padding: var(--space-5); color: var(--text-secondary); }
  `]
})
export class StrategiesComponent {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);

  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly error = signal('');
  readonly strategies = signal<Strategy[]>([]);
  readonly report = signal<AdherenceReport | null>(null);
  readonly showArchived = signal(false);

  private readonly byStrategy = computed(() => new Map((this.report()?.byStrategy ?? []).map(a => [a.strategyId, a])));

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    forkJoin({ strategies: this.api.getStrategies(this.showArchived()), report: this.api.getAdherence() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ strategies, report }) => { this.strategies.set(strategies); this.report.set(report); this.state.set('ready'); },
        error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
      });
  }

  toggleArchived(): void {
    this.showArchived.set(!this.showArchived());
    this.load();
  }

  mandatory(s: Strategy): number {
    return s.rules.filter(r => r.isMandatory).length;
  }

  adherence(id: number): StrategyAdherence | undefined {
    return this.byStrategy().get(id);
  }
}
