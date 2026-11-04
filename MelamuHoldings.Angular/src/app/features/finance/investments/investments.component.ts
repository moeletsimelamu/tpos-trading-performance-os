import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { FinanceApiService, todayIso } from '../finance-api.service';
import { InvestmentAccount, Investments, Valuation } from '../finance.models';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

// Brokerage accounts: what the business put in (from the ledger), what the broker says they are worth (valuations the
// owner records), and the unrealised gain or loss between them. Recording a valuation never moves money.
@Component({
  selector: 'app-finance-investments',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header section="Finance" title="Investments"
        description="Brokerage accounts against the values you record from the broker. Contributions come from the ledger; a valuation is an observation and never moves money. Unrealised gains and losses are not cash." />

      @switch (state()) {
        @case ('loading') { <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading investments...</span></div> }
        @case ('error') { <app-load-error title="Could not load investments" [message]="error()" (retry)="load()" /> }
        @case ('ready') {
          @if (data()!.accounts.length === 0) {
            <div class="card empty-state">
              <h2>No brokerage accounts</h2>
              <p>Add the broker as an account of type Brokerage, record what you put in as a transfer or exchange, then record its value here.</p>
              <a class="btn" routerLink="/finance/accounts">Set up accounts</a>
            </div>
          } @else {
            @if (data()!.totals.length > 0) {
              <div class="metric-strip">
                <!-- Currencies are peers and are never added together -->
                @for (t of data()!.totals; track t.currency) {
                  <div class="metric metric-lead">
                    <span class="metric-label">Valued · {{ t.currency }}</span>
                    <span class="metric-value">{{ t.value | money: t.currency }}</span>
                    <span class="metric-note">{{ t.accounts }} {{ t.accounts === 1 ? 'account' : 'accounts' }}, at their latest valuations</span>
                  </div>
                  <div class="metric">
                    <span class="metric-label">Unrealised · {{ t.currency }}</span>
                    <span class="metric-value" [class.text-negative]="t.unrealised < 0" [class.text-positive]="t.unrealised > 0">{{ t.unrealised | money: t.currency : true }}</span>
                    <span class="metric-note">Not cash until sold and withdrawn</span>
                  </div>
                }
              </div>
            }

            @for (a of data()!.accounts; track a.accountId) {
              <section class="card account" [attr.aria-labelledby]="'inv-' + a.accountId">
                <div class="card-header">
                  <div>
                    <h2 [id]="'inv-' + a.accountId">{{ a.accountName }}</h2>
                    <span class="small muted">{{ a.institution ? a.institution + ' · ' : '' }}{{ a.currency }}@if (a.taxTreatment === 'TaxFree') { · Tax-free }@if (a.taxTreatment === 'Taxable') { · Taxable }@if (a.ownership !== 'Company') { · {{ a.ownership === 'Personal' ? 'Personal' : 'Ownership not stated' }} }@if (a.status === 'Closed') { · closed }</span>
                  </div>
                  @if (a.status === 'Active' && recording() !== a.accountId) {
                    <button type="button" class="btn btn-sm" (click)="startRecording(a)">Record valuation</button>
                  }
                </div>

                @if (recording() === a.accountId) {
                  <form class="valuation-form" [formGroup]="form" (ngSubmit)="record(a)" [attr.aria-label]="'Record a valuation of ' + a.accountName">
                    <div class="form-grid">
                      <div class="field"><label [for]="'v-date-' + a.accountId">Valued on</label><input [id]="'v-date-' + a.accountId" type="date" formControlName="valuedOn" /></div>
                      <div class="field"><label [for]="'v-value-' + a.accountId">Value ({{ a.currency }})</label><input [id]="'v-value-' + a.accountId" type="number" min="0" step="0.01" formControlName="value" /></div>
                      <div class="field wide"><label [for]="'v-note-' + a.accountId">Note (optional)</label><input [id]="'v-note-' + a.accountId" formControlName="note" maxlength="200" placeholder="e.g. Monthly statement" /></div>
                    </div>
                    @if (formError()) { <p class="notice notice-error" role="alert">{{ formError() }}</p> }
                    <div class="form-actions">
                      <button type="submit" class="btn btn-primary" [disabled]="form.invalid || busy()">{{ busy() ? 'Saving…' : 'Save valuation' }}</button>
                      <button type="button" class="btn" (click)="recording.set(null)">Cancel</button>
                    </div>
                  </form>
                }

                @if (a.latest; as v) {
                  <dl class="figures">
                    <div><dt>Value on {{ v.valuedOn }}</dt><dd>{{ v.value.amount | money: v.value.currency }}</dd></div>
                    <div><dt>Net contributions</dt><dd>{{ a.netContributions.amount | money: a.currency }}</dd></div>
                    <div><dt>Recorded income</dt><dd>{{ a.recordedIncome.amount | money: a.currency }}</dd></div>
                    <div><dt>Recorded costs</dt><dd>{{ a.recordedCosts.amount | money: a.currency }}</dd></div>
                    <div><dt>Recorded in the ledger</dt><dd>{{ a.recordedBalance.amount | money: a.currency }}</dd></div>
                    <div class="lead">
                      <dt>Unrealised gain or loss</dt>
                      <dd [class.text-negative]="a.unrealised!.amount < 0" [class.text-positive]="a.unrealised!.amount > 0">{{ a.unrealised!.amount | money: a.currency : true }}</dd>
                    </div>
                  </dl>
                  <p class="small secondary">
                    Ledger figures are as of {{ a.asOf }}, the valuation date. Against what was put in, the account is {{ a.gainOnContributions!.amount | money: a.currency : true }}, recorded income and costs included.
                  </p>
                  @if (a.transactionsSinceValuation > 0) {
                    <p class="notice notice-warning">
                      {{ a.transactionsSinceValuation }} {{ a.transactionsSinceValuation === 1 ? 'transaction' : 'transactions' }} since this valuation
                      ({{ a.movedSinceValuation.amount | money: a.currency : true }}). They are not in the gain or loss; record a new valuation to include them.
                    </p>
                  }
                } @else {
                  <dl class="figures">
                    <div><dt>Net contributions</dt><dd>{{ a.netContributions.amount | money: a.currency }}</dd></div>
                    <div><dt>Recorded in the ledger</dt><dd>{{ a.recordedBalance.amount | money: a.currency }}</dd></div>
                  </dl>
                  <p class="small secondary">Not valued yet, so there is no gain or loss to show. Record what the broker reports the account is worth.</p>
                }

                <div class="history-toggle">
                  <button type="button" class="btn-link small" (click)="toggleHistory(a)">{{ historyFor() === a.accountId ? 'Hide valuations' : 'All valuations' }}</button>
                  <a class="small" [routerLink]="['/finance/transactions']" [queryParams]="{ accountId: a.accountId }">Ledger</a>
                </div>
                @if (historyFor() === a.accountId) {
                  @if (rowError()) { <p class="notice notice-error" role="alert">{{ rowError() }}</p> }
                  @if (history() === null) { <p class="small muted pad">Loading…</p> }
                  @else if (history()!.length === 0) { <p class="small muted pad">No valuations recorded.</p> }
                  @else {
                    <div class="history-table">
                    <table class="data-table">
                      <thead><tr><th>Date</th><th class="num">Value</th><th class="num">Recorded</th><th class="num">Unrealised</th><th><span class="visually-hidden">Actions</span></th></tr></thead>
                      <tbody>
                        @for (h of history(); track h.id) {
                          <tr>
                            <td><div class="nowrap">{{ h.valuedOn }}</div>@if (h.note) { <div class="small muted">{{ h.note }}</div> }</td>
                            <td class="num">{{ h.value.amount | money: h.value.currency }}</td>
                            <td class="num">{{ h.recordedBalance.amount | money: h.recordedBalance.currency }}</td>
                            <td class="num">{{ h.unrealised.amount | money: h.unrealised.currency : true }}</td>
                            <td class="actions">
                              @if (confirmingDelete() === h.id) {
                                <button type="button" class="btn btn-sm btn-danger" [disabled]="busy()" (click)="remove(a, h)">Delete it</button>
                                <button type="button" class="btn-link small" (click)="confirmingDelete.set(null)">Keep</button>
                              } @else {
                                <button type="button" class="btn-link small" (click)="confirmingDelete.set(h.id)">Delete</button>
                              }
                            </td>
                          </tr>
                        }
                      </tbody>
                    </table>
                    </div>
                  }
                }
              </section>
            }
          }
        }
      }
    </div>
  `,
  styles: [`
    .account { margin-top: var(--space-5); }
    .account .card-header { align-items: flex-start; gap: var(--space-3); }
    .valuation-form { padding: 0 var(--space-5) var(--space-4); }
    .wide { grid-column: 1 / -1; }
    .figures { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: var(--space-4); padding: var(--space-2) var(--space-5) var(--space-3); margin: 0; }
    .figures dt { font-size: var(--text-sm); color: var(--text-secondary); }
    .figures dd { margin: var(--space-1) 0 0; font-weight: 600; font-variant-numeric: tabular-nums; }
    .figures .lead dd { font-size: var(--text-lg); }
    .account > p { margin: 0 var(--space-5) var(--space-3); }
    .history-toggle { display: flex; gap: var(--space-4); padding: 0 var(--space-5) var(--space-4); }
    .history-table { overflow-x: auto; }
    .pad { padding: 0 var(--space-5) var(--space-4); }
    .actions { display: flex; gap: var(--space-3); align-items: center; justify-content: flex-end; flex-wrap: wrap; }
    .nowrap { white-space: nowrap; }
  `]
})
export class InvestmentsComponent {
  private api = inject(FinanceApiService);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);

  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly error = signal('');
  readonly data = signal<Investments | null>(null);
  readonly recording = signal<number | null>(null);
  readonly busy = signal(false);
  readonly formError = signal('');
  readonly historyFor = signal<number | null>(null);
  readonly history = signal<Valuation[] | null>(null);
  readonly rowError = signal('');
  readonly confirmingDelete = signal<number | null>(null);

  readonly form = this.fb.group({
    valuedOn: this.fb.nonNullable.control(todayIso(), Validators.required),
    value: this.fb.control<number | null>(null, [Validators.required, Validators.min(0)]),
    note: this.fb.nonNullable.control('', Validators.maxLength(200))
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.api.getInvestments().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: d => { this.data.set(d); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  startRecording(a: InvestmentAccount): void {
    this.form.reset({ valuedOn: todayIso(), value: null, note: '' });
    this.formError.set('');
    this.recording.set(a.accountId);
  }

  record(a: InvestmentAccount): void {
    if (this.form.invalid || this.busy()) return;
    const v = this.form.getRawValue();
    this.busy.set(true);
    this.formError.set('');
    this.api.recordValuation(a.accountId, { valuedOn: v.valuedOn, value: v.value!, note: v.note.trim() || null })
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: () => { this.busy.set(false); this.recording.set(null); this.afterChange(a); },
        error: err => { this.busy.set(false); this.formError.set(`Not saved: ${describeHttpError(err)}`); }
      });
  }

  toggleHistory(a: InvestmentAccount): void {
    const opening = this.historyFor() !== a.accountId;
    this.historyFor.set(opening ? a.accountId : null);
    this.confirmingDelete.set(null);
    this.rowError.set('');
    this.history.set(null);
    if (opening) this.loadHistory(a);
  }

  remove(a: InvestmentAccount, v: Valuation): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.confirmingDelete.set(null);
    this.api.deleteValuation(a.accountId, v.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.busy.set(false); this.afterChange(a); },
      error: err => { this.busy.set(false); this.rowError.set(`Not deleted: ${describeHttpError(err)}`); }
    });
  }

  private loadHistory(a: InvestmentAccount): void {
    this.api.getValuations(a.accountId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: h => this.history.set(h),
      error: err => { this.history.set([]); this.rowError.set(`Could not load valuations: ${describeHttpError(err)}`); }
    });
  }

  private afterChange(a: InvestmentAccount): void {
    this.load();
    if (this.historyFor() === a.accountId) { this.history.set(null); this.loadHistory(a); }
  }
}
