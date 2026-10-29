import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { FinanceApiService } from '../finance/finance-api.service';
import { Allocation, Balances, Money, Payout, Transaction, TYPE_LABELS, UNALLOCATED_FUND_ID } from '../finance/finance.models';
import { TradingApiService } from '../trading/trading-api.service';
import { AnalyticsSummary, BehaviourSummary } from '../trading/models/analytics.model';
import { MoneyPipe } from '../../shared/money.pipe';
import { LoadErrorComponent } from '../../shared/load-error.component';
import { describeHttpError } from '../../shared/http-error';
import { PageHeaderComponent } from '../../shared/ui/page-header.component';
import { IconComponent, IconName } from '../../shared/ui/icon.component';
import { OverviewPeriodComponent } from './overview-period.component';

type LoadState = 'loading' | 'ready' | 'error';

// Something in the recorded data that the operator should look at, and where to deal with it
export interface AttentionItem {
  tone: 'warning' | 'negative' | 'info';
  title: string;
  detail: string;
  link: string;
  queryParams?: Record<string, string | number>;
  action: string;
}

const RECENT_COUNT = 6;
const RECENT_PAYOUTS = 5;

@Component({
  selector: 'app-executive-overview',
  standalone: true,
  imports: [DecimalPipe, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent, IconComponent, OverviewPeriodComponent],
  template: `
    <div class="page">
      <app-page-header section="Overview" title="Executive overview"
        [description]="'The position of the business as recorded on ' + today + '. Every figure comes from the ledger and the trading journal.'">
        <a class="btn" routerLink="/finance/transactions">Ledger</a>
        <a class="btn btn-primary" routerLink="/finance/transactions/new"><app-icon name="plus" [size]="16" />Record transaction</a>
      </app-page-header>

      @switch (financeState()) {
        @case ('loading') {
          <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading the business position...</span></div>
        }
        @case ('error') {
          <app-load-error title="Could not load the financial position" [message]="financeError()" (retry)="loadFinance()" />
        }
        @case ('ready') {
          @if (balances(); as b) {
            <section aria-labelledby="cash-heading">
              <h2 id="cash-heading" class="visually-hidden">Cash position</h2>
              @if (b.cashTotals.length > 0) {
                <div class="metric-strip">
                  <!-- Currencies are peers: none leads, and they are never added together -->
                  @for (t of b.cashTotals; track t.currency) {
                    <div class="metric metric-lead">
                      <span class="metric-label">Cash · {{ t.currency }}</span>
                      <span class="metric-value" [class.text-negative]="t.amount < 0">{{ t.amount | money: t.currency }}</span>
                      <span class="metric-note">Bank and cash accounts</span>
                    </div>
                  }
                  <div class="metric">
                    <span class="metric-label">Active accounts</span>
                    <span class="metric-value">{{ activeAccounts() }}</span>
                    <span class="metric-note"><a routerLink="/finance/accounts">View accounts</a></span>
                  </div>
                </div>
              } @else {
                <div class="card empty-state">
                  <h2>No cash recorded yet</h2>
                  <p>Add the account payouts arrive in and record its opening balance. The overview fills in from the ledger.</p>
                  <a class="btn" routerLink="/finance/accounts">Set up accounts</a>
                </div>
              }
            </section>

            <app-overview-period />

            <div class="grid">
              <section class="card table-card" aria-labelledby="funds-heading">
                <div class="card-header">
                  <h2 id="funds-heading">Fund positions</h2>
                  <a class="small" routerLink="/finance/funds">All funds</a>
                </div>
                <table class="data-table">
                  <thead><tr><th>Fund</th><th class="num">Balance</th></tr></thead>
                  <tbody>
                    @for (f of b.funds; track f.id) {
                      <tr>
                        <td>
                          <a [routerLink]="['/finance/transactions']" [queryParams]="{ fundId: f.id }">{{ f.name }}</a>
                          @if (f.id === unallocatedId && f.balances.length > 0) { <span class="tag tag-warning">Not assigned</span> }
                        </td>
                        <td class="num">
                          @for (m of f.balances; track m.currency) { <div>{{ m.amount | money: m.currency }}</div> }
                          @empty { <span class="muted">—</span> }
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </section>

              <section class="card attention" aria-labelledby="attention-heading">
                <div class="card-header"><h2 id="attention-heading">Needs attention</h2></div>
                <ul class="attention-list">
                  @for (item of attention(); track item.title) {
                    <li [class]="'attention-item ' + item.tone">
                      <app-icon [name]="iconFor(item)" [size]="18" />
                      <div>
                        <p class="attention-title">{{ item.title }}</p>
                        <p class="attention-detail">{{ item.detail }}</p>
                        <a class="small" [routerLink]="item.link" [queryParams]="item.queryParams">{{ item.action }}</a>
                      </div>
                    </li>
                  } @empty {
                    <li class="attention-item clear"><app-icon name="check" [size]="18" /><p>Nothing in the recorded data needs attention.</p></li>
                  }
                </ul>
                <p class="coverage">As of today, whatever the period above. Checks cover recorded accounts, funds, payouts, allocations and trades. Obligations, budgets and reserve targets are not tracked yet.</p>
              </section>
            </div>

            <div class="grid">
              <section class="card table-card" aria-labelledby="activity-heading">
                <div class="card-header">
                  <h2 id="activity-heading">Recent activity</h2>
                  <a class="small" routerLink="/finance/transactions">Full ledger</a>
                </div>
                @if (recent().length > 0) {
                  <table class="data-table">
                    <thead><tr><th>Date</th><th>Transaction</th><th class="num">Amount</th></tr></thead>
                    <tbody>
                      @for (t of recent(); track t.id) {
                        <tr>
                          <td class="nowrap secondary">{{ t.date }}</td>
                          <td>
                            <div class="tx-title">{{ t.description }}</div>
                            <div class="small muted">{{ typeLabel(t) }}{{ t.reversedByTransactionId ? ' · reversed' : '' }}</div>
                          </td>
                          <td class="num">
                            @for (m of headline(t); track $index) {
                              <div [class.amount-in]="m.amount > 0" [class.amount-out]="m.amount < 0">{{ m.amount | money: m.currency : true }}</div>
                            }
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                } @else {
                  <p class="empty-state small">No transactions recorded yet.</p>
                }
              </section>

              <div class="stack">
              <section class="card table-card" aria-labelledby="payouts-heading">
                <div class="card-header">
                  <h2 id="payouts-heading">Recent payouts</h2>
                  <a class="small" routerLink="/finance/payouts">All payouts</a>
                </div>
                @if (recentPayouts().length > 0) {
                  <table class="data-table">
                    <thead><tr><th>Payout</th><th>Status</th><th class="num">Amount</th></tr></thead>
                    <tbody>
                      @for (p of recentPayouts(); track p.id) {
                        <tr>
                          <td><a [routerLink]="['/finance/payouts', p.id]">#{{ p.id }} {{ p.source }}</a><div class="small muted">{{ payoutDate(p) }}</div></td>
                          <td><span [class]="'tag ' + payoutTone(p)">{{ p.status }}</span></td>
                          <td class="num">
                            {{ payoutAmount(p).amount | money: payoutAmount(p).currency }}
                            @if (p.status === 'Requested' || p.status === 'Approved') { <div class="small muted">not cash yet</div> }
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                } @else {
                  <p class="empty-state small">No payouts recorded yet. <a routerLink="/finance/payouts">Record a payout</a></p>
                }
              </section>
              <section class="card trading" aria-labelledby="trading-heading">
                <div class="card-header">
                  <h2 id="trading-heading">Trading operations</h2>
                  <a class="small" routerLink="/trading/performance">Performance</a>
                </div>
                @switch (tradingState()) {
                  @case ('loading') { <div class="loading-state"><div class="spinner"></div></div> }
                  @case ('error') { <app-load-error title="Could not load trading results" [message]="tradingError()" (retry)="loadTrading()" /> }
                  @case ('ready') {
                    @if (trading(); as s) {
                      @if (s.totalTrades === 0) {
                        <p class="empty-state small">No trades in the journal yet. <a routerLink="/trading/import">Import trades</a></p>
                      } @else {
                        <dl class="figures">
                          <div><dt>Journal P&amp;L</dt><dd [class.text-positive]="s.totalPnL > 0" [class.text-negative]="s.totalPnL < 0">{{ s.totalPnL > 0 ? '+' : '' }}{{ s.totalPnL | number: '1.2-2' }}</dd></div>
                          <div><dt>Trades</dt><dd>{{ s.totalTrades }}</dd></div>
                          <div><dt>Win rate</dt><dd>{{ s.winRate | number: '1.1-1' }}%</dd></div>
                          <div><dt>Expectancy</dt><dd>{{ s.expectancy > 0 ? '+' : '' }}{{ s.expectancy | number: '1.2-2' }}</dd></div>
                        </dl>
                        <p class="coverage">Trading results are not business cash. Money counts here only once a payout is recorded as received in the ledger.</p>
                      }
                    }
                  }
                }
              </section>
              </div>
            </div>
          }
        }
      }
    </div>
  `,
  styles: [`
    .grid { display: grid; grid-template-columns: minmax(0, 3fr) minmax(0, 2fr); gap: var(--space-5); margin-top: var(--space-5); }
    @media (max-width: 1099px) { .grid { grid-template-columns: minmax(0, 1fr); } }
    .grid > .card, .grid > .stack { align-self: start; }
    .stack { display: grid; gap: var(--space-5); }
    .stack > .card { padding: 0; }
    .tag { margin-left: var(--space-2); }
    .attention, .trading { padding: 0; }
    .attention-list { list-style: none; }
    .attention-item {
      display: flex; gap: var(--space-3); padding: var(--space-3) var(--space-5);
      border-bottom: 1px solid var(--border-subtle);
      app-icon { margin-top: 2px; }
      &.warning app-icon { color: var(--warning); }
      &.negative app-icon { color: var(--negative); }
      &.info app-icon { color: var(--info); }
      &.clear { color: var(--text-secondary); app-icon { color: var(--positive); } }
    }
    .attention-title { font-weight: 500; }
    .attention-detail { color: var(--text-secondary); font-size: var(--text-sm); margin-bottom: 2px; }
    .coverage { color: var(--text-muted); font-size: var(--text-xs); padding: var(--space-3) var(--space-5); }
    .tx-title { font-weight: 500; }
    .figures {
      display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-4) var(--space-5); padding: var(--space-5) var(--space-5) var(--space-2);
      dt { font-size: var(--text-sm); color: var(--text-secondary); }
      dd { font-size: var(--text-lg); font-weight: 600; }
    }
    .empty-state.small { padding: var(--space-6) var(--space-5); }
  `]
})
export class OverviewComponent {
  private finance = inject(FinanceApiService);
  private tradingApi = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);

  readonly unallocatedId = UNALLOCATED_FUND_ID;
  readonly today = new Date().toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });

  readonly financeState = signal<LoadState>('loading');
  readonly financeError = signal('');
  readonly balances = signal<Balances | null>(null);
  readonly recent = signal<Transaction[]>([]);
  readonly payouts = signal<Payout[]>([]);
  readonly allocations = signal<Allocation[]>([]);

  readonly tradingState = signal<LoadState>('loading');
  readonly tradingError = signal('');
  readonly trading = signal<AnalyticsSummary | null>(null);
  readonly behaviour = signal<BehaviourSummary | null>(null);

  readonly activeAccounts = computed(() => this.balances()?.accounts.filter(a => a.status === 'Active').length ?? 0);
  readonly attention = computed(() => attentionItems(this.balances(), this.behaviour(), this.payouts(), this.allocations()));
  // The latest payouts recorded, whatever their status
  readonly recentPayouts = computed(() => [...this.payouts()].sort((a, b) => b.id - a.id).slice(0, RECENT_PAYOUTS));

  constructor() {
    this.loadFinance();
    this.loadTrading();
  }

  loadFinance(): void {
    this.financeState.set('loading');
    forkJoin({
      balances: this.finance.getBalances(),
      recent: this.finance.getTransactions({ page: 1, pageSize: RECENT_COUNT }),
      payouts: this.finance.getPayouts(),
      allocations: this.finance.getAllocations()
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ balances, recent, payouts, allocations }) => {
        this.balances.set(balances);
        this.recent.set(recent.items);
        this.payouts.set(payouts);
        this.allocations.set(allocations);
        this.financeState.set('ready');
      },
      error: err => { this.financeError.set(describeHttpError(err)); this.financeState.set('error'); }
    });
  }

  loadTrading(): void {
    this.tradingState.set('loading');
    forkJoin({ summary: this.tradingApi.getSummary(), behaviour: this.tradingApi.getBehaviour() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ summary, behaviour }) => {
          this.trading.set(summary);
          this.behaviour.set(behaviour);
          this.tradingState.set('ready');
        },
        error: err => { this.tradingError.set(describeHttpError(err)); this.tradingState.set('error'); }
      });
  }

  typeLabel(t: Transaction): string {
    return TYPE_LABELS[t.type];
  }

  // The amounts that describe a transaction at a glance: what entered or left the business,
  // or for internal movements (transfers, earmarks, exchanges) the amount that moved
  headline(t: Transaction): Money[] {
    const byCurrency = new Map<string, number>();
    for (const e of t.entries) byCurrency.set(e.currency, round2((byCurrency.get(e.currency) ?? 0) + e.amount));
    const net = [...byCurrency].filter(([, amount]) => amount !== 0).map(([currency, amount]) => ({ currency, amount }));
    if (net.length > 0) return net;
    const moved = t.entries.find(e => e.amount > 0);
    return moved ? [{ currency: moved.currency, amount: moved.amount }] : [];
  }

  // What a payout amounts to so far: credited once received, otherwise the approved or requested amount
  payoutAmount(p: Payout): Money {
    return p.credited ?? p.approved ?? p.requested;
  }

  payoutDate(p: Payout): string {
    return p.status === 'Received' ? `Received ${p.receivedOn}` : p.status === 'Rejected' ? `Rejected ${p.rejectedOn}` : `Requested ${p.requestedOn}`;
  }

  payoutTone(p: Payout): string {
    return p.status === 'Received' ? 'tag-positive' : p.status === 'Rejected' ? 'tag-negative' : 'tag-warning';
  }

  iconFor(item: AttentionItem): IconName {
    return item.tone === 'info' ? 'info' : 'alert';
  }
}

function round2(value: number): number {
  return Math.round(value * 100) / 100;
}

function formatMoney(m: Money): string {
  return `${m.currency} ${m.amount.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

// Derives what needs looking at from recorded data only; nothing here is a forecast or a guess
export function attentionItems(
  balances: Balances | null, behaviour: BehaviourSummary | null,
  payouts: Payout[] = [], allocations: Allocation[] = []
): AttentionItem[] {
  const items: AttentionItem[] = [];

  // Money has arrived but nobody has decided where it goes
  for (const payout of payouts.filter(p => p.status === 'Received' && !p.allocationId && p.credited)) {
    items.push({
      tone: 'warning', title: `Payout #${payout.id} received but not allocated`,
      detail: `${formatMoney(payout.credited!)} credited to ${payout.receivedIntoAccountName} on ${payout.receivedOn}. Preview and confirm the split.`,
      link: `/finance/payouts/${payout.id}`, action: 'Allocate'
    });
  }

  // Decided, but the money has not been earmarked or moved yet
  for (const allocation of allocations.filter(a => a.status !== 'Cancelled' && a.outstanding.amount > 0)) {
    items.push({
      tone: 'warning', title: `Allocation #${allocation.id} not fully settled`,
      detail: `${formatMoney(allocation.outstanding)} of ${formatMoney(allocation.total)} is still unallocated in ${allocation.sourceAccountName}.`,
      link: `/finance/allocations/${allocation.id}`, action: 'Settle'
    });
  }

  // On the way, and explicitly not cash yet
  const pending = payouts.filter(p => p.status === 'Requested' || p.status === 'Approved');
  if (pending.length > 0) {
    const totals = new Map<string, number>();
    for (const p of pending) totals.set(p.requested.currency, round2((totals.get(p.requested.currency) ?? 0) + (p.approved ?? p.requested).amount));
    items.push({
      tone: 'info', title: `${pending.length} ${pending.length === 1 ? 'payout' : 'payouts'} on the way`,
      detail: `${[...totals].map(([currency, amount]) => formatMoney({ currency, amount })).join(' and ')} requested or approved. Not cash until received.`,
      link: '/finance/payouts', action: 'View payouts'
    });
  }

  if (balances) {
    const active = balances.accounts.filter(a => a.status === 'Active');
    if (active.length === 0) {
      items.push({
        tone: 'info', title: 'No accounts set up',
        detail: 'Add the accounts money is held in before recording income or payouts.',
        link: '/finance/accounts', action: 'Set up accounts'
      });
    }

    const unallocated = balances.funds.find(f => f.id === UNALLOCATED_FUND_ID)?.balances.filter(b => b.amount !== 0) ?? [];
    if (unallocated.length > 0) {
      items.push({
        tone: 'warning', title: 'Money not assigned to a fund',
        detail: `${unallocated.map(formatMoney).join(' and ')} is unallocated. Allocate received payouts, or earmark the rest, so each amount has a purpose.`,
        link: '/finance/transactions/new', queryParams: { type: 'reallocation' }, action: 'Earmark money'
      });
    }

    // A fund can be spent below zero (the ledger records it rather than blocking it); say so per currency
    for (const fund of balances.funds) {
      const negative = fund.balances.filter(b => b.amount < 0);
      if (negative.length === 0) continue;
      items.push({
        tone: 'negative', title: `${fund.name} is below zero`,
        detail: `Recorded fund balance ${negative.map(formatMoney).join(' and ')}: more was spent or moved out of it than it held. Earmark money into it or check its entries.`,
        link: '/finance/transactions', queryParams: { fundId: fund.id }, action: 'Review its transactions'
      });
    }
    for (const account of active.filter(a => a.balance.amount < 0)) {
      items.push({
        tone: 'negative', title: `${account.name} is below zero`,
        detail: `Recorded balance ${formatMoney(account.balance)}. Check for a missing receipt or a wrong entry.`,
        link: '/finance/transactions', queryParams: { accountId: account.id }, action: 'Review its transactions'
      });
    }

    const unstated = active.filter(a => a.ownership === 'Unspecified');
    if (unstated.length > 0) {
      items.push({
        tone: 'info', title: 'Ownership not stated',
        detail: `${unstated.map(a => a.name).join(', ')}: company or personal money is not recorded.`,
        link: '/finance/accounts', action: 'Review accounts'
      });
    }
  }

  const critical = behaviour?.alerts.filter(a => a.severity === 'Critical').length ?? 0;
  if (critical > 0) {
    items.push({
      tone: 'warning', title: `${critical} critical trading behaviour ${critical === 1 ? 'alert' : 'alerts'}`,
      detail: 'Detected from the journal: overtrading or long loss streaks.',
      link: '/trading/behaviour', action: 'Review behaviour'
    });
  }
  return items;
}
