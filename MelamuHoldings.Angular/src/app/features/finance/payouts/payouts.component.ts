import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { FinanceApiService, newIdempotencyKey, todayIso } from '../finance-api.service';
import { ALLOCATION_STATUS, CurrencyInfo, Money, PAYOUT_STATUS, Payout, PayoutStatus } from '../finance.models';
import { amountValidator } from '../amount.validator';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

type LoadState = 'loading' | 'ready' | 'error';
type Filter = 'All' | PayoutStatus;

@Component({
  selector: 'app-finance-payouts',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header section="Finance" title="Payouts"
        description="Trading payouts from request to money received. A request or an approval is not cash: only a received payout reaches the ledger.">
        <a class="btn" routerLink="/finance/allocations">Allocations</a>
        <button type="button" class="btn btn-primary" (click)="toggleForm()" [attr.aria-expanded]="showForm()">
          {{ showForm() ? 'Cancel' : 'Record payout request' }}
        </button>
      </app-page-header>

      @if (showForm()) {
        <form class="card form-card" [formGroup]="form" (ngSubmit)="create()" aria-label="New payout request">
          <div class="form-grid">
            <div class="field">
              <label for="po-source">Paid by</label>
              <input id="po-source" formControlName="source" placeholder="e.g. the prop firm" />
            </div>
            <div class="field">
              <label for="po-currency">Currency of the payout</label>
              <select id="po-currency" formControlName="currency">
                @for (c of currencies(); track c.code) { <option [value]="c.code">{{ c.code }}</option> }
              </select>
            </div>
            <div class="field">
              <label for="po-amount">Amount requested</label>
              <input id="po-amount" type="number" step="0.01" formControlName="amount" />
              @if (form.controls.amount.touched && form.controls.amount.errors?.['amount']; as e) { <span class="error">{{ e }}</span> }
            </div>
            <div class="field">
              <label for="po-date">Requested on</label>
              <input id="po-date" type="date" formControlName="requestedOn" />
            </div>
            <div class="field">
              <label for="po-ref">Reference (optional)</label>
              <input id="po-ref" formControlName="reference" placeholder="Payout or ticket number" />
            </div>
            <div class="field">
              <label for="po-notes">Notes (optional)</label>
              <input id="po-notes" formControlName="notes" />
            </div>
          </div>
          @if (formError()) { <p class="notice notice-error" role="alert">{{ formError() }}</p> }
          <div class="form-actions">
            <button type="submit" class="btn btn-primary" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Record request' }}</button>
          </div>
        </form>
      }

      @switch (state()) {
        @case ('loading') {
          <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading payouts...</span></div>
        }
        @case ('error') {
          <app-load-error title="Could not load payouts" [message]="error()" (retry)="load()" />
        }
        @case ('ready') {
          @if (expected().length > 0) {
            <section class="metric-strip expected" aria-label="Expected payouts">
              @for (m of expected(); track m.currency) {
                <div class="metric">
                  <span class="metric-label">Expected · {{ m.currency }}</span>
                  <span class="metric-value">{{ m.amount | money: m.currency }}</span>
                  <span class="metric-note">Requested or approved, not yet received. Not cash.</span>
                </div>
              }
            </section>
          }

          <div class="card table-card">
            <div class="tabs" role="tablist" aria-label="Filter payouts by status">
              @for (f of filters; track f) {
                <button type="button" role="tab" class="tab" [attr.aria-selected]="filter() === f" (click)="filter.set(f)">
                  {{ f }} <span class="count">{{ count(f) }}</span>
                </button>
              }
            </div>
            @if (visible().length === 0) {
              <p class="empty-state small">
                {{ payouts().length === 0 ? 'No payouts recorded yet. Record a request as soon as you ask for a payout, so it can be followed to the bank.' : 'No payouts with this status.' }}
              </p>
            } @else {
              <table class="data-table">
                <thead>
                  <tr><th>Requested</th><th>Paid by</th><th class="num">Amount</th><th>Status</th><th>Received</th><th>Allocation</th></tr>
                </thead>
                <tbody>
                  @for (p of visible(); track p.id) {
                    <tr>
                      <td class="nowrap secondary">{{ p.requestedOn }}</td>
                      <td>
                        <a [routerLink]="['/finance/payouts', p.id]">{{ p.source }}</a>
                        <div class="small muted">#{{ p.id }}{{ p.reference ? ' · ' + p.reference : '' }}</div>
                      </td>
                      <td class="num">
                        {{ (p.approved ?? p.requested).amount | money: p.requested.currency }}
                        @if (p.approved && p.approved.amount !== p.requested.amount) { <div class="small muted">requested {{ p.requested.amount | money: p.requested.currency }}</div> }
                      </td>
                      <td><span [class]="payoutStatus[p.status].tag">{{ payoutStatus[p.status].label }}</span></td>
                      <td class="small">
                        @if (p.credited) {
                          {{ p.credited.amount | money: p.credited.currency }}
                          <div class="muted">{{ p.receivedOn }} · {{ p.receivedIntoAccountName }}</div>
                        } @else { <span class="muted">—</span> }
                      </td>
                      <td class="small">
                        @if (p.allocationId && p.allocationStatus) {
                          <a [routerLink]="['/finance/allocations', p.allocationId]"><span [class]="allocationStatus[p.allocationStatus].tag">{{ allocationStatus[p.allocationStatus].label }}</span></a>
                        } @else if (p.status === 'Received') {
                          <a [routerLink]="['/finance/payouts', p.id]">Allocate</a>
                        } @else { <span class="muted">—</span> }
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
    .form-card, .expected { margin-bottom: var(--space-5); }
    .count { color: var(--text-muted); font-weight: 400; margin-left: 2px; }
    .empty-state.small { padding: var(--space-8) var(--space-5); }
  `]
})
export class PayoutsComponent {
  private api = inject(FinanceApiService);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);

  readonly payoutStatus = PAYOUT_STATUS;
  readonly allocationStatus = ALLOCATION_STATUS;
  readonly filters: Filter[] = ['All', 'Requested', 'Approved', 'Received', 'Rejected'];

  readonly state = signal<LoadState>('loading');
  readonly error = signal('');
  readonly payouts = signal<Payout[]>([]);
  readonly currencies = signal<CurrencyInfo[]>([{ code: 'USD', minorUnits: 2 }]);
  readonly filter = signal<Filter>('All');
  readonly showForm = signal(false);
  readonly saving = signal(false);
  readonly formError = signal('');

  // Kept while the same request is retried; renewed when the form changes
  private key = newIdempotencyKey();

  readonly form = this.fb.nonNullable.group({
    source: ['', [Validators.required, Validators.maxLength(100)]],
    currency: ['USD', Validators.required],
    amount: [null as number | null, [Validators.required, amountValidator()]],
    requestedOn: [todayIso(), Validators.required],
    reference: ['', Validators.maxLength(100)],
    notes: ['', Validators.maxLength(500)]
  });

  readonly visible = computed(() => this.filter() === 'All' ? this.payouts() : this.payouts().filter(p => p.status === this.filter()));

  // What is on its way, per currency: display only, and labelled as not cash
  readonly expected = computed<Money[]>(() => {
    const totals = new Map<string, number>();
    for (const p of this.payouts().filter(p => p.status === 'Requested' || p.status === 'Approved')) {
      const amount = (p.approved ?? p.requested).amount;
      totals.set(p.requested.currency, Math.round(((totals.get(p.requested.currency) ?? 0) + amount) * 100) / 100);
    }
    return [...totals].map(([currency, amount]) => ({ currency, amount }));
  });

  constructor() {
    this.load();
    this.api.getCurrencies().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: c => this.currencies.set(c), error: () => {} });
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (!this.saving()) this.key = newIdempotencyKey();
    });
  }

  count(filter: Filter): number {
    return filter === 'All' ? this.payouts().length : this.payouts().filter(p => p.status === filter).length;
  }

  load(): void {
    this.state.set('loading');
    this.api.getPayouts().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: p => { this.payouts.set(p); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  toggleForm(): void {
    this.showForm.set(!this.showForm());
    this.formError.set('');
  }

  create(): void {
    if (this.saving()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    this.saving.set(true);
    this.formError.set('');
    this.api.createPayout({ ...v, reference: v.reference || undefined, notes: v.notes || undefined }, this.key)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.showForm.set(false);
          this.form.reset();
          this.key = newIdempotencyKey();
          this.load();
        },
        error: err => { this.saving.set(false); this.formError.set(`Not recorded: ${describeHttpError(err)}`); }
      });
  }
}
