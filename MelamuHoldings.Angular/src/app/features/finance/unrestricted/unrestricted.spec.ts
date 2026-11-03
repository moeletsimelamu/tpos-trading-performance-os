import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { UnrestrictedComponent } from './unrestricted.component';
import { UnrestrictedCashReport, UnrestrictedCurrency } from '../finance.models';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;

const zar: UnrestrictedCurrency = {
  currency: 'ZAR', held: 70000, operatingMinimum: 5000, owedToOtherFunds: 0, payoutsNotAllocated: 0, committedPurchases: 4000,
  amount: 61000, obligationsDue: 8000, afterObligations: 53000,
  holdings: [
    { accountId: 1, accountName: 'Operating ZAR', fundId: 1, fundName: 'Unallocated', amount: { amount: 50000, currency: 'ZAR' } },
    { accountId: 1, accountName: 'Operating ZAR', fundId: 2, fundName: 'Operating Capital', amount: { amount: 20000, currency: 'ZAR' } }
  ]
};

describe('UnrestrictedComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  async function render(report: UnrestrictedCashReport) {
    TestBed.configureTestingModule({ imports: [UnrestrictedComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(UnrestrictedComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/unrestricted-cash`).flush(report);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  const text = (el: Element) => el.textContent!.trim().replace(/\s+/g, ' ');

  it('shows the working from what is held to what is free, and bills beside it, not deducted', async () => {
    const el = await render({ currencies: [zar], notCounted: [] });
    const rows = [...el.querySelectorAll('tbody tr')].map(r => [...r.querySelectorAll('td')].map(text));

    expect(rows).toEqual([
      ['Unallocated · Operating ZAR', 'ZAR 50,000.00'],
      ['Operating Capital · Operating ZAR', 'ZAR 20,000.00'],
      ['Held', 'ZAR 70,000.00'],
      ["Less Operating Capital's minimum", 'ZAR 5,000.00'],
      ['Less what confirmed allocations still owe other funds', 'ZAR 0.00'],
      ['Less received payouts not allocated yet', 'ZAR 0.00'],
      ['Less committed purchases', 'ZAR 4,000.00'],
      ['Unrestricted cash', 'ZAR 61,000.00']
    ]);
    const beside = text(el.querySelector('.beside')!);
    expect(beside).toContain('ZAR 8,000.00 of obligations fall due within 30 days');
    expect(beside).toContain('They are not deducted; ZAR 53,000.00 would be left if all were paid.');
    expect(el.querySelector('.notice')).toBeNull();
  });

  it('keeps each currency apart, and says so when more is spoken for than is held', async () => {
    const usd: UnrestrictedCurrency = { ...zar, currency: 'USD', held: 100, operatingMinimum: 0, committedPurchases: 0, payoutsNotAllocated: 500, amount: -400, obligationsDue: 0, afterObligations: -400, holdings: [] };
    const el = await render({ currencies: [usd, zar], notCounted: [] });
    const cards = [...el.querySelectorAll('section.working')];

    expect(cards.map(c => text(c.querySelector('h2')!))).toEqual(['USD', 'ZAR']);
    expect(text(cards[0])).toContain('More is spoken for than is held.');
    expect(cards[0].querySelector('.total .num')!.classList.contains('text-negative')).toBe(true);
    expect(cards[0].querySelector('.beside')).toBeNull();                // no bills in this currency
    expect(text(cards[1])).not.toContain('More is spoken for');
  });

  it('lists what is not counted with the reason, and has an empty state', async () => {
    const el = await render({ currencies: [], notCounted: [{ reason: 'In a personal account', amount: { amount: 7000, currency: 'ZAR' }, accountName: 'Personal ZAR' }] });

    expect(text(el)).toContain('No cash to work out yet');
    expect(text(el.querySelector('.not-counted li')!)).toBe('ZAR 7,000.00In a personal account · Personal ZAR');
  });
});
