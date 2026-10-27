import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { forkJoin, startWith } from 'rxjs';
import { FinanceApiService, newIdempotencyKey, todayIso } from '../finance-api.service';
import { Account, Fund, RECORD_KINDS, RecordKind, Transaction, UNALLOCATED_FUND_ID } from '../finance.models';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';

type Field = 'accountId' | 'fundId' | 'fromAccountId' | 'fromFundId' | 'toAccountId' | 'toFundId'
  | 'amount' | 'amountSent' | 'amountReceived' | 'category';

// Which inputs each kind of movement uses; the rest are disabled so they neither show nor validate
const FIELDS: Record<RecordKind, Field[]> = {
  'income': ['accountId', 'fundId', 'amount', 'category'],
  'expense': ['accountId', 'fundId', 'amount', 'category'],
  'opening-balance': ['accountId', 'fundId', 'amount'],
  'transfer': ['fromAccountId', 'fromFundId', 'toAccountId', 'toFundId', 'amount'],
  'reallocation': ['accountId', 'fromFundId', 'toFundId', 'amount'],
  'exchange': ['fromAccountId', 'fromFundId', 'amountSent', 'toAccountId', 'toFundId', 'amountReceived']
};
const ALL_FIELDS: Field[] = [...new Set(Object.values(FIELDS).flat())];

interface PreviewEntry { account: string; fund: string; amount: number; currency: string; }

// Amounts are whole cents; opening balances may be negative (an overdrawn account) but never zero
function amountValidator(allowNegative: () => boolean) {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = control.value;
    if (value === null || value === '') return null;
    const n = Number(value);
    if (!Number.isFinite(n)) return { amount: 'Enter a number.' };
    if (Math.round(n * 100) / 100 !== n) return { amount: 'Use at most 2 decimal places.' };
    if (allowNegative() ? n === 0 : n <= 0) return { amount: allowNegative() ? 'Must not be zero.' : 'Must be greater than zero.' };
    return null;
  };
}

@Component({
  selector: 'app-record-transaction',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, MoneyPipe, LoadErrorComponent],
  templateUrl: './record-transaction.component.html',
  styles: [`
    .kinds { display: flex; flex-wrap: wrap; gap: 0.4rem; margin-bottom: 0.5rem; }
    .kind[aria-pressed='true'] { border-color: var(--accent-amber); color: var(--accent-amber); }
    .kind-help { color: var(--text-secondary); font-size: 13px; margin-bottom: 1rem; }
    .preview { margin-top: 1.25rem; border-top: 1px solid var(--border-subtle); padding-top: 0.9rem; }
    .preview h2 { font-size: 13px; font-weight: 600; margin-bottom: 0.4rem; }
    .entry { display: flex; justify-content: space-between; gap: 1rem; font-size: 13px; padding: 0.15rem 0; }
  `]
})
export class RecordTransactionComponent {
  private api = inject(FinanceApiService);
  private route = inject(ActivatedRoute);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);

  readonly kinds = RECORD_KINDS;
  readonly kind = signal<RecordKind>('income');
  readonly loadState = signal<'loading' | 'ready' | 'error'>('loading');
  readonly loadError = signal('');
  readonly accounts = signal<Account[]>([]);
  readonly funds = signal<Fund[]>([]);
  readonly submitting = signal(false);
  readonly submitError = signal('');
  readonly recorded = signal<Transaction | null>(null);

  // One key per intended transaction: kept for retries of the same values, renewed when anything changes
  private idempotencyKey = newIdempotencyKey();

  readonly form = this.fb.group({
    accountId: [null as number | null, Validators.required],
    fundId: [UNALLOCATED_FUND_ID as number | null, Validators.required],
    fromAccountId: [null as number | null, Validators.required],
    fromFundId: [UNALLOCATED_FUND_ID as number | null, Validators.required],
    toAccountId: [null as number | null, Validators.required],
    toFundId: [UNALLOCATED_FUND_ID as number | null, Validators.required],
    amount: [null as number | null, [Validators.required, amountValidator(() => this.kind() === 'opening-balance')]],
    amountSent: [null as number | null, [Validators.required, amountValidator(() => false)]],
    amountReceived: [null as number | null, [Validators.required, amountValidator(() => false)]],
    category: ['', Validators.maxLength(50)],
    date: [todayIso(), Validators.required],
    description: ['', [Validators.required, Validators.maxLength(200)]],
    reference: ['', Validators.maxLength(100)]
  });

  private readonly value = toSignal(this.form.valueChanges.pipe(startWith(this.form.getRawValue())), { requireSync: true });

  readonly activeAccounts = computed(() => this.accounts().filter(a => a.status === 'Active'));
  readonly kindHelp = computed(() => this.kinds.find(k => k.kind === this.kind())!.help);

  readonly accountCurrency = computed(() => this.account(this.value().accountId)?.currency ?? '');
  readonly fromCurrency = computed(() => this.account(this.value().fromAccountId)?.currency ?? '');
  readonly toCurrency = computed(() => this.account(this.value().toAccountId)?.currency ?? '');

  // Transfers stay within one currency; exchanges must change currency
  readonly toAccountOptions = computed(() => {
    const from = this.account(this.value().fromAccountId);
    return this.activeAccounts().filter(a => a.id !== from?.id && (!from
      || (this.kind() === 'transfer' ? a.currency === from.currency : a.currency !== from.currency)));
  });

  readonly impliedRate = computed(() => {
    const v = this.value();
    return v.amountSent && v.amountReceived ? (Number(v.amountReceived) / Number(v.amountSent)).toFixed(4) : null;
  });

  // What will post, for the person to check before submitting. Display only: the server is authoritative.
  readonly preview = computed<PreviewEntry[]>(() => {
    const v = this.value();
    const entry = (accountId: number | null | undefined, fundId: number | null | undefined, amount: number): PreviewEntry | null => {
      const account = this.account(accountId);
      const fund = this.funds().find(f => f.id === fundId);
      return account && fund && Number.isFinite(amount) && amount !== 0
        ? { account: account.name, fund: fund.name, amount, currency: account.currency } : null;
    };
    const amount = Number(v.amount);
    const entries: (PreviewEntry | null)[] = {
      'income': [entry(v.accountId, v.fundId, amount)],
      'opening-balance': [entry(v.accountId, v.fundId, amount)],
      'expense': [entry(v.accountId, v.fundId, -amount)],
      'transfer': [entry(v.fromAccountId, v.fromFundId, -amount), entry(v.toAccountId, v.toFundId, amount)],
      'reallocation': [entry(v.accountId, v.fromFundId, -amount), entry(v.accountId, v.toFundId, amount)],
      'exchange': [entry(v.fromAccountId, v.fromFundId, -Number(v.amountSent)), entry(v.toAccountId, v.toFundId, Number(v.amountReceived))]
    }[this.kind()];
    return entries.every(e => e) ? entries as PreviewEntry[] : [];
  });

  constructor() {
    const params = this.route.snapshot.queryParamMap;
    const requested = params.get('type') as RecordKind | null;
    this.selectKind(requested && FIELDS[requested] ? requested : 'income');
    if (params.get('accountId')) this.form.patchValue({ accountId: Number(params.get('accountId')) });

    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (!this.submitting()) this.idempotencyKey = newIdempotencyKey();
    });

    this.load();
  }

  load(): void {
    this.loadState.set('loading');
    forkJoin({ accounts: this.api.getAccounts(), funds: this.api.getFunds() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ accounts, funds }) => { this.accounts.set(accounts); this.funds.set(funds); this.loadState.set('ready'); },
        error: err => { this.loadError.set(describeHttpError(err)); this.loadState.set('error'); }
      });
  }

  selectKind(kind: RecordKind): void {
    this.kind.set(kind);
    for (const field of ALL_FIELDS) {
      const control = this.form.controls[field];
      if (FIELDS[kind].includes(field)) control.enable({ emitEvent: false });
      else control.disable({ emitEvent: false });
    }
    this.form.controls.amount.updateValueAndValidity();
    this.submitError.set('');
  }

  uses(field: Field): boolean {
    return FIELDS[this.kind()].includes(field);
  }

  submit(): void {
    if (this.submitting()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const body: Record<string, unknown> = { date: v.date, description: v.description, reference: v.reference || undefined };
    for (const field of FIELDS[this.kind()]) body[field] = field === 'category' ? (v.category || undefined) : v[field];

    this.submitting.set(true);
    this.submitError.set('');
    this.recorded.set(null);
    this.api.record(this.kind(), body, this.idempotencyKey).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: transaction => {
        this.recorded.set(transaction);
        this.submitting.set(false);
        this.form.patchValue({ amount: null, amountSent: null, amountReceived: null, description: '', reference: '', category: '' });
        this.form.markAsUntouched();
        this.idempotencyKey = newIdempotencyKey();
      },
      error: err => {
        this.submitting.set(false);
        this.submitError.set(`Not recorded: ${describeHttpError(err)}`);
      }
    });
  }

  private account(id: number | null | undefined): Account | undefined {
    return this.accounts().find(a => a.id === id);
  }
}
