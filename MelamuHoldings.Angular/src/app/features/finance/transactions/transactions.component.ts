import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { FinanceApiService, newIdempotencyKey, todayIso } from '../finance-api.service';
import { Account, Fund, Paged, Transaction, TransactionFilter, TransactionType, TYPE_LABELS } from '../finance.models';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

type LoadState = 'loading' | 'ready' | 'error';
const PAGE_SIZE = 25;

@Component({
  selector: 'app-finance-transactions',
  standalone: true,
  imports: [DatePipe, FormsModule, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header section="Finance" title="Transactions"
        description="The ledger. Entries are never edited; a mistake is corrected by reversing it.">
        <a class="btn btn-primary" routerLink="/finance/transactions/new">Record transaction</a>
      </app-page-header>

      <form class="card filters" (ngSubmit)="applyFilters()" aria-label="Filter transactions">
        <div class="field"><label for="f-from">From</label><input id="f-from" type="date" name="from" [(ngModel)]="draft.from" /></div>
        <div class="field"><label for="f-to">To</label><input id="f-to" type="date" name="to" [(ngModel)]="draft.to" /></div>
        <div class="field">
          <label for="f-type">Type</label>
          <select id="f-type" name="type" [(ngModel)]="draft.type">
            <option value="">All types</option>
            @for (t of types; track t) { <option [value]="t">{{ typeLabel(t) }}</option> }
          </select>
        </div>
        <div class="field">
          <label for="f-account">Account</label>
          <select id="f-account" name="accountId" [(ngModel)]="draft.accountId">
            <option [ngValue]="null">All accounts</option>
            @for (a of accounts(); track a.id) { <option [ngValue]="a.id">{{ a.name }}</option> }
          </select>
        </div>
        <div class="field">
          <label for="f-fund">Fund</label>
          <select id="f-fund" name="fundId" [(ngModel)]="draft.fundId">
            <option [ngValue]="null">All funds</option>
            @for (f of funds(); track f.id) { <option [ngValue]="f.id">{{ f.name }}</option> }
          </select>
        </div>
        <label class="check"><input type="checkbox" name="cash" [(ngModel)]="draft.cash" />Bank and cash accounts only</label>
        <div class="filter-actions">
          <button type="submit" class="btn">Apply</button>
          <button type="button" class="btn-link small" (click)="clearFilters()">Clear</button>
        </div>
      </form>

      @switch (state()) {
        @case ('loading') {
          <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading transactions...</span></div>
        }
        @case ('error') {
          <app-load-error title="Could not load transactions" [message]="error()" (retry)="load()" />
        }
        @case ('ready') {
          @if (result(); as r) {
            @if (r.totalCount === 0) {
              <div class="card">No transactions match. <a routerLink="/finance/transactions/new">Record one</a>.</div>
            } @else {
              <div class="card table-card">
                <table class="data-table">
                  <thead><tr><th>Date</th><th>Type</th><th>Description</th><th>Entries</th><th>Status</th></tr></thead>
                  <tbody>
                    @for (t of r.items; track t.id) {
                      <tr class="row" (click)="toggle(t.id)" [class.open]="expanded() === t.id">
                        <td class="nowrap secondary">{{ t.date }}</td>
                        <td><span class="tag" [class.tag-info]="t.type !== 'Reversal'" [class.tag-warning]="t.type === 'Reversal'">{{ typeLabel(t.type) }}</span></td>
                        <td>
                          <button type="button" class="btn-link row-toggle" [attr.aria-expanded]="expanded() === t.id">{{ t.description }}</button>
                          @if (t.reference) { <div class="muted small">Ref {{ t.reference }}</div> }
                        </td>
                        <td class="small">
                          @for (e of t.entries; track $index) {
                            <div class="entry">
                              <span>{{ e.accountName }} · {{ e.fundName }}@if (e.role === 'Fee') { <span class="tag tag-warning fee">Fee</span> }</span>
                              <span [class.amount-in]="e.amount > 0" [class.amount-out]="e.amount < 0">{{ e.amount | money: e.currency : true }}</span>
                            </div>
                          }
                        </td>
                        <td class="small nowrap">
                          @if (t.reversedByTransactionId) { <span class="muted">Reversed by #{{ t.reversedByTransactionId }}</span> }
                          @else if (t.reversesTransactionId) { Reverses #{{ t.reversesTransactionId }} }
                          @else { <span class="muted">#{{ t.id }}</span> }
                        </td>
                      </tr>
                      @if (expanded() === t.id) {
                        <tr class="detail">
                          <td colspan="5">
                            <div class="detail-grid small">
                              <span>Transaction #{{ t.id }}</span>
                              <span>Recorded {{ t.recordedAt | date: 'yyyy-MM-dd HH:mm' }}</span>
                              @if (t.category) { <span>Category: {{ t.category }}</span> }
                              @if (t.exchangeRate) { <span>Effective rate: {{ t.exchangeRate }} (from the amounts)</span> }
                              @if (t.quotedRate) { <span>Quoted rate: {{ t.quotedRate }} (as entered from the provider)</span> }
                            </div>
                            @if (canReverse(t)) {
                              <form class="reverse" (ngSubmit)="reverse(t)" aria-label="Reverse transaction">
                                <div class="field"><label for="rev-date">Reversal date</label><input id="rev-date" type="date" name="revDate" [(ngModel)]="reverseDate" required /></div>
                                <div class="field grow"><label for="rev-reason">Reason</label><input id="rev-reason" name="revReason" [(ngModel)]="reverseReason" required maxlength="200" placeholder="Why this entry was wrong" /></div>
                                <button type="submit" class="btn btn-danger" [disabled]="!reverseReason.trim() || !reverseDate || reversing()">
                                  {{ reversing() ? 'Reversing…' : 'Reverse transaction' }}
                                </button>
                              </form>
                              @if (reverseError()) { <p class="notice notice-error" role="alert">{{ reverseError() }}</p> }
                            }
                          </td>
                        </tr>
                      }
                    }
                  </tbody>
                </table>
              </div>
              <nav class="pager" aria-label="Transaction pages">
                <button type="button" class="btn" [disabled]="r.page <= 1" (click)="goToPage(r.page - 1)">Previous</button>
                <span class="small muted">Page {{ r.page }} of {{ r.totalPages }} · {{ r.totalCount }} transactions</span>
                <button type="button" class="btn" [disabled]="r.page >= r.totalPages" (click)="goToPage(r.page + 1)">Next</button>
              </nav>
            }
          }
        }
      }
    </div>
  `,
  styles: [`
    .filters { display: flex; flex-wrap: wrap; gap: 0.75rem; align-items: flex-end; margin-bottom: 1rem; padding: 1rem; }
    .filter-actions { display: flex; gap: 0.75rem; align-items: center; padding-bottom: 0.3rem; }
    .check { display: flex; gap: var(--space-2); align-items: center; font-size: var(--text-sm); padding-bottom: 0.55rem; white-space: nowrap; }
    .check input { height: auto; }
    .row { cursor: pointer; &:hover { background: var(--surface-hover); } }
    .row-toggle { color: var(--text-primary); text-align: left; }
    .entry { display: flex; justify-content: space-between; gap: 1rem; }
    .fee { margin-left: 0.4rem; }
    .nowrap { white-space: nowrap; }
    .detail td { background: var(--surface-sunken); }
    .detail-grid { display: flex; flex-wrap: wrap; gap: 0.4rem 1.5rem; color: var(--text-secondary); margin-bottom: 0.75rem; }
    .reverse { display: flex; flex-wrap: wrap; gap: 0.75rem; align-items: flex-end; .grow { flex: 1; min-width: 220px; } }
    .pager { display: flex; align-items: center; gap: 1rem; margin-top: 1rem; }
  `]
})
export class TransactionsComponent {
  private api = inject(FinanceApiService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  readonly types = Object.keys(TYPE_LABELS) as TransactionType[];
  readonly state = signal<LoadState>('loading');
  readonly error = signal('');
  readonly result = signal<Paged<Transaction> | null>(null);
  readonly accounts = signal<Account[]>([]);
  readonly funds = signal<Fund[]>([]);
  readonly expanded = signal<number | null>(null);
  readonly reversing = signal(false);
  readonly reverseError = signal('');

  // The filter in force comes from the URL; `draft` is what the form is editing
  private filter: TransactionFilter = { page: 1, pageSize: PAGE_SIZE };
  draft: TransactionFilter = { ...this.filter };
  reverseDate = todayIso();
  reverseReason = '';
  private reverseKey = newIdempotencyKey();

  constructor() {
    forkJoin({ accounts: this.api.getAccounts(), funds: this.api.getFunds() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: ({ accounts, funds }) => { this.accounts.set(accounts); this.funds.set(funds); }, error: () => {} });

    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      this.filter = filterFrom(params);
      this.draft = { ...this.filter };
      this.load();
    });
  }

  typeLabel(type: TransactionType): string {
    return TYPE_LABELS[type];
  }

  load(): void {
    this.state.set('loading');
    this.expanded.set(null);
    this.api.getTransactions(this.filter).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: r => { this.result.set(r); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  applyFilters(): void {
    this.navigate({ ...this.draft, page: 1 });
  }

  clearFilters(): void {
    this.navigate({ page: 1, pageSize: PAGE_SIZE });
  }

  goToPage(page: number): void {
    this.navigate({ ...this.filter, page });
  }

  toggle(id: number): void {
    const opening = this.expanded() !== id;
    this.expanded.set(opening ? id : null);
    if (opening) {
      this.reverseDate = todayIso();
      this.reverseReason = '';
      this.reverseError.set('');
      this.reverseKey = newIdempotencyKey();
    }
  }

  canReverse(t: Transaction): boolean {
    return t.type !== 'Reversal' && !t.reversedByTransactionId;
  }

  reverse(t: Transaction): void {
    if (this.reversing() || !this.reverseReason.trim()) return;
    this.reversing.set(true);
    this.reverseError.set('');
    // Same key on a retry after a failure, so a reversal whose response was lost cannot post twice
    this.api.reverse(t.id, { date: this.reverseDate, reason: this.reverseReason.trim() }, this.reverseKey)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => { this.reversing.set(false); this.load(); },
        error: err => { this.reversing.set(false); this.reverseError.set(`Not reversed: ${describeHttpError(err)}`); }
      });
  }

  private navigate(filter: TransactionFilter): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        from: filter.from || null, to: filter.to || null, type: filter.type || null,
        accountId: filter.accountId || null, fundId: filter.fundId || null, cash: filter.cash ? 'true' : null,
        page: filter.page > 1 ? filter.page : null
      }
    });
  }
}

function filterFrom(params: ParamMap): TransactionFilter {
  const number = (key: string) => (params.get(key) ? Number(params.get(key)) : null);
  return {
    from: params.get('from') ?? undefined,
    to: params.get('to') ?? undefined,
    type: (params.get('type') as TransactionType | null) ?? '',
    accountId: number('accountId'),
    fundId: number('fundId'),
    cash: params.get('cash') === 'true',
    page: number('page') ?? 1,
    pageSize: PAGE_SIZE
  };
}
