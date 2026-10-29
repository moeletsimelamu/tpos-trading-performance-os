import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { OverviewComponent, attentionItems } from './overview.component';
import { Account, Allocation, Balances, Fund, Payout } from '../finance/finance.models';
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

describe('attentionItems', () => {
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
    const others = http.match(r => [`${api}/transactions`, `${api}/payouts`, `${api}/allocations`].includes(r.url));
    expect(others.length).toBe(3);
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
