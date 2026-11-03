import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { OverviewComponent, attentionItems } from './overview.component';
import { Account, Allocation, Balances, BudgetMonth, ContinuityReport, Fund, InvestmentAccount, Obligation, Payout, PlannedPurchase } from '../finance/finance.models';
import { BehaviourSummary } from '../trading/models/analytics.model';
import { environment } from '../../../environments/environment';

const api = environment.apiUrl;

function account(overrides: Partial<Account>): Account {
  return {
    id: 1, name: 'Operating', type: 'Bank', currency: 'ZAR', ownership: 'Company', status: 'Active',
    balance: { amount: 0, currency: 'ZAR' }, funds: [], createdAt: '2026-10-27T00:00:00Z', ...overrides
  };
}

function fund(id: number, name: string, balances: Fund['balances'] = []): Fund {
  return { id, name, isSystem: id === 1, balances };
}

const noBehaviour: BehaviourSummary = {
  alerts: [], revengeTradingCount: 0, overtradingDayCount: 0, maxConsecutiveLosses: 0,
  strategyDeviationCount: 0, deviationPnLImpact: 0
};

const balances: Balances = {
  accounts: [account({ balance: { amount: 16250.5, currency: 'ZAR' } })],
  funds: [fund(1, 'Unallocated', [{ amount: 13250.5, currency: 'ZAR' }]), fund(3, 'Business Continuity', [{ amount: 3000, currency: 'ZAR' }])],
  cashTotals: [{ amount: 350, currency: 'USD' }, { amount: 16250.5, currency: 'ZAR' }]
};

const zar = (amount: number) => ({ amount, currency: 'ZAR' });
const continuity: ContinuityReport = {
  settings: { continuityFundId: 3, continuityFundName: 'Business Continuity', months: 24, currency: 'ZAR', monthlyEssentialExpense: 10000 },
  cash: zar(70000), target: zar(240000), coverageMonths: 7, shortfall: zar(170000), belowTarget: true,
  earmarked: zar(30000), separated: zar(40000), holdings: [], notCounted: []
};

describe('attentionItems', () => {
  it('warns about a budget exceeded this month, and leaves the others alone', () => {
    const line = (category: string, actual: number, exceeded: boolean) => ({
      id: 1, month: '2026-11', category, budget: zar(8000), actual: zar(actual), remaining: zar(8000 - actual), usedPercent: 0, exceeded, transactions: 1
    });
    const budgets: BudgetMonth = { month: '2026-11', from: '2026-11-01', to: '2026-11-30', lines: [line('Rent', 8500, true), line('Fibre', 100, false)], unbudgeted: [], totals: [] };
    const items = attentionItems(null, null, [], [], [], null, null, budgets);

    expect(items.map(i => [i.tone, i.title])).toEqual([['warning', 'Rent is over budget']]);
    expect(items[0].detail).toBe('ZAR 8,500.00 spent of ZAR 8,000.00 in 2026-11; ZAR 500.00 over.');
  });

  it('warns about a committed purchase the fund cannot carry, notes a proposed one, and flags a reversed expense', () => {
    const purchase = (name: string, status: PlannedPurchase['status'], verdict: 'Affordable' | 'BreachesMinimum' | 'NotEnoughMoney' | null, reversed = false): PlannedPurchase => ({
      id: 1, name, amount: zar(16000), fundId: 2, fundName: 'Operating Capital', status, expenseReversed: reversed, createdAt: '',
      affordability: verdict && { verdict, available: zar(20000), committedElsewhere: zar(0), purchase: zar(16000), after: zar(4000), shortfall: zar(1000), obligationsDue: zar(0) }
    });
    const items = attentionItems(null, null, [], [], [], null, null, null, [
      purchase('Server', 'Committed', 'BreachesMinimum'), purchase('Desk', 'Proposed', 'NotEnoughMoney'), purchase('Laptop', 'Proposed', 'Affordable'), purchase('Chair', 'Purchased', null, true)
    ]);

    expect(items.map(i => [i.tone, i.title])).toEqual([
      ['warning', 'Server would breach the Operating Capital minimum'],
      ['info', 'Operating Capital cannot pay for Desk'],
      ['warning', 'The expense for Chair was reversed']
    ]);
    expect(items[0].detail).toBe('ZAR 16,000.00, committed, would leave ZAR 4,000.00; ZAR 1,000.00 short.');
  });

  it('asks for a valuation of a brokerage account never valued, or moved since its valuation', () => {
    const inv = (overrides: Partial<InvestmentAccount>): InvestmentAccount => ({
      accountId: 1, accountName: 'Broker', currency: 'ZAR', ownership: 'Company', status: 'Active', asOf: '2026-10-25',
      netContributions: zar(1000), recordedIncome: zar(0), recordedCosts: zar(0), recordedBalance: zar(1000),
      transactionsSinceValuation: 0, movedSinceValuation: zar(0), currentRecordedBalance: zar(1000), ...overrides
    });
    const valued = { id: 1, accountId: 1, valuedOn: '2026-10-25', value: zar(1100), recordedAt: '', recordedBalance: zar(1000), unrealised: zar(100) };
    const accounts = [
      inv({ accountName: 'Never valued' }),
      inv({ accountId: 2, accountName: 'Moved', latest: valued, transactionsSinceValuation: 2, movedSinceValuation: zar(500) }),
      inv({ accountId: 3, accountName: 'Up to date', latest: valued }),
      inv({ accountId: 4, accountName: 'Empty', currentRecordedBalance: zar(0) }),
      inv({ accountId: 5, accountName: 'Closed', status: 'Closed' })
    ];
    const items = attentionItems(null, null, [], [], [], null, { accounts, totals: [] });

    expect(items.map(i => [i.tone, i.title])).toEqual([['info', 'Never valued is not valued'], ['info', 'Moved moved since its valuation']]);
    expect(items[1].detail).toBe('2 transactions after 2026-10-25 are not in its unrealised gain or loss.');
  });

  it('warns when continuity is below its target, and asks for the essential expense when it cannot be measured', () => {
    const below = attentionItems(null, null, [], [], [], continuity);
    expect(below.map(i => [i.tone, i.title])).toEqual([['warning', 'Continuity covers 7.0 of 24 months']]);
    expect(below[0].detail).toBe('ZAR 70,000.00 counted against a target of ZAR 240,000.00; ZAR 170,000.00 short.');

    const met = { ...continuity, cash: zar(250000), coverageMonths: 25, shortfall: zar(0), belowTarget: false };
    expect(attentionItems(null, null, [], [], [], met)).toEqual([]);

    const unmeasured: ContinuityReport = {
      ...continuity, settings: { ...continuity.settings, monthlyEssentialExpense: null }, target: null, coverageMonths: null, shortfall: null, belowTarget: false
    };
    expect(attentionItems(null, null, [], [], [], unmeasured).map(i => [i.tone, i.title])).toEqual([['info', 'Continuity is not measured']]);
  });

  it('asks for accounts when none are set up', () => {
    const items = attentionItems({ accounts: [], funds: [fund(1, 'Unallocated')], cashTotals: [] }, null);

    expect(items.map(i => i.title)).toEqual(['No accounts set up']);
  });

  it('flags unallocated money in every currency, with a link to earmark it', () => {
    const items = attentionItems({
      ...balances,
      funds: [fund(1, 'Unallocated', [{ amount: 350, currency: 'USD' }, { amount: 13250.5, currency: 'ZAR' }])]
    }, noBehaviour);

    const item = items.find(i => i.title === 'Money not assigned to a fund')!;
    expect(item.detail).toContain('USD 350.00 and ZAR 13,250.50');
    expect(item.queryParams).toEqual({ type: 'reallocation' });
  });

  it('flags overdue obligations, those due within 14 days, and payments whose expense was reversed', () => {
    const ob = (id: number, name: string, standing: Obligation['standing'], reversed = false): Obligation => ({
      id, name, amount: { amount: 8500, currency: 'ZAR' }, recurrence: 'Monthly', firstDueDate: '2026-10-01', dueDate: '2026-10-25',
      status: 'Active', standing, daysUntilDue: -4, createdAt: '',
      payments: reversed ? [{ id: 1, transactionId: 9, dueDate: '2026-09-25', paidOn: '2026-09-24', amount: { amount: 8500, currency: 'ZAR' }, reversed: true }] : []
    });
    const items = attentionItems(null, null, [], [], [ob(1, 'Rent', 'Overdue'), ob(2, 'Insurance', 'DueSoon'), ob(3, 'Fibre', 'DueSoon'), ob(4, 'Software', 'Upcoming', true)]);

    expect(items.map(i => i.title)).toEqual(['Rent is overdue', '2 obligations due within 14 days', 'A payment of Software was reversed']);
    expect(items[0].tone).toBe('negative');
    expect(items[1].detail).toContain('Insurance ZAR 8,500.00 on 2026-10-25');
  });

  it('flags a reversed payment on a paid one-off and opens the closed ones, but not on a cancelled obligation', () => {
    const closed = (name: string, status: Obligation['status'], standing: Obligation['standing']): Obligation => ({
      id: 1, name, amount: { amount: 900, currency: 'ZAR' }, recurrence: 'None', firstDueDate: '2026-10-01', dueDate: '2026-10-01',
      status, standing, daysUntilDue: -30, createdAt: '',
      payments: [{ id: 1, transactionId: 9, dueDate: '2026-10-01', paidOn: '2026-10-01', amount: { amount: 900, currency: 'ZAR' }, reversed: true }]
    });
    const items = attentionItems(null, null, [], [], [closed('Licence', 'Completed', 'Completed'), closed('Old lease', 'Cancelled', 'Cancelled')]);

    expect(items.map(i => i.title)).toEqual(['A payment of Licence was reversed']);
    expect(items[0].queryParams).toEqual({ closed: 'true' });
  });

  it('flags a fund below its minimum, but leaves a fund below zero to its own warning', () => {
    const withMinimum = (id: number, name: string, amount: number): Fund =>
      ({ ...fund(id, name, amount === 0 ? [] : [{ amount, currency: 'ZAR' }]), minimum: { amount: 20000, currency: 'ZAR' } });
    const items = attentionItems({
      accounts: [account({})], cashTotals: [],
      funds: [withMinimum(2, 'Operating Capital', 15000), withMinimum(4, 'Wealth Fund', 0), withMinimum(5, 'Owner\'s Distribution', -10), { ...withMinimum(6, 'Healthy', 25000) }]
    }, null);

    expect(items.filter(i => i.title.endsWith('below its minimum')).map(i => i.title)).toEqual(['Operating Capital is below its minimum', 'Wealth Fund is below its minimum']);
    expect(items.find(i => i.title === 'Operating Capital is below its minimum')!.detail).toBe('ZAR 15,000.00 against a minimum of ZAR 20,000.00.');
    expect(items.some(i => i.title === 'Owner\'s Distribution is below zero')).toBe(true);
  });

  it('flags a fund below zero in each currency where it is, with a link to its transactions', () => {
    const items = attentionItems({
      accounts: [account({})],
      funds: [fund(2, 'Operating Capital', [{ amount: -120, currency: 'ZAR' }, { amount: 50, currency: 'USD' }]), fund(3, 'Wealth Fund', [{ amount: 10, currency: 'ZAR' }])],
      cashTotals: []
    }, null);

    const below = items.filter(i => i.title.endsWith('is below zero'));
    expect(below.map(i => i.title)).toEqual(['Operating Capital is below zero']);
    expect(below[0].detail).toContain('ZAR -120.00');
    expect(below[0].detail).not.toContain('USD');
    expect(below[0].queryParams).toEqual({ fundId: 2 });
  });

  it('flags negative balances and unstated ownership on active accounts only', () => {
    const items = attentionItems({
      accounts: [
        account({ id: 2, name: 'Reserve', balance: { amount: -20, currency: 'ZAR' } }),
        account({ id: 3, name: 'Wallet', ownership: 'Unspecified' }),
        account({ id: 4, name: 'Old', ownership: 'Unspecified', status: 'Closed', balance: { amount: -5, currency: 'ZAR' } })
      ],
      funds: [fund(1, 'Unallocated')],
      cashTotals: []
    }, noBehaviour);

    expect(items.map(i => i.title)).toEqual(['Reserve is below zero', 'Ownership not stated']);
    expect(items[1].detail).toContain('Wallet');
    expect(items[1].detail).not.toContain('Old');
  });

  it('counts only critical behaviour alerts', () => {
    const behaviour: BehaviourSummary = {
      ...noBehaviour,
      alerts: [
        { alertType: 'Overtrading', severity: 'Critical', description: '', tradeIds: [], detectedOnDate: '2026-10-08' },
        { alertType: 'RevengeTrading', severity: 'Warning', description: '', tradeIds: [], detectedOnDate: '2026-10-08' }
      ]
    };

    expect(attentionItems(null, behaviour).map(i => i.title)).toEqual(['1 critical trading behaviour alert']);
  });
});

describe('attentionItems for payouts and allocations', () => {
  const payout = (overrides: Partial<Payout>): Payout => ({
    id: 4, source: 'Prop firm', status: 'Received', requested: { amount: 1350, currency: 'USD' }, requestedOn: '2026-10-27',
    createdAt: '2026-10-27T00:00:00Z', credited: { amount: 1350, currency: 'USD' }, receivedOn: '2026-10-27',
    receivedIntoAccountName: 'Payouts USD', ...overrides
  });
  const allocation = (overrides: Partial<Allocation>): Allocation => ({
    id: 9, payoutId: 4, payoutSource: 'Prop firm', policyId: 1, policyVersion: 1, sourceAccountId: 2, sourceAccountName: 'Payouts USD',
    status: 'PartiallySettled', total: { amount: 1350, currency: 'USD' }, settled: { amount: 675, currency: 'USD' },
    outstanding: { amount: 675, currency: 'USD' }, confirmedAt: '2026-10-27T00:00:00Z', lines: [], ...overrides
  });

  it('asks to allocate a received payout and to settle an open allocation', () => {
    const titles = attentionItems(null, null, [payout({})], [allocation({})]).map(i => i.title);

    expect(titles).toEqual(['Payout #4 received but not allocated', 'Allocation #9 not fully settled']);
  });

  it('ignores allocated payouts, settled or cancelled allocations', () => {
    const items = attentionItems(null, null,
      [payout({ allocationId: 9, allocationStatus: 'Reconciled' })],
      [allocation({ status: 'Reconciled', outstanding: { amount: 0, currency: 'USD' } }), allocation({ id: 10, status: 'Cancelled' })]);

    expect(items).toEqual([]);
  });

  it('labels requested and approved payouts as on the way, not cash', () => {
    const items = attentionItems(null, null, [
      payout({ id: 1, status: 'Requested', credited: null }),
      payout({ id: 2, status: 'Approved', credited: null, approved: { amount: 600, currency: 'USD' } })
    ]);

    expect(items.map(i => i.title)).toEqual(['2 payouts on the way']);
    expect(items[0].detail).toBe('USD 1,950.00 requested or approved. Not cash until received.');
  });
});

describe('OverviewComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OverviewComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    });
    http = TestBed.inject(HttpTestingController);
  });

  // The period section loads its own figures once the finance position shows; tested in overview-period.spec.ts
  afterEach(() => {
    http.match(r => r.url === `${api}/overview`).filter(r => !r.cancelled)
      .forEach(r => r.flush({ from: '2026-10-01', to: '2026-10-29', currencies: [], months: [], monthsTruncated: false, payoutsReceived: [] }));
    http.verify();
  });

  async function render() {
    const fixture = TestBed.createComponent(OverviewComponent);
    await fixture.whenStable();
    return fixture;
  }

  function flushFinance() {
    http.expectOne(`${api}/balances`).flush(balances);
    http.expectOne(r => r.url === `${api}/transactions`).flush({ items: [], page: 1, pageSize: 6, totalCount: 0, totalPages: 0 });
    http.expectOne(`${api}/payouts`).flush([]);
    http.expectOne(`${api}/allocations`).flush([]);
    http.expectOne(r => r.url === `${api}/obligations` && r.params.get('includeClosed') === 'true').flush([]);
    http.expectOne(`${api}/continuity`).flush(continuity);
    http.expectOne(`${api}/investments`).flush({ accounts: [], totals: [] });
    http.expectOne(`${api}/budgets`).flush({ month: '2026-11', from: '2026-11-01', to: '2026-11-30', lines: [], unbudgeted: [], totals: [] });
    http.expectOne(r => r.url === `${api}/planned-purchases`).flush([]);
  }

  function flushTrading(totalTrades: number) {
    http.expectOne(`${api}/analytics/summary`).flush({
      totalTrades, winRate: 50, expectancy: 79.25, averageRR: 0.46, profitFactor: 1.87,
      totalPnL: 1585, maxDrawdown: 800, winCount: 10, lossCount: 9, breakevenCount: 1
    });
    http.expectOne(`${api}/analytics/behaviour`).flush(noBehaviour);
  }

  it('shows each currency separately and never adds them together', async () => {
    const fixture = await render();
    flushFinance();
    flushTrading(20);
    await fixture.whenStable();

    const strip = (fixture.nativeElement as HTMLElement).querySelector('.metric-strip')!.textContent!;
    expect(strip).toContain('USD 350.00');
    expect(strip).toContain('ZAR 16,250.50');
    expect(strip).not.toContain('16,600.50');
    const metrics = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.metric-strip .metric')].map(m => [...m.children].map(c => c.textContent!.trim()));
    expect(metrics).toContainEqual(['Continuity', '7.0 months', 'of 24 months covered']);
  });

  it('lists the latest payouts with their status, and says which are not cash yet', async () => {
    const fixture = await render();
    const payout = (id: number, status: Payout['status'], extra: Partial<Payout> = {}): Payout => ({
      id, source: 'Prop firm', status, requested: { amount: 1000, currency: 'USD' }, requestedOn: '2026-10-01', createdAt: '', ...extra
    });
    http.expectOne(`${api}/balances`).flush(balances);
    http.expectOne(r => r.url === `${api}/transactions`).flush({ items: [], page: 1, pageSize: 6, totalCount: 0, totalPages: 0 });
    http.expectOne(`${api}/payouts`).flush([
      payout(1, 'Received', { credited: { amount: 950, currency: 'USD' }, receivedOn: '2026-10-05', receivedIntoAccountName: 'Operating', allocationId: 3 }),
      payout(2, 'Approved', { approved: { amount: 800, currency: 'USD' } }),
      ...[3, 4, 5, 6].map(id => payout(id, 'Rejected', { rejectedOn: '2026-10-02' }))
    ]);
    http.expectOne(`${api}/allocations`).flush([]);
    http.expectOne(r => r.url === `${api}/obligations` && r.params.get('includeClosed') === 'true').flush([]);
    http.expectOne(`${api}/continuity`).flush(continuity);
    http.expectOne(`${api}/investments`).flush({ accounts: [], totals: [] });
    http.expectOne(`${api}/budgets`).flush({ month: '2026-11', from: '2026-11-01', to: '2026-11-30', lines: [], unbudgeted: [], totals: [] });
    http.expectOne(r => r.url === `${api}/planned-purchases`).flush([]);
    flushTrading(0);
    await fixture.whenStable();

    const rows = [...(fixture.nativeElement as HTMLElement).querySelectorAll('section[aria-labelledby="payouts-heading"] tbody tr')];
    expect(rows.map(r => r.querySelector('a')!.textContent)).toEqual(['#6 Prop firm', '#5 Prop firm', '#4 Prop firm', '#3 Prop firm', '#2 Prop firm']);   // newest five
    expect(rows[4].textContent).toContain('USD 800.00');
    expect(rows[4].textContent).toContain('not cash yet');
  });

  it('labels trading P&L as not business cash', async () => {
    const fixture = await render();
    flushFinance();
    flushTrading(20);
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('+1,585.00');
    expect(text).toContain('Trading results are not business cash');
  });

  it('keeps the trading summary when the finance data fails to load', async () => {
    const fixture = await render();
    http.expectOne(`${api}/balances`).flush(null, { status: 403, statusText: 'Forbidden' });
    // forkJoin abandons the other finance requests once the balances fail
    const others = http.match(r => [`${api}/transactions`, `${api}/payouts`, `${api}/allocations`, `${api}/obligations`, `${api}/continuity`, `${api}/investments`, `${api}/budgets`, `${api}/planned-purchases`].includes(r.url));
    expect(others.length).toBe(8);
    expect(others.every(r => r.cancelled)).toBe(true);
    flushTrading(0);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Could not load the financial position');
    expect(fixture.componentInstance.tradingState()).toBe('ready');
  });

  it('shows the net amount for external flows and the moved amount for internal ones', () => {
    const component = TestBed.createComponent(OverviewComponent).componentInstance;
    const entry = (accountId: number, fundId: number, amount: number, currency = 'ZAR') =>
      ({ accountId, accountName: '', fundId, fundName: '', amount, currency, role: 'Principal' as const });
    const base = { id: 1, date: '2026-10-27', recordedAt: '', description: '' };

    expect(component.headline({ ...base, type: 'Expense', entries: [entry(1, 1, -3000)] }))
      .toEqual([{ currency: 'ZAR', amount: -3000 }]);
    expect(component.headline({ ...base, type: 'Transfer', entries: [entry(1, 1, -2000), entry(2, 1, 2000)] }))
      .toEqual([{ currency: 'ZAR', amount: 2000 }]);
    expect(component.headline({ ...base, type: 'CurrencyExchange', entries: [entry(1, 1, -1000, 'USD'), entry(2, 1, 18250.5)] }))
      .toEqual([{ currency: 'USD', amount: -1000 }, { currency: 'ZAR', amount: 18250.5 }]);

    http.match(() => true);
  });
});
