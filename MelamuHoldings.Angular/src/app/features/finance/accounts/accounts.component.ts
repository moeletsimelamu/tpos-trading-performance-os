import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { FinanceApiService } from '../finance-api.service';
import { Account, AccountType, Balances, CurrencyInfo, Ownership } from '../finance.models';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-finance-accounts',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, MoneyPipe, LoadErrorComponent],
  template: `
    <div class="page">
      <header>
        <div>
          <h1>Accounts</h1>
          <p>Real places money is held. Balances are summed from recorded transactions.</p>
        </div>
        <button type="button" class="btn" (click)="showForm.set(!showForm())" [attr.aria-expanded]="showForm()">
          {{ showForm() ? 'Cancel' : 'New account' }}
        </button>
      </header>

      @if (showForm()) {
        <form class="card form-card" [formGroup]="form" (ngSubmit)="create()" aria-label="New account">
          <div class="form-grid">
            <div class="field">
              <label for="acc-name">Name</label>
              <input id="acc-name" formControlName="name" placeholder="e.g. FNB Business Cheque" />
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
              <span class="hint">Fixed once created.</span>
            </div>
            <div class="field">
              <label for="acc-owner">Owned by</label>
              <select id="acc-owner" formControlName="ownership">
                @for (o of ownerships; track o) { <option [value]="o">{{ o }}</option> }
              </select>
              <span class="hint">State it; the app never guesses from the name.</span>
            </div>
            <div class="field">
              <label for="acc-inst">Institution (optional)</label>
              <input id="acc-inst" formControlName="institution" />
            </div>
          </div>
          @if (formError()) { <p class="notice notice-error" role="alert">{{ formError() }}</p> }
          <div class="form-actions">
            <button type="submit" class="btn btn-primary" [disabled]="form.invalid || saving()">
              {{ saving() ? 'Saving…' : 'Create account' }}
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
            <section class="totals" aria-label="Cash totals">
              <span class="totals-label">Cash in Bank and Cash accounts</span>
              @for (t of b.cashTotals; track t.currency) {
                <span class="mono total">{{ t.amount | money: t.currency }}</span>
              } @empty {
                <span class="muted">No cash recorded yet</span>
              }
            </section>

            @if (b.accounts.length === 0) {
              <div class="card">No accounts yet. Add the account payouts arrive in, then record its opening balance.</div>
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
                          <div class="muted small">{{ a.institution ?? '' }}{{ a.status === 'Closed' ? ' · Closed' : '' }}</div>
                        </td>
                        <td>{{ a.type }}</td>
                        <td>{{ a.ownership }}</td>
                        <td class="num mono">{{ a.balance.amount | money: a.balance.currency }}</td>
                        <td class="small">
                          @for (f of a.funds; track f.fundId) {
                            <div>{{ f.fundName }} <span class="mono">{{ f.balance.amount | money: f.balance.currency }}</span></div>
                          } @empty { <span class="muted">—</span> }
                        </td>
                        <td class="actions">
                          @if (a.status === 'Active') {
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
    .form-card { margin-bottom: 1.25rem; }
    .totals { display: flex; flex-wrap: wrap; align-items: baseline; gap: 0.5rem 1.25rem; margin-bottom: 1rem; }
    .totals-label { font-size: 12px; color: var(--text-secondary); }
    .total { font-size: 1.15rem; font-weight: 500; }
    .closed td { color: var(--text-muted); }
    .actions { white-space: nowrap; display: flex; gap: 0.75rem; align-items: center; }
  `]
})
export class AccountsComponent {
  private api = inject(FinanceApiService);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);

  readonly types: AccountType[] = ['Bank', 'Cash', 'Brokerage', 'Other'];
  readonly ownerships: Ownership[] = ['Company', 'Personal', 'Unspecified'];

  readonly state = signal<LoadState>('loading');
  readonly error = signal('');
  readonly balances = signal<Balances | null>(null);
  readonly currencies = signal<CurrencyInfo[]>([]);
  readonly showForm = signal(false);
  readonly saving = signal(false);
  readonly formError = signal('');
  readonly actionError = signal('');
  readonly confirmingClose = signal<number | null>(null);

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    type: ['Bank' as AccountType, Validators.required],
    currency: ['ZAR', Validators.required],
    ownership: ['Company' as Ownership, Validators.required],
    institution: ['', Validators.maxLength(100)]
  });

  constructor() {
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

  create(): void {
    if (this.form.invalid || this.saving()) return;
    this.saving.set(true);
    this.formError.set('');
    const value = this.form.getRawValue();
    this.api.createAccount({ ...value, institution: value.institution || undefined })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.showForm.set(false);
          this.form.reset();
          this.load();
        },
        error: err => { this.saving.set(false); this.formError.set(describeHttpError(err)); }
      });
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
