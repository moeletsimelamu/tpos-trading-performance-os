import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';
import { TradingApiService } from '../trading-api.service';
import { CalendarMonth, JournalOverview, TradeSummary } from '../models/journal-view.model';
import { TRADE_STATUS, TradeStatus } from '../models/trade.model';
import { JournalCalendarComponent } from './journal-calendar.component';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';
import { IconComponent } from '../../../shared/ui/icon.component';

interface TradeList { title: string; status: TradeStatus; count: number; trades: TradeSummary[]; empty: string; }

// The journal's home: what needs finishing, the month by day, and what changed last
@Component({
  selector: 'app-journal-overview',
  standalone: true,
  imports: [DatePipe, RouterLink, JournalCalendarComponent, LoadErrorComponent, PageHeaderComponent, IconComponent],
  template: `
    <div class="page">
      <app-page-header section="Trading operations" title="Journal"
        description="What needs finishing, and your trading by day. Only closed and reviewed trades count in totals.">
        <a class="btn btn-primary" routerLink="/trading/trades/new"><app-icon name="plus" [size]="16" />New trade</a>
      </app-page-header>

      @switch (state()) {
        @case ('loading') { <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading the journal...</span></div> }
        @case ('error') { <app-load-error title="Could not load the journal" [message]="error()" (retry)="load()" /> }
        @case ('ready') {
          @if (overview(); as o) {
            <section class="metric-strip" aria-label="Journal status">
              <a class="metric" routerLink="/trading/trades" [queryParams]="{ status: 'Draft' }">
                <span class="metric-label">Drafts</span><span class="metric-value">{{ o.drafts }}</span>
                <span class="metric-note">Not counted until closed</span>
              </a>
              <a class="metric" routerLink="/trading/trades" [queryParams]="{ status: 'Open' }">
                <span class="metric-label">Open</span><span class="metric-value">{{ o.open }}</span>
                <span class="metric-note">Positions still running</span>
              </a>
              <a class="metric" routerLink="/trading/trades" [queryParams]="{ status: 'Closed' }">
                <span class="metric-label">Awaiting review</span><span class="metric-value">{{ o.awaitingReview }}</span>
                <span class="metric-note">Closed, review not written</span>
              </a>
              <a class="metric" routerLink="/trading/trades" [queryParams]="{ status: 'Reviewed' }">
                <span class="metric-label">Reviewed</span><span class="metric-value">{{ o.reviewed }}</span>
                <span class="metric-note">Complete entries</span>
              </a>
            </section>

            @if (pendingMatches() > 0) {
              <p class="notice notice-warning" role="status">
                {{ pendingMatches() }} imported {{ pendingMatches() === 1 ? 'row matches a trade' : 'rows match trades' }} you journaled.
                <a routerLink="/trading/import">Decide on the Import page</a>; nothing was duplicated.
              </p>
            }

            <section class="card calendar" aria-label="Calendar">
              @if (calendarError()) {
                <p class="notice notice-error" role="alert">Could not load this month: {{ calendarError() }}</p>
              }
              @if (calendar(); as c) { <app-journal-calendar [month]="c" (previous)="shiftMonth(-1)" (next)="shiftMonth(1)" /> }
            </section>

            <div class="lists">
              @for (list of lists(); track list.status) {
                <section class="card list" [attr.aria-labelledby]="'list-' + list.status">
                  <div class="list-head">
                    <h2 [id]="'list-' + list.status">{{ list.title }} <span class="muted">{{ list.count }}</span></h2>
                    @if (list.count > list.trades.length) {
                      <a class="small" routerLink="/trading/trades" [queryParams]="{ status: list.status }">All {{ list.count }}</a>
                    }
                  </div>
                  <ul>
                    @for (t of list.trades; track t.id) {
                      <li>
                        <a [routerLink]="['/trading/trades', t.id]">{{ t.instrument }}{{ t.direction ? ' · ' + t.direction : '' }}</a>
                        <span class="small secondary nowrap">{{ t.tradeDate | date: 'd MMM' }}{{ t.strategyName ? ' · ' + t.strategyName : '' }}</span>
                      </li>
                    } @empty { <li class="muted small">{{ list.empty }}</li> }
                  </ul>
                </section>
              }
              <section class="card list" aria-labelledby="list-recent">
                <div class="list-head"><h2 id="list-recent">Recent activity</h2></div>
                <ul>
                  @for (t of o.recent; track t.id) {
                    <li>
                      <a [routerLink]="['/trading/trades', t.id]">{{ t.instrument }}</a>
                      <span class="small secondary nowrap"><span [class]="status[t.status].tag">{{ status[t.status].label }}</span> {{ t.lastChangedAt | date: 'd MMM, HH:mm' }}</span>
                    </li>
                  } @empty { <li class="muted small">Nothing recorded yet. Start with New trade.</li> }
                </ul>
              </section>
            </div>
          }
        }
      }
    </div>
  `,
  styles: [`
    a.metric { color: inherit; text-decoration: none; }
    a.metric:hover .metric-label { color: var(--text-primary); text-decoration: underline; }
    .calendar, .notice { margin-top: var(--space-5); }
    .lists { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: var(--space-4); margin-top: var(--space-5); }
    .list-head { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: var(--space-3); }
    .list h2 { font-size: var(--text-md); }
    .list ul { list-style: none; display: grid; gap: var(--space-2); }
    .list li { display: flex; justify-content: space-between; gap: var(--space-3); align-items: baseline; }
  `]
})
export class JournalOverviewComponent {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);

  readonly status = TRADE_STATUS;
  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly error = signal('');
  readonly overview = signal<JournalOverview | null>(null);
  readonly calendar = signal<CalendarMonth | null>(null);
  readonly calendarError = signal('');
  readonly pendingMatches = signal(0);
  readonly lists = signal<TradeList[]>([]);

  private shown = { year: new Date().getFullYear(), month: new Date().getMonth() + 1 };

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    forkJoin({
      overview: this.api.getJournalOverview(),
      calendar: this.api.getCalendar(this.shown.year, this.shown.month),
      // The match count is a reminder; the page works without it
      matches: this.api.getImportMatches().pipe(catchError(() => of([])))
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ overview, calendar, matches }) => {
        this.overview.set(overview);
        this.calendar.set(calendar);
        this.pendingMatches.set(matches.length);
        this.lists.set([
          { title: 'Drafts', status: 'Draft', count: overview.drafts, trades: overview.draftTrades, empty: 'No drafts.' },
          { title: 'Open', status: 'Open', count: overview.open, trades: overview.openTrades, empty: 'No open positions.' },
          { title: 'Awaiting review', status: 'Closed', count: overview.awaitingReview, trades: overview.awaitingReviewTrades, empty: 'Every closed trade is reviewed.' }
        ]);
        this.state.set('ready');
      },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  shiftMonth(delta: number): void {
    const index = this.shown.year * 12 + (this.shown.month - 1) + delta;
    this.shown = { year: Math.floor(index / 12), month: (index % 12) + 1 };
    this.calendarError.set('');
    this.api.getCalendar(this.shown.year, this.shown.month).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: c => this.calendar.set(c),
      error: err => this.calendarError.set(describeHttpError(err))
    });
  }
}
