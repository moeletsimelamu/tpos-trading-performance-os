import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { OverviewPeriodComponent, periodPresets } from './overview-period.component';
import { Overview } from '../finance/finance.models';
import { environment } from '../../../environments/environment';

const api = environment.apiUrl;

const overview: Overview = {
  from: '2026-08-01', to: '2026-09-30', monthsTruncated: false,
  currencies: [{
    currency: 'USD', cashAtStart: 1000, netCashMovement: 848, cashAtEnd: 2348,
    openingBalances: { type: 'OpeningBalance', label: 'Opening balances recorded', amount: 500, transactions: 1 },
    movements: [
      { type: 'Income', label: 'Income received', amount: 1350, transactions: 1, gross: 1400, fees: -50 },
      { type: 'Expense', label: 'Expenses', amount: -340, transactions: 2 },
      { type: 'CurrencyExchange', label: 'Currency exchanges', amount: -102, transactions: 1 },
      { type: 'Transfer', label: 'Transfers', amount: -100, transactions: 2 },
      { type: 'Reversal', label: 'Reversals (corrections)', amount: 40, transactions: 1 }
    ]
  }],
  months: [
    { start: '2026-08-01', end: '2026-08-31', currency: 'USD', income: 1350, expenses: -300, netCashMovement: 950, transactions: 4 },
    { start: '2026-09-01', end: '2026-09-30', currency: 'USD', income: 0, expenses: -40, netCashMovement: -102, transactions: 3 }
  ],
  payoutsReceived: [{ currency: 'USD', credited: 1350, payouts: 1 }]
};

describe('Period presets', () => {
  it('start this month, last month, this quarter, the year and twelve months back from today', () => {
    const presets = periodPresets(new Date(2026, 9, 29));   // 29 October 2026
    expect(presets.map(p => [p.key, p.from, p.to])).toEqual([
      ['month', '2026-10-01', '2026-10-29'],
      ['last-month', '2026-09-01', '2026-09-30'],
      ['quarter', '2026-10-01', '2026-10-29'],
      ['year', '2026-01-01', '2026-10-29'],
      ['12-months', '2025-10-30', '2026-10-29']
    ]);
    expect(periodPresets(new Date(2026, 0, 15))[1]).toEqual(expect.objectContaining({ from: '2025-12-01', to: '2025-12-31' }));
  });
});

describe('OverviewPeriodComponent', () => {
  let http: HttpTestingController;
  let query: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  afterEach(() => http.verify());

  async function render(params: Record<string, string> = { from: '2026-08-01', to: '2026-09-30' }) {
    query = new BehaviorSubject(convertToParamMap(params));
    TestBed.configureTestingModule({
      imports: [OverviewPeriodComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: ActivatedRoute, useValue: { queryParamMap: query.asObservable() } }]
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(OverviewPeriodComponent);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('asks for the period in the address and shows how cash moved, each line linked to its ledger list', async () => {
    const { fixture, el } = await render();
    const req = http.expectOne(r => r.url === `${api}/overview`);
    expect([req.request.params.get('from'), req.request.params.get('to')]).toEqual(['2026-08-01', '2026-09-30']);
    req.flush(overview);
    await fixture.whenStable();

    const rows = [...el.querySelectorAll('section[aria-label="Cash movement in USD"] tbody tr')]
      .map(r => [r.querySelector('th')!.textContent!.trim().split('  ')[0].trim(), r.querySelector('.num')!.textContent!.trim()]);
    expect(rows.map(r => r[1])).toEqual(['USD 1,000.00', 'USD +1,350.00', 'USD -340.00', 'USD -102.00', 'USD -100.00', 'USD +40.00', 'USD +848.00', 'USD +500.00', 'USD 2,348.00']);
    expect(el.textContent).toContain('USD 1,400.00 gross, less USD 50.00 in fees deducted');
    expect(el.textContent).toContain('Net cash movement is what came in less what went out, not profit.');

    const expenses = [...el.querySelectorAll('a')].find(a => a.textContent === '2 transactions' && a.getAttribute('href')!.includes('type=Expense'))!;
    expect(expenses.getAttribute('href')).toBe('/finance/transactions?from=2026-08-01&to=2026-09-30&type=Expense&cash=true');
    // By month, with each month's own dates in the link
    expect(el.textContent).toContain('By month');
    const august = [...el.querySelectorAll('a')].find(a => a.getAttribute('href')!.includes('type=Income') && a.getAttribute('href')!.includes('to=2026-08-31'));
    expect(august?.textContent).toBe('USD 1,350.00');
    expect(el.querySelector('.metric-strip')!.textContent).toContain('USD 1,350.00');
  });

  it('defaults to this month, and a preset puts its period in the address', async () => {
    const { fixture, el } = await render({});
    const presets = periodPresets(new Date());
    const req = http.expectOne(r => r.url === `${api}/overview`);
    expect(req.request.params.get('from')).toBe(presets[0].from);
    req.flush({ ...overview, currencies: [], months: [], payoutsReceived: [] });
    await fixture.whenStable();
    expect(el.textContent).toContain('No money recorded in bank or cash accounts');

    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    ([...el.querySelectorAll('.presets button')].find(b => b.textContent === 'Last month') as HTMLButtonElement).click();
    expect(navigate).toHaveBeenCalledWith([], expect.objectContaining({ queryParams: { from: presets[1].from, to: presets[1].to } }));

    // A changed address reloads; a reversed or malformed one is ignored
    query.next(convertToParamMap({ from: presets[1].from, to: presets[1].to }));
    expect(http.expectOne(r => r.url === `${api}/overview`).request.params.get('to')).toBe(presets[1].to);
    query.next(convertToParamMap({ from: '2026-09-30', to: '2026-09-01' }));
    expect(http.expectOne(r => r.url === `${api}/overview`).request.params.get('from')).toBe(presets[1].from);
  });
});
