import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { PurchasesComponent } from './purchases.component';
import { Affordability, Fund, PlannedPurchase } from '../finance.models';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;
const zar = (amount: number) => ({ amount, currency: 'ZAR' });

const funds: Fund[] = [
  { id: 1, name: 'Unallocated', isSystem: true, balances: [] },
  { id: 4, name: 'Wealth Fund', isSystem: false, balances: [] },
  { id: 2, name: 'Operating Capital', isSystem: false, balances: [] }
];

const affordable: Affordability = {
  verdict: 'Affordable', available: zar(20000), committedElsewhere: zar(0), purchase: zar(10000), after: zar(10000), minimum: zar(5000), obligationsDue: zar(0)
};

function purchase(overrides: Partial<PlannedPurchase> = {}): PlannedPurchase {
  return {
    id: 7, name: 'Laptop', amount: zar(10000), fundId: 2, fundName: 'Operating Capital', category: 'Equipment', targetDate: '2026-12-01',
    status: 'Proposed', expenseReversed: false, affordability: affordable, createdAt: '2026-11-03T00:00:00Z', ...overrides
  };
}

describe('PurchasesComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  const listUrl = (r: { url: string }) => r.url === `${api}/planned-purchases`;

  function flushList(list: PlannedPurchase[]) {
    http.expectOne(listUrl).flush(list);
    http.expectOne(`${api}/funds`).flush(funds);
  }

  async function render(list: PlannedPurchase[]) {
    TestBed.configureTestingModule({ imports: [PurchasesComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PurchasesComponent);
    await fixture.whenStable();
    flushList(list);
    http.expectOne(`${api}/currencies`).flush([{ code: 'USD', minorUnits: 2 }, { code: 'ZAR', minorUnits: 2 }]);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement, c: fixture.componentInstance };
  }

  const text = (el: Element) => el.textContent!.trim().replace(/\s+/g, ' ');
  const button = (el: HTMLElement, label: string) => [...el.querySelectorAll('button')].find(b => text(b) === label) as HTMLButtonElement;

  it('answers whether each purchase can be afforded, in words and with the figures behind it', async () => {
    const { el } = await render([
      purchase(),
      purchase({ id: 8, name: 'Server', status: 'Committed', affordability: { ...affordable, verdict: 'BreachesMinimum', committedElsewhere: zar(10000), after: zar(4000), shortfall: zar(1000), obligationsDue: zar(700) } }),
      purchase({ id: 9, name: 'Desk', affordability: { ...affordable, verdict: 'NoMinimum', minimum: null, after: zar(500) }, fundName: 'Wealth Fund' }),
      purchase({ id: 10, name: 'Gift', affordability: { ...affordable, verdict: 'NotEnoughMoney', minimum: null, after: zar(-10), shortfall: zar(10), minimumInOtherCurrency: { amount: 100, currency: 'USD' } }, fundName: "Owner's Distribution" })
    ]);
    const cards = [...el.querySelectorAll('section.purchase')].map(text);

    expect(cards[0]).toContain('Affordable');
    expect(cards[0]).toContain('Leaves ZAR 10,000.00 in Operating Capital, at or above its minimum of ZAR 5,000.00.');
    expect(cards[1]).toContain('Would leave ZAR 4,000.00 in Operating Capital, ZAR 1,000.00 below its minimum of ZAR 5,000.00.');
    expect(cards[1]).toContain('less ZAR 10,000.00 committed to other purchases');
    expect(cards[1]).toContain('ZAR 700.00 of obligations fall due within 30 days; they are not deducted');
    expect(cards[2]).toContain('Would leave ZAR 500.00 in Wealth Fund. It has no minimum in ZAR to compare with.');
    expect(cards[3]).toContain("Owner's Distribution is ZAR 10.00 short. Its minimum of USD 100.00 is in another currency and is never converted.");
  });

  it('moves a purchase from proposed to committed to bought, reloading because reservations change', async () => {
    const { fixture, el } = await render([purchase()]);

    button(el, 'Commit').click();
    http.expectOne(`${api}/planned-purchases/7/commit`).flush(purchase({ status: 'Committed' }));
    flushList([purchase({ status: 'Committed' })]);
    await fixture.whenStable();
    expect(button(el, 'Commit')).toBeUndefined();

    button(el, 'Record as bought').click();
    http.expectOne(`${api}/planned-purchases/7/expense-candidates`).flush([
      { transactionId: 31, date: '2026-11-02', description: 'Laptop shop', category: 'Equipment', amount: zar(9800) }
    ]);
    await fixture.whenStable();
    expect(el.textContent).toContain('It stays in the ledger as it is; this only links it.');

    button(el, 'Use this expense').click();
    const req = http.expectOne(`${api}/planned-purchases/7/purchase`);
    expect(req.request.body).toEqual({ transactionId: 31 });
    const bought = purchase({ status: 'Purchased', affordability: null, transactionId: 31, paid: zar(9800), purchasedOn: '2026-11-02', expenseReversed: true });
    req.flush(bought);
    flushList([bought]);
    await fixture.whenStable();

    const card = text(el.querySelector('section.purchase')!);
    expect(card).toContain('Bought on 2026-11-02 for ZAR 9,800.00');
    expect(card).toContain('Expense reversed');
    expect(card).not.toContain('Affordable');
    expect(button(el, 'Undo this purchase')).toBeDefined();
  });

  it('sends a new purchase, defaults to the operating fund and offers no system fund', async () => {
    const { fixture, el, c } = await render([]);
    expect(el.textContent).toContain('No planned purchases');

    c.startCreate();
    await fixture.whenStable();
    expect(c.form.getRawValue().fundId).toBe(2);
    expect([...el.querySelectorAll('#pp-fund option')].map(text)).toEqual(['Wealth Fund', 'Operating Capital']);

    c.form.patchValue({ name: ' Laptop ', amount: 10000, category: ' ', targetDate: '' });
    c.save();
    const post = http.expectOne(`${api}/planned-purchases`);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ name: 'Laptop', amount: 10000, currency: 'ZAR', fundId: 2, category: null, targetDate: null, notes: null });
    post.flush(purchase());
    flushList([purchase()]);
    expect(c.editing()).toBeUndefined();
  });

  it('asks before cancelling, and shows closed ones only when asked', async () => {
    const { fixture, el, c } = await render([purchase()]);

    button(el, 'Cancel').click();
    await fixture.whenStable();
    button(el, 'Cancel it').click();
    http.expectOne(`${api}/planned-purchases/7/cancel`).flush(purchase({ status: 'Cancelled', affordability: null }));
    flushList([]);
    await fixture.whenStable();

    c.toggleClosed();
    const req = http.expectOne(listUrl);
    expect(req.request.params.get('includeClosed')).toBe('true');
    req.flush([purchase({ status: 'Cancelled', affordability: null })]);
    http.expectOne(`${api}/funds`).flush(funds);
    await fixture.whenStable();
    expect(text(el.querySelector('section.purchase')!)).toContain('Cancelled');
  });

  it('opens with the closed ones when the overview links here about a bought purchase', async () => {
    TestBed.configureTestingModule({ imports: [PurchasesComponent], providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ closed: 'true' }) } } }
    ] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PurchasesComponent);
    await fixture.whenStable();

    const req = http.expectOne(listUrl);
    expect(req.request.params.get('includeClosed')).toBe('true');
    req.flush([]);
    http.expectOne(`${api}/funds`).flush(funds);
    http.expectOne(`${api}/currencies`).flush([]);
    expect(fixture.componentInstance.includeClosed()).toBe(true);
  });

  it('shows a committed purchase the same way as other things committed with nothing moved yet', async () => {
    const { el } = await render([purchase({ status: 'Committed' })]);
    const tag = [...el.querySelectorAll('section.purchase .tag')].find(t => text(t) === 'Committed')!;
    expect(tag.classList.contains('tag-info')).toBe(true);
  });
});
