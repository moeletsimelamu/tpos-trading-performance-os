import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TradingApiService } from '../trading-api.service';
import { PHASES, PropAccount, PropAccountPhase, STATUS_TAGS, drawdownLabel } from '../models/prop-account.model';
import { PropRulesFieldsComponent, rulesFrom, rulesGroup } from './prop-rules';
import { todayIso } from '../../finance/finance-api.service';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

// Each attempt at a prop firm account, its rules and where it stands. Nothing on this page records or moves money.
@Component({
  selector: 'app-prop-accounts',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent, PropRulesFieldsComponent],
  template: `
    <div class="page">
      <app-page-header section="Trading operations" title="Prop accounts"
        description="One record per attempt: its phases, rules and history stay together. A new challenge after a breach is a new record.">
        <button type="button" class="btn btn-primary" (click)="startCreate()">New account</button>
      </app-page-header>

      @if (creating()) {
        <form class="card form-card" [formGroup]="form" (ngSubmit)="save()" aria-label="New prop firm account">
          <div class="form-grid">
            <div class="field"><label for="pa-firm">Firm</label><input id="pa-firm" formControlName="firm" maxlength="100" /></div>
            <div class="field"><label for="pa-nickname">Nickname</label><input id="pa-nickname" formControlName="nickname" maxlength="100" placeholder="e.g. 100k challenge, attempt 2" /></div>
            <div class="field"><label for="pa-size">Account size</label><input id="pa-size" type="number" min="0.01" step="0.01" formControlName="accountSize" /></div>
            <div class="field">
              <label for="pa-currency">Currency</label>
              <select id="pa-currency" formControlName="currency">
                @for (c of currencies(); track c) { <option [value]="c">{{ c }}</option> }
              </select>
            </div>
            <div class="field">
              <label for="pa-phase">Phase</label>
              <select id="pa-phase" formControlName="phase">
                @for (p of phases; track p) { <option [value]="p">{{ p }}</option> }
              </select>
            </div>
            <div class="field"><label for="pa-opened">Opened on</label><input id="pa-opened" type="date" formControlName="openedOn" /></div>
            <div class="field"><label for="pa-login">Login or account number (optional)</label><input id="pa-login" formControlName="login" maxlength="50" autocomplete="off" /></div>
            <div class="field wide"><label for="pa-notes">Notes (optional)</label><input id="pa-notes" formControlName="notes" maxlength="500" /></div>
          </div>
          <p class="small secondary">Never put the account's password here.</p>
          <app-prop-rules-fields [group]="form.controls.rules" [currency]="form.controls.currency.value" prefix="new-rules" />
          @if (formError()) { <p class="notice notice-error" role="alert">{{ formError() }}</p> }
          <div class="form-actions">
            <button type="submit" class="btn btn-primary" [disabled]="form.invalid || busy()">{{ busy() ? 'Saving…' : 'Save account' }}</button>
            <button type="button" class="btn" (click)="creating.set(false)">Cancel</button>
          </div>
        </form>
      }

      @switch (state()) {
        @case ('loading') { <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading accounts...</span></div> }
        @case ('error') { <app-load-error title="Could not load prop accounts" [message]="error()" (retry)="load()" /> }
        @case ('ready') {
          <div class="metric-strip">
            <div class="metric"><span class="metric-label">Active</span><span class="metric-value">{{ count('Active') }}</span></div>
            <div class="metric"><span class="metric-label">Funded and active</span><span class="metric-value">{{ fundedActive() }}</span></div>
            <div class="metric"><span class="metric-label">Waiting on the firm</span><span class="metric-value">{{ count('Pending') }}</span></div>
            <div class="metric"><span class="metric-label">Breached</span><span class="metric-value" [class.text-negative]="count('Breached') > 0">{{ count('Breached') }}</span></div>
          </div>

          <div class="toolbar">
            <label class="check"><input type="checkbox" [checked]="includeClosed()" (change)="toggleClosed()" />Show closed</label>
          </div>

          <div class="card table-card">
            @if (accounts().length === 0) {
              <p class="empty-state small">No prop firm accounts recorded. Add each account you buy, with the rules the firm sets for it.</p>
            } @else {
              <table class="data-table">
                <thead><tr><th>Account</th><th class="num">Size</th><th>Phase</th><th>Status</th><th>Balance since</th><th>Drawdown</th><th class="num">Profit target</th></tr></thead>
                <tbody>
                  @for (a of accounts(); track a.id) {
                    <tr>
                      <td>
                        <a class="name" [routerLink]="['/trading/prop-accounts', a.id]">{{ a.nickname }}</a>
                        <div class="small muted">{{ a.firm }}</div>
                      </td>
                      <td class="num">{{ a.accountSize.amount | money: a.accountSize.currency }}</td>
                      <td>{{ a.phase }}</td>
                      <td><span [class]="statusTags[a.status]">{{ a.status }}</span></td>
                      <td class="nowrap">{{ a.runStartedOn }}</td>
                      <td>
                        {{ drawdown(a) }}
                        @if (a.rules.rules.maxDrawdown !== null) { <div class="small muted">{{ a.rules.rules.maxDrawdown! | money: a.accountSize.currency }}</div> }
                      </td>
                      <td class="num">
                        @if (a.rules.rules.profitTarget !== null) { {{ a.rules.rules.profitTarget! | money: a.accountSize.currency }} } @else { <span class="muted">Not set</span> }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </div>
        }
      }
    </div>
  `,
  styles: [`
    .form-card { margin-bottom: var(--space-5); }
    .wide { grid-column: 1 / -1; }
    .toolbar { display: flex; justify-content: flex-end; margin: var(--space-4) 0 var(--space-2); }
    .check { display: flex; gap: var(--space-2); align-items: center; font-size: var(--text-sm); }
    .check input { height: auto; }
    .name { font-weight: 500; }
    .nowrap { white-space: nowrap; }
    .empty-state.small { padding: var(--space-6) var(--space-5); }
  `]
})
export class PropAccountsComponent {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);
  private router = inject(Router);

  readonly phases = PHASES;
  readonly statusTags = STATUS_TAGS;
  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly error = signal('');
  readonly accounts = signal<PropAccount[]>([]);
  readonly currencies = signal<string[]>(['USD']);
  readonly includeClosed = signal(false);
  readonly creating = signal(false);
  readonly busy = signal(false);
  readonly formError = signal('');

  readonly fundedActive = computed(() => this.accounts().filter(a => a.status === 'Active' && a.phase === 'Funded').length);

  readonly form = this.fb.nonNullable.group({
    firm: ['', [Validators.required, Validators.maxLength(100)]],
    nickname: ['', [Validators.required, Validators.maxLength(100)]],
    accountSize: [null as number | null, [Validators.required, Validators.min(0.01)]],
    currency: ['USD', Validators.required],
    phase: ['Evaluation' as PropAccountPhase, Validators.required],
    openedOn: [todayIso(), Validators.required],
    login: ['', Validators.maxLength(50)],
    notes: ['', Validators.maxLength(500)],
    rules: rulesGroup(this.fb)
  });

  constructor() {
    this.load();
    this.api.getPropAccountCurrencies().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: c => this.currencies.set(c), error: () => {} });
  }

  load(): void {
    this.state.set('loading');
    this.api.getPropAccounts(this.includeClosed()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: a => { this.accounts.set(a); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  toggleClosed(): void {
    this.includeClosed.update(v => !v);
    this.load();
  }

  startCreate(): void {
    this.form.reset();
    this.formError.set('');
    this.creating.set(true);
  }

  save(): void {
    if (this.form.invalid || this.busy()) return;
    const v = this.form.getRawValue();
    this.busy.set(true);
    this.formError.set('');
    this.api.createPropAccount({
      firm: v.firm.trim(), nickname: v.nickname.trim(), accountSize: v.accountSize!, currency: v.currency, phase: v.phase, openedOn: v.openedOn,
      login: v.login.trim() || null, notes: v.notes.trim() || null, rules: rulesFrom(this.form.controls.rules)
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: created => { this.busy.set(false); this.router.navigate(['/trading/prop-accounts', created.id]); },
      error: err => { this.busy.set(false); this.formError.set(`Not saved: ${describeHttpError(err)}`); }
    });
  }

  count(status: PropAccount['status']): number {
    return this.accounts().filter(a => a.status === status).length;
  }

  drawdown(a: PropAccount): string {
    return drawdownLabel(a.rules.rules.drawdownModel);
  }
}
