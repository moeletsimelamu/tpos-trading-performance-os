import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ContinuityComponent } from './continuity.component';
import { ContinuityReport, Fund } from '../finance.models';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;
const zar = (amount: number) => ({ amount, currency: 'ZAR' });
const usd = (amount: number) => ({ amount, currency: 'USD' });

const funds: Fund[] = [
  { id: 1, name: 'Unallocated', isSystem: true, balances: [] },
  { id: 3, name: 'Business Continuity', isSystem: false, balances: [] },
  { id: 6, name: 'Trading Restoration', isSystem: false, balances: [], target: usd(5000) }
];

function report(overrides: Partial<ContinuityReport> = {}): ContinuityReport {
  return {
    settings: {
      continuityFundId: 3, continuityFundName: 'Business Continuity', months: 24, currency: 'ZAR', monthlyEssentialExpense: 10000,
      restorationFundId: 6, restorationFundName: 'Trading Restoration'
    },
    cash: zar(70000), target: zar(240000), coverageMonths: 7, shortfall: zar(170000), belowTarget: true,
    earmarked: zar(30000), separated: zar(40000),
    holdings: [
      { accountId: 1, accountName: 'Operating', amount: zar(30000), separated: false },
      { accountId: 2, accountName: 'Reserve', amount: zar(40000), separated: true }
    ],
    notCounted: [
      { reason: 'In USD, not ZAR; never converted', amount: usd(100), accountName: 'Payouts' },
      { reason: 'Allocated but not yet earmarked or transferred', amount: usd(405) }
    ],
    restoration: { fundId: 6, fundName: 'Trading Restoration', target: usd(5000), cash: usd(1000), shortfall: usd(4000), progress: 20, notCounted: [] },
    ...overrides
  };
}

describe('ContinuityComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  async function render(r: ContinuityReport) {
    TestBed.configureTestingModule({ imports: [ContinuityComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ContinuityComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/continuity`).flush(r);
    http.expectOne(`${api}/funds`).flush(funds);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement, c: fixture.componentInstance };
  }

  const text = (el: Element) => el.textContent!.trim().replace(/\s+/g, ' ');

  it('shows coverage, where the counted money is, what is not counted, and restoration apart', async () => {
    const { el } = await render(report());
    const metrics = [...el.querySelectorAll('.metric-strip .metric')].map(m => [...m.children].map(text));

    expect(metrics).toEqual([
      ['Coverage', '7.0 months', 'of 24 months of essential expenses'],
      ['Counted · ZAR', 'ZAR 70,000.00', 'Business Continuity, bank and cash accounts'],
      ['Target', 'ZAR 240,000.00', '24 × ZAR 10,000.00'],
      ['Shortfall', 'ZAR 170,000.00', 'Still to set aside']
    ]);
    expect([...el.querySelectorAll('section[aria-labelledby="held-heading"] tbody tr')].map(r => [...r.querySelectorAll('div, .tag, .num')].map(text)))
      .toEqual([['Operating', 'Earmarked', 'ZAR 30,000.00'], ['Reserve', 'Separated', 'ZAR 40,000.00']]);
    expect([...el.querySelectorAll('.not-counted li')].map(li => [...li.children].map(text))).toEqual([
      ['USD 100.00', 'In USD, not ZAR; never converted · Payouts'],
      ['USD 405.00', 'Allocated but not yet earmarked or transferred']
    ]);
    expect([...el.querySelectorAll('.restoration-figures > div')].map(d => [...d.children].map(text)))
      .toEqual([['Held', 'USD 1,000.00'], ['Target', 'USD 5,000.00'], ['Shortfall', 'USD 4,000.00']]);
    expect(text(el.querySelector('.restoration')!)).toContain('20% of the target');
  });

  it('says what is missing rather than showing a zero', async () => {
    const { el } = await render(report({
      settings: { ...report().settings, monthlyEssentialExpense: null }, target: null, coverageMonths: null, shortfall: null, belowTarget: false,
      restoration: { fundId: 6, fundName: 'Trading Restoration', notCounted: [] }
    }));
    const metrics = [...el.querySelectorAll('.metric-strip .metric')].map(m => [...m.children].map(text));

    expect(metrics).toEqual([
      ['Coverage', 'Not measured', 'Enter the monthly essential expense'],
      ['Counted · ZAR', 'ZAR 70,000.00', 'Business Continuity, bank and cash accounts']
    ]);
    expect(el.textContent).toContain('Trading Restoration has no target yet.');
  });

  it('saves the settings, offers no system fund, and refuses the same fund twice', async () => {
    const { fixture, el, c } = await render(report());
    c.startEdit();
    await fixture.whenStable();
    expect([...el.querySelectorAll('#ct-fund option')].map(text)).toEqual(['Business Continuity', 'Trading Restoration']);

    c.form.patchValue({ restorationFundId: 3 });
    c.save();
    expect(c.formError()).toBe('Continuity and restoration must be different funds.');

    c.form.patchValue({ months: 18, monthlyEssentialExpense: 12500.5, restorationFundId: null });
    c.save();
    const req = http.expectOne(`${api}/continuity/settings`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ continuityFundId: 3, months: 18, monthlyEssentialExpense: 12500.5, restorationFundId: null });
    req.flush(report().settings);
    http.expectOne(`${api}/continuity`).flush(report());
    http.expectOne(`${api}/funds`).flush(funds);
    expect(c.editing()).toBe(false);
  });
});
