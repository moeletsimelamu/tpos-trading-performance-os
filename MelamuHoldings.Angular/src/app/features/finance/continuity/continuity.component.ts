import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { FinanceApiService } from '../finance-api.service';
import { ContinuityReport, Fund } from '../finance.models';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

// The most months a reserve can be asked to cover (the API refuses more)
export const MAX_MONTHS = 120;

// How many months of essential expenses the continuity reserve covers, from recorded balances only, and how far the
// separate restoration capital is from its target. Nothing here moves money or converts a currency.
@Component({
  selector: 'app-finance-continuity',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header section="Finance" title="Continuity"
        description="How many months of essential expenses the continuity reserve covers, counted from recorded balances. Money in another currency, outside bank and cash accounts, or not yet earmarked is shown but never counted.">
        @if (!editing()) { <button type="button" class="btn" (click)="startEdit()" [disabled]="state() !== 'ready'">Change settings</button> }
      </app-page-header>

      @if (editing()) {
        <form class="card form-card" [formGroup]="form" (ngSubmit)="save()" aria-label="Continuity settings">
          <div class="form-grid">
            <div class="field">
              <label for="ct-fund">Continuity fund</label>
              <select id="ct-fund" formControlName="continuityFundId">
                @for (f of choosable(); track f.id) { <option [ngValue]="f.id">{{ f.name }}</option> }
              </select>
            </div>
            <div class="field"><label for="ct-months">Months to cover</label><input id="ct-months" type="number" min="1" [max]="maxMonths" step="1" formControlName="months" /></div>
            <div class="field">
              <label for="ct-expense">Monthly essential expense ({{ report()?.settings?.currency }})</label>
              <input id="ct-expense" type="number" min="0.01" step="0.01" formControlName="monthlyEssentialExpense" placeholder="Not entered" />
            </div>
            <div class="field">
              <label for="ct-restoration">Restoration fund</label>
              <select id="ct-restoration" formControlName="restorationFundId">
                <option [ngValue]="null">None</option>
                @for (f of choosable(); track f.id) { <option [ngValue]="f.id">{{ f.name }}</option> }
              </select>
            </div>
          </div>
          <p class="small secondary">The essential expense is what the business and household need each month to keep going; enter it yourself. Continuity and restoration must be different funds, so no money counts towards both.</p>
          @if (formError()) { <p class="notice notice-error" role="alert">{{ formError() }}</p> }
          <div class="form-actions">
            <button type="submit" class="btn btn-primary" [disabled]="form.invalid || busy()">{{ busy() ? 'Saving…' : 'Save settings' }}</button>
            <button type="button" class="btn" (click)="editing.set(false)">Cancel</button>
          </div>
        </form>
      }

      @switch (state()) {
        @case ('loading') { <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading continuity...</span></div> }
        @case ('error') { <app-load-error title="Could not load continuity" [message]="error()" (retry)="load()" /> }
        @case ('ready') {
          @if (report(); as r) {
            <div class="metric-strip">
              <div class="metric metric-lead">
                <span class="metric-label">Coverage</span>
                <span class="metric-value" [class.text-negative]="r.belowTarget">{{ r.coverageMonths != null ? months(r.coverageMonths) : 'Not measured' }}</span>
                <span class="metric-note">{{ r.coverageMonths != null ? 'of ' + r.settings.months + ' months of essential expenses' : 'Enter the monthly essential expense' }}</span>
              </div>
              <div class="metric">
                <span class="metric-label">Counted · {{ r.cash.currency }}</span>
                <span class="metric-value">{{ r.cash.amount | money: r.cash.currency }}</span>
                <span class="metric-note">{{ r.settings.continuityFundName }}, bank and cash accounts</span>
              </div>
              @if (r.target; as t) {
                <div class="metric">
                  <span class="metric-label">Target</span>
                  <span class="metric-value">{{ t.amount | money: t.currency }}</span>
                  <span class="metric-note">{{ r.settings.months }} × {{ r.settings.monthlyEssentialExpense! | money: t.currency }}</span>
                </div>
              }
              @if (r.shortfall; as s) {
                <div class="metric">
                  <span class="metric-label">Shortfall</span>
                  <span class="metric-value" [class.text-negative]="s.amount > 0">{{ s.amount > 0 ? (s.amount | money: s.currency) : 'None' }}</span>
                  <span class="metric-note">{{ s.amount > 0 ? 'Still to set aside' : 'The target is met' }}</span>
                </div>
              }
            </div>

            <div class="grid">
              <section class="card table-card" aria-labelledby="held-heading">
                <div class="card-header">
                  <h2 id="held-heading">Where the counted money is</h2>
                  <a class="small" [routerLink]="['/finance/transactions']" [queryParams]="{ fundId: r.settings.continuityFundId }">Ledger</a>
                </div>
                @if (r.holdings.length === 0) {
                  <p class="empty-state small">No {{ r.cash.currency }} of {{ r.settings.continuityFundName }} in a bank or cash account. Allocate payouts to it, or earmark money for it.</p>
                } @else {
                  <table class="data-table">
                    <thead><tr><th>Account</th><th class="num">Amount</th></tr></thead>
                    <tbody>
                      @for (h of r.holdings; track h.accountId) {
                        <tr>
                          <td>
                            <div>{{ h.accountName }}</div>
                            <span [class]="h.separated ? 'tag tag-positive' : 'tag'">{{ h.separated ? 'Separated' : 'Earmarked' }}</span>
                          </td>
                          <td class="num">{{ h.amount.amount | money: h.amount.currency }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                  <p class="split small secondary">
                    Separated (in an account holding nothing else): {{ r.separated.amount | money: r.separated.currency }}.
                    Earmarked (in an account shared with other funds): {{ r.earmarked.amount | money: r.earmarked.currency }}.
                  </p>
                }
              </section>

              <section class="card" aria-labelledby="not-counted-heading">
                <div class="card-header"><h2 id="not-counted-heading">Shown, not counted</h2></div>
                @if (r.notCounted.length === 0) {
                  <p class="small muted">Nothing: all of {{ r.settings.continuityFundName }} is counted.</p>
                } @else {
                  <ul class="not-counted">
                    @for (n of r.notCounted; track $index) {
                      <li>
                        <span class="num">{{ n.amount.amount | money: n.amount.currency }}</span>
                        <span class="small secondary">{{ n.reason }}@if (n.accountName) { · {{ n.accountName }} }</span>
                      </li>
                    }
                  </ul>
                }
              </section>
            </div>

            <section class="card restoration" aria-labelledby="restoration-heading">
              <div class="card-header"><h2 id="restoration-heading">Trading restoration</h2></div>
              @if (r.restoration; as rs) {
                <p class="small secondary">Capital to restore trading after a setback, kept in {{ rs.fundName }} apart from the continuity reserve.</p>
                @if (rs.target; as t) {
                  <div class="restoration-figures">
                    <div><span class="small muted">Held</span><div class="figure">{{ rs.cash!.amount | money: rs.cash!.currency }}</div></div>
                    <div><span class="small muted">Target</span><div class="figure">{{ t.amount | money: t.currency }}</div></div>
                    <div><span class="small muted">Shortfall</span><div class="figure">{{ rs.shortfall!.amount > 0 ? (rs.shortfall!.amount | money: rs.shortfall!.currency) : 'None' }}</div></div>
                  </div>
                  <div class="progress" [attr.aria-label]="rs.progress + '% of the target'"><div class="fill" [style.width.%]="rs.progress"></div></div>
                  <span class="small muted">{{ rs.progress }}% of the target</span>
                  @for (n of rs.notCounted; track $index) {
                    <p class="small secondary">Not counted: {{ n.amount.amount | money: n.amount.currency }} · {{ n.reason }}@if (n.accountName) { · {{ n.accountName }} }</p>
                  }
                } @else {
                  <p class="small">{{ rs.fundName }} has no target yet. <a routerLink="/finance/funds">Set one on the Funds page</a>; it is measured in the target's currency.</p>
                }
              } @else {
                <p class="small muted">No restoration fund is chosen. Choose one in the settings to follow it here.</p>
              }
            </section>
          }
        }
      }
    </div>
  `,
  styles: [`
    .form-card { margin-bottom: var(--space-5); }
    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: var(--space-5); margin-top: var(--space-5); }
    .split { padding: var(--space-3) var(--space-5); }
    .not-counted { list-style: none; display: grid; gap: var(--space-3); padding: 0 var(--space-5) var(--space-4); }
    .not-counted li { display: flex; flex-direction: column; gap: var(--space-1); }
    .restoration { margin-top: var(--space-5); }
    .restoration > p, .restoration > span, .restoration-figures, .restoration .progress { margin-left: var(--space-5); margin-right: var(--space-5); }
    .restoration-figures { display: flex; flex-wrap: wrap; gap: var(--space-6); margin-top: var(--space-3); }
    .figure { font-weight: 600; font-variant-numeric: tabular-nums; }
    .progress { height: 6px; border-radius: 3px; background: var(--surface-sunken); overflow: hidden; margin-top: var(--space-3); margin-bottom: var(--space-1); max-width: 320px; }
    .progress .fill { height: 100%; background: var(--positive); }
    .restoration > :last-child { margin-bottom: var(--space-4); }
    .empty-state.small { padding: var(--space-6) var(--space-5); }
  `]
})
export class ContinuityComponent {
  private api = inject(FinanceApiService);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);

  readonly maxMonths = MAX_MONTHS;
  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly error = signal('');
  readonly report = signal<ContinuityReport | null>(null);
  readonly funds = signal<Fund[]>([]);
  readonly editing = signal(false);
  readonly busy = signal(false);
  readonly formError = signal('');

  // The system fund holds money waiting to be allocated, so it can hold neither
  readonly choosable = computed(() => this.funds().filter(f => !f.isSystem));

  readonly form = this.fb.group({
    continuityFundId: this.fb.nonNullable.control(0, Validators.required),
    months: this.fb.control<number | null>(24, [Validators.required, Validators.min(1), Validators.max(MAX_MONTHS)]),
    monthlyEssentialExpense: this.fb.control<number | null>(null, Validators.min(0.01)),
    restorationFundId: this.fb.control<number | null>(null)
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    forkJoin({ report: this.api.getContinuity(), funds: this.api.getFunds() }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ report, funds }) => { this.report.set(report); this.funds.set(funds); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  startEdit(): void {
    const s = this.report()!.settings;
    this.form.reset({
      continuityFundId: s.continuityFundId, months: s.months,
      monthlyEssentialExpense: s.monthlyEssentialExpense ?? null, restorationFundId: s.restorationFundId ?? null
    });
    this.formError.set('');
    this.editing.set(true);
  }

  save(): void {
    if (this.form.invalid || this.busy()) return;
    const v = this.form.getRawValue();
    if (v.restorationFundId === v.continuityFundId) {
      this.formError.set('Continuity and restoration must be different funds.');
      return;
    }
    this.busy.set(true);
    this.formError.set('');
    this.api.saveContinuitySettings({
      continuityFundId: v.continuityFundId, months: v.months!,
      monthlyEssentialExpense: v.monthlyEssentialExpense || null, restorationFundId: v.restorationFundId
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.busy.set(false); this.editing.set(false); this.load(); },
      error: err => { this.busy.set(false); this.formError.set(`Not saved: ${describeHttpError(err)}`); }
    });
  }

  months(value: number): string {
    return `${value.toFixed(1)} ${value === 1 ? 'month' : 'months'}`;
  }
}
