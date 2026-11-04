import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { forkJoin, Observable } from 'rxjs';
import { FinanceApiService } from '../finance-api.service';
import { Affordability, AffordabilityVerdict, CurrencyInfo, Fund, PaymentCandidate, PlannedPurchase, PlannedPurchaseStatus } from '../finance.models';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

const VERDICT: Record<AffordabilityVerdict, { label: string; tag: string }> = {
  Affordable: { label: 'Affordable', tag: 'tag tag-positive' },
  BreachesMinimum: { label: 'Breaches the minimum', tag: 'tag tag-negative' },
  NoMinimum: { label: 'No minimum to compare', tag: 'tag' },
  NotEnoughMoney: { label: 'Not enough money', tag: 'tag tag-negative' }
};

const STATUS: Record<PlannedPurchaseStatus, { label: string; tag: string }> = {
  Proposed: { label: 'Proposed', tag: 'tag' },
  Committed: { label: 'Committed', tag: 'tag tag-info' },
  Purchased: { label: 'Purchased', tag: 'tag tag-positive' },
  Cancelled: { label: 'Cancelled', tag: 'tag' }
};

// What the business intends to buy, and whether the fund it comes from can afford it. The check warns and never blocks,
// and a purchase is recorded by linking an expense from the ledger; nothing on this page records or moves money.
@Component({
  selector: 'app-finance-purchases',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header section="Finance" title="Planned purchases"
        description="What you intend to buy, and whether it can be afforded without breaching the fund's minimum. The check warns and never blocks; nothing here moves money.">
        <button type="button" class="btn btn-primary" (click)="startCreate()">New purchase</button>
      </app-page-header>

      @if (editing() !== undefined) {
        <form class="card form-card" [formGroup]="form" (ngSubmit)="save()" [attr.aria-label]="editing() ? 'Edit purchase' : 'New purchase'">
          <div class="form-grid">
            <div class="field"><label for="pp-name">Name</label><input id="pp-name" formControlName="name" maxlength="100" /></div>
            <div class="field"><label for="pp-amount">Amount</label><input id="pp-amount" type="number" min="0.01" step="0.01" formControlName="amount" /></div>
            <div class="field">
              <label for="pp-currency">Currency</label>
              <select id="pp-currency" formControlName="currency">
                @for (c of currencies(); track c.code) { <option [value]="c.code">{{ c.code }}</option> }
              </select>
            </div>
            <div class="field">
              <label for="pp-fund">Paid from fund</label>
              <select id="pp-fund" formControlName="fundId">
                @for (f of choosable(); track f.id) { <option [ngValue]="f.id">{{ f.name }}</option> }
              </select>
            </div>
            <div class="field"><label for="pp-category">Category (optional)</label><input id="pp-category" formControlName="category" maxlength="50" placeholder="e.g. Equipment" /></div>
            <div class="field"><label for="pp-target">Target date (optional)</label><input id="pp-target" type="date" formControlName="targetDate" /></div>
            <div class="field wide"><label for="pp-notes">Notes (optional)</label><input id="pp-notes" formControlName="notes" maxlength="500" /></div>
          </div>
          @if (formError()) { <p class="notice notice-error" role="alert">{{ formError() }}</p> }
          <div class="form-actions">
            <button type="submit" class="btn btn-primary" [disabled]="form.invalid || busy()">{{ busy() ? 'Saving…' : 'Save purchase' }}</button>
            <button type="button" class="btn" (click)="editing.set(undefined)">Cancel</button>
          </div>
        </form>
      }

      @switch (state()) {
        @case ('loading') { <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading planned purchases...</span></div> }
        @case ('error') { <app-load-error title="Could not load planned purchases" [message]="error()" (retry)="load()" /> }
        @case ('ready') {
          <div class="toolbar">
            <label class="check"><input type="checkbox" [checked]="includeClosed()" (change)="toggleClosed()" />Show purchased and cancelled</label>
          </div>

          @if (rowError()) { <p class="notice notice-error" role="alert">{{ rowError() }}</p> }

          @for (p of purchases(); track p.id) {
            <section class="card purchase" [attr.aria-labelledby]="'pp-' + p.id">
              <div class="card-header">
                <div>
                  <h2 [id]="'pp-' + p.id">{{ p.name }}</h2>
                  <span class="small muted">{{ p.fundName }}@if (p.category) { · {{ p.category }} }@if (p.targetDate) { · by {{ p.targetDate }} }</span>
                </div>
                <div class="head-figures">
                  <span class="amount">{{ p.amount.amount | money: p.amount.currency }}</span>
                  <span [class]="status[p.status].tag">{{ status[p.status].label }}</span>
                </div>
              </div>

              @if (p.affordability; as a) {
                <div class="check-result">
                  <span [class]="verdict[a.verdict].tag">{{ verdict[a.verdict].label }}</span>
                  <p class="small">{{ answer(p, a) }}</p>
                  <p class="small secondary">
                    {{ a.available.amount | money: a.available.currency }} of {{ p.fundName }} in company bank and cash accounts@if (a.committedElsewhere.amount > 0) {, less {{ a.committedElsewhere.amount | money: a.committedElsewhere.currency }} committed to other purchases}.
                    @if (a.obligationsDue.amount > 0) { {{ a.obligationsDue.amount | money: a.obligationsDue.currency }} of obligations fall due within 30 days; they are not deducted, because they are not tied to a fund. }
                  </p>
                </div>
              }

              @if (p.status === 'Purchased') {
                <p class="small pad">
                  Bought on {{ p.purchasedOn }} for {{ p.paid!.amount | money: p.paid!.currency }}
                  · <a [routerLink]="['/finance/transactions']" [queryParams]="{ from: p.purchasedOn, to: p.purchasedOn, type: 'Expense' }">transaction {{ p.transactionId }}</a>
                  @if (p.expenseReversed) { <span class="tag tag-negative">Expense reversed</span> }
                </p>
              }
              @if (p.notes) { <p class="small secondary pad">{{ p.notes }}</p> }

              <div class="actions">
                @switch (p.status) {
                  @case ('Proposed') {
                    <button type="button" class="btn btn-sm" [disabled]="busy()" (click)="act(api.commitPurchase(p.id), 'Not committed')">Commit</button>
                    <button type="button" class="btn-link small" (click)="startEdit(p)">Edit</button>
                  }
                  @case ('Committed') {
                    <button type="button" class="btn btn-sm" (click)="toggleCandidates(p)">{{ buying() === p.id ? 'Close' : 'Record as bought' }}</button>
                    <button type="button" class="btn-link small" [disabled]="busy()" (click)="act(api.withdrawPurchaseCommitment(p.id), 'Not changed')">Back to proposed</button>
                  }
                  @case ('Purchased') {
                    <button type="button" class="btn-link small" [disabled]="busy()" (click)="act(api.undoPurchase(p.id), 'Not undone')">Undo this purchase</button>
                  }
                }
                @if (p.status === 'Proposed' || p.status === 'Committed') {
                  @if (confirmingCancel() === p.id) {
                    <button type="button" class="btn btn-sm btn-danger" [disabled]="busy()" (click)="confirmingCancel.set(null); act(api.cancelPurchase(p.id), 'Not cancelled')">Cancel it</button>
                    <button type="button" class="btn-link small" (click)="confirmingCancel.set(null)">Keep</button>
                  } @else {
                    <button type="button" class="btn-link small" (click)="confirmingCancel.set(p.id)">Cancel</button>
                  }
                }
              </div>

              @if (buying() === p.id) {
                <div class="candidates-panel">
                  @if (candidates() === null) { <p class="small muted">Looking for expenses…</p> }
                  @else if (candidates()!.length === 0) {
                    <p class="small">No unlinked {{ p.amount.currency }} expense found. <a routerLink="/finance/transactions/new" [queryParams]="{ type: 'expense' }">Record the expense first</a>, then link it here.</p>
                  } @else {
                    <p class="small secondary">Choose the expense that paid for it. It stays in the ledger as it is; this only links it.</p>
                    <ul class="candidates">
                      @for (c of candidates(); track c.transactionId) {
                        <li>
                          <span>{{ c.date }} · {{ c.description }}@if (c.category) { <span class="muted"> ({{ c.category }})</span> }</span>
                          <span class="num">{{ c.amount.amount | money: c.amount.currency }}</span>
                          <button type="button" class="btn btn-sm" [disabled]="busy()" (click)="act(api.recordPurchase(p.id, c.transactionId), 'Not recorded')">Use this expense</button>
                        </li>
                      }
                    </ul>
                  }
                </div>
              }
            </section>
          } @empty {
            <div class="card empty-state">
              <h2>No planned purchases</h2>
              <p>Add something you intend to buy to see whether it can be afforded without breaching the fund's minimum.</p>
            </div>
          }
        }
      }
    </div>
  `,
  styles: [`
    .form-card { margin-bottom: var(--space-5); }
    .wide { grid-column: 1 / -1; }
    .toolbar { display: flex; justify-content: flex-end; margin-bottom: var(--space-2); }
    .check { display: flex; gap: var(--space-2); align-items: center; font-size: var(--text-sm); }
    .check input { height: auto; }
    .purchase { margin-top: var(--space-4); }
    .purchase .card-header { align-items: flex-start; gap: var(--space-3); flex-wrap: wrap; }
    .head-figures { display: flex; gap: var(--space-3); align-items: center; flex-wrap: wrap; }
    .amount { font-weight: 600; font-variant-numeric: tabular-nums; }
    .check-result { padding: var(--space-2) var(--space-5) var(--space-3); }
    .check-result p { margin-top: var(--space-2); }
    .pad { padding: 0 var(--space-5) var(--space-3); }
    .actions { display: flex; gap: var(--space-3); align-items: center; flex-wrap: wrap; padding: 0 var(--space-5) var(--space-4); }
    .candidates-panel { padding: var(--space-3) var(--space-5) var(--space-4); background: var(--surface-sunken); }
    .candidates { list-style: none; display: grid; gap: var(--space-2); margin-top: var(--space-2); }
    .candidates li { display: flex; gap: var(--space-3); align-items: center; flex-wrap: wrap; }
    .candidates li > span:first-child { flex: 1 1 240px; }
  `]
})
export class PurchasesComponent {
  readonly api = inject(FinanceApiService);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);

  readonly verdict = VERDICT;
  readonly status = STATUS;
  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly error = signal('');
  readonly purchases = signal<PlannedPurchase[]>([]);
  readonly funds = signal<Fund[]>([]);
  readonly currencies = signal<CurrencyInfo[]>([]);
  // The overview's warning about a bought purchase links here with closed=true, or it would not be on the page
  readonly includeClosed = signal(inject(ActivatedRoute).snapshot.queryParamMap.get('closed') === 'true');
  // undefined: no form; null: adding; a purchase: editing it
  readonly editing = signal<PlannedPurchase | null | undefined>(undefined);
  readonly busy = signal(false);
  readonly formError = signal('');
  readonly rowError = signal('');
  readonly buying = signal<number | null>(null);
  readonly candidates = signal<PaymentCandidate[] | null>(null);
  readonly confirmingCancel = signal<number | null>(null);

  // The system fund holds money waiting to be allocated, so nothing is paid from it
  readonly choosable = computed(() => this.funds().filter(f => !f.isSystem));

  readonly form = this.fb.group({
    name: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(100)]),
    amount: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    currency: this.fb.nonNullable.control('ZAR', Validators.required),
    fundId: this.fb.control<number | null>(null, Validators.required),
    category: this.fb.nonNullable.control('', Validators.maxLength(50)),
    targetDate: this.fb.nonNullable.control(''),
    notes: this.fb.nonNullable.control('', Validators.maxLength(500))
  });

  constructor() {
    this.load();
    this.api.getCurrencies().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: c => this.currencies.set(c), error: () => {} });
  }

  load(): void {
    this.state.set('loading');
    forkJoin({ purchases: this.api.getPlannedPurchases(this.includeClosed()), funds: this.api.getFunds() }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ purchases, funds }) => { this.purchases.set(purchases); this.funds.set(funds); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  toggleClosed(): void {
    this.includeClosed.update(v => !v);
    this.load();
  }

  // The operating fund first, since that is the minimum the check is mostly about
  startCreate(): void {
    const operating = this.choosable().find(f => f.name === 'Operating Capital') ?? this.choosable()[0];
    this.form.reset({ name: '', amount: null, currency: 'ZAR', fundId: operating?.id ?? null, category: '', targetDate: '', notes: '' });
    this.formError.set('');
    this.editing.set(null);
  }

  startEdit(p: PlannedPurchase): void {
    this.form.reset({
      name: p.name, amount: p.amount.amount, currency: p.amount.currency, fundId: p.fundId,
      category: p.category ?? '', targetDate: p.targetDate ?? '', notes: p.notes ?? ''
    });
    this.formError.set('');
    this.editing.set(p);
  }

  save(): void {
    if (this.form.invalid || this.busy()) return;
    const v = this.form.getRawValue();
    this.busy.set(true);
    this.formError.set('');
    this.api.savePlannedPurchase(this.editing()?.id ?? null, {
      name: v.name.trim(), amount: v.amount!, currency: v.currency, fundId: v.fundId!,
      category: v.category.trim() || null, targetDate: v.targetDate || null, notes: v.notes.trim() || null
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.busy.set(false); this.editing.set(undefined); this.load(); },
      error: err => { this.busy.set(false); this.formError.set(`Not saved: ${describeHttpError(err)}`); }
    });
  }

  toggleCandidates(p: PlannedPurchase): void {
    const opening = this.buying() !== p.id;
    this.buying.set(opening ? p.id : null);
    this.candidates.set(null);
    this.rowError.set('');
    if (opening) {
      this.api.getPurchaseCandidates(p.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: c => this.candidates.set(c),
        error: err => { this.candidates.set([]); this.rowError.set(`Could not look for expenses: ${describeHttpError(err)}`); }
      });
    }
  }

  // A change to one purchase moves what is reserved for the others, so the whole list is reloaded
  act(request: Observable<PlannedPurchase>, failure: string): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.rowError.set('');
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.busy.set(false); this.buying.set(null); this.load(); },
      error: err => { this.busy.set(false); this.rowError.set(`${failure}: ${describeHttpError(err)}`); }
    });
  }

  answer(p: PlannedPurchase, a: Affordability): string {
    const money = (m: { amount: number; currency: string }) =>
      `${m.currency} ${m.amount.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
    switch (a.verdict) {
      case 'Affordable':
        return `Leaves ${money(a.after)} in ${p.fundName}, at or above its minimum of ${money(a.minimum!)}.`;
      case 'BreachesMinimum':
        return `Would leave ${money(a.after)} in ${p.fundName}, ${money(a.shortfall!)} below its minimum of ${money(a.minimum!)}.`;
      case 'NoMinimum':
        return `Would leave ${money(a.after)} in ${p.fundName}. ${this.noMinimum(p, a, money)}`;
      case 'NotEnoughMoney':
        return `${p.fundName} is ${money(a.shortfall!)} short. ${this.noMinimum(p, a, money)}`;
    }
  }

  private noMinimum(p: PlannedPurchase, a: Affordability, money: (m: { amount: number; currency: string }) => string): string {
    return a.minimumInOtherCurrency
      ? `Its minimum of ${money(a.minimumInOtherCurrency)} is in another currency and is never converted.`
      : `It has no minimum in ${p.amount.currency} to compare with.`;
  }
}
