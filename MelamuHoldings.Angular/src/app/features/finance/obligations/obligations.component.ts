import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FinanceApiService, todayIso } from '../finance-api.service';
import { CurrencyInfo, Obligation, ObligationStanding, PaymentCandidate, RECURRENCES, Recurrence } from '../finance.models';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

const STANDING: Record<ObligationStanding, { label: string; tag: string }> = {
  Overdue: { label: 'Overdue', tag: 'tag tag-negative' },
  DueSoon: { label: 'Due soon', tag: 'tag tag-warning' },
  Upcoming: { label: 'Upcoming', tag: 'tag' },
  Completed: { label: 'Paid', tag: 'tag tag-positive' },
  Cancelled: { label: 'Cancelled', tag: 'tag' }
};

// What is coming due within this many days is totalled at the top of the page
export const HORIZON_DAYS = 30;

// What the business has to pay, and when. A payment is recorded by linking an expense already in the ledger;
// nothing on this page records or moves money.
@Component({
  selector: 'app-finance-obligations',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header section="Finance" title="Obligations"
        description="What the business has to pay, and when. Record each payment by linking the expense from the ledger; nothing here moves money.">
        <button type="button" class="btn btn-primary" (click)="startCreate()">New obligation</button>
      </app-page-header>

      @if (editing() !== undefined) {
        <form class="card form-card" [formGroup]="form" (ngSubmit)="save()" [attr.aria-label]="editing() ? 'Edit obligation' : 'New obligation'">
          <div class="form-grid">
            <div class="field"><label for="ob-name">Name</label><input id="ob-name" formControlName="name" maxlength="100" /></div>
            <div class="field"><label for="ob-amount">Amount</label><input id="ob-amount" type="number" min="0.01" step="0.01" formControlName="amount" /></div>
            <div class="field">
              <label for="ob-currency">Currency</label>
              <select id="ob-currency" formControlName="currency">
                @for (c of currencies(); track c.code) { <option [value]="c.code">{{ c.code }}</option> }
              </select>
            </div>
            <div class="field">
              <label for="ob-recurrence">How often</label>
              <select id="ob-recurrence" formControlName="recurrence">
                @for (r of recurrences; track r.value) { <option [value]="r.value">{{ r.label }}</option> }
              </select>
            </div>
            <div class="field"><label for="ob-first">{{ form.controls.recurrence.value === 'None' ? 'Due date' : 'First due date' }}</label><input id="ob-first" type="date" formControlName="firstDueDate" /></div>
            <div class="field"><label for="ob-category">Category (optional)</label><input id="ob-category" formControlName="category" maxlength="50" placeholder="e.g. Rent" /></div>
            <div class="field wide"><label for="ob-notes">Notes (optional)</label><input id="ob-notes" formControlName="notes" maxlength="500" /></div>
          </div>
          @if (scheduleLocked()) { <p class="small secondary">Payments are recorded, so the currency and schedule are fixed. Cancel this obligation and add a new one to change them.</p> }
          @if (formError()) { <p class="notice notice-error" role="alert">{{ formError() }}</p> }
          <div class="form-actions">
            <button type="submit" class="btn btn-primary" [disabled]="form.invalid || busy()">{{ busy() ? 'Saving…' : 'Save obligation' }}</button>
            <button type="button" class="btn" (click)="editing.set(undefined)">Cancel</button>
          </div>
        </form>
      }

      @switch (state()) {
        @case ('loading') { <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading obligations...</span></div> }
        @case ('error') { <app-load-error title="Could not load obligations" [message]="error()" (retry)="load()" /> }
        @case ('ready') {
          <div class="metric-strip">
            @for (d of dueSoon(); track d.currency) {
              <div class="metric">
                <span class="metric-label">Due in the next {{ horizon }} days · {{ d.currency }}</span>
                <span class="metric-value">{{ d.amount | money: d.currency }}</span>
                <span class="metric-note">{{ d.count }} {{ d.count === 1 ? 'payment' : 'payments' }}, overdue ones included</span>
              </div>
            } @empty {
              <div class="metric"><span class="metric-label">Due in the next {{ horizon }} days</span><span class="metric-value">Nothing</span></div>
            }
            <div class="metric">
              <span class="metric-label">Overdue</span>
              <span class="metric-value" [class.text-negative]="overdue() > 0">{{ overdue() }}</span>
              <span class="metric-note">Past their due date and not recorded as paid</span>
            </div>
          </div>

          <div class="toolbar">
            <label class="check"><input type="checkbox" [checked]="includeClosed()" (change)="toggleClosed()" />Show paid and cancelled</label>
          </div>

          <div class="card table-card">
            @if (obligations().length === 0) {
              <p class="empty-state small">No obligations recorded. Add rent, subscriptions, insurance or anything else the business has to pay.</p>
            } @else {
              <table class="data-table">
                <thead><tr><th>Obligation</th><th class="num">Amount</th><th>How often</th><th>Next due</th><th><span class="visually-hidden">Actions</span></th></tr></thead>
                <tbody>
                  @for (o of obligations(); track o.id) {
                    <tr>
                      <td>
                        <div class="name">{{ o.name }}</div>
                        @if (o.category) { <div class="small muted">{{ o.category }}</div> }
                        @if (hasReversedPayment(o)) { <span class="tag tag-negative">A payment's expense was reversed</span> }
                      </td>
                      <td class="num">{{ o.amount.amount | money: o.amount.currency }}</td>
                      <td>{{ recurrenceLabel(o.recurrence) }}</td>
                      <td>
                        <div class="nowrap">{{ o.dueDate }}</div>
                        <span [class]="standing[o.standing].tag">{{ standing[o.standing].label }}</span>
                        @if (o.status === 'Active') { <span class="small muted"> {{ dueText(o) }}</span> }
                      </td>
                      <td class="actions">
                        <button type="button" class="btn-link small" (click)="toggle(o)">{{ expanded() === o.id ? 'Close' : (o.status === 'Active' ? 'Record payment' : 'Payments') }}</button>
                        @if (o.status === 'Active') {
                          <button type="button" class="btn-link small" (click)="startEdit(o)">Edit</button>
                          @if (confirmingCancel() === o.id) {
                            <button type="button" class="btn btn-sm btn-danger" [disabled]="busy()" (click)="cancel(o)">Cancel it</button>
                            <button type="button" class="btn-link small" (click)="confirmingCancel.set(null)">Keep</button>
                          } @else {
                            <button type="button" class="btn-link small" (click)="confirmingCancel.set(o.id)">Cancel</button>
                          }
                        }
                      </td>
                    </tr>
                    @if (expanded() === o.id) {
                      <tr class="detail">
                        <td colspan="5">
                          @if (rowError()) { <p class="notice notice-error" role="alert">{{ rowError() }}</p> }
                          @if (o.payments.length > 0) {
                            <h3>Payments</h3>
                            <ul class="payments">
                              @for (p of o.payments; track p.id; let last = $last) {
                                <li>
                                  Due {{ p.dueDate }} · paid {{ p.paidOn }} · {{ p.amount.amount | money: p.amount.currency }}
                                  · <a [routerLink]="['/finance/transactions']" [queryParams]="{ from: p.paidOn, to: p.paidOn, type: 'Expense' }">transaction {{ p.transactionId }}</a>
                                  @if (p.reversed) { <span class="tag tag-negative">Expense reversed</span> }
                                  @if (last && o.status !== 'Cancelled') { <button type="button" class="btn-link small" [disabled]="busy()" (click)="undo(o)">Undo this payment</button> }
                                </li>
                              }
                            </ul>
                          }
                          @if (o.status === 'Active') {
                            <h3>Record the payment due {{ o.dueDate }}</h3>
                            @if (candidates() === null) { <p class="small muted">Looking for expenses…</p> }
                            @else if (candidates()!.length === 0) {
                              <p class="small">No unlinked {{ o.amount.currency }} expense in the last 60 days. <a routerLink="/finance/transactions/new" [queryParams]="{ type: 'expense' }">Record the expense first</a>, then link it here.</p>
                            } @else {
                              <p class="small secondary">Choose the expense that paid it. It stays in the ledger as it is; this only links it.</p>
                              <ul class="candidates">
                                @for (c of candidates(); track c.transactionId) {
                                  <li>
                                    <span>{{ c.date }} · {{ c.description }}@if (c.category) { <span class="muted"> ({{ c.category }})</span> }</span>
                                    <span class="num">{{ c.amount.amount | money: c.amount.currency }}</span>
                                    <button type="button" class="btn btn-sm" [disabled]="busy()" (click)="pay(o, c)">Use this expense</button>
                                  </li>
                                }
                              </ul>
                            }
                          }
                        </td>
                      </tr>
                    }
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
    .form-card { margin-bottom: var(--space-5); }
    .wide { grid-column: 1 / -1; }
    .toolbar { display: flex; justify-content: flex-end; margin: var(--space-4) 0 var(--space-2); }
    .check { display: flex; gap: var(--space-2); align-items: center; font-size: var(--text-sm); }
    .check input { height: auto; }
    .name { font-weight: 500; }
    .actions { display: flex; gap: var(--space-3); align-items: center; justify-content: flex-end; flex-wrap: wrap; }
    .detail td { background: var(--surface-sunken); white-space: normal; }
    .detail h3 { font-size: var(--text-sm); margin: var(--space-2) 0; }
    .payments, .candidates { list-style: none; display: grid; gap: var(--space-2); margin-bottom: var(--space-3); }
    .candidates li { display: flex; gap: var(--space-3); align-items: center; flex-wrap: wrap; }
    .candidates li > span:first-child { flex: 1 1 240px; }
    .nowrap { white-space: nowrap; }
    .empty-state.small { padding: var(--space-6) var(--space-5); }
  `]
})
export class ObligationsComponent {
  private api = inject(FinanceApiService);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);

  readonly standing = STANDING;
  readonly recurrences = RECURRENCES;
  readonly horizon = HORIZON_DAYS;
  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly error = signal('');
  readonly obligations = signal<Obligation[]>([]);
  // The overview's warning about a paid obligation links here with closed=true, or it would not be on the page
  readonly includeClosed = signal(inject(ActivatedRoute).snapshot.queryParamMap.get('closed') === 'true');
  readonly currencies = signal<CurrencyInfo[]>([]);
  // undefined: no form; null: adding; an obligation: editing it
  readonly editing = signal<Obligation | null | undefined>(undefined);
  readonly busy = signal(false);
  readonly formError = signal('');
  readonly expanded = signal<number | null>(null);
  readonly candidates = signal<PaymentCandidate[] | null>(null);
  readonly rowError = signal('');
  readonly confirmingCancel = signal<number | null>(null);

  readonly scheduleLocked = computed(() => (this.editing()?.payments.length ?? 0) > 0);
  readonly overdue = computed(() => this.obligations().filter(o => o.standing === 'Overdue').length);

  // Active obligations due within the horizon (overdue ones included), totalled per currency; never across currencies
  readonly dueSoon = computed(() => {
    const totals = new Map<string, { amount: number; count: number }>();
    for (const o of this.obligations().filter(x => x.status === 'Active' && x.daysUntilDue <= HORIZON_DAYS)) {
      const t = totals.get(o.amount.currency) ?? { amount: 0, count: 0 };
      totals.set(o.amount.currency, { amount: Math.round((t.amount + o.amount.amount) * 100) / 100, count: t.count + 1 });
    }
    return [...totals].sort(([a], [b]) => a.localeCompare(b)).map(([currency, t]) => ({ currency, ...t }));
  });

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    amount: [null as number | null, [Validators.required, Validators.min(0.01)]],
    currency: ['ZAR', Validators.required],
    recurrence: ['Monthly' as Recurrence, Validators.required],
    firstDueDate: [todayIso(), Validators.required],
    category: ['', Validators.maxLength(50)],
    notes: ['', Validators.maxLength(500)]
  });

  constructor() {
    this.load();
    this.api.getCurrencies().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: c => this.currencies.set(c), error: () => {} });
  }

  load(): void {
    this.state.set('loading');
    this.api.getObligations(this.includeClosed()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: o => { this.obligations.set(o); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  toggleClosed(): void {
    this.includeClosed.update(v => !v);
    this.load();
  }

  startCreate(): void {
    this.form.reset();
    this.form.enable();
    this.formError.set('');
    this.editing.set(null);
  }

  startEdit(o: Obligation): void {
    this.form.reset({
      name: o.name, amount: o.amount.amount, currency: o.amount.currency, recurrence: o.recurrence,
      firstDueDate: o.firstDueDate, category: o.category ?? '', notes: o.notes ?? ''
    });
    this.form.enable();
    this.form.controls.currency.disable();
    if (o.payments.length > 0) { this.form.controls.recurrence.disable(); this.form.controls.firstDueDate.disable(); }
    this.formError.set('');
    this.editing.set(o);
  }

  save(): void {
    if (this.form.invalid || this.busy()) return;
    const v = this.form.getRawValue();
    const editing = this.editing();
    this.busy.set(true);
    this.formError.set('');
    this.api.saveObligation(editing?.id ?? null, {
      name: v.name.trim(), amount: v.amount!, currency: v.currency, recurrence: v.recurrence, firstDueDate: v.firstDueDate,
      category: v.category.trim() || null, notes: v.notes.trim() || null
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.busy.set(false); this.editing.set(undefined); this.load(); },
      error: err => { this.busy.set(false); this.formError.set(`Not saved: ${describeHttpError(err)}`); }
    });
  }

  toggle(o: Obligation): void {
    const opening = this.expanded() !== o.id;
    this.expanded.set(opening ? o.id : null);
    this.rowError.set('');
    this.candidates.set(null);
    if (opening && o.status === 'Active') {
      this.api.getPaymentCandidates(o.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: c => this.candidates.set(c),
        error: err => { this.candidates.set([]); this.rowError.set(`Could not look for expenses: ${describeHttpError(err)}`); }
      });
    }
  }

  pay(o: Obligation, c: PaymentCandidate): void {
    this.act(this.api.recordObligationPayment(o.id, c.transactionId), 'Not recorded');
  }

  undo(o: Obligation): void {
    this.act(this.api.undoObligationPayment(o.id), 'Not undone');
  }

  cancel(o: Obligation): void {
    this.confirmingCancel.set(null);
    this.act(this.api.cancelObligation(o.id), 'Not cancelled');
  }

  recurrenceLabel(r: Recurrence): string {
    return RECURRENCES.find(x => x.value === r)!.label;
  }

  dueText(o: Obligation): string {
    const d = o.daysUntilDue;
    return d === 0 ? 'today' : d > 0 ? `in ${d} ${d === 1 ? 'day' : 'days'}` : `${-d} ${d === -1 ? 'day' : 'days'} late`;
  }

  hasReversedPayment(o: Obligation): boolean {
    return o.payments.some(p => p.reversed);
  }

  private act(request: ReturnType<FinanceApiService['undoObligationPayment']>, failure: string): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.rowError.set('');
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: updated => {
        this.busy.set(false);
        this.obligations.update(list => list.map(x => x.id === updated.id ? updated : x));
        const keepOpen = this.expanded() === updated.id;
        this.expanded.set(null);
        if (keepOpen) this.toggle(updated);
      },
      error: err => { this.busy.set(false); this.rowError.set(`${failure}: ${describeHttpError(err)}`); }
    });
  }
}

