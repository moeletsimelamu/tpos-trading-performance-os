import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { TradingApiService } from '../trading-api.service';
import { EVENT_LABELS, PHASES, PropAccount, PropAccountBalance, PropAccountPhase, PropAccountStatus, RULE_STANDING, STATUS_TAGS } from '../models/prop-account.model';
import { PropRulesFieldsComponent, PropRulesSummaryComponent, RulesGroup, rulesFrom, rulesGroup } from './prop-rules';
import { PropAccountLedgerComponent } from './prop-account-ledger.component';
import { todayIso } from '../../finance/finance-api.service';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

type Panel = 'details' | 'rules' | 'phase' | 'status' | 'reset' | 'balance';

const STATUSES: PropAccountStatus[] = ['Active', 'Pending', 'Breached', 'Closed'];

// One account: where it stands, the rules that apply, and every change that got it here
@Component({
  selector: 'app-prop-account-detail',
  standalone: true,
  imports: [NgTemplateOutlet, ReactiveFormsModule, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent, PropRulesFieldsComponent, PropRulesSummaryComponent, PropAccountLedgerComponent],
  template: `
    <div class="page">
      @switch (state()) {
        @case ('loading') { <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading account...</span></div> }
        @case ('error') { <app-load-error title="Could not load this account" [message]="error()" (retry)="load()" /> }
        @case ('ready') {
          @if (account(); as a) {
            <app-page-header section="Prop accounts" [title]="a.nickname" [description]="a.firm">
              <a routerLink="/trading/prop-accounts" class="btn">All accounts</a>
            </app-page-header>

            <div class="metric-strip">
              <div class="metric"><span class="metric-label">Status</span><span class="metric-value"><span [class]="statusTags[a.status]">{{ a.status }}</span></span></div>
              <div class="metric"><span class="metric-label">Phase</span><span class="metric-value">{{ a.phase }}</span></div>
              <div class="metric"><span class="metric-label">Account size</span><span class="metric-value">{{ a.accountSize.amount | money: a.accountSize.currency }}</span></div>
              <div class="metric"><span class="metric-label">Starting balance</span><span class="metric-value">{{ a.startingBalance.amount | money: a.startingBalance.currency }}</span><span class="metric-note">Since {{ a.runStartedOn }}. Restarts with each phase and reset.</span></div>
            </div>

            @if (a.login || a.notes) {
              <p class="small secondary details">
                @if (a.login) { Login {{ a.login }} } @if (a.login && a.notes) { · } {{ a.notes }}
              </p>
            }

            <div class="actions">
              @if (a.status !== 'Closed') {
                <button type="button" class="btn btn-sm" (click)="open('details')">Edit details</button>
                <button type="button" class="btn btn-sm" (click)="open('rules')">Change rules</button>
                @if (a.status === 'Active' || a.status === 'Pending') { <button type="button" class="btn btn-sm" (click)="open('phase')">Change phase</button> }
                <button type="button" class="btn btn-sm" (click)="open('reset')">Record a reset</button>
                <button type="button" class="btn btn-sm" (click)="open('balance')">Enter a balance</button>
              }
              <button type="button" class="btn btn-sm" (click)="open('status')">Change status</button>
            </div>

            @switch (panel()) {
              @case ('details') {
                <form class="card form-card" [formGroup]="detailsForm" (ngSubmit)="saveDetails(a)" aria-label="Edit account details">
                  <div class="form-grid">
                    <div class="field"><label for="d-firm">Firm</label><input id="d-firm" formControlName="firm" maxlength="100" /></div>
                    <div class="field"><label for="d-nickname">Nickname</label><input id="d-nickname" formControlName="nickname" maxlength="100" /></div>
                    <div class="field"><label for="d-size">Account size</label><input id="d-size" type="number" min="0.01" step="0.01" formControlName="accountSize" /></div>
                    <div class="field"><label for="d-login">Login or account number (optional)</label><input id="d-login" formControlName="login" maxlength="50" autocomplete="off" /></div>
                    <div class="field wide"><label for="d-notes">Notes (optional)</label><input id="d-notes" formControlName="notes" maxlength="500" /></div>
                  </div>
                  <p class="small secondary">Correcting the size also corrects the current starting balance. The currency cannot change. Never put a password here.</p>
                  <ng-container *ngTemplateOutlet="formFooter; context: { invalid: detailsForm.invalid, label: 'Save details' }" />
                </form>
              }
              @case ('rules') {
                <form class="card form-card" [formGroup]="rulesForm" (ngSubmit)="saveRules(a)" aria-label="Change rules">
                  <div class="form-grid">
                    <div class="field"><label for="r-from">Applies from</label><input id="r-from" type="date" formControlName="effectiveFrom" /></div>
                  </div>
                  <app-prop-rules-fields [group]="rulesForm.controls.rules" [currency]="a.accountSize.currency" prefix="edit-rules" />
                  <ng-container *ngTemplateOutlet="formFooter; context: { invalid: rulesForm.invalid, label: 'Save as a new version' }" />
                </form>
              }
              @case ('phase') {
                <form class="card form-card" [formGroup]="phaseForm" (ngSubmit)="savePhase(a)" aria-label="Change phase">
                  <div class="form-grid">
                    <div class="field">
                      <label for="p-phase">New phase</label>
                      <select id="p-phase" formControlName="phase">
                        @for (p of otherPhases(); track p) { <option [value]="p">{{ p }}</option> }
                      </select>
                    </div>
                    <div class="field"><label for="p-date">Date</label><input id="p-date" type="date" formControlName="date" /></div>
                    <div class="field"><label for="p-start">Starting balance</label><input id="p-start" type="number" min="0.01" step="0.01" formControlName="startingBalance" /></div>
                    <div class="field"><label for="p-login">New login (optional)</label><input id="p-login" formControlName="login" maxlength="50" autocomplete="off" /></div>
                    <div class="field wide"><label for="p-reason">{{ goingBack() ? 'Reason (required when going back)' : 'Note (optional)' }}</label><input id="p-reason" formControlName="reason" maxlength="500" /></div>
                  </div>
                  <p class="small secondary">The balance restarts in the new phase. Check the rules below: they start as the current ones and become a new version.</p>
                  <app-prop-rules-fields [group]="phaseForm.controls.rules" [currency]="a.accountSize.currency" prefix="phase-rules" />
                  <ng-container *ngTemplateOutlet="formFooter; context: { invalid: phaseForm.invalid || (goingBack() && !phaseForm.controls.reason.value.trim()), label: 'Change phase' }" />
                </form>
              }
              @case ('status') {
                <form class="card form-card" [formGroup]="statusForm" (ngSubmit)="saveStatus(a)" aria-label="Change status">
                  <div class="form-grid">
                    <div class="field">
                      <label for="s-status">New status</label>
                      <select id="s-status" formControlName="status">
                        @for (s of otherStatuses(); track s) { <option [value]="s">{{ s }}</option> }
                      </select>
                    </div>
                    <div class="field"><label for="s-date">Date</label><input id="s-date" type="date" formControlName="date" /></div>
                    <div class="field wide"><label for="s-reason">{{ reasonLabel() }}</label><input id="s-reason" formControlName="reason" maxlength="500" /></div>
                  </div>
                  <p class="small secondary">Pending means waiting on the firm, for example for verification or the next phase's login.</p>
                  <ng-container *ngTemplateOutlet="formFooter; context: { invalid: statusForm.invalid || (reasonRequired() && !statusForm.controls.reason.value.trim()), label: 'Change status' }" />
                </form>
              }
              @case ('balance') {
                <form class="card form-card" [formGroup]="balanceForm" (ngSubmit)="saveBalance(a)" aria-label="Enter a balance">
                  <div class="form-grid">
                    <div class="field"><label for="b-date">As of the end of</label><input id="b-date" type="date" formControlName="asOf" /></div>
                    <div class="field"><label for="b-balance">Balance on the firm's dashboard</label><input id="b-balance" type="number" min="0" step="0.01" formControlName="balance" /></div>
                    @if (a.rules.rules.maxDrawdown !== null) {
                      <div class="field"><label for="b-level">Drawdown level it shows (optional)</label><input id="b-level" type="number" min="0" step="0.01" formControlName="drawdownLevel" /></div>
                    }
                    <div class="field wide"><label for="b-note">Note (optional)</label><input id="b-note" formControlName="note" maxlength="500" placeholder="e.g. after the October payout" /></div>
                  </div>
                  <p class="small secondary">The balance replaces the app's figure as of that day, and trades on later days move on from it. After a payout, enter the drawdown level too: the balance alone cannot tell a withdrawal from a loss.</p>
                  <ng-container *ngTemplateOutlet="formFooter; context: { invalid: balanceForm.invalid, label: 'Save balance' }" />
                </form>
              }
              @case ('reset') {
                <form class="card form-card" [formGroup]="resetForm" (ngSubmit)="saveReset(a)" aria-label="Record a reset">
                  <div class="form-grid">
                    <div class="field"><label for="x-date">Date</label><input id="x-date" type="date" formControlName="date" /></div>
                    <div class="field"><label for="x-start">Starting balance</label><input id="x-start" type="number" min="0.01" step="0.01" formControlName="startingBalance" /></div>
                    <div class="field wide"><label for="x-note">Note (optional)</label><input id="x-note" formControlName="note" maxlength="500" /></div>
                  </div>
                  <p class="small secondary">For a reset bought from the firm: the account is active again in the same phase and the balance restarts. A new challenge after a breach is a new account instead.</p>
                  <ng-container *ngTemplateOutlet="formFooter; context: { invalid: resetForm.invalid, label: 'Record the reset' }" />
                </form>
              }
            }

            <ng-template #formFooter let-invalid="invalid" let-label="label">
              @if (formError()) { <p class="notice notice-error" role="alert">{{ formError() }}</p> }
              <div class="form-actions">
                <button type="submit" class="btn btn-primary" [disabled]="invalid || busy()">{{ busy() ? 'Saving…' : label }}</button>
                <button type="button" class="btn" (click)="panel.set(null)">Cancel</button>
              </div>
            </ng-template>

            @if (a.standing.figures; as f) {
              <section class="card section" aria-label="Where it stands">
                <h2>Where it stands <span class="small muted">Since {{ a.runStartedOn }}</span></h2>
                <p class="small secondary">Worked out from {{ f.tradesCounted }} closed {{ f.tradesCounted === 1 ? 'trade' : 'trades' }} linked to this account and the balances you entered. These are not the firm's figures, and the firm's own numbers decide.</p>
                <dl class="standing">
                  <div><dt>Balance</dt><dd>{{ f.balance | money: a.accountSize.currency }}</dd></div>
                  <div><dt>Profit or loss</dt><dd [class.text-negative]="f.profit < 0">{{ f.profit | money: a.accountSize.currency : true }}</dd></div>
                  <div>
                    <dt>Drawdown room</dt>
                    @if (f.drawdown; as d) {
                      <dd>
                        {{ d.room | money: a.accountSize.currency }} <span [class]="ruleStanding[d.standing].tag">{{ ruleStanding[d.standing].label }}</span>
                        <span class="note">Breached at {{ d.level | money: a.accountSize.currency }}, with a limit of {{ d.limit | money: a.accountSize.currency }}</span>
                      </dd>
                    } @else { <dd class="muted">No limit set</dd> }
                  </div>
                  <div>
                    <dt>Daily loss</dt>
                    @if (f.dailyLoss; as l) {
                      <dd>
                        @if (l.worstLoss != null) { Worst {{ l.worstLoss | money: a.accountSize.currency }} on {{ l.worstDay }} } @else { No losing day }
                        <span [class]="ruleStanding[l.standing].tag">{{ ruleStanding[l.standing].label }}</span>
                        <span class="note">Limit {{ l.limit | money: a.accountSize.currency }} a day</span>
                        @if (l.daysPastLimit.length > 0) { <span class="note text-negative">Past the limit on {{ l.daysPastLimit.join(', ') }}</span> }
                      </dd>
                    } @else { <dd class="muted">No limit set</dd> }
                  </div>
                  <div>
                    <dt>Profit target</dt>
                    @if (f.target; as t) {
                      <dd>
                        {{ t.profit | money: a.accountSize.currency }} of {{ t.target | money: a.accountSize.currency }}
                        @if (t.reached) { <span class="tag tag-positive">Reached</span> } @else { <span class="note">{{ t.remaining | money: a.accountSize.currency }} to go</span> }
                      </dd>
                    } @else { <dd class="muted">Not set</dd> }
                  </div>
                  <div>
                    <dt>Trading days</dt>
                    @if (f.minimumDays; as m) {
                      <dd>
                        {{ m.done }} of {{ m.required }}
                        @if (m.reached) { <span class="tag tag-positive">Reached</span> }
                      </dd>
                    } @else { <dd>{{ f.tradingDays }} <span class="note">No minimum set</span></dd> }
                  </div>
                </dl>
                @if (a.standing.openTrades > 0) { <p class="small secondary">{{ a.standing.openTrades }} open {{ a.standing.openTrades === 1 ? 'trade is' : 'trades are' }} not counted until closed.</p> }
                @if (a.standing.tradesBeforeRun > 0) { <p class="small secondary">{{ a.standing.tradesBeforeRun }} {{ a.standing.tradesBeforeRun === 1 ? 'trade' : 'trades' }} closed before {{ a.runStartedOn }} {{ a.standing.tradesBeforeRun === 1 ? 'belongs' : 'belong' }} to an earlier phase or reset and {{ a.standing.tradesBeforeRun === 1 ? 'is' : 'are' }} not counted.</p> }
                @if (f.drawdown?.model === 'TrailingIntraday') { <p class="small secondary">Intraday trailing follows unrealised profit, which closed trades cannot show. The firm's level can be higher than this.</p> }
                @if (a.standing.linkedTrades > 0) {
                  <p class="small">
                    <a routerLink="/trading/trades" [queryParams]="{ account: a.nickname }">View its {{ a.standing.linkedTrades }} {{ a.standing.linkedTrades === 1 ? 'trade' : 'trades' }}</a>
                    · <a routerLink="/trading/performance" [queryParams]="{ account: a.nickname }">Performance</a>
                  </p>
                } @else { <p class="small secondary">No trades are linked yet. Choose this account on a trade, or when importing a file.</p> }
              </section>
            }

            @if (a.balances.length > 0) {
              <section class="card table-card section" aria-label="Balances entered">
                <h2 class="table-heading">Balances entered</h2>
                @if (balanceError()) { <p class="notice notice-error" role="alert">{{ balanceError() }}</p> }
                <table class="data-table">
                  <thead><tr><th>As of</th><th class="num">Firm's balance</th><th class="num">Drawdown level</th><th class="num">Trades came to</th><th class="num">Difference</th><th>Note</th><th><span class="visually-hidden">Actions</span></th></tr></thead>
                  <tbody>
                    @for (b of a.balances; track b.id) {
                      <tr>
                        <td class="nowrap">{{ b.asOf }}</td>
                        <td class="num">{{ b.balance.amount | money: b.balance.currency }}</td>
                        <td class="num">@if (b.drawdownLevel) { {{ b.drawdownLevel.amount | money: b.drawdownLevel.currency }} }</td>
                        @if (b.expected != null) {
                          <td class="num">{{ b.expected | money: b.balance.currency }}</td>
                          <td class="num" [class.text-negative]="b.difference !== 0">{{ b.difference! | money: b.balance.currency : true }}</td>
                        } @else { <td class="muted" colspan="2">Before this phase or reset</td> }
                        <td class="note">{{ b.note }}</td>
                        <td>
                          @if (a.status !== 'Closed') {
                            @if (confirmingRemove() === b.id) {
                              <button type="button" class="btn btn-sm btn-danger" [disabled]="busy()" (click)="removeBalance(a, b)">Remove it</button>
                              <button type="button" class="btn-link small" (click)="confirmingRemove.set(null)">Keep</button>
                            } @else {
                              <button type="button" class="btn-link small" (click)="confirmingRemove.set(b.id)">Remove</button>
                            }
                          }
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
                <p class="small secondary table-note">A difference means trades are missing or wrong, or money left the account, such as a payout.</p>
              </section>
            }

            <app-prop-account-ledger [accountId]="a.id" />

            <section class="card section" aria-label="Current rules">
              <h2>Rules <span class="small muted">Version {{ a.rules.version }}, from {{ a.rules.effectiveFrom }}</span></h2>
              <app-prop-rules-summary [rules]="a.rules.rules" [currency]="a.accountSize.currency" />
            </section>

            <section class="card table-card section" aria-label="History">
              <h2 class="table-heading">History</h2>
              <table class="data-table">
                <thead><tr><th>Date</th><th>What happened</th><th>Phase</th><th>Status</th><th class="num">Starting balance</th><th>Note</th></tr></thead>
                <tbody>
                  @for (e of a.history; track e.id) {
                    <tr>
                      <td class="nowrap">{{ e.occurredOn }}</td>
                      <td>{{ eventLabels[e.kind] }}</td>
                      <td>{{ e.phase }}</td>
                      <td><span [class]="statusTags[e.status]">{{ e.status }}</span></td>
                      <td class="num">@if (e.startingBalance) { {{ e.startingBalance.amount | money: e.startingBalance.currency }} }</td>
                      <td class="note">{{ e.note }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </section>

            @if (a.ruleHistory.length > 1) {
              <section class="card section" aria-label="Earlier rule versions">
                <h2>Earlier rule versions</h2>
                @for (r of a.ruleHistory.slice(1); track r.version) {
                  <h3>Version {{ r.version }} · {{ r.phase }} · from {{ r.effectiveFrom }}</h3>
                  <app-prop-rules-summary [rules]="r.rules" [currency]="a.accountSize.currency" />
                }
              </section>
            }
          }
        }
      }
    </div>
  `,
  styles: [`
    .details { margin-top: var(--space-3); }
    .actions { display: flex; flex-wrap: wrap; gap: var(--space-2); margin: var(--space-4) 0; }
    .form-card { margin-bottom: var(--space-5); }
    .wide { grid-column: 1 / -1; }
    .section { margin-bottom: var(--space-5); }
    .section:not(.table-card) { padding: var(--space-5); }
    h2 { font-size: var(--text-base); margin-bottom: var(--space-3); }
    .table-heading { padding: var(--space-4) var(--space-5) 0; }
    h3 { font-size: var(--text-sm); margin: var(--space-4) 0 var(--space-2); }
    .nowrap { white-space: nowrap; }
    .note { white-space: normal; }
    dd .note { display: block; font-size: var(--text-xs); color: var(--text-secondary); }
    dd .note.text-negative { color: var(--negative); }
    .standing { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: var(--space-4) var(--space-5); margin: var(--space-4) 0; }
    .standing dt { font-size: var(--text-xs); color: var(--text-secondary); }
    .standing dd { margin: 0; font-size: var(--text-sm); }
    .table-note { padding: var(--space-3) var(--space-5); }
  `]
})
export class PropAccountDetailComponent {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);
  private id = Number(inject(ActivatedRoute).snapshot.paramMap.get('id'));

  readonly statusTags = STATUS_TAGS;
  readonly ruleStanding = RULE_STANDING;
  readonly eventLabels = EVENT_LABELS;
  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly error = signal('');
  readonly account = signal<PropAccount | null>(null);
  readonly panel = signal<Panel | null>(null);
  readonly busy = signal(false);
  readonly formError = signal('');
  readonly balanceError = signal('');
  readonly confirmingRemove = signal<number | null>(null);

  // The forms' selects are read through signals so the labels follow them on a zoneless page
  private readonly chosenPhase = signal<PropAccountPhase>('Evaluation');
  private readonly chosenStatus = signal<PropAccountStatus>('Active');

  readonly otherPhases = computed(() => PHASES.filter(p => p !== this.account()?.phase));
  readonly otherStatuses = computed(() => STATUSES.filter(s => s !== this.account()?.status));
  readonly goingBack = computed(() => PHASES.indexOf(this.chosenPhase()) < PHASES.indexOf(this.account()?.phase ?? 'Evaluation'));
  readonly reasonRequired = computed(() => this.chosenStatus() === 'Breached' || this.account()?.status === 'Breached' || this.account()?.status === 'Closed');
  readonly reasonLabel = computed(() => this.chosenStatus() === 'Breached' ? 'Which rule was breached' : this.reasonRequired() ? 'Reason (required)' : 'Note (optional)');

  readonly detailsForm = this.fb.nonNullable.group({
    firm: ['', [Validators.required, Validators.maxLength(100)]],
    nickname: ['', [Validators.required, Validators.maxLength(100)]],
    accountSize: [null as number | null, [Validators.required, Validators.min(0.01)]],
    login: ['', Validators.maxLength(50)],
    notes: ['', Validators.maxLength(500)]
  });

  readonly rulesForm = this.fb.nonNullable.group({
    effectiveFrom: [todayIso(), Validators.required],
    rules: rulesGroup(this.fb) as RulesGroup
  });

  readonly phaseForm = this.fb.nonNullable.group({
    phase: ['Verification' as PropAccountPhase, Validators.required],
    date: [todayIso(), Validators.required],
    startingBalance: [null as number | null, [Validators.required, Validators.min(0.01)]],
    login: ['', Validators.maxLength(50)],
    reason: ['', Validators.maxLength(500)],
    rules: rulesGroup(this.fb) as RulesGroup
  });

  readonly statusForm = this.fb.nonNullable.group({
    status: ['Pending' as PropAccountStatus, Validators.required],
    date: [todayIso(), Validators.required],
    reason: ['', Validators.maxLength(500)]
  });

  readonly resetForm = this.fb.nonNullable.group({
    date: [todayIso(), Validators.required],
    startingBalance: [null as number | null, [Validators.required, Validators.min(0.01)]],
    note: ['', Validators.maxLength(500)]
  });

  readonly balanceForm = this.fb.nonNullable.group({
    asOf: [todayIso(), Validators.required],
    balance: [null as number | null, [Validators.required, Validators.min(0)]],
    drawdownLevel: [null as number | null, Validators.min(0)],
    note: ['', Validators.maxLength(500)]
  });

  constructor() {
    this.phaseForm.controls.phase.valueChanges.pipe(takeUntilDestroyed()).subscribe(p => this.chosenPhase.set(p));
    this.statusForm.controls.status.valueChanges.pipe(takeUntilDestroyed()).subscribe(s => this.chosenStatus.set(s));
    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.api.getPropAccount(this.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: a => { this.account.set(a); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  open(panel: Panel): void {
    const a = this.account()!;
    this.formError.set('');
    if (panel === 'details') {
      this.detailsForm.reset({ firm: a.firm, nickname: a.nickname, accountSize: a.accountSize.amount, login: a.login ?? '', notes: a.notes ?? '' });
    } else if (panel === 'rules') {
      this.rulesForm.reset({ effectiveFrom: todayIso() });
      this.rulesForm.controls.rules.reset(this.rulesValue(a));
    } else if (panel === 'phase') {
      const next = PHASES[Math.min(PHASES.indexOf(a.phase) + 1, PHASES.length - 1)];
      const phase = next === a.phase ? this.otherPhases()[0] : next;
      this.phaseForm.reset({ phase, date: todayIso(), startingBalance: a.accountSize.amount, login: '', reason: '' });
      this.phaseForm.controls.rules.reset(this.rulesValue(a));
    } else if (panel === 'status') {
      this.statusForm.reset({ status: this.otherStatuses()[0], date: todayIso(), reason: '' });
    } else if (panel === 'balance') {
      this.balanceForm.reset({ asOf: todayIso(), balance: null, drawdownLevel: null, note: '' });
    } else {
      this.resetForm.reset({ date: todayIso(), startingBalance: a.accountSize.amount, note: '' });
    }
    this.panel.set(panel);
  }

  saveDetails(a: PropAccount): void {
    const v = this.detailsForm.getRawValue();
    this.submit(this.detailsForm.invalid, this.api.updatePropAccount(a.id, {
      firm: v.firm.trim(), nickname: v.nickname.trim(), accountSize: v.accountSize!, login: v.login.trim() || null, notes: v.notes.trim() || null
    }));
  }

  saveRules(a: PropAccount): void {
    this.submit(this.rulesForm.invalid, this.api.changePropAccountRules(a.id, {
      effectiveFrom: this.rulesForm.controls.effectiveFrom.value, rules: rulesFrom(this.rulesForm.controls.rules)
    }));
  }

  savePhase(a: PropAccount): void {
    const v = this.phaseForm.getRawValue();
    this.submit(this.phaseForm.invalid, this.api.movePropAccountPhase(a.id, {
      phase: v.phase, date: v.date, startingBalance: v.startingBalance, login: v.login.trim() || null,
      reason: v.reason.trim() || null, rules: rulesFrom(this.phaseForm.controls.rules)
    }));
  }

  saveStatus(a: PropAccount): void {
    const v = this.statusForm.getRawValue();
    this.submit(this.statusForm.invalid, this.api.changePropAccountStatus(a.id, { status: v.status, date: v.date, reason: v.reason.trim() || null }));
  }

  saveReset(a: PropAccount): void {
    const v = this.resetForm.getRawValue();
    this.submit(this.resetForm.invalid, this.api.resetPropAccount(a.id, { date: v.date, startingBalance: v.startingBalance, note: v.note.trim() || null }));
  }

  saveBalance(a: PropAccount): void {
    const v = this.balanceForm.getRawValue();
    this.submit(this.balanceForm.invalid, this.api.recordPropAccountBalance(a.id, { asOf: v.asOf, balance: v.balance!, drawdownLevel: v.drawdownLevel, note: v.note.trim() || null }));
  }

  removeBalance(a: PropAccount, b: PropAccountBalance): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.balanceError.set('');
    this.api.removePropAccountBalance(a.id, b.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: updated => { this.busy.set(false); this.confirmingRemove.set(null); this.account.set(updated); },
      error: err => { this.busy.set(false); this.balanceError.set(`Not removed: ${describeHttpError(err)}`); }
    });
  }

  private rulesValue(a: PropAccount) {
    return { ...a.rules.rules, notes: a.rules.rules.notes ?? '' };
  }

  private submit(invalid: boolean, request: Observable<PropAccount>): void {
    if (invalid || this.busy()) return;
    this.busy.set(true);
    this.formError.set('');
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: updated => { this.busy.set(false); this.account.set(updated); this.panel.set(null); },
      error: err => { this.busy.set(false); this.formError.set(`Not saved: ${describeHttpError(err)}`); }
    });
  }
}
