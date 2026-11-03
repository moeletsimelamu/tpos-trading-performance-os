import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { BudgetsComponent } from './budgets.component';
import { BudgetLine, BudgetMonth } from '../finance.models';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;
const zar = (amount: number) => ({ amount, currency: 'ZAR' });

const rent: BudgetLine = { id: 3, month: '2026-11', category: 'Rent', budget: zar(8000), actual: zar(8500), remaining: zar(-500), usedPercent: 106.2, exceeded: true, transactions: 1 };

function month(overrides: Partial<BudgetMonth> = {}): BudgetMonth {
  return {
    month: '2026-11', from: '2026-11-01', to: '2026-11-30',
    lines: [rent, { ...rent, id: 4, category: 'Fibre', budget: zar(1000), actual: zar(300), remaining: zar(700), usedPercent: 30, exceeded: false }],
    unbudgeted: [{ category: 'Travel', actual: { amount: 150, currency: 'USD' }, transactions: 2 }, { category: null, actual: zar(75), transactions: 1 }],
    totals: [{ currency: 'ZAR', budget: 9000, actualBudgeted: 8800, actualUnbudgeted: 75 }],
    ...overrides
  };
}

describe('BudgetsComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  const budgetsUrl = (r: { url: string }) => r.url === `${api}/budgets`;

  async function render(data: BudgetMonth) {
    TestBed.configureTestingModule({ imports: [BudgetsComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(BudgetsComponent);
    await fixture.whenStable();
    http.expectOne(budgetsUrl).flush(data);
    http.expectOne(`${api}/currencies`).flush([{ code: 'USD', minorUnits: 2 }, { code: 'ZAR', minorUnits: 2 }]);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement, c: fixture.componentInstance };
  }

  const text = (el: Element) => el.textContent!.trim().replace(/\s+/g, ' ');

  it('shows budget versus actual, marks what is over, and lists spending outside the budget', async () => {
    const { el } = await render(month());

    const rows = [...el.querySelectorAll('section[aria-labelledby="lines-heading"] tbody tr')];
    expect(rows.map(r => [...r.querySelectorAll('td.num')].map(text))).toEqual([
      ['ZAR 8,000.00', 'ZAR 8,500.00', 'ZAR -500.00'],
      ['ZAR 1,000.00', 'ZAR 300.00', 'ZAR 700.00']
    ]);
    expect(rows[0].querySelector('.tag-negative')?.textContent).toBe('Over budget');
    expect(rows[1].querySelector('.tag-negative')).toBeNull();
    expect((rows[0].querySelector('.fill') as HTMLElement).style.width).toBe('100%');   // the bar stops at full; the text says how far over

    const metrics = [...el.querySelectorAll('.metric-strip .metric')].map(m => [...m.children].map(text));
    expect(metrics[0]).toEqual(['Budgeted · ZAR', 'ZAR 9,000.00', 'ZAR 8,800.00 spent against it']);
    expect(metrics[1]).toEqual(['Outside the budget · ZAR', 'ZAR 75.00', 'Expenses with no line for their category']);

    const outside = [...el.querySelectorAll('.unbudgeted tbody tr')].map(text);
    expect(outside[0]).toContain('Travel');
    expect(outside[0]).toContain('USD 150.00');
    expect(outside[1]).toContain('No category');
  });

  it('asks for another month, and adds a line to the month shown', async () => {
    const { fixture, c } = await render(month({ lines: [] }));

    c.chooseMonth('2026-10');
    const req = http.expectOne(budgetsUrl);
    expect(req.request.params.get('month')).toBe('2026-10');
    req.flush(month({ month: '2026-10', lines: [] }));
    await fixture.whenStable();

    c.budgetFor({ category: 'Travel', actual: { amount: 150, currency: 'USD' }, transactions: 2 });
    expect(c.form.getRawValue()).toEqual({ category: 'Travel', amount: null, currency: 'USD' });
    c.form.patchValue({ amount: 200 });
    c.save();
    const post = http.expectOne(`${api}/budgets`);
    expect(post.request.body).toEqual({ month: '2026-10', category: 'Travel', amount: 200, currency: 'USD' });
    post.flush(rent);
    http.expectOne(budgetsUrl).flush(month({ month: '2026-10' }));
    expect(c.editing()).toBeUndefined();
  });

  it('edits a line without changing its currency, and asks before deleting', async () => {
    const { fixture, el, c } = await render(month());

    c.startEdit(rent);
    expect(c.form.controls.currency.disabled).toBe(true);
    c.form.patchValue({ amount: 9000 });
    c.save();
    const put = http.expectOne(`${api}/budgets/3`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ category: 'Rent', amount: 9000 });
    put.flush(rent);
    http.expectOne(budgetsUrl).flush(month());
    await fixture.whenStable();

    ([...el.querySelectorAll('button')].find(b => b.textContent === 'Delete') as HTMLButtonElement).click();
    await fixture.whenStable();
    ([...el.querySelectorAll('button')].find(b => b.textContent === 'Delete it') as HTMLButtonElement).click();
    const del = http.expectOne(`${api}/budgets/3`);
    expect(del.request.method).toBe('DELETE');
    del.flush(null);
    http.expectOne(budgetsUrl).flush(month({ lines: [], unbudgeted: [], totals: [] }));
    await fixture.whenStable();
    expect(el.textContent).toContain('No budget for 2026-11.');
  });
});
