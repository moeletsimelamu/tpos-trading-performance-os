import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { PropAccountsComponent } from './prop-accounts.component';
import { PropAccountDetailComponent } from './prop-account-detail.component';
import { PropAccount, PropAccountFigures, PropAccountRules } from '../models/prop-account.model';
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
    standing: { figures: figures(), linkedTrades: 0, openTrades: 0, tradesBeforeRun: 0 }, balances: [],
    createdAt: '2026-10-01T00:00:00Z', ...overrides
  };
}

function figures(overrides: Partial<PropAccountFigures> = {}): PropAccountFigures {
  return {
    balance: 102000, profit: 2000, tradesCounted: 3, tradingDays: 2,
    drawdown: { model: 'Static', limit: 10000, level: 90000, room: 12000, standing: 'Clear' },
    dailyLoss: { limit: 5000, latestDay: '2026-10-05', latestDayPnL: 1000, worstDay: '2026-10-04', worstLoss: 1500, daysPastLimit: [], standing: 'Clear' },
    target: { target: 8000, profit: 2000, remaining: 6000, reached: false },
    minimumDays: { required: 4, done: 2, remaining: 2, reached: false },
    targetMet: false, ...overrides
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
    expect(first).toContain('USD 102,000.00');
    expect(first).toContain('of USD 100,000.00, since 2026-10-01');
    expect(first).toContain('USD 12,000.00');
    expect(first).toContain('USD 2,000.00 of USD 8,000.00');
    expect(first).not.toContain('Near the limit');
    expect(el.textContent).toContain("The firm's own figures decide.");
    expect(el.querySelector('a.name')!.getAttribute('href')).toBe('/trading/prop-accounts/5');
  });

  it('flags a rule near or at its limit in the list', async () => {
    const { el } = await render([account({ standing: { figures: figures({
      drawdown: { model: 'Static', limit: 10000, level: 90000, room: 1500, standing: 'Near' },
      dailyLoss: { limit: 5000, daysPastLimit: ['2026-10-04'], standing: 'Reached' }, target: null
    }), linkedTrades: 1, openTrades: 0, tradesBeforeRun: 0 } })]);
    const row = el.querySelector('tbody tr')!;
    expect([...row.querySelectorAll('.tag-warning')].map(t => t.textContent)).toEqual(['Near the limit']);
    expect([...row.querySelectorAll('.tag-negative')].map(t => t.textContent)).toEqual(['At the limit']);
    expect(row.textContent).toContain('Not set');
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
    expect(buttons(el)).toEqual(['Edit details', 'Change rules', 'Change phase', 'Record a reset', 'Enter a balance', 'Change status']);
    TestBed.resetTestingModule();

    const breached = await render(account({ status: 'Breached' }));
    expect(buttons(breached.el)).toEqual(['Edit details', 'Change rules', 'Record a reset', 'Enter a balance', 'Change status']);
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

  it('shows where the account stands, labelled as not the firm\'s figures, with what is left out', async () => {
    const { el } = await render(account({ standing: { figures: figures(), linkedTrades: 6, openTrades: 1, tradesBeforeRun: 2 } }));
    const section = el.querySelector('section[aria-label="Where it stands"]')!;
    const text = section.textContent!.replace(/\s+/g, ' ');
    expect(text).toContain('Worked out from 3 closed trades');
    expect(text).toContain("These are not the firm's figures");
    expect(text).toContain('USD 102,000.00');
    expect(text).toContain('USD +2,000.00');
    expect(text).toContain('USD 12,000.00');
    expect(text).toContain('Breached at USD 90,000.00, with a limit of USD 10,000.00');
    expect(text).toContain('Worst USD 1,500.00 on 2026-10-04');
    expect(text).toContain('USD 2,000.00 of USD 8,000.00');
    expect(text).toContain('USD 6,000.00 to go');
    expect(text).toContain('2 of 4');
    expect(text).toContain('1 open trade is not counted until closed.');
    expect(text).toContain('2 trades closed before 2026-10-01 belong to an earlier phase or reset and are not counted.');
    expect(text).not.toContain('Intraday trailing');
    const links = [...section.querySelectorAll('a')];
    expect(links[0].textContent).toContain('View its 6 trades');
    expect(links.map(a => a.getAttribute('href'))).toEqual(['/trading/trades?account=100k%20attempt%201', '/trading/performance?account=100k%20attempt%201']);
  });

  it('says when closed trades cannot show the limit, when nothing is set and when no trade is linked', async () => {
    const { el } = await render(account({ standing: { figures: figures({
      drawdown: { model: 'TrailingIntraday', limit: 10000, level: 95000, room: 7000, standing: 'Clear' }, dailyLoss: null, target: null, minimumDays: null
    }), linkedTrades: 0, openTrades: 0, tradesBeforeRun: 0 } }));
    const text = el.querySelector('section[aria-label="Where it stands"]')!.textContent!.replace(/\s+/g, ' ');
    expect(text).toContain('Intraday trailing follows unrealised profit');
    expect(text).toContain('No limit set');
    expect(text).toContain('No minimum set');
    expect(text).toContain('No trades are linked yet.');
  });

  it('enters a balance and lists it with what the trades came to, and removes it after asking', async () => {
    const entry = { id: 3, asOf: '2026-11-01', balance: { amount: 101500, currency: 'USD' }, drawdownLevel: { amount: 91000, currency: 'USD' }, note: 'After payout', expected: 102000, difference: -500, recordedAt: '2026-11-01T00:00:00Z' };
    const { fixture, c, el } = await render(account());
    c.open('balance');
    await fixture.whenStable();
    c.balanceForm.patchValue({ asOf: '2026-11-01', balance: 101500, drawdownLevel: 91000, note: ' After payout ' });
    c.saveBalance(c.account()!);
    const req = http.expectOne(`${api}/prop-accounts/5/balances`);
    expect(req.request.body).toEqual({ asOf: '2026-11-01', balance: 101500, drawdownLevel: 91000, note: 'After payout' });
    req.flush(account({ balances: [entry] }));
    await fixture.whenStable();

    const row = el.querySelector('section[aria-label="Balances entered"] tbody tr')!;
    const text = row.textContent!.replace(/\s+/g, ' ');
    expect(text).toContain('USD 101,500.00');
    expect(text).toContain('USD 91,000.00');
    expect(text).toContain('USD 102,000.00');
    expect(text).toContain('USD -500.00');

    c.confirmingRemove.set(3);
    await fixture.whenStable();
    (row.querySelector('.btn-danger') as HTMLButtonElement).click();
    const del = http.expectOne(`${api}/prop-accounts/5/balances/3`);
    expect(del.request.method).toBe('DELETE');
    del.flush(account());
    await fixture.whenStable();
    expect(el.querySelector('section[aria-label="Balances entered"]')).toBeNull();
  });

  it('marks a balance from before the current run as not applying', async () => {
    const { el } = await render(account({ balances: [{ id: 3, asOf: '2026-10-01', balance: { amount: 1, currency: 'USD' }, recordedAt: '2026-10-01T00:00:00Z' }] }));
    expect(el.querySelector('section[aria-label="Balances entered"]')!.textContent).toContain('Before this phase or reset');
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
