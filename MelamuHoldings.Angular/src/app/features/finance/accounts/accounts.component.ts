import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { FinanceApiService } from '../finance-api.service';
import { ACCOUNT_CATEGORY_LABELS, Account, AccountCategory, AccountType, Balances, CurrencyInfo, Ownership, TAX_TREATMENT_LABELS, TaxTreatment, categoriesFor, otherCashText } from '../finance.models';
import { MoneyPipe, formatMoney } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-finance-accounts',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header section="Finance" title="Accounts"
        description="Real places money is held. Balances are summed from recorded transactions. A fund is a purpose, not an account, and one fund can span several accounts.">
        <button type="button" class="btn" (click)="toggleNew()" [attr.aria-expanded]="showForm()">
          {{ showForm() ? 'Cancel' : 'New account' }}
        </button>
      </app-page-header>

      @if (showForm()) {
        <form class="card form-card" [formGroup]="form" (ngSubmit)="save()" [attr.aria-label]="editing() ? 'Edit account' : 'New account'">
          <div class="form-grid">
            <div class="field">
              <label for="acc-name">Name</label>
              <input id="acc-name" formControlName="name" maxlength="100" />
            </div>
            <div class="field">
              <label for="acc-type">Type</label>
              <select id="acc-type" formControlName="type">
                @for (t of types; track t) { <option [value]="t">{{ t }}</option> }
              </select>
            </div>
            <div class="field">
              <label for="acc-currency">Currency</label>
              <select id="acc-currency" formControlName="currency">
                @for (c of currencies(); track c.code) { <option [value]="c.code">{{ c.code }}</option> }
              </select>
              <span class="hint">Type and currency are fixed once created.</span>
            </div>
            <div class="field">
              <label for="acc-category">Category</label>
              <select id="acc-category" formControlName="category">
                @for (c of categories(); track c) { <option [value]="c">{{ categoryLabels[c] }}</option> }
              </select>
            </div>
            <div class="field">
              <label for="acc-owner">Owned by</label>
              <select id="acc-owner" formControlName="ownership">
                @for (o of ownerships; track o) { <option [value]="o">{{ o }}</option> }
              </select>
              <span class="hint">State it; the app never guesses from the name.</span>
            </div>
            <div class="field">
              <label for="acc-tax">Tax treatment</label>
              <select id="acc-tax" formControlName="taxTreatment">
                @for (t of taxTreatments; track t) { <option [value]="t">{{ taxLabels[t] }}</option> }
              </select>
              <span class="hint">Tax-free for a TFSA. Also never guessed.</span>
            </div>
            <div class="field">
              <label for="acc-inst">Institution (optional)</label>
              <input id="acc-inst" formControlName="institution" maxlength="100" />
            </div>
            <div class="field">
              <label for="acc-reference">Reference (optional)</label>
              <input id="acc-reference" formControlName="reference" maxlength="20" autocomplete="off" placeholder="e.g. last four digits" />
              <span class="hint">At most four digits. Never the full account number.</span>
            </div>
            <div class="field">
              <label for="acc-opened">Date opened (optional)</label>
              <input id="acc-opened" type="date" formControlName="openedOn" />
            </div>
            <div class="field">
              <label for="acc-fund">Mainly for fund (optional)</label>
              <select id="acc-fund" formControlName="homeFundId">
                <option [ngValue]="null">None</option>
                @for (f of homeFunds(); track f.id) { <option [ngValue]="f.id">{{ f.name }}</option> }
              </select>
              <span class="hint">A label. It restricts nothing, and fund balances still come from the ledger.</span>
            </div>
            <div class="field wide">
              <label for="acc-notes">Notes (optional)</label>
              <input id="acc-notes" formControlName="notes" maxlength="500" />
            </div>
          </div>
          @if (formError()) { <p class="notice notice-error" role="alert">{{ formError() }}</p> }
          <div class="form-actions">
            <button type="submit" class="btn btn-primary" [disabled]="form.invalid || saving()">
              {{ saving() ? 'Saving…' : editing() ? 'Save changes' : 'Create account' }}
            </button>
          </div>
        </form>
      }

      @switch (state()) {
        @case ('loading') {
          <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading accounts...</span></div>
        }
        @case ('error') {
          <app-load-error title="Could not load accounts" [message]="error()" (retry)="load()" />
        }
        @case ('ready') {
          @if (balances(); as b) {
            @if (b.cashTotals.length > 0) {
              <section class="metric-strip totals" aria-label="Cash totals">
                @for (t of b.cashTotals; track t.currency) {
                  <div class="metric">
                    <span class="metric-label">Cash · {{ t.currency }}</span>
                    <span class="metric-value">{{ t.amount | money: t.currency }}</span>
                    <span class="metric-note">Company bank and cash accounts</span>
                  </div>
                }
              </section>
            }
            @if (otherCash(); as other) { <p class="small secondary other-cash">Not counted as business cash: {{ other }}.</p> }

            @if (b.accounts.length === 0) {
              <div class="card empty-state">
                <h2>No accounts yet</h2>
                <p>Add the account payouts arrive in, then record its opening balance.</p>
              </div>
            } @else {
              <div class="card table-card">
                <table class="data-table">
                  <thead>
                    <tr><th>Account</th><th>Type</th><th>Owner</th><th class="num">Balance</th><th>By fund</th><th><span class="visually-hidden">Actions</span></th></tr>
                  </thead>
                  <tbody>
                    @for (a of b.accounts; track a.id) {
                      <tr [class.closed]="a.status === 'Closed'">
                        <td>
                          <a [routerLink]="['/finance/transactions']" [queryParams]="{ accountId: a.id }">{{ a.name }}</a>
                          <div class="muted small">{{ details(a) }}</div>
                        </td>
                        <td>
                          {{ a.type }}
                          @if (a.category && a.category !== 'NotStated') { <div class="muted small">{{ categoryLabels[a.category] }}</div> }
                        </td>
                        <td>
                          <span [class]="a.ownership === 'Company' ? '' : 'tag tag-warning'">{{ a.ownership === 'Unspecified' ? 'Not stated' : a.ownership }}</span>
                          @if (a.taxTreatment && a.taxTreatment !== 'NotStated') { <div class="muted small">{{ taxLabels[a.taxTreatment] }}</div> }
                        </td>
                        <td class="num">{{ a.balance.amount | money: a.balance.currency }}</td>
                        <td class="small">
                          @for (f of a.funds; track f.fundId) {
                            <div>{{ f.fundName }} <span>{{ f.balance.amount | money: f.balance.currency }}</span></div>
                          } @empty { <span class="muted">—</span> }
                          @if (a.homeFundName) { <div class="muted">Mainly for {{ a.homeFundName }}</div> }
                        </td>
                        <td class="actions">
                          @if (a.status === 'Active') {
                            <button type="button" class="btn-link small" (click)="startEdit(a)">Edit</button>
                            <a class="btn-link small" [routerLink]="['/finance/transactions/new']" [queryParams]="{ type: 'opening-balance', accountId: a.id }">Opening balance</a>
                            @if (a.balance.amount === 0) {
                              @if (confirmingClose() === a.id) {
                                <button type="button" class="btn btn-danger" (click)="close(a)" [disabled]="saving()">Confirm close</button>
                                <button type="button" class="btn-link small" (click)="confirmingClose.set(null)">Keep</button>
                              } @else {
                                <button type="button" class="btn-link small" (click)="confirmingClose.set(a.id)">Close</button>
                              }
                            }
                          }
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
              @if (actionError()) { <p class="notice notice-error" role="alert">{{ actionError() }}</p> }
            }
          }
        }
      }
    </div>
  `,
  styles: [`
    .form-card { margin-bottom: var(--space-5); }
    .totals { margin-bottom: var(--space-5); }
    .other-cash { margin: calc(-1 * var(--space-3)) 0 var(--space-5); }
    .wide { grid-column: 1 / -1; }
    .closed td { color: var(--text-muted); }
    .actions { white-space: nowrap; display: flex; gap: var(--space-3); align-items: center; justify-content: flex-end; }
  `]
})
export class AccountsComponent {
  private api = inject(FinanceApiService);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);

  readonly types: AccountType[] = ['Bank', 'Cash', 'Brokerage', 'Other'];
  readonly ownerships: Ownership[] = ['Company', 'Personal', 'Unspecified'];
  readonly taxTreatments: TaxTreatment[] = ['NotStated', 'Taxable', 'TaxFree'];
  readonly categoryLabels = ACCOUNT_CATEGORY_LABELS;
  readonly taxLabels = TAX_TREATMENT_LABELS;

  readonly state = signal<LoadState>('loading');
  readonly error = signal('');
  readonly balances = signal<Balances | null>(null);
  readonly currencies = signal<CurrencyInfo[]>([]);
  readonly showForm = signal(false);
  readonly saving = signal(false);
  readonly formError = signal('');
  readonly actionError = signal('');
  readonly confirmingClose = signal<number | null>(null);
  // The account being changed; null when adding a new one
  readonly editing = signal<Account | null>(null);

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    type: ['Bank' as AccountType, Validators.required],
    currency: ['ZAR', Validators.required],
    category: ['NotStated' as AccountCategory, Validators.required],
    ownership: ['Company' as Ownership, Validators.required],
    taxTreatment: ['NotStated' as TaxTreatment, Validators.required],
    institution: ['', Validators.maxLength(100)],
    reference: ['', Validators.maxLength(20)],
    openedOn: [''],
    homeFundId: [null as number | null],
    notes: ['', Validators.maxLength(500)]
  });

  private readonly chosenType = toSignal(this.form.controls.type.valueChanges, { initialValue: 'Bank' as AccountType });
  readonly categories = computed(() => categoriesFor(this.chosenType()));
  // Funds an account can be for: not the holding fund for money waiting to be assigned
  readonly homeFunds = computed(() => (this.balances()?.funds ?? []).filter(f => !f.isSystem));
  readonly otherCash = computed(() => otherCashText(this.balances()?.otherCash, m => formatMoney(m.amount, m.currency)));

  constructor() {
    // A category from another type of account does not apply
    this.form.controls.type.valueChanges.pipe(takeUntilDestroyed()).subscribe(type => {
      if (!categoriesFor(type).includes(this.form.controls.category.value)) this.form.controls.category.setValue('NotStated');
    });
    this.load();
    this.api.getCurrencies().pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: c => this.currencies.set(c), error: () => this.currencies.set([{ code: 'ZAR', minorUnits: 2 }]) });
  }

  load(): void {
    this.state.set('loading');
    this.api.getBalances().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: b => { this.balances.set(b); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  toggleNew(): void {
    this.resetForm();
    this.showForm.update(open => !open);
  }

  private resetForm(): void {
    this.editing.set(null);
    this.form.reset();
    this.form.controls.type.enable();
    this.form.controls.currency.enable();
    this.formError.set('');
  }

  startEdit(a: Account): void {
    this.editing.set(a);
    this.form.reset({
      name: a.name, type: a.type, currency: a.currency, category: a.category ?? 'NotStated', ownership: a.ownership,
      taxTreatment: a.taxTreatment ?? 'NotStated', institution: a.institution ?? '', reference: a.reference ?? '',
      openedOn: a.openedOn ?? '', homeFundId: a.homeFundId ?? null, notes: a.notes ?? ''
    });
    this.form.controls.type.disable();
    this.form.controls.currency.disable();
    this.formError.set('');
    this.showForm.set(true);
  }

  save(): void {
    if (this.form.invalid || this.saving()) return;
    this.saving.set(true);
    this.formError.set('');
    const v = this.form.getRawValue();
    const details = {
      name: v.name.trim(), ownership: v.ownership, institution: v.institution.trim() || null, notes: v.notes.trim() || null,
      category: v.category, taxTreatment: v.taxTreatment, reference: v.reference.trim() || null, openedOn: v.openedOn || null, homeFundId: v.homeFundId
    };
    const editing = this.editing();
    const request = editing ? this.api.updateAccount(editing.id, details) : this.api.createAccount({ ...details, type: v.type, currency: v.currency });
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.saving.set(false);
        this.resetForm();
        this.showForm.set(false);
        this.load();
      },
      error: err => { this.saving.set(false); this.formError.set(describeHttpError(err)); }
    });
  }

  // What identifies the account under its name
  details(a: Account): string {
    return [a.institution, a.reference ? `Ref ${a.reference}` : null, a.openedOn ? `Opened ${a.openedOn}` : null, a.status === 'Closed' ? 'Closed' : null].filter(Boolean).join(' · ');
  }

  close(account: Account): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.actionError.set('');
    this.api.closeAccount(account.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.saving.set(false); this.confirmingClose.set(null); this.load(); },
      error: err => { this.saving.set(false); this.actionError.set(`'${account.name}' was not closed: ${describeHttpError(err)}`); }
    });
  }
}
