import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { forkJoin, startWith } from 'rxjs';
import { FinanceApiService, newIdempotencyKey, todayIso } from '../finance-api.service';
import { ALLOCATION_STATUS, Account, Allocation, AllocationLine, FeeChargedTo, SettlementMethod } from '../finance.models';
import { amountValidator, rateValidator } from '../amount.validator';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

type LoadState = 'loading' | 'ready' | 'error';

export const METHODS: { method: SettlementMethod; label: string; help: string }[] = [
  { method: 'Earmark', label: 'Earmark in place', help: 'The money stays in the receiving account and is assigned to this fund.' },
  { method: 'Transfer', label: 'Transfer', help: 'The money moves to another account in the same currency.' },
  { method: 'Exchange', label: 'Exchange', help: 'The money is converted into an account in another currency, at the rate you actually got.' }
];

@Component({
  selector: 'app-allocation-detail',
  standalone: true,
  imports: [DatePipe, FormsModule, ReactiveFormsModule, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent],
  templateUrl: './allocation-detail.component.html',
  styles: [`
    .summary { margin-bottom: var(--space-5); }
    .settlement { display: flex; gap: var(--space-2); align-items: baseline; font-size: var(--text-sm); }
    .settlement.reversed { text-decoration: line-through; color: var(--text-muted); }
    .settle-row > td { background: var(--surface-sunken); }
    .methods { display: flex; flex-wrap: wrap; gap: var(--space-2); margin-bottom: var(--space-2); }
    .method[aria-pressed='true'] { background: var(--primary); border-color: var(--primary); color: var(--text-inverse); }
    .method-help { color: var(--text-secondary); font-size: var(--text-sm); margin-bottom: var(--space-4); }
    .cancel { margin-top: var(--space-5); }
    .actions { text-align: right; }
  `]
})
export class AllocationDetailComponent {
  private api = inject(FinanceApiService);
  private route = inject(ActivatedRoute);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);

  readonly status = ALLOCATION_STATUS;
  readonly methods = METHODS;
  private readonly id = Number(this.route.snapshot.paramMap.get('id'));

  readonly state = signal<LoadState>('loading');
  readonly error = signal('');
  readonly allocation = signal<Allocation | null>(null);
  readonly accounts = signal<Account[]>([]);
  readonly settling = signal<AllocationLine | null>(null);
  readonly method = signal<SettlementMethod>('Earmark');
  readonly busy = signal(false);
  readonly actionError = signal('');
  readonly confirmingCancel = signal(false);
  cancelReason = '';

  // Kept for retries of the same settlement; renewed whenever the form changes
  private key = newIdempotencyKey();

  readonly form = this.fb.group({
    amount: [null as number | null, [Validators.required, amountValidator()]],
    toAccountId: [null as number | null, Validators.required],
    amountReceived: [null as number | null, [Validators.required, amountValidator()]],
    fee: [null as number | null, amountValidator()],
    feeChargedTo: ['Sending' as FeeChargedTo],
    quotedRate: [null as number | null, rateValidator],
    date: [todayIso(), Validators.required],
    reference: ['', Validators.maxLength(100)]
  });

  private readonly value = toSignal(this.form.valueChanges.pipe(startWith(this.form.getRawValue())), { requireSync: true });

  readonly currency = computed(() => this.allocation()?.total.currency ?? '');
  readonly destination = computed(() => this.accounts().find(a => a.id === this.value().toAccountId));

  // Transfers stay in the allocation's currency; exchanges must change it
  readonly destinations = computed(() => {
    const a = this.allocation();
    if (!a) return [];
    return this.accounts().filter(acc => acc.status === 'Active' && acc.id !== a.sourceAccountId
      && (this.method() === 'Transfer' ? acc.currency === a.total.currency : acc.currency !== a.total.currency));
  });

  readonly impliedRate = computed(() => {
    const v = this.value();
    return v.amount && v.amountReceived ? (Number(v.amountReceived) / Number(v.amount)).toFixed(4) : null;
  });

  readonly canCancel = computed(() => {
    const a = this.allocation();
    return !!a && a.status !== 'Cancelled' && a.settled.amount === 0;
  });

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (!this.busy()) this.key = newIdempotencyKey();
    });
    this.load();
  }

  load(): void {
    this.state.set('loading');
    forkJoin({ allocation: this.api.getAllocation(this.id), accounts: this.api.getAccounts() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ allocation, accounts }) => {
          this.allocation.set(allocation);
          this.accounts.set(accounts);
          this.state.set('ready');
        },
        error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
      });
  }

  startSettling(line: AllocationLine): void {
    this.settling.set(line);
    this.actionError.set('');
    this.form.reset({ amount: line.outstanding.amount, toAccountId: null, amountReceived: null, fee: null, feeChargedTo: 'Sending', quotedRate: null, date: todayIso(), reference: '' });
    this.selectMethod('Earmark');
  }

  selectMethod(method: SettlementMethod): void {
    this.method.set(method);
    // Fields a method does not use are disabled, so they neither show nor block submission
    const { toAccountId, amountReceived, fee, feeChargedTo, quotedRate } = this.form.controls;
    if (method === 'Earmark') toAccountId.disable(); else toAccountId.enable();
    for (const control of [amountReceived, fee, feeChargedTo, quotedRate]) {
      if (method === 'Exchange') control.enable(); else control.disable();
    }
    toAccountId.setValue(null);
  }

  methodHelp(): string {
    return METHODS.find(m => m.method === this.method())!.help;
  }

  settle(): void {
    const a = this.allocation();
    const line = this.settling();
    if (!a || !line || this.busy()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const body: Record<string, unknown> = { method: this.method(), amount: v.amount, date: v.date, reference: v.reference || undefined };
    if (this.method() !== 'Earmark') body['toAccountId'] = v.toAccountId;
    if (this.method() === 'Exchange') {
      body['amountReceived'] = v.amountReceived;
      if (v.fee) { body['fee'] = v.fee; body['feeChargedTo'] = v.feeChargedTo; }
      if (v.quotedRate) body['quotedRate'] = v.quotedRate;
    }

    this.busy.set(true);
    this.actionError.set('');
    this.api.settleLine(a.id, line.id, body, this.key).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: updated => {
        this.busy.set(false);
        this.allocation.set(updated);
        this.settling.set(null);
        this.key = newIdempotencyKey();
      },
      error: err => { this.busy.set(false); this.actionError.set(`Not settled: ${describeHttpError(err)}`); }
    });
  }

  cancel(): void {
    const a = this.allocation();
    if (!a || this.busy() || !this.cancelReason.trim()) return;
    this.busy.set(true);
    this.actionError.set('');
    this.api.cancelAllocation(a.id, this.cancelReason.trim()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: updated => { this.busy.set(false); this.allocation.set(updated); this.confirmingCancel.set(false); },
      error: err => { this.busy.set(false); this.actionError.set(`Not cancelled: ${describeHttpError(err)}`); }
    });
  }
}
