import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { FinanceApiService } from '../finance-api.service';
import { BudgetLine, BudgetMonth, CurrencyInfo, UnbudgetedSpend } from '../finance.models';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

// Budget versus actual per month and category. Actual comes from expense transactions with the same category; a budget
// never moves money and never blocks an expense.
@Component({
  selector: 'app-finance-budgets',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header section="Finance" title="Budgets"
        description="What you plan to spend per category each month, against the expenses recorded with that category. A budget never moves money or blocks an expense.">
        <button type="button" class="btn btn-primary" (click)="startCreate()">New budget line</button>
      </app-page-header>

      <div class="toolbar">
        <div class="field month-field">
          <label for="bd-month">Month</label>
          <input id="bd-month" type="month" [value]="data()?.month ?? ''" (change)="chooseMonth($any($event.target).value)" />
        </div>
      </div>

      @if (editing() !== undefined) {
        <form class="card form-card" [formGroup]="form" (ngSubmit)="save()" [attr.aria-label]="editing() ? 'Edit budget line' : 'New budget line'">
          <div class="form-grid">
            <div class="field">
              <label for="bd-category">Category</label>
              <input id="bd-category" formControlName="category" maxlength="50" list="bd-categories" placeholder="As used on expenses" />
              <datalist id="bd-categories">
                @for (u of data()?.unbudgeted ?? []; track $index) { @if (u.category) { <option [value]="u.category"></option> } }
              </datalist>
            </div>
            <div class="field"><label for="bd-amount">Budget</label><input id="bd-amount" type="number" min="0.01" step="0.01" formControlName="amount" /></div>
            <div class="field">
              <label for="bd-currency">Currency</label>
              <select id="bd-currency" formControlName="currency">
                @for (c of currencies(); track c.code) { <option [value]="c.code">{{ c.code }}</option> }
              </select>
            </div>
          </div>
          @if (formError()) { <p class="notice notice-error" role="alert">{{ formError() }}</p> }
          <div class="form-actions">
            <button type="submit" class="btn btn-primary" [disabled]="form.invalid || busy()">{{ busy() ? 'Saving…' : 'Save budget line' }}</button>
            <button type="button" class="btn" (click)="editing.set(undefined)">Cancel</button>
          </div>
        </form>
      }

      @switch (state()) {
        @case ('loading') { <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading budgets...</span></div> }
        @case ('error') { <app-load-error title="Could not load budgets" [message]="error()" (retry)="load()" /> }
        @case ('ready') {
          @if (data(); as d) {
            @if (d.totals.length > 0) {
              <div class="metric-strip">
                <!-- Currencies are peers and are never added together -->
                @for (t of d.totals; track t.currency) {
                  <div class="metric">
                    <span class="metric-label">Budgeted · {{ t.currency }}</span>
                    <span class="metric-value">{{ t.budget | money: t.currency }}</span>
                    <span class="metric-note">{{ t.actualBudgeted | money: t.currency }} spent against it</span>
                  </div>
                  <div class="metric">
                    <span class="metric-label">Outside the budget · {{ t.currency }}</span>
                    <span class="metric-value">{{ t.actualUnbudgeted | money: t.currency }}</span>
                    <span class="metric-note">Expenses with no line for their category</span>
                  </div>
                }
              </div>
            }

            <section class="card table-card" aria-labelledby="lines-heading">
              <div class="card-header">
                <h2 id="lines-heading">Budget versus actual</h2>
                <a class="small" [routerLink]="['/finance/transactions']" [queryParams]="{ from: d.from, to: d.to, type: 'Expense' }">Expenses this month</a>
              </div>
              @if (d.lines.length === 0) {
                <p class="empty-state small">No budget for {{ d.month }}. Add a line per category, using the categories you record on expenses.</p>
              } @else {
                <table class="data-table">
                  <thead><tr><th>Category</th><th class="num">Budget</th><th class="num">Actual</th><th class="num">Remaining</th><th><span class="visually-hidden">Actions</span></th></tr></thead>
                  <tbody>
                    @for (l of d.lines; track l.id) {
                      <tr>
                        <td>
                          <div class="name">{{ l.category }}</div>
                          <div class="progress" [attr.aria-label]="l.usedPercent + '% of the budget used'"><div class="fill" [class.over]="l.exceeded" [style.width.%]="barWidth(l)"></div></div>
                          <span class="small muted">{{ l.usedPercent }}% used</span>
                          @if (l.exceeded) { <span class="tag tag-negative over">Over budget</span> }
                        </td>
                        <td class="num">{{ l.budget.amount | money: l.budget.currency }}</td>
                        <td class="num">{{ l.actual.amount | money: l.actual.currency }}</td>
                        <td class="num" [class.text-negative]="l.exceeded">{{ l.remaining.amount | money: l.remaining.currency }}</td>
                        <td class="actions">
                          <button type="button" class="btn-link small" (click)="startEdit(l)">Edit</button>
                          @if (confirmingDelete() === l.id) {
                            <button type="button" class="btn btn-sm btn-danger" [disabled]="busy()" (click)="remove(l)">Delete it</button>
                            <button type="button" class="btn-link small" (click)="confirmingDelete.set(null)">Keep</button>
                          } @else {
                            <button type="button" class="btn-link small" (click)="confirmingDelete.set(l.id)">Delete</button>
                          }
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              }
              @if (rowError()) { <p class="notice notice-error" role="alert">{{ rowError() }}</p> }
            </section>

            @if (d.unbudgeted.length > 0) {
              <section class="card table-card unbudgeted" aria-labelledby="unbudgeted-heading">
                <div class="card-header"><h2 id="unbudgeted-heading">Spent outside the budget</h2></div>
                <table class="data-table">
                  <thead><tr><th>Category</th><th class="num">Spent</th><th><span class="visually-hidden">Actions</span></th></tr></thead>
                  <tbody>
                    @for (u of d.unbudgeted; track $index) {
                      <tr>
                        <td>{{ u.category ?? 'No category' }} <span class="small muted">· {{ u.transactions }} {{ u.transactions === 1 ? 'expense' : 'expenses' }}</span></td>
                        <td class="num">{{ u.actual.amount | money: u.actual.currency }}</td>
                        <td class="actions">
                          @if (u.category) { <button type="button" class="btn-link small" (click)="budgetFor(u)">Budget it</button> }
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </section>
            }
          }
        }
      }
    </div>
  `,
  styles: [`
    .toolbar { margin-bottom: var(--space-4); }
    .month-field { max-width: 220px; }
    .form-card { margin-bottom: var(--space-5); }
    .name { font-weight: 500; }
    .over { margin-left: var(--space-2); }
    .progress { height: 6px; border-radius: 3px; background: var(--surface-sunken); overflow: hidden; margin: var(--space-1) 0; max-width: 200px; }
    .progress .fill { height: 100%; background: var(--positive); }
    .progress .fill.over { background: var(--negative); }
    .actions { display: flex; gap: var(--space-3); align-items: center; justify-content: flex-end; flex-wrap: wrap; }
    .unbudgeted { margin-top: var(--space-5); }
    .table-card .notice { margin: var(--space-3) var(--space-5); }
    .empty-state.small { padding: var(--space-6) var(--space-5); }
  `]
})
export class BudgetsComponent {
  private api = inject(FinanceApiService);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);

  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly error = signal('');
  readonly data = signal<BudgetMonth | null>(null);
  readonly currencies = signal<CurrencyInfo[]>([]);
  // undefined: no form; null: adding; a line: editing it
  readonly editing = signal<BudgetLine | null | undefined>(undefined);
  readonly busy = signal(false);
  readonly formError = signal('');
  readonly rowError = signal('');
  readonly confirmingDelete = signal<number | null>(null);
  private month: string | undefined;

  readonly form = this.fb.group({
    category: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(50)]),
    amount: this.fb.control<number | null>(null, [Validators.required, Validators.min(0.01)]),
    currency: this.fb.nonNullable.control('ZAR', Validators.required)
  });

  constructor() {
    this.load();
    this.api.getCurrencies().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: c => this.currencies.set(c), error: () => {} });
  }

  load(): void {
    this.state.set('loading');
    this.api.getBudgets(this.month).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: d => { this.data.set(d); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  chooseMonth(month: string): void {
    if (!month) return;
    this.month = month;
    this.editing.set(undefined);
    this.load();
  }

  startCreate(): void {
    this.form.reset({ category: '', amount: null, currency: 'ZAR' });
    this.form.controls.currency.enable();
    this.formError.set('');
    this.editing.set(null);
  }

  budgetFor(u: UnbudgetedSpend): void {
    this.startCreate();
    this.form.patchValue({ category: u.category ?? '', currency: u.actual.currency });
  }

  // The currency of a line is fixed: a budget in another currency is another line
  startEdit(l: BudgetLine): void {
    this.form.reset({ category: l.category, amount: l.budget.amount, currency: l.budget.currency });
    this.form.controls.currency.disable();
    this.formError.set('');
    this.editing.set(l);
  }

  save(): void {
    if (this.form.invalid || this.busy()) return;
    const v = this.form.getRawValue();
    const editing = this.editing();
    const request = editing
      ? this.api.updateBudgetLine(editing.id, { category: v.category.trim(), amount: v.amount! })
      : this.api.createBudgetLine({ month: this.data()!.month, category: v.category.trim(), amount: v.amount!, currency: v.currency });
    this.busy.set(true);
    this.formError.set('');
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.busy.set(false); this.editing.set(undefined); this.load(); },
      error: err => { this.busy.set(false); this.formError.set(`Not saved: ${describeHttpError(err)}`); }
    });
  }

  remove(l: BudgetLine): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.rowError.set('');
    this.api.deleteBudgetLine(l.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.busy.set(false); this.confirmingDelete.set(null); this.load(); },
      error: err => { this.busy.set(false); this.rowError.set(`Not deleted: ${describeHttpError(err)}`); }
    });
  }

  barWidth(l: BudgetLine): number {
    return Math.min(100, l.usedPercent);
  }
}
