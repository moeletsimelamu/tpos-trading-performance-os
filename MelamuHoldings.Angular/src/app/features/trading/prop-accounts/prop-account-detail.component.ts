import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { TradingApiService } from '../trading-api.service';
import { EVENT_LABELS, PHASES, PropAccount, PropAccountPhase, PropAccountStatus, STATUS_TAGS } from '../models/prop-account.model';
import { PropRulesFieldsComponent, PropRulesSummaryComponent, RulesGroup, rulesFrom, rulesGroup } from './prop-rules';
import { todayIso } from '../../finance/finance-api.service';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

type Panel = 'details' | 'rules' | 'phase' | 'status' | 'reset';

const STATUSES: PropAccountStatus[] = ['Active', 'Pending', 'Breached', 'Closed'];

// One account: where it stands, the rules that apply, and every change that got it here
@Component({
  selector: 'app-prop-account-detail',
  standalone: true,
  imports: [NgTemplateOutlet, ReactiveFormsModule, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent, PropRulesFieldsComponent, PropRulesSummaryComponent],
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
  `]
})
export class PropAccountDetailComponent {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);
  private id = Number(inject(ActivatedRoute).snapshot.paramMap.get('id'));

  readonly statusTags = STATUS_TAGS;
  readonly eventLabels = EVENT_LABELS;
  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly error = signal('');
  readonly account = signal<PropAccount | null>(null);
  readonly panel = signal<Panel | null>(null);
  readonly busy = signal(false);
  readonly formError = signal('');

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
