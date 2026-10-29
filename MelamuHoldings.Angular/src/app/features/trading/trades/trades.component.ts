import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TradingApiService } from '../trading-api.service';
import { TRADE_STATUS, Trade, TradeStatus } from '../models/trade.model';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';
import { IconComponent } from '../../../shared/ui/icon.component';

type LoadState = 'loading' | 'ready' | 'error';
type SortKey = 'date' | 'instrument' | 'pnl' | 'rr';

const PAGE_SIZE = 25;
const STATUSES: TradeStatus[] = ['Draft', 'Open', 'Closed', 'Reviewed'];

@Component({
  selector: 'app-trades',
  standalone: true,
  imports: [DatePipe, DecimalPipe, FormsModule, RouterLink, LoadErrorComponent, PageHeaderComponent, IconComponent],
  template: `
    <div class="page">
      <app-page-header section="Trading operations" title="Trades"
        [description]="filtered().length + ' of ' + trades().length + ' trades'">
        <a class="btn" routerLink="/trading/import"><app-icon name="import" [size]="16" />Import trades</a>
        <a class="btn btn-primary" routerLink="/trading/trades/new"><app-icon name="plus" [size]="16" />New trade</a>
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

          @if (dateFilter(); as day) {
            <p class="notice date-filter" role="status">
              Trades on {{ day | date: 'EEEE d MMMM yyyy' }}.
              <button type="button" class="btn-link" (click)="setFilter(dateFilter, '')">Show all dates</button>
            </p>
          }

          <div class="card table-card">
            <div class="tabs" role="tablist" aria-label="Filter trades by status">
              @for (f of statusFilters; track f) {
                <button type="button" role="tab" class="tab" [attr.aria-selected]="statusFilter() === f" (click)="setFilter(statusFilter, f)">
                  {{ f === 'All' ? 'All' : statusLabel(f) }} <span class="count">{{ count(f) }}</span>
                </button>
              }
            </div>
            <div class="toolbar" role="search">
              <div class="field grow">
                <label for="trade-search">Search</label>
                <input id="trade-search" type="search" placeholder="Instrument, strategy, session or day"
                  [ngModel]="searchTerm()" (ngModelChange)="setFilter(searchTerm, $event)" />
              </div>
              <div class="field">
                <label for="trade-direction">Direction</label>
                <select id="trade-direction" [ngModel]="directionFilter()" (ngModelChange)="setFilter(directionFilter, $event)">
                  <option value="">All directions</option>
                  <option value="Long">Long</option>
                  <option value="Short">Short</option>
                </select>
              </div>
              <div class="field">
                <label for="trade-strategy">Strategy</label>
                <select id="trade-strategy" [ngModel]="strategyFilter()" (ngModelChange)="setFilter(strategyFilter, $event)">
                  <option value="">All strategies</option>
                  @for (s of strategies(); track s) { <option [value]="s">{{ s }}</option> }
                </select>
              </div>
              <div class="field">
                <label for="trade-session">Session</label>
                <select id="trade-session" [ngModel]="sessionFilter()" (ngModelChange)="setFilter(sessionFilter, $event)">
                  <option value="">All sessions</option>
                  @for (s of sessions(); track s) { <option [value]="s">{{ s }}</option> }
                </select>
              </div>
              @if (accounts().length > 0) {
                <div class="field">
                  <label for="trade-account">Account</label>
                  <select id="trade-account" [ngModel]="accountFilter()" (ngModelChange)="setFilter(accountFilter, $event)">
                    <option value="">All accounts</option>
                    @for (a of accounts(); track a) { <option [value]="a">{{ a }}</option> }
                  </select>
                </div>
              }
              <div class="field">
                <label for="trade-result">Result</label>
                <select id="trade-result" [ngModel]="resultFilter()" (ngModelChange)="setFilter(resultFilter, $event)">
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
                  <th [attr.aria-sort]="ariaSort('date')"><button type="button" class="sort" (click)="sortBy('date')">Date{{ arrow('date') }}</button></th>
                  <th [attr.aria-sort]="ariaSort('instrument')"><button type="button" class="sort" (click)="sortBy('instrument')">Instrument{{ arrow('instrument') }}</button></th>
                  <th>Direction</th>
                  <th class="num">Entry</th>
                  <th class="num">Exit</th>
                  <th class="num">Risk</th>
                  <th class="num" [attr.aria-sort]="ariaSort('pnl')"><button type="button" class="sort" (click)="sortBy('pnl')">P&amp;L{{ arrow('pnl') }}</button></th>
                  <th class="num" [attr.aria-sort]="ariaSort('rr')"><button type="button" class="sort" (click)="sortBy('rr')">R{{ arrow('rr') }}</button></th>
                  <th>Strategy</th>
                  <th>Session</th>
                  <th class="num">Duration</th>
                  <th>Override</th>
                  <th>Rules</th>
                  <th>Status</th>
                  <th><span class="visually-hidden">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (trade of page(); track trade.id) {
                  <tr>
                    <td class="nowrap">{{ trade.tradeDate }}</td>
                    <td><a [routerLink]="['/trading/trades', trade.id]">{{ trade.instrument }}</a></td>
                    <td>{{ trade.direction ?? '—' }}</td>
                    <td class="num">{{ trade.entryPrice != null ? (trade.entryPrice | number:'1.2-5') : '—' }}</td>
                    <td class="num">{{ trade.exitPrice != null ? (trade.exitPrice | number:'1.2-5') : '—' }}</td>
                    <td class="num">{{ trade.riskAmount != null ? (trade.riskAmount | number:'1.2-2') : '—' }}</td>
                    <td class="num" [class.text-positive]="(trade.pnL ?? 0) > 0" [class.text-negative]="(trade.pnL ?? 0) < 0">
                      @if (trade.pnL != null) { {{ trade.pnL > 0 ? '+' : '' }}{{ trade.pnL | number:'1.2-2' }} } @else { — }
                    </td>
                    <td class="num">{{ trade.rr != null ? (trade.rr | number:'1.2-2') + 'R' : '—' }}</td>
                    <td>{{ trade.strategyName ?? '—' }}</td>
                    <td class="nowrap">{{ trade.session || '—' }}</td>
                    <td class="num">{{ trade.tradeDuration != null ? trade.tradeDuration + ' min' : '—' }}</td>
                    <td>
                      @if (trade.isManualOverride) { <span class="tag tag-warning">Override</span> }
                      @else { <span class="muted">—</span> }
                    </td>
                    <td>
                      @switch (trade.ruleAdherence) {
                        @case ('Followed') { <span class="tag tag-positive">Followed</span> }
                        @case ('Broke') { <span class="tag tag-negative">Broke a rule</span> }
                        @case ('Unfinished') { <span class="tag">Unfinished</span> }
                        @default { <span class="muted">—</span> }
                      }
                    </td>
                    <td><span [class]="tradeStatus[trade.status].tag">{{ tradeStatus[trade.status].label }}</span></td>
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
                    <td colspan="15" class="empty-row">
                      {{ trades().length === 0 ? 'No trades recorded yet. Record one with New trade, or import your history.' : 'No trades match these filters.' }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
            @if (filtered().length > pageSize) {
              <nav class="pager" aria-label="Pages">
                <span class="small secondary">Showing {{ firstShown() }}–{{ lastShown() }} of {{ filtered().length }}</span>
                <button type="button" class="btn btn-sm" [disabled]="pageIndex() === 0" (click)="pageIndex.set(pageIndex() - 1)">Previous</button>
                <button type="button" class="btn btn-sm" [disabled]="lastShown() >= filtered().length" (click)="pageIndex.set(pageIndex() + 1)">Next</button>
              </nav>
            }
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
    .count { color: var(--text-muted); font-weight: 400; margin-left: 2px; }
    .sort { border: none; background: none; padding: 0; font: inherit; color: inherit; cursor: pointer; }
    .sort:hover { color: var(--text-primary); text-decoration: underline; }
    .pager { display: flex; gap: var(--space-2); align-items: center; justify-content: flex-end; padding: var(--space-3) var(--space-4); border-top: 1px solid var(--border-subtle); }
    .pager span { margin-right: auto; }
    .date-filter { margin-bottom: var(--space-4); }
  `]
})
export class TradesComponent {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);
  private route = inject(ActivatedRoute);

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
  readonly statusFilter = signal<'All' | TradeStatus>('All');
  readonly strategyFilter = signal('');
  readonly sessionFilter = signal('');
  readonly accountFilter = signal('');
  // A day chosen on the journal calendar (yyyy-MM-dd)
  readonly dateFilter = signal('');
  readonly sortKey = signal<SortKey>('date');
  readonly sortDescending = signal(true);
  readonly pageIndex = signal(0);
  readonly pageSize = PAGE_SIZE;
  readonly statusFilters: ('All' | TradeStatus)[] = ['All', 'Draft', 'Open', 'Closed', 'Reviewed'];
  readonly tradeStatus = TRADE_STATUS;

  readonly filtered = computed(() => {
    const term = this.searchTerm().toLowerCase();
    const direction = this.directionFilter();
    const result = this.resultFilter();
    const status = this.statusFilter();
    const strategy = this.strategyFilter();
    const session = this.sessionFilter();
    const account = this.accountFilter();
    const date = this.dateFilter();

    return this.trades().filter(trade => {
      const pnl = trade.pnL ?? null;
      const matchesSearch = !term ||
        trade.instrument.toLowerCase().includes(term) ||
        !!trade.strategyName?.toLowerCase().includes(term) ||
        (trade.session ?? '').toLowerCase().includes(term) ||
        (trade.dayOfWeek ?? '').toLowerCase().includes(term);

      const matchesDirection = !direction || trade.direction === direction;

      // A draft or open trade has no result yet, so a result filter leaves it out
      const matchesResult = !result ||
        (result === 'win' && pnl !== null && pnl > 0) ||
        (result === 'loss' && pnl !== null && pnl < 0) ||
        (result === 'breakeven' && pnl === 0);

      const matchesStatus = status === 'All' || trade.status === status;

      return matchesSearch && matchesDirection && matchesResult && matchesStatus
        && (!strategy || trade.strategyName === strategy)
        && (!session || trade.session === session)
        && (!account || trade.tradingAccount === account)
        && (!date || trade.tradeDate === date);
    });
  });

  readonly strategies = computed(() => this.distinct(t => t.strategyName));
  readonly sessions = computed(() => this.distinct(t => t.session));
  readonly accounts = computed(() => this.distinct(t => t.tradingAccount));

  // Trades without a value for the sorted column (a draft has no P&L) go last either way
  readonly sorted = computed(() => {
    const key = this.sortKey();
    const direction = this.sortDescending() ? -1 : 1;
    const value = (t: Trade): string | number | null =>
      key === 'date' ? `${t.tradeDate} ${t.entryTime ?? ''}` : key === 'instrument' ? t.instrument.toLowerCase() : key === 'pnl' ? t.pnL ?? null : t.rr ?? null;
    return [...this.filtered()].sort((a, b) => {
      const x = value(a), y = value(b);
      if (x === null || y === null) return x === y ? b.id - a.id : x === null ? 1 : -1;
      return (x < y ? -1 : x > y ? 1 : b.id - a.id) * direction;
    });
  });

  readonly page = computed(() => this.sorted().slice(this.pageIndex() * PAGE_SIZE, (this.pageIndex() + 1) * PAGE_SIZE));
  readonly firstShown = computed(() => this.filtered().length === 0 ? 0 : this.pageIndex() * PAGE_SIZE + 1);
  readonly lastShown = computed(() => Math.min((this.pageIndex() + 1) * PAGE_SIZE, this.filtered().length));

  constructor() {
    // The journal page links here with a status or a calendar day
    const query = this.route.snapshot.queryParamMap;
    const status = query.get('status') as TradeStatus | null;
    if (status && STATUSES.includes(status)) this.statusFilter.set(status);
    const date = query.get('date');
    if (date && /^\d{4}-\d{2}-\d{2}$/.test(date)) this.dateFilter.set(date);
    this.load();
  }

  // Any change to the filters starts again at the first page
  setFilter<T>(filter: { set(value: T): void }, value: T): void {
    filter.set(value);
    this.pageIndex.set(0);
  }

  sortBy(key: SortKey): void {
    if (this.sortKey() === key) this.sortDescending.set(!this.sortDescending());
    else { this.sortKey.set(key); this.sortDescending.set(key !== 'instrument'); }
    this.pageIndex.set(0);
  }

  ariaSort(key: SortKey): 'ascending' | 'descending' | null {
    return this.sortKey() === key ? (this.sortDescending() ? 'descending' : 'ascending') : null;
  }

  arrow(key: SortKey): string {
    return this.sortKey() === key ? (this.sortDescending() ? ' ↓' : ' ↑') : '';
  }

  private distinct(pick: (t: Trade) => string | null | undefined): string[] {
    return [...new Set(this.trades().map(pick).filter((v): v is string => !!v))].sort((a, b) => a.localeCompare(b));
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

  count(filter: 'All' | TradeStatus): number {
    return filter === 'All' ? this.trades().length : this.trades().filter(t => t.status === filter).length;
  }

  statusLabel(status: TradeStatus): string {
    return status === 'Draft' ? 'Drafts' : TRADE_STATUS[status].label;
  }

  clearFilters(): void {
    this.searchTerm.set('');
    this.directionFilter.set('');
    this.resultFilter.set('');
    this.strategyFilter.set('');
    this.sessionFilter.set('');
    this.accountFilter.set('');
    this.dateFilter.set('');
    this.pageIndex.set(0);
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
