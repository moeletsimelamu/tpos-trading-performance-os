import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { forkJoin, startWith } from 'rxjs';
import { FinanceApiService, newIdempotencyKey, todayIso } from '../finance-api.service';
import { ALLOCATION_STATUS, Account, AllocationPolicy, AllocationPreview, PAYOUT_STATUS, Payout } from '../finance.models';
import { amountValidator } from '../amount.validator';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-payout-detail',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent],
  templateUrl: './payout-detail.component.html',
  styles: [`
    .layout { display: grid; grid-template-columns: minmax(0, 2fr) minmax(0, 3fr); gap: var(--space-5); align-items: start; }
    @media (max-width: 999px) { .layout { grid-template-columns: minmax(0, 1fr); } }
    .card.flush { padding: 0; }
    .timeline { list-style: none; padding: var(--space-2) var(--space-5) var(--space-4); }
    .step { display: grid; grid-template-columns: 14px 1fr; gap: var(--space-3); padding: var(--space-3) 0; }
    .dot { width: 10px; height: 10px; border-radius: 50%; margin-top: 5px; border: 2px solid var(--border); background: var(--surface); }
    .step.done .dot { border-color: var(--navy-900); background: var(--navy-900); }
    .step.stopped .dot { border-color: var(--negative); background: var(--negative); }
    .step-title { font-weight: 500; }
    .step-detail { color: var(--text-secondary); font-size: var(--text-sm); }
    .panel { padding: var(--space-5); }
    .panel + .panel { border-top: 1px solid var(--border-subtle); }
    .panel h3 { font-size: var(--text-md); margin-bottom: var(--space-1); }
    .panel > p { color: var(--text-secondary); font-size: var(--text-sm); margin-bottom: var(--space-4); }
    .total-row td { font-weight: 600; border-top: 1px solid var(--border); }
    .secondary-actions { display: flex; gap: var(--space-4); margin-top: var(--space-3); }
  `]
})
export class PayoutDetailComponent {
  private api = inject(FinanceApiService);
  private route = inject(ActivatedRoute);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);

  readonly payoutStatus = PAYOUT_STATUS;
  readonly allocationStatus = ALLOCATION_STATUS;
  private readonly id = Number(this.route.snapshot.paramMap.get('id'));

  readonly state = signal<LoadState>('loading');
  readonly error = signal('');
  readonly payout = signal<Payout | null>(null);
  readonly accounts = signal<Account[]>([]);
  readonly policies = signal<AllocationPolicy[]>([]);
  readonly busy = signal(false);
  readonly actionError = signal('');
  readonly showReject = signal(false);
  readonly showUndo = signal(false);

  readonly preview = signal<AllocationPreview | null>(null);
  readonly previewError = signal('');
  readonly policyId = signal<number | null>(null);

  // One key per intended action: kept across retries of the same values, renewed when they change
  private receiveKey = newIdempotencyKey();
  private confirmKey = newIdempotencyKey();
  private undoKey = newIdempotencyKey();

  readonly approveForm = this.fb.group({
    amount: [null as number | null, [Validators.required, amountValidator()]],
    date: [todayIso(), Validators.required]
  });

  readonly rejectForm = this.fb.nonNullable.group({
    reason: ['', [Validators.required, Validators.maxLength(200)]],
    date: [todayIso(), Validators.required]
  });

  readonly receiveForm = this.fb.group({
    accountId: [null as number | null, Validators.required],
    amountCredited: [null as number | null, [Validators.required, amountValidator()]],
    fee: [null as number | null, amountValidator()],
    date: [todayIso(), Validators.required],
    reference: ['', Validators.maxLength(100)]
  });

  readonly undoForm = this.fb.nonNullable.group({
    reason: ['', [Validators.required, Validators.maxLength(200)]],
    date: [todayIso(), Validators.required]
  });

  private readonly receiveValue = toSignal(this.receiveForm.valueChanges.pipe(startWith(this.receiveForm.getRawValue())), { requireSync: true });

  readonly activeAccounts = computed(() => this.accounts().filter(a => a.status === 'Active'));
  readonly receiveAccount = computed(() => this.accounts().find(a => a.id === this.receiveValue().accountId));

  // When the money lands in another currency, show the rate the amounts imply (the server records the same figure)
  readonly impliedRate = computed(() => {
    const p = this.payout();
    const account = this.receiveAccount();
    const v = this.receiveValue();
    if (!p || !account || account.currency === p.requested.currency || !v.amountCredited) return null;
    const approved = (p.approved ?? p.requested).amount;
    return ((Number(v.amountCredited) + Number(v.fee ?? 0)) / approved).toFixed(4);
  });

  readonly title = computed(() => {
    const p = this.payout();
    return p ? `Payout #${p.id} · ${p.source}` : 'Payout';
  });

  constructor() {
    this.receiveForm.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (!this.busy()) this.receiveKey = newIdempotencyKey();
    });
    this.load();
  }

  load(): void {
    this.state.set('loading');
    forkJoin({ payout: this.api.getPayout(this.id), accounts: this.api.getAccounts(), policies: this.api.getPolicies() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ payout, accounts, policies }) => {
          this.accounts.set(accounts);
          this.policies.set(policies);
          this.policyId.set(policies.find(p => p.isActive)?.id ?? null);
          this.show(payout);
          this.state.set('ready');
        },
        error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
      });
  }

  approve(): void {
    const p = this.payout();
    if (!p || !this.ready(this.approveForm)) return;
    const v = this.approveForm.getRawValue();
    this.run(this.api.approvePayout(p.id, { amount: v.amount, date: v.date! }));
  }

  reject(): void {
    const p = this.payout();
    if (!p || !this.ready(this.rejectForm)) return;
    this.run(this.api.rejectPayout(p.id, this.rejectForm.getRawValue()));
  }

  receive(): void {
    const p = this.payout();
    if (!p || !this.ready(this.receiveForm)) return;
    const v = this.receiveForm.getRawValue();
    this.run(this.api.receivePayout(p.id, {
      accountId: v.accountId, amountCredited: v.amountCredited, fee: v.fee ?? undefined, date: v.date, reference: v.reference || undefined
    }, this.receiveKey), () => (this.receiveKey = newIdempotencyKey()));
  }

  undoReceipt(): void {
    const p = this.payout();
    if (!p || !this.ready(this.undoForm)) return;
    this.run(this.api.undoReceipt(p.id, this.undoForm.getRawValue(), this.undoKey), () => (this.undoKey = newIdempotencyKey()));
  }

  selectPolicy(id: number): void {
    this.policyId.set(id);
    this.loadPreview();
  }

  confirm(): void {
    const preview = this.preview();
    if (!preview || this.busy()) return;
    this.busy.set(true);
    this.actionError.set('');
    this.api.confirmAllocation(preview.payoutId, preview.policyId, this.confirmKey)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.confirmKey = newIdempotencyKey();
          this.api.getPayout(preview.payoutId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(p => this.show(p));
        },
        error: err => { this.busy.set(false); this.actionError.set(`Not confirmed: ${describeHttpError(err)}`); }
      });
  }

  policyLabel(policy: AllocationPolicy): string {
    return `v${policy.version}${policy.isActive ? ' (active)' : ''}: ` + policy.lines.map(l => `${l.fundName} ${l.percentage}%`).join(' / ');
  }

  private show(payout: Payout): void {
    this.payout.set(payout);
    this.showReject.set(false);
    this.showUndo.set(false);
    if (payout.status === 'Requested') this.approveForm.patchValue({ amount: payout.requested.amount });
    if (payout.status === 'Approved' && payout.approved) {
      const sameCurrency = this.activeAccounts().filter(a => a.currency === payout.requested.currency);
      this.receiveForm.patchValue({
        accountId: sameCurrency.length === 1 ? sameCurrency[0].id : null,
        amountCredited: payout.approved.amount
      }, { emitEvent: false });
      this.receiveKey = newIdempotencyKey();
    }
    this.preview.set(null);
    if (payout.status === 'Received' && !payout.allocationId) this.loadPreview();
  }

  private loadPreview(): void {
    const p = this.payout();
    if (!p) return;
    this.previewError.set('');
    this.api.previewAllocation(p.id, this.policyId() ?? undefined).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: preview => { this.preview.set(preview); this.confirmKey = newIdempotencyKey(); },
      error: err => { this.preview.set(null); this.previewError.set(describeHttpError(err)); }
    });
  }

  private ready(form: { invalid: boolean; markAllAsTouched(): void }): boolean {
    if (this.busy()) return false;
    if (form.invalid) {
      form.markAllAsTouched();
      return false;
    }
    return true;
  }

  private run(request: ReturnType<FinanceApiService['getPayout']>, onSuccess?: () => void): void {
    this.busy.set(true);
    this.actionError.set('');
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: payout => { this.busy.set(false); onSuccess?.(); this.show(payout); },
      error: err => { this.busy.set(false); this.actionError.set(`Not saved: ${describeHttpError(err)}`); }
    });
  }
}
