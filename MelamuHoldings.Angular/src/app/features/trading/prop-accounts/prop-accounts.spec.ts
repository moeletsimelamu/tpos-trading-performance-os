import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { PropAccountsComponent } from './prop-accounts.component';
import { PropAccountDetailComponent } from './prop-account-detail.component';
import { PropAccount, PropAccountRules } from '../models/prop-account.model';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;

const RULES: PropAccountRules = {
  drawdownModel: 'Static', maxDrawdown: 10000, trailingLocksAtStart: false, dailyLossLimit: 5000, profitTarget: 8000, minimumTradingDays: 4,
  payoutSplitPercent: 80, purchaseFee: 500, resetFee: null, monthlyFee: null, notes: null
};

function account(overrides: Partial<PropAccount> = {}): PropAccount {
  const set = { version: 1, phase: 'Evaluation' as const, effectiveFrom: '2026-10-01', rules: RULES, createdAt: '2026-10-01T00:00:00Z' };
  return {
    id: 5, firm: 'Test Firm', nickname: '100k attempt 1', accountSize: { amount: 100000, currency: 'USD' }, phase: 'Evaluation', status: 'Active', login: 'L-1',
    openedOn: '2026-10-01', runStartedOn: '2026-10-01', startingBalance: { amount: 100000, currency: 'USD' }, rules: set, ruleHistory: [set],
    history: [{ id: 1, kind: 'Opened', occurredOn: '2026-10-01', phase: 'Evaluation', status: 'Active', login: 'L-1', startingBalance: { amount: 100000, currency: 'USD' }, recordedAt: '2026-10-01T00:00:00Z' }],
    createdAt: '2026-10-01T00:00:00Z', ...overrides
  };
}

describe('PropAccountsComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  async function render(list: PropAccount[]) {
    TestBed.configureTestingModule({ imports: [PropAccountsComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PropAccountsComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/prop-accounts`).flush(list);
    http.expectOne(`${api}/prop-accounts/currencies`).flush(['USD', 'ZAR']);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement, c: fixture.componentInstance };
  }

  it('counts accounts by where they stand and lists each with its rules at a glance', async () => {
    const { el } = await render([
      account(),
      account({ id: 6, nickname: 'Funded one', phase: 'Funded' }),
      account({ id: 7, nickname: 'Waiting', status: 'Pending' }),
      account({ id: 8, nickname: 'Blown', status: 'Breached' })
    ]);

    const metrics = [...el.querySelectorAll('.metric-strip .metric')].map(m => [...m.children].map(x => x.textContent!.trim()));
    expect(metrics).toEqual([['Active', '2'], ['Funded and active', '1'], ['Waiting on the firm', '1'], ['Breached', '1']]);
    const first = el.querySelector('tbody tr')!.textContent!.replace(/\s+/g, ' ');
    expect(first).toContain('100k attempt 1');
    expect(first).toContain('USD 100,000.00');
    expect(first).toContain('Static (from the starting balance)');
    expect(first).toContain('USD 8,000.00');
    expect(el.querySelector('a.name')!.getAttribute('href')).toBe('/trading/prop-accounts/5');
  });

  it('says what to do when there are no accounts, and asks for closed ones on request', async () => {
    const { fixture, el, c } = await render([]);
    expect(el.textContent).toContain('No prop firm accounts recorded.');

    c.toggleClosed();
    const req = http.expectOne(r => r.url === `${api}/prop-accounts`);
    expect(req.request.params.get('includeClosed')).toBe('true');
    req.flush([account({ status: 'Closed' })]);
    await fixture.whenStable();
    expect(el.textContent).toContain('100k attempt 1');
  });

  it('sends a new account with empty limits as empty and opens it', async () => {
    const { c } = await render([]);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    c.startCreate();
    c.form.patchValue({ firm: ' Test Firm ', nickname: ' Attempt 2 ', accountSize: 50000, currency: 'ZAR', phase: 'Verification', openedOn: '2026-11-01', login: ' ' });
    c.form.controls.rules.patchValue({ drawdownModel: 'TrailingEndOfDay', maxDrawdown: 2500, trailingLocksAtStart: true, notes: '  ' });
    c.save();

    const req = http.expectOne(`${api}/prop-accounts`);
    expect(req.request.body).toEqual({
      firm: 'Test Firm', nickname: 'Attempt 2', accountSize: 50000, currency: 'ZAR', phase: 'Verification', openedOn: '2026-11-01', login: null, notes: null,
      rules: {
        drawdownModel: 'TrailingEndOfDay', maxDrawdown: 2500, trailingLocksAtStart: true, dailyLossLimit: null, profitTarget: null, minimumTradingDays: null,
        payoutSplitPercent: null, purchaseFee: null, resetFee: null, monthlyFee: null, notes: null
      }
    });
    req.flush(account({ id: 9 }));
    expect(navigate).toHaveBeenCalledWith(['/trading/prop-accounts', 9]);
  });

  it('does not send a limit for a model without one, or a lock for a model that does not trail', async () => {
    const { c } = await render([]);
    c.startCreate();
    c.form.patchValue({ firm: 'F', nickname: 'N', accountSize: 100 });
    c.form.controls.rules.patchValue({ drawdownModel: 'None', maxDrawdown: 50, trailingLocksAtStart: true });
    c.save();

    const req = http.expectOne(`${api}/prop-accounts`);
    expect(req.request.body.rules.maxDrawdown).toBeNull();
    expect(req.request.body.rules.trailingLocksAtStart).toBe(false);
    req.flush(account(), { status: 400, statusText: 'Bad Request' });
    expect(c.formError()).toContain('Not saved');
  });
});

describe('PropAccountDetailComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  async function render(a: PropAccount) {
    TestBed.configureTestingModule({ imports: [PropAccountDetailComponent], providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '5' }) } } }
    ] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PropAccountDetailComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/prop-accounts/5`).flush(a);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement, c: fixture.componentInstance };
  }

  const buttons = (el: HTMLElement) => [...el.querySelectorAll('.actions button')].map(b => b.textContent!.trim());

  it('shows where the account stands, its rules and its history', async () => {
    const { el } = await render(account());
    const text = el.textContent!.replace(/\s+/g, ' ');
    expect(text).toContain('Version 1, from 2026-10-01');
    expect(text).toContain('Static (from the starting balance) · USD 10,000.00');
    expect(text).toContain('Purchase USD 500.00');
    expect(text).toContain('Login L-1');
    expect(el.querySelector('section[aria-label="History"] tbody tr')!.textContent).toContain('Opened');
    expect(el.querySelector('section[aria-label="Earlier rule versions"]')).toBeNull();
  });

  it('offers only what the status allows', async () => {
    const { el } = await render(account());
    expect(buttons(el)).toEqual(['Edit details', 'Change rules', 'Change phase', 'Record a reset', 'Change status']);
    TestBed.resetTestingModule();

    const breached = await render(account({ status: 'Breached' }));
    expect(buttons(breached.el)).toEqual(['Edit details', 'Change rules', 'Record a reset', 'Change status']);
    TestBed.resetTestingModule();

    const closed = await render(account({ status: 'Closed' }));
    expect(buttons(closed.el)).toEqual(['Change status']);
  });

  it('starts a phase change at the next phase with the current rules and sends a new version', async () => {
    const { fixture, c, el } = await render(account());
    c.open('phase');
    await fixture.whenStable();
    expect(c.phaseForm.controls.phase.value).toBe('Verification');
    expect(c.phaseForm.controls.startingBalance.value).toBe(100000);
    expect(c.phaseForm.controls.rules.controls.maxDrawdown.value).toBe(10000);

    c.phaseForm.patchValue({ phase: 'Funded', date: '2026-11-02', login: 'L-2' });
    c.phaseForm.controls.rules.patchValue({ profitTarget: null });
    c.savePhase(c.account()!);

    const req = http.expectOne(`${api}/prop-accounts/5/phase`);
    expect(req.request.body).toEqual({ phase: 'Funded', date: '2026-11-02', startingBalance: 100000, login: 'L-2', reason: null, rules: { ...RULES, profitTarget: null } });
    req.flush(account({ phase: 'Funded', runStartedOn: '2026-11-02' }));
    await fixture.whenStable();
    expect(c.panel()).toBeNull();
    expect(el.textContent).toContain('Since 2026-11-02');
  });

  it('asks for a reason when going back a phase, when a breach is recorded and when leaving one', async () => {
    const { fixture, c } = await render(account({ phase: 'Funded' }));
    c.open('phase');
    await fixture.whenStable();
    c.phaseForm.controls.phase.setValue('Evaluation');
    expect(c.goingBack()).toBe(true);
    expect(c.otherPhases()).toEqual(['Evaluation', 'Verification']);

    c.open('status');
    c.statusForm.controls.status.setValue('Breached');
    expect(c.reasonRequired()).toBe(true);
    expect(c.reasonLabel()).toBe('Which rule was breached');
    c.statusForm.controls.status.setValue('Closed');
    expect(c.reasonRequired()).toBe(false);
    c.account.set(account({ status: 'Breached' }));
    c.statusForm.controls.status.setValue('Active');
    expect(c.reasonRequired()).toBe(true);
    expect(c.reasonLabel()).toBe('Reason (required)');
  });

  it('records a reset and shows the reason when the API refuses a change', async () => {
    const { fixture, c, el } = await render(account({ status: 'Breached' }));
    c.open('reset');
    await fixture.whenStable();
    c.resetForm.patchValue({ date: '2026-11-02', note: ' Paid reset ' });
    c.saveReset(c.account()!);
    const req = http.expectOne(`${api}/prop-accounts/5/reset`);
    expect(req.request.body).toEqual({ date: '2026-11-02', startingBalance: 100000, note: 'Paid reset' });
    req.flush({ title: 'The reset date cannot be before the last change (2026-11-03).' }, { status: 422, statusText: 'Unprocessable' });
    await fixture.whenStable();

    expect(c.panel()).toBe('reset');
    expect(el.querySelector('[role="alert"]')!.textContent).toContain('cannot be before the last change');
  });

  it('shows earlier rule versions once there are any', async () => {
    const current = { version: 2, phase: 'Funded' as const, effectiveFrom: '2026-11-01', rules: { ...RULES, profitTarget: null }, createdAt: '2026-11-01T00:00:00Z' };
    const first = { version: 1, phase: 'Evaluation' as const, effectiveFrom: '2026-10-01', rules: RULES, createdAt: '2026-10-01T00:00:00Z' };
    const { el } = await render(account({ rules: current, ruleHistory: [current, first] }));
    const earlier = el.querySelector('section[aria-label="Earlier rule versions"]')!;
    expect(earlier.textContent).toContain('Version 1 · Evaluation · from 2026-10-01');
    expect(earlier.textContent).toContain('USD 8,000.00');
  });
});
