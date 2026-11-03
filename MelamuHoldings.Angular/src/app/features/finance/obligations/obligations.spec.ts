import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { ObligationsComponent } from './obligations.component';
import { Obligation } from '../finance.models';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;

function obligation(overrides: Partial<Obligation> = {}): Obligation {
  return {
    id: 7, name: 'Office rent', amount: { amount: 8500, currency: 'ZAR' }, category: 'Rent', recurrence: 'Monthly',
    firstDueDate: '2026-10-25', dueDate: '2026-10-25', status: 'Active', standing: 'Overdue', daysUntilDue: -4,
    payments: [], createdAt: '2026-10-01T00:00:00Z', ...overrides
  };
}

describe('ObligationsComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  async function render(list: Obligation[]) {
    TestBed.configureTestingModule({ imports: [ObligationsComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ObligationsComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/obligations`).flush(list);
    http.expectOne(`${api}/currencies`).flush([{ code: 'USD', minorUnits: 2 }, { code: 'ZAR', minorUnits: 2 }]);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement, c: fixture.componentInstance };
  }

  it('opens with the closed ones when the overview links here about a paid obligation', async () => {
    TestBed.configureTestingModule({ imports: [ObligationsComponent], providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ closed: 'true' }) } } }
    ] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ObligationsComponent);
    await fixture.whenStable();

    const req = http.expectOne(r => r.url === `${api}/obligations`);
    expect(req.request.params.get('includeClosed')).toBe('true');
    req.flush([]);
    http.expectOne(`${api}/currencies`).flush([]);
    expect(fixture.componentInstance.includeClosed()).toBe(true);
  });

  it('totals what is due within 30 days per currency, overdue included, and counts what is late', async () => {
    const { el } = await render([
      obligation(),
      obligation({ id: 8, name: 'Fibre', amount: { amount: 999.5, currency: 'ZAR' }, standing: 'DueSoon', daysUntilDue: 10, dueDate: '2026-11-08' }),
      obligation({ id: 9, name: 'Data feed', amount: { amount: 120, currency: 'USD' }, standing: 'Upcoming', daysUntilDue: 25, dueDate: '2026-11-23' }),
      obligation({ id: 10, name: 'Insurance', standing: 'Upcoming', daysUntilDue: 200, dueDate: '2027-05-17' })
    ]);
    const metrics = [...el.querySelectorAll('.metric-strip .metric')].map(m => [...m.children].map(c => c.textContent!.trim().replace(/\s+/g, ' ')));

    expect(metrics).toEqual([
      ['Due in the next 30 days · USD', 'USD 120.00', '1 payment, overdue ones included'],
      ['Due in the next 30 days · ZAR', 'ZAR 9,499.50', '2 payments, overdue ones included'],   // never added across currencies
      ['Overdue', '1', 'Past their due date and not recorded as paid']
    ]);
    expect(el.textContent).toContain('4 days late');
  });

  it('records a payment by linking an expense chosen from the candidates', async () => {
    const { fixture, el } = await render([obligation()]);
    ([...el.querySelectorAll('button')].find(b => b.textContent === 'Record payment') as HTMLButtonElement).click();
    http.expectOne(`${api}/obligations/7/payment-candidates`).flush([
      { transactionId: 31, date: '2026-10-24', description: 'Rent October', category: 'Rent', amount: { amount: 8500, currency: 'ZAR' } }
    ]);
    await fixture.whenStable();
    expect(el.textContent).toContain('It stays in the ledger as it is; this only links it.');

    ([...el.querySelectorAll('button')].find(b => b.textContent === 'Use this expense') as HTMLButtonElement).click();
    const req = http.expectOne(`${api}/obligations/7/payments`);
    expect(req.request.body).toEqual({ transactionId: 31 });
    req.flush(obligation({ standing: 'Upcoming', daysUntilDue: 27, dueDate: '2026-11-25',
      payments: [{ id: 1, transactionId: 31, dueDate: '2026-10-25', paidOn: '2026-10-24', amount: { amount: 8500, currency: 'ZAR' }, reversed: false }] }));
    http.expectOne(`${api}/obligations/7/payment-candidates`).flush([]);
    await fixture.whenStable();

    expect(el.textContent).toContain('Due 2026-10-25 · paid 2026-10-24');
    expect(el.textContent).toContain('2026-11-25');
    expect(el.textContent).toContain('Record the expense first');
  });

  it('sends a new obligation, and fixes the currency and schedule of one already paid', async () => {
    const { fixture, c } = await render([]);
    c.startCreate();
    c.form.setValue({ name: ' Fibre ', amount: 999.5, currency: 'ZAR', recurrence: 'Monthly', firstDueDate: '2026-11-08', category: '', notes: '' });
    c.save();
    const req = http.expectOne(`${api}/obligations`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Fibre', amount: 999.5, currency: 'ZAR', recurrence: 'Monthly', firstDueDate: '2026-11-08', category: null, notes: null });
    req.flush(obligation());
    http.expectOne(`${api}/obligations`).flush([]);
    await fixture.whenStable();

    c.startEdit(obligation({ payments: [{ id: 1, transactionId: 31, dueDate: '2026-10-25', paidOn: '2026-10-24', amount: { amount: 8500, currency: 'ZAR' }, reversed: false }] }));
    expect([c.form.controls.currency.disabled, c.form.controls.recurrence.disabled, c.form.controls.firstDueDate.disabled, c.form.controls.amount.disabled]).toEqual([true, true, true, false]);
  });
});
