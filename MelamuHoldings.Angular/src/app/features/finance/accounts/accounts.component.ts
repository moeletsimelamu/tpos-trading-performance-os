import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { FinanceApiService } from '../finance-api.service';
import { PaymentCardComponent } from './payment-card.component';
import { ACCOUNT_CATEGORY_LABELS, Account, AccountCategory, AccountType, Balances, CARD_FINISHES, CardFinish, CurrencyInfo, Ownership, PaymentCard, PaymentCardKind, PaymentCardNetwork, TAX_TREATMENT_LABELS, TaxTreatment, categoriesFor, otherCashText } from '../finance.models';
import { MoneyPipe, formatMoney } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

type LoadState = 'loading' | 'ready' | 'error';
type AccountsView = 'panels' | 'table';

const VIEW_KEY = 'mhos.accounts.view';

// One to three letters to recognise a provider by, in a plain tile. No bank's logo is used without its permission.
export function providerInitials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '?';
  if (/^[A-Z]{2,3}$/.test(words[0])) return words[0];
  return words.slice(0, 2).map(w => w[0].toUpperCase()).join('');
}

function readView(): AccountsView {
  try { return localStorage.getItem(VIEW_KEY) === 'table' ? 'table' : 'panels'; } catch { return 'panels'; }
}

@Component({
  selector: 'app-finance-accounts',
  standalone: true,
  imports: [NgTemplateOutlet, ReactiveFormsModule, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent, PaymentCardComponent],
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
              <div class="view-bar">
                <div class="tabs" role="tablist" aria-label="Show accounts as">
                  <button type="button" class="tab" role="tab" [attr.aria-selected]="view() === 'panels'" (click)="setView('panels')">Panels</button>
                  <button type="button" class="tab" role="tab" [attr.aria-selected]="view() === 'table'" (click)="setView('table')">Table</button>
                </div>
              </div>
              @if (actionError()) { <p class="notice notice-error" role="alert">{{ actionError() }}</p> }

              @if (view() === 'panels') {
                @for (group of groups(); track group.title) {
                  <section class="group" [attr.aria-label]="group.title">
                    <h2>{{ group.title }}</h2>
                    @if (group.note) { <p class="small secondary group-note">{{ group.note }}</p> }
                    <div class="panels">
                      @for (a of group.accounts; track a.id) {
                        <article class="panel" [class.closed]="a.status === 'Closed'" [class.with-card]="activeCards(a).length > 0" [attr.data-type]="a.type" [attr.aria-label]="a.name">
                          @if (activeCards(a).length > 0) {
                            <div class="card-column">
                              @for (c of activeCards(a); track c.id) {
                                <div class="card-slot">
                                  <app-payment-card [card]="c" [provider]="a.institution ?? a.name" />
                                  <div class="card-caption">
                                    <span>{{ cardLabel(c) }}</span>
                                    @if (a.status === 'Active') {
                                      @if (confirmingCard() === c.id) {
                                        <button type="button" class="btn btn-sm btn-danger" [disabled]="saving()" (click)="cancelCard(a, c)">Record as cancelled</button>
                                        <button type="button" class="btn-link small" (click)="confirmingCard.set(null)">Keep</button>
                                      } @else {
                                        <button type="button" class="btn-link small" (click)="confirmingCard.set(c.id)">Cancel card</button>
                                      }
                                    }
                                  </div>
                                </div>
                              }
                            </div>
                          }
                          <div class="panel-body">
                          <header class="panel-head">
                            <span class="provider-badge" aria-hidden="true">{{ initials(a) }}</span>
                            <div class="provider">
                              <span class="provider-name">{{ a.institution ?? 'Provider not stated' }}</span>
                              <span class="small muted">{{ kind(a) }}</span>
                            </div>
                            @if (a.status === 'Closed') { <span class="tag">Closed</span> }
                            @else if (a.taxTreatment === 'TaxFree') { <span class="tag tag-info">Tax-free</span> }
                          </header>
                          <a class="panel-name" [routerLink]="['/finance/transactions']" [queryParams]="{ accountId: a.id }">{{ a.name }}</a>
                          <div class="panel-balance">{{ a.balance.amount | money: a.balance.currency }}</div>
                          <div class="small muted">Recorded balance</div>
                          <dl class="panel-facts">
                            <div>
                              <dt>By fund</dt>
                              <dd>
                                @for (f of a.funds; track f.fundId) { <div class="fund-line"><span>{{ f.fundName }}</span><span class="fund-amount">{{ f.balance.amount | money: f.balance.currency }}</span></div> }
                                @empty { <span class="muted">Nothing recorded</span> }
                              </dd>
                            </div>
                            @if (a.homeFundName) { <div><dt>Mainly for</dt><dd>{{ a.homeFundName }}</dd></div> }
                            @if (a.reference) { <div><dt>Reference</dt><dd>{{ a.reference }}</dd></div> }
                            @if (a.openedOn) { <div><dt>Opened</dt><dd>{{ a.openedOn }}</dd></div> }
                            @if (a.taxTreatment === 'Taxable') { <div><dt>Tax</dt><dd>Taxable</dd></div> }
                            @if (a.notes) { <div><dt>Notes</dt><dd>{{ a.notes }}</dd></div> }
                            @if (a.type === 'Bank') {
                              <div>
                                <dt>Payment cards</dt>
                                <dd>
                                  @for (c of cancelledCards(a); track c.id) {
                                    <div class="card-line cancelled"><span>{{ cardLabel(c) }}</span> <span class="tag">Cancelled</span></div>
                                  }
                                  @if (!(a.cards ?? []).length) { <span class="muted">None recorded</span> }
                                  @if (a.status === 'Active' && addingCardTo() !== a.id) { <div><button type="button" class="btn-link small" (click)="startCard(a)">Add a card</button></div> }
                                </dd>
                              </div>
                            }
                          </dl>
                          @if (addingCardTo() === a.id) {
                            <form class="card-form" [formGroup]="cardForm" (ngSubmit)="addCard(a)" aria-label="Add a payment card">
                              <div class="field">
                                <label [for]="'card-kind-' + a.id">Kind</label>
                                <select [id]="'card-kind-' + a.id" formControlName="kind"><option value="Debit">Debit</option><option value="Credit">Credit</option></select>
                              </div>
                              <div class="field">
                                <label [for]="'card-network-' + a.id">Network</label>
                                <select [id]="'card-network-' + a.id" formControlName="network"><option value="NotStated">Not stated</option><option value="Visa">Visa</option><option value="Mastercard">Mastercard</option><option value="Other">Other</option></select>
                              </div>
                              <div class="field">
                                <label [for]="'card-finish-' + a.id">Colour</label>
                                <select [id]="'card-finish-' + a.id" formControlName="finish">
                                  @for (f of finishes; track f) { <option [value]="f">{{ f }}</option> }
                                </select>
                              </div>
                              <div class="field">
                                <label [for]="'card-four-' + a.id">Last four digits (optional)</label>
                                <input [id]="'card-four-' + a.id" formControlName="lastFour" inputmode="numeric" maxlength="4" autocomplete="off" />
                              </div>
                              <div class="field wide">
                                <label [for]="'card-name-' + a.id">Name (optional)</label>
                                <input [id]="'card-name-' + a.id" formControlName="name" maxlength="50" autocomplete="off" />
                              </div>
                              <p class="small secondary wide">Only enough to recognise it. Never enter the full number, expiry, security code or cardholder name.</p>
                              @if (cardError()) { <p class="notice notice-error wide" role="alert">{{ cardError() }}</p> }
                              <div class="form-actions wide">
                                <button type="submit" class="btn btn-sm btn-primary" [disabled]="cardForm.invalid || saving()">Save card</button>
                                <button type="button" class="btn-link small" (click)="addingCardTo.set(null)">Cancel</button>
                              </div>
                            </form>
                          }
                          <footer class="panel-actions">
                            <a class="btn-link small" [routerLink]="['/finance/transactions']" [queryParams]="{ accountId: a.id }">Transactions</a>
                            <ng-container *ngTemplateOutlet="accountActions; context: { $implicit: a }" />
                          </footer>
                          </div>
                        </article>
                      }
                    </div>
                  </section>
                }
                <p class="small muted honesty">Cards are drawn from what you recorded here, not the bank's own artwork, and show only the last four digits. Nothing here connects to a bank.</p>
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
                          <ng-container *ngTemplateOutlet="accountActions; context: { $implicit: a }" />
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
              }

              <ng-template #accountActions let-a>
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
              </ng-template>
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
    .view-bar { display: flex; justify-content: flex-end; margin-bottom: var(--space-3); }
    .group { margin-bottom: var(--space-6); }
    .group h2 { font-size: var(--text-md); margin-bottom: var(--space-1); }
    .group-note { margin-bottom: var(--space-2); }
    .panels { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(320px, 100%), 1fr)); gap: var(--space-4); align-items: start; margin-top: var(--space-3); }
    /* A plain record of an account, deliberately not card-shaped: no chip, number pattern or card proportions */
    .panel { background: var(--surface); border: 1px solid var(--border-subtle); border-top: 3px solid var(--navy-900); border-radius: var(--radius); padding: var(--space-4); display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .panel[data-type='Brokerage'] { border-top-color: #b8a06a; }
    .panel[data-type='Cash'], .panel[data-type='Other'] { border-top-color: var(--text-muted); }
    .panel.closed { opacity: .7; }
    .panel.with-card { grid-column: 1 / -1; flex-direction: row; flex-wrap: wrap; gap: var(--space-5); }
    .card-column { flex: 0 1 320px; min-width: min(260px, 100%); display: grid; gap: var(--space-4); align-content: start; }
    .card-caption { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-1) var(--space-3); margin-top: var(--space-2); font-size: var(--text-sm); }
    .panel-body { flex: 1 1 320px; min-width: 0; display: flex; flex-direction: column; gap: 2px; }
    .panel-head { display: flex; align-items: center; gap: var(--space-3); margin-bottom: var(--space-3); }
    .provider-badge { width: 36px; height: 36px; flex-shrink: 0; display: flex; align-items: center; justify-content: center; border-radius: var(--radius); background: var(--surface-sunken); color: var(--text-secondary); font-size: 13px; font-weight: 600; }
    .provider { display: flex; flex-direction: column; min-width: 0; flex: 1; line-height: 1.3; }
    .provider-name { font-weight: 500; overflow-wrap: anywhere; }
    .panel-name { font-size: var(--text-md); font-weight: 600; overflow-wrap: anywhere; }
    .panel-balance { font-size: 22px; font-weight: 600; font-variant-numeric: tabular-nums; margin-top: var(--space-2); overflow-wrap: anywhere; }
    .panel-facts { display: grid; gap: var(--space-2); margin: var(--space-3) 0 0; padding-top: var(--space-3); border-top: 1px solid var(--border-subtle); }
    .panel-facts dt { font-size: var(--text-xs); color: var(--text-secondary); }
    .panel-facts dd { margin: 0; font-size: var(--text-sm); }
    .fund-line { display: flex; justify-content: space-between; gap: var(--space-3); flex-wrap: wrap; }
    .fund-amount { font-variant-numeric: tabular-nums; white-space: nowrap; }
    .card-line { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-2); }
    .card-line.cancelled > span:first-child { color: var(--text-muted); }
    .card-form { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(130px, 100%), 1fr)); gap: var(--space-2) var(--space-3); margin-top: var(--space-3); padding: var(--space-3); background: var(--surface-sunken); border-radius: var(--radius); }
    .panel-actions { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-2) var(--space-3); margin-top: var(--space-3); padding-top: var(--space-3); border-top: 1px solid var(--border-subtle); }
    .honesty { margin-top: var(--space-2); }
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
  // The owner's own preference for this page; if it cannot be read or kept, the panels show
  readonly view = signal<AccountsView>(readView());
  readonly addingCardTo = signal<number | null>(null);
  readonly confirmingCard = signal<number | null>(null);
  readonly cardError = signal('');
  readonly finishes = CARD_FINISHES;
  readonly cardForm = this.fb.nonNullable.group({
    kind: ['Debit' as PaymentCardKind, Validators.required],
    network: ['NotStated' as PaymentCardNetwork, Validators.required],
    finish: ['Navy' as CardFinish, Validators.required],
    lastFour: ['', Validators.pattern(/^\d{4}$/)],
    name: ['', Validators.maxLength(50)]
  });

  // Business accounts first, because personal money must never read as the company's. Open accounts lead each group.
  readonly groups = computed(() => {
    const accounts = this.balances()?.accounts ?? [];
    const of = (ownership: Ownership) => accounts.filter(a => a.ownership === ownership).sort((a, b) => (a.status === b.status ? a.name.localeCompare(b.name) : a.status === 'Active' ? -1 : 1));
    return [
      { title: 'Business accounts', note: '', accounts: of('Company') },
      { title: 'Personal accounts', note: 'Your own money. Not counted as business cash.', accounts: of('Personal') },
      { title: 'Ownership not stated', note: 'Not counted as business cash until you state who owns them.', accounts: of('Unspecified') }
    ].filter(g => g.accounts.length > 0);
  });

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

  setView(view: AccountsView): void {
    this.view.set(view);
    try { localStorage.setItem(VIEW_KEY, view); } catch { /* the choice just is not remembered */ }
  }

  initials(a: Account): string {
    return providerInitials(a.institution ?? a.name);
  }

  // What sort of account it is, at a glance
  kind(a: Account): string {
    const category = a.category && a.category !== 'NotStated' ? ` · ${this.categoryLabels[a.category]}` : '';
    return `${a.type}${category} · ${a.currency}`;
  }

  activeCards(a: Account): PaymentCard[] {
    return (a.cards ?? []).filter(c => c.status === 'Active');
  }

  cancelledCards(a: Account): PaymentCard[] {
    return (a.cards ?? []).filter(c => c.status === 'Cancelled');
  }

  cardLabel(c: PaymentCard): string {
    const ending = c.lastFour ? ` ending ${c.lastFour}` : '';
    return `${c.name ? `${c.name}: ` : ''}${c.kind} card${ending}`;
  }

  startCard(a: Account): void {
    this.cardForm.reset();
    this.cardError.set('');
    this.addingCardTo.set(a.id);
  }

  addCard(a: Account): void {
    if (this.cardForm.invalid || this.saving()) return;
    const v = this.cardForm.getRawValue();
    this.saving.set(true);
    this.cardError.set('');
    this.api.addPaymentCard(a.id, { kind: v.kind, network: v.network, finish: v.finish, lastFour: v.lastFour.trim() || null, name: v.name.trim() || null }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.saving.set(false); this.addingCardTo.set(null); this.load(); },
      error: err => { this.saving.set(false); this.cardError.set(`Not saved: ${describeHttpError(err)}`); }
    });
  }

  cancelCard(a: Account, c: PaymentCard): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.actionError.set('');
    this.api.cancelPaymentCard(a.id, c.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.saving.set(false); this.confirmingCard.set(null); this.load(); },
      error: err => { this.saving.set(false); this.actionError.set(`That card was not changed: ${describeHttpError(err)}`); }
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
