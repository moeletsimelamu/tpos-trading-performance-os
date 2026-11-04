import { Component, DestroyRef, OnInit, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { TradingApiService } from '../trading-api.service';
import { OUTCOME_LABELS, PropAccountFeeCandidate, PropAccountFeeKind, PropAccountLedger, PropAccountPayout } from '../models/prop-account.model';
import { MoneyPipe } from '../../../shared/money.pipe';
import { describeHttpError } from '../../../shared/http-error';

type Adding = 'fee' | 'payout' | null;

// What one account has cost and paid back. Fees are expenses already in the ledger, so linking one moves no money.
// The figures are the owner's: a signed-in user without the role simply does not see this section.
@Component({
  selector: 'app-prop-account-ledger',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, MoneyPipe],
  template: `
    @if (!hidden()) {
      <section class="card section" aria-label="Fees and payouts">
        <h2>Fees and payouts</h2>
        @if (loadError()) {
          <p class="notice notice-error" role="alert">{{ loadError() }}</p>
        } @else if (ledger(); as l) {
          <p class="outcome"><span [class]="outcomes[l.economics.outcome].tag">{{ outcomes[l.economics.outcome].label }}</span></p>

          @if (l.economics.lines.length > 0) {
            <table class="data-table lines">
              <thead><tr><th>Currency</th><th class="num">Fees paid</th><th class="num">Payouts received</th><th class="num">Net</th></tr></thead>
              <tbody>
                @for (line of l.economics.lines; track line.currency) {
                  <tr>
                    <td>{{ line.currency }}</td>
                    <td class="num">{{ line.fees | money: line.currency }}</td>
                    <td class="num">{{ line.payoutsReceived | money: line.currency }}</td>
                    <td class="num" [class.text-negative]="line.net < 0">{{ line.net | money: line.currency : true }}</td>
                  </tr>
                }
              </tbody>
            </table>
            @if (l.economics.lines.length > 1) { <p class="small secondary">Each currency stands alone. Nothing is converted.</p> }
          }
          @for (m of l.economics.awaiting; track m.currency) {
            <p class="small secondary awaiting">Awaiting {{ m.amount | money: m.currency }}: requested or approved, not received, and not counted above.</p>
          }

          <div class="link-actions">
            <button type="button" class="btn btn-sm" (click)="startFee()">Link an expense</button>
            <button type="button" class="btn btn-sm" (click)="startPayout()">Link a payout</button>
          </div>

          @if (adding() === 'fee') {
            <form class="link-form" [formGroup]="feeForm" (ngSubmit)="linkFee()" aria-label="Link an expense">
              @if (feeCandidates().length === 0) {
                <p class="small secondary">No unlinked expenses found. <a routerLink="/finance/transactions">Record the expense first</a>, then link it here.</p>
              } @else {
                <div class="form-grid">
                  <div class="field wide">
                    <label for="fee-transaction">Expense</label>
                    <select id="fee-transaction" formControlName="transactionId">
                      @for (c of feeCandidates(); track c.transactionId) {
                        <option [ngValue]="c.transactionId">{{ c.date }} · {{ c.description }} · {{ c.amount.amount | money: c.amount.currency }}</option>
                      }
                    </select>
                  </div>
                  <div class="field">
                    <label for="fee-kind">What it paid for</label>
                    <select id="fee-kind" formControlName="kind">
                      @for (k of feeKinds; track k.value) { <option [value]="k.value">{{ k.label }}</option> }
                    </select>
                  </div>
                </div>
                <p class="small secondary">This names an expense already in the ledger as a cost of this account. No money moves and no balance changes.</p>
              }
              @if (actionError()) { <p class="notice notice-error" role="alert">{{ actionError() }}</p> }
              <div class="form-actions">
                @if (feeCandidates().length > 0) { <button type="submit" class="btn btn-primary" [disabled]="feeForm.invalid || busy()">Link expense</button> }
                <button type="button" class="btn" (click)="adding.set(null)">Cancel</button>
              </div>
            </form>
          }

          @if (adding() === 'payout') {
            <form class="link-form" [formGroup]="payoutForm" (ngSubmit)="linkPayout()" aria-label="Link a payout">
              @if (payoutCandidates().length === 0) {
                <p class="small secondary">No unlinked payouts found. <a routerLink="/finance/payouts">Record the payout first</a>, then link it here.</p>
              } @else {
                <div class="form-grid">
                  <div class="field wide">
                    <label for="payout-choice">Payout</label>
                    <select id="payout-choice" formControlName="payoutId">
                      @for (p of payoutCandidates(); track p.id) {
                        <option [ngValue]="p.id">{{ p.requestedOn }} · {{ p.source }} · {{ p.amount.amount | money: p.amount.currency }} · {{ p.status }}</option>
                      }
                    </select>
                  </div>
                </div>
              }
              @if (actionError()) { <p class="notice notice-error" role="alert">{{ actionError() }}</p> }
              <div class="form-actions">
                @if (payoutCandidates().length > 0) { <button type="submit" class="btn btn-primary" [disabled]="payoutForm.invalid || busy()">Link payout</button> }
                <button type="button" class="btn" (click)="adding.set(null)">Cancel</button>
              </div>
            </form>
          }

          @if (removeError()) { <p class="notice notice-error" role="alert">{{ removeError() }}</p> }

          <h3>Fees</h3>
          @if (l.fees.length === 0) {
            <p class="small secondary">No fees linked. The purchase, resets and monthly fees are not known until their expenses are linked here.</p>
          } @else {
            <table class="data-table">
              <thead><tr><th>Date</th><th>Expense</th><th>For</th><th class="num">Amount</th><th><span class="visually-hidden">Actions</span></th></tr></thead>
              <tbody>
                @for (f of l.fees; track f.id) {
                  <tr [class.reversed]="f.reversed">
                    <td class="nowrap">{{ f.date }}</td>
                    <td class="note">{{ f.description }} <span class="small muted">#{{ f.transactionId }}</span> @if (f.reversed) { <span class="tag">Reversed, not counted</span> }</td>
                    <td>{{ kindLabel(f.kind) }}</td>
                    <td class="num">{{ f.amount.amount | money: f.amount.currency }}</td>
                    <td class="row-actions">
                      @if (confirming() === 'fee' + f.id) {
                        <button type="button" class="btn btn-sm btn-danger" [disabled]="busy()" (click)="unlinkFee(f.id)">Remove link</button>
                        <button type="button" class="btn-link small" (click)="confirming.set(null)">Keep</button>
                      } @else {
                        <button type="button" class="btn-link small" (click)="confirming.set('fee' + f.id)">Remove</button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          }

          <h3>Payouts</h3>
          @if (l.payouts.length === 0) {
            <p class="small secondary">No payouts linked.</p>
          } @else {
            <table class="data-table">
              <thead><tr><th>Requested</th><th>Paid by</th><th>Status</th><th class="num">Amount</th><th class="num">Received</th><th><span class="visually-hidden">Actions</span></th></tr></thead>
              <tbody>
                @for (p of l.payouts; track p.id) {
                  <tr>
                    <td class="nowrap">{{ p.requestedOn }}</td>
                    <td><a [routerLink]="['/finance/payouts', p.id]">{{ p.source }}</a></td>
                    <td>{{ p.status }}</td>
                    <td class="num">{{ p.amount.amount | money: p.amount.currency }}</td>
                    <td class="num">
                      @if (p.credited) { {{ p.credited.amount | money: p.credited.currency }} <div class="small muted">{{ p.receivedOn }}</div> }
                      @else { <span class="muted">Not received</span> }
                    </td>
                    <td class="row-actions">
                      @if (confirming() === 'payout' + p.id) {
                        <button type="button" class="btn btn-sm btn-danger" [disabled]="busy()" (click)="unlinkPayout(p.id)">Remove link</button>
                        <button type="button" class="btn-link small" (click)="confirming.set(null)">Keep</button>
                      } @else {
                        <button type="button" class="btn-link small" (click)="confirming.set('payout' + p.id)">Remove</button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          }
        }
      </section>
    }
  `,
  styles: [`
    .section { margin-bottom: var(--space-5); padding: var(--space-5); }
    h2 { font-size: var(--text-base); margin-bottom: var(--space-3); }
    h3 { font-size: var(--text-sm); margin: var(--space-5) 0 var(--space-2); }
    .outcome { margin-bottom: var(--space-3); }
    .lines { margin-bottom: var(--space-2); }
    .awaiting { margin-top: var(--space-2); }
    .link-actions { display: flex; flex-wrap: wrap; gap: var(--space-2); margin-top: var(--space-4); }
    .link-form { margin-top: var(--space-4); padding: var(--space-4); border: 1px solid var(--border-subtle); border-radius: var(--radius-md); }
    .wide { grid-column: 1 / -1; }
    .nowrap { white-space: nowrap; }
    .note { white-space: normal; }
    .row-actions { white-space: nowrap; text-align: right; }
    tr.reversed .num { text-decoration: line-through; color: var(--text-muted); }
  `]
})
export class PropAccountLedgerComponent implements OnInit {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);

  readonly accountId = input.required<number>();

  readonly outcomes = OUTCOME_LABELS;
  readonly feeKinds: { value: PropAccountFeeKind; label: string }[] = [
    { value: 'Purchase', label: 'Buying the account' },
    { value: 'Reset', label: 'A reset' },
    { value: 'Monthly', label: 'A monthly fee' },
    { value: 'Other', label: 'Something else' }
  ];

  readonly hidden = signal(false);
  readonly loadError = signal('');
  readonly ledger = signal<PropAccountLedger | null>(null);
  readonly adding = signal<Adding>(null);
  readonly feeCandidates = signal<PropAccountFeeCandidate[]>([]);
  readonly payoutCandidates = signal<PropAccountPayout[]>([]);
  readonly busy = signal(false);
  readonly actionError = signal('');
  readonly removeError = signal('');
  readonly confirming = signal<string | null>(null);

  readonly feeForm = this.fb.nonNullable.group({
    transactionId: [null as number | null, Validators.required],
    kind: ['Purchase' as PropAccountFeeKind, Validators.required]
  });
  readonly payoutForm = this.fb.nonNullable.group({ payoutId: [null as number | null, Validators.required] });

  ngOnInit(): void {
    this.api.getPropAccountLedger(this.accountId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: l => this.ledger.set(l),
      error: (err: HttpErrorResponse) => {
        if (err.status === 403) this.hidden.set(true);
        else this.loadError.set(`Could not load fees and payouts: ${describeHttpError(err)}`);
      }
    });
  }

  kindLabel(kind: PropAccountFeeKind): string {
    return this.feeKinds.find(k => k.value === kind)!.label;
  }

  startFee(): void {
    this.actionError.set('');
    this.api.getPropAccountFeeCandidates(this.accountId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: list => {
        this.feeCandidates.set(list);
        this.feeForm.reset({ transactionId: list[0]?.transactionId ?? null, kind: 'Purchase' });
        this.adding.set('fee');
      },
      error: err => { this.adding.set('fee'); this.actionError.set(`Could not load expenses: ${describeHttpError(err)}`); }
    });
  }

  startPayout(): void {
    this.actionError.set('');
    this.api.getPropAccountPayoutCandidates(this.accountId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: list => {
        this.payoutCandidates.set(list);
        this.payoutForm.reset({ payoutId: list[0]?.id ?? null });
        this.adding.set('payout');
      },
      error: err => { this.adding.set('payout'); this.actionError.set(`Could not load payouts: ${describeHttpError(err)}`); }
    });
  }

  linkFee(): void {
    if (this.feeForm.invalid || this.busy()) return;
    const v = this.feeForm.getRawValue();
    this.run(this.api.linkPropAccountFee(this.accountId(), { transactionId: v.transactionId!, kind: v.kind }), this.actionError);
  }

  linkPayout(): void {
    if (this.payoutForm.invalid || this.busy()) return;
    this.run(this.api.linkPropAccountPayout(this.accountId(), this.payoutForm.getRawValue().payoutId!), this.actionError);
  }

  unlinkFee(feeId: number): void {
    this.run(this.api.unlinkPropAccountFee(this.accountId(), feeId), this.removeError);
  }

  unlinkPayout(payoutId: number): void {
    this.run(this.api.unlinkPropAccountPayout(this.accountId(), payoutId), this.removeError);
  }

  private run(request: Observable<PropAccountLedger>, errors: typeof this.actionError): void {
    if (this.busy()) return;
    this.busy.set(true);
    errors.set('');
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: l => { this.busy.set(false); this.ledger.set(l); this.adding.set(null); this.confirming.set(null); },
      error: err => { this.busy.set(false); errors.set(`Not saved: ${describeHttpError(err)}`); }
    });
  }
}
