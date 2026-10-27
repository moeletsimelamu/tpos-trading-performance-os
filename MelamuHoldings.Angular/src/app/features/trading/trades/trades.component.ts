import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TradingApiService } from '../trading-api.service';
import { Trade } from '../models/trade.model';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-trades',
  standalone: true,
  imports: [DecimalPipe, FormsModule, LoadErrorComponent],
  template: `
    <div class="trades-page">
      <header class="page-header">
        <div>
          <h1>Trades</h1>
          <p class="text-muted mono">{{ filtered().length }} of {{ trades().length }} trades</p>
        </div>
      </header>

      @switch (state()) {
        @case ('loading') {
          <div class="loading-state">
            <div class="spinner"></div>
            <span class="loading-text">Loading trades...</span>
          </div>
        }
        @case ('error') {
          <app-load-error title="Could not load trades" [message]="error()" (retry)="load()" />
        }
        @case ('ready') {
          <!-- Filters -->
          <div class="card filters-bar">
            <input
              class="filter-input"
              type="text"
              aria-label="Search trades"
              placeholder="Search instrument, strategy, session..."
              [ngModel]="searchTerm()"
              (ngModelChange)="searchTerm.set($event)"
            />
            <select class="filter-select" aria-label="Direction" [ngModel]="directionFilter()" (ngModelChange)="directionFilter.set($event)">
              <option value="">All Directions</option>
              <option value="Long">Long</option>
              <option value="Short">Short</option>
            </select>
            <select class="filter-select" aria-label="Result" [ngModel]="resultFilter()" (ngModelChange)="resultFilter.set($event)">
              <option value="">All Results</option>
              <option value="win">Wins</option>
              <option value="loss">Losses</option>
              <option value="breakeven">Breakeven</option>
            </select>
            <button type="button" class="btn-clear" (click)="clearFilters()">Clear</button>
          </div>

          @if (actionError()) {
            <p class="action-error mono" role="alert">{{ actionError() }}</p>
          }

          <!-- Table -->
          <div class="card table-card">
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Date</th>
                    <th>Instrument</th>
                    <th>Direction</th>
                    <th>Entry</th>
                    <th>Exit</th>
                    <th>Risk</th>
                    <th>P&L</th>
                    <th>RR</th>
                    <th>Strategy</th>
                    <th>Session</th>
                    <th>Duration</th>
                    <th>Override</th>
                    <th><span class="visually-hidden">Actions</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (trade of filtered(); track trade.id) {
                    <tr [class.row-win]="trade.pnL > 0" [class.row-loss]="trade.pnL < 0">
                      <td class="mono">{{ trade.tradeDate }}</td>
                      <td class="bold">{{ trade.instrument }}</td>
                      <td>
                        <span class="tag" [class.tag-green]="trade.direction === 'Long'" [class.tag-red]="trade.direction === 'Short'">
                          {{ trade.direction }}
                        </span>
                      </td>
                      <td class="mono">{{ trade.entryPrice | number:'1.2-5' }}</td>
                      <td class="mono">{{ trade.exitPrice | number:'1.2-5' }}</td>
                      <td class="mono">{{ trade.riskAmount | number:'1.2-2' }}</td>
                      <td class="mono" [class.text-green]="trade.pnL > 0" [class.text-red]="trade.pnL < 0">
                        {{ trade.pnL >= 0 ? '+' : '' }}{{ trade.pnL | number:'1.2-2' }}
                      </td>
                      <td class="mono" [class.text-green]="trade.rr >= 1" [class.text-red]="trade.rr < 0">
                        {{ trade.rr | number:'1.2-2' }}R
                      </td>
                      <td>
                        @if (trade.strategyName) {
                          <span class="tag tag-blue">{{ trade.strategyName }}</span>
                        } @else {
                          <span class="text-muted">—</span>
                        }
                      </td>
                      <td>{{ trade.session }}</td>
                      <td class="mono">{{ trade.tradeDuration }}m</td>
                      <td>
                        @if (trade.isManualOverride) {
                          <span class="tag tag-amber">Yes</span>
                        } @else {
                          <span class="text-muted">—</span>
                        }
                      </td>
                      <td>
                        <button type="button" class="btn-delete" [attr.aria-label]="'Delete trade ' + trade.id"
                          [disabled]="deletingId() === trade.id" (click)="deleteTrade(trade.id)">✕</button>
                      </td>
                    </tr>
                  } @empty {
                    <tr>
                      <td colspan="13" class="empty-state">
                        {{ trades().length === 0 ? 'No trades recorded yet. Import a CSV from the Import page.' : 'No trades match these filters.' }}
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </div>
        }
      }
    </div>
  `,
  styles: [`
    .trades-page { max-width: 1400px; }

    .page-header {
      margin-bottom: 1.5rem;
      h1 { font-family: var(--font-display); font-size: 1.75rem; font-weight: 800; }
    }

    .filters-bar {
      display: flex;
      gap: 0.75rem;
      align-items: center;
      margin-bottom: 1rem;
      flex-wrap: wrap;
    }

    .filter-input {
      flex: 1;
      min-width: 200px;
      background: var(--bg-secondary);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      padding: 0.5rem 0.75rem;
      color: var(--text-primary);
      font-family: var(--font-mono);
      font-size: 13px;
      outline: none;
      transition: border-color 0.15s;
      &:focus { border-color: var(--accent-amber); }
      &::placeholder { color: var(--text-muted); }
    }

    .filter-select {
      background: var(--bg-secondary);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      padding: 0.5rem 0.75rem;
      color: var(--text-primary);
      font-family: var(--font-mono);
      font-size: 13px;
      outline: none;
      cursor: pointer;
      &:focus { border-color: var(--accent-amber); }
    }

    .btn-clear {
      background: transparent;
      border: 1px solid var(--border);
      border-radius: var(--radius);
      padding: 0.5rem 1rem;
      color: var(--text-secondary);
      font-size: 13px;
      cursor: pointer;
      transition: all 0.15s;
      &:hover { border-color: var(--accent-red); color: var(--accent-red); }
    }

    .action-error { color: var(--accent-red); font-size: 12px; margin-bottom: 0.75rem; }

    .table-card { padding: 0; overflow: hidden; }
    .table-wrap { overflow-x: auto; }

    table { width: 100%; border-collapse: collapse; font-size: 13px; }

    thead tr { border-bottom: 1px solid var(--border); }

    th {
      padding: 0.75rem 1rem;
      text-align: left;
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--text-secondary);
      white-space: nowrap;
    }

    td {
      padding: 0.7rem 1rem;
      border-bottom: 1px solid var(--border-subtle);
      color: var(--text-data);
      white-space: nowrap;
    }

    tr:last-child td { border-bottom: none; }

    tbody tr {
      transition: background 0.1s;
      &:hover { background: var(--bg-card-hover); }
    }

    .row-win td:first-child { border-left: 2px solid var(--accent-green); }
    .row-loss td:first-child { border-left: 2px solid var(--accent-red); }
    .bold { font-weight: 600; color: var(--text-primary); }

    .btn-delete {
      background: transparent;
      border: 1px solid transparent;
      border-radius: 4px;
      color: var(--text-muted);
      cursor: pointer;
      padding: 2px 6px;
      font-size: 12px;
      transition: all 0.15s;
      &:hover { border-color: var(--accent-red); color: var(--accent-red); }
      &:disabled { opacity: 0.4; cursor: wait; }
    }

    .empty-state {
      text-align: center;
      color: var(--text-muted);
      padding: 3rem;
      font-family: var(--font-mono);
    }

    .visually-hidden {
      position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap;
    }
  `]
})
export class TradesComponent {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);

  readonly state = signal<LoadState>('loading');
  readonly error = signal('');
  readonly actionError = signal('');
  readonly deletingId = signal<number | null>(null);
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
    if (!confirm('Delete this trade?')) return;

    this.actionError.set('');
    this.deletingId.set(id);
    this.api.deleteTrade(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.trades.update(trades => trades.filter(t => t.id !== id));
          this.deletingId.set(null);
        },
        error: err => {
          this.actionError.set(`Trade was not deleted: ${describeHttpError(err)}`);
          this.deletingId.set(null);
        }
      });
  }
}
