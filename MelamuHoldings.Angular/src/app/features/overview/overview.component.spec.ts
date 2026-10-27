import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { OverviewComponent, attentionItems } from './overview.component';
import { Account, Balances, Fund } from '../finance/finance.models';
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

describe('OverviewComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OverviewComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render() {
    const fixture = TestBed.createComponent(OverviewComponent);
    await fixture.whenStable();
    return fixture;
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
    http.expectOne(`${api}/balances`).flush(balances);
    http.expectOne(r => r.url === `${api}/transactions`).flush({ items: [], page: 1, pageSize: 6, totalCount: 0, totalPages: 0 });
    flushTrading(20);
    await fixture.whenStable();

    const strip = (fixture.nativeElement as HTMLElement).querySelector('.metric-strip')!.textContent!;
    expect(strip).toContain('USD 350.00');
    expect(strip).toContain('ZAR 16,250.50');
    expect(strip).not.toContain('16,600.50');
  });

  it('labels trading P&L as not business cash', async () => {
    const fixture = await render();
    http.expectOne(`${api}/balances`).flush(balances);
    http.expectOne(r => r.url === `${api}/transactions`).flush({ items: [], page: 1, pageSize: 6, totalCount: 0, totalPages: 0 });
    flushTrading(20);
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('+1,585.00');
    expect(text).toContain('Trading results are not business cash');
  });

  it('keeps the trading summary when the finance data fails to load', async () => {
    const fixture = await render();
    http.expectOne(`${api}/balances`).flush(null, { status: 403, statusText: 'Forbidden' });
    // forkJoin abandons the ledger request once the balances fail
    expect(http.match(r => r.url === `${api}/transactions`).every(r => r.cancelled)).toBe(true);
    flushTrading(0);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Could not load the financial position');
    expect(fixture.componentInstance.tradingState()).toBe('ready');
  });

  it('shows the net amount for external flows and the moved amount for internal ones', () => {
    const component = TestBed.createComponent(OverviewComponent).componentInstance;
    const entry = (accountId: number, fundId: number, amount: number, currency = 'ZAR') =>
      ({ accountId, accountName: '', fundId, fundName: '', amount, currency });
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
