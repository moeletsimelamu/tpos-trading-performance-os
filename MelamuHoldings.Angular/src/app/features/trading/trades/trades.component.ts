import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TradingApiService } from '../trading-api.service';
import { Trade } from '../models/trade.model';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';
import { IconComponent } from '../../../shared/ui/icon.component';

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-trades',
  standalone: true,
  imports: [DecimalPipe, FormsModule, RouterLink, LoadErrorComponent, PageHeaderComponent, IconComponent],
  template: `
    <div class="page">
      <app-page-header section="Trading operations" title="Trades"
        [description]="filtered().length + ' of ' + trades().length + ' trades'">
        <a class="btn" routerLink="/trading/import"><app-icon name="import" [size]="16" />Import trades</a>
      </app-page-header>

      @switch (state()) {
        @case ('loading') {
          <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading trades...</span></div>
        }
        @case ('error') {
          <app-load-error title="Could not load trades" [message]="error()" (retry)="load()" />
        }
        @case ('ready') {
          @if (actionError()) { <p class="notice notice-error" role="alert">{{ actionError() }}</p> }

          <div class="card table-card">
            <div class="toolbar" role="search">
              <div class="field grow">
                <label for="trade-search">Search</label>
                <input id="trade-search" type="search" placeholder="Instrument, strategy, session or day"
                  [ngModel]="searchTerm()" (ngModelChange)="searchTerm.set($event)" />
              </div>
              <div class="field">
                <label for="trade-direction">Direction</label>
                <select id="trade-direction" [ngModel]="directionFilter()" (ngModelChange)="directionFilter.set($event)">
                  <option value="">All directions</option>
                  <option value="Long">Long</option>
                  <option value="Short">Short</option>
                </select>
              </div>
              <div class="field">
                <label for="trade-result">Result</label>
                <select id="trade-result" [ngModel]="resultFilter()" (ngModelChange)="resultFilter.set($event)">
                  <option value="">All results</option>
                  <option value="win">Wins</option>
                  <option value="loss">Losses</option>
                  <option value="breakeven">Breakeven</option>
                </select>
              </div>
              <button type="button" class="btn" (click)="clearFilters()">Clear</button>
            </div>

            <table class="data-table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Instrument</th>
                  <th>Direction</th>
                  <th class="num">Entry</th>
                  <th class="num">Exit</th>
                  <th class="num">Risk</th>
                  <th class="num">P&amp;L</th>
                  <th class="num">R</th>
                  <th>Strategy</th>
                  <th>Session</th>
                  <th class="num">Duration</th>
                  <th>Override</th>
                  <th><span class="visually-hidden">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (trade of filtered(); track trade.id) {
                  <tr>
                    <td class="nowrap">{{ trade.tradeDate }}</td>
                    <td class="strong">{{ trade.instrument }}</td>
                    <td>{{ trade.direction }}</td>
                    <td class="num">{{ trade.entryPrice | number:'1.2-5' }}</td>
                    <td class="num">{{ trade.exitPrice | number:'1.2-5' }}</td>
                    <td class="num">{{ trade.riskAmount | number:'1.2-2' }}</td>
                    <td class="num" [class.text-positive]="trade.pnL > 0" [class.text-negative]="trade.pnL < 0">
                      {{ trade.pnL > 0 ? '+' : '' }}{{ trade.pnL | number:'1.2-2' }}
                    </td>
                    <td class="num">{{ trade.rr | number:'1.2-2' }}R</td>
                    <td>{{ trade.strategyName ?? '—' }}</td>
                    <td class="nowrap">{{ trade.session }}</td>
                    <td class="num">{{ trade.tradeDuration }} min</td>
                    <td>
                      @if (trade.isManualOverride) { <span class="tag tag-warning">Override</span> }
                      @else { <span class="muted">—</span> }
                    </td>
                    <td class="actions">
                      @if (confirmingDelete() === trade.id) {
                        <button type="button" class="btn btn-sm btn-danger" [disabled]="deletingId() === trade.id" (click)="deleteTrade(trade.id)">Confirm delete</button>
                        <button type="button" class="btn-link small" (click)="confirmingDelete.set(null)">Keep</button>
                      } @else {
                        <button type="button" class="icon-button" [attr.aria-label]="'Delete trade ' + trade.id" title="Delete trade"
                          (click)="confirmingDelete.set(trade.id)"><app-icon name="trash" [size]="16" /></button>
                      }
                    </td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="13" class="empty-row">
                      {{ trades().length === 0 ? 'No trades recorded yet. Import your trade history from the Import page.' : 'No trades match these filters.' }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    </div>
  `,
  styles: [`
    .grow { flex: 1; min-width: 220px; }
    .data-table td { white-space: nowrap; }
    .strong { font-weight: 500; }
    .actions { display: flex; gap: var(--space-3); align-items: center; justify-content: flex-end; }
    .icon-button {
      display: flex; padding: 4px; border: none; border-radius: var(--radius-sm); background: none;
      color: var(--text-muted); cursor: pointer;
      &:hover { color: var(--negative); background: var(--negative-bg); }
    }
    .empty-row { text-align: center; color: var(--text-muted); padding: var(--space-10) var(--space-4); }
  `]
})
export class TradesComponent {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);

  readonly state = signal<LoadState>('loading');
  readonly error = signal('');
  readonly actionError = signal('');
  readonly deletingId = signal<number | null>(null);
  // Deleting is two clicks: the first asks, the second deletes
  readonly confirmingDelete = signal<number | null>(null);
  readonly trades = signal<Trade[]>([]);
  readonly searchTerm = signal('');
  readonly directionFilter = signal('');
  readonly resultFilter = signal('');

  readonly filtered = computed(() => {
    const term = this.searchTerm().toLowerCase();
    const direction = this.directionFilter();
    const result = this.resultFilter();

    return this.trades().filter(trade => {
      const matchesSearch = !term ||
        trade.instrument.toLowerCase().includes(term) ||
        !!trade.strategyName?.toLowerCase().includes(term) ||
        trade.session.toLowerCase().includes(term) ||
        trade.dayOfWeek.toLowerCase().includes(term);

      const matchesDirection = !direction || trade.direction === direction;

      const matchesResult = !result ||
        (result === 'win' && trade.pnL > 0) ||
        (result === 'loss' && trade.pnL < 0) ||
        (result === 'breakeven' && trade.pnL === 0);

      return matchesSearch && matchesDirection && matchesResult;
    });
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.api.getTrades()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: trades => {
          this.trades.set(trades);
          this.state.set('ready');
        },
        error: err => {
          this.error.set(describeHttpError(err));
          this.state.set('error');
        }
      });
  }

  clearFilters(): void {
    this.searchTerm.set('');
    this.directionFilter.set('');
    this.resultFilter.set('');
  }

  deleteTrade(id: number): void {
    if (this.deletingId() !== null) return;

    this.actionError.set('');
    this.deletingId.set(id);
    this.api.deleteTrade(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.trades.update(trades => trades.filter(t => t.id !== id));
          this.deletingId.set(null);
          this.confirmingDelete.set(null);
        },
        error: err => {
          this.actionError.set(`Trade was not deleted: ${describeHttpError(err)}`);
          this.deletingId.set(null);
        }
      });
  }
}
