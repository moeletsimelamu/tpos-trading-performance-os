import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { FundsComponent } from './funds.component';
import { Fund, fundStanding } from '../finance.models';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;

const operating: Fund = {
  id: 2, name: 'Operating Capital', isSystem: false,
  balances: [{ amount: 15000, currency: 'ZAR' }, { amount: 300, currency: 'USD' }],
  minimum: { amount: 20000, currency: 'ZAR' }, target: { amount: 60000, currency: 'ZAR' }
};

describe('Fund minimum and target', () => {
  it('compares only the balance in their currency, and caps progress between 0 and 100', () => {
    expect(fundStanding(operating)).toEqual({ balance: { amount: 15000, currency: 'ZAR' }, belowMinimum: true, progress: 25 });
    expect(fundStanding({ ...operating, balances: [{ amount: 300, currency: 'USD' }] })!.balance).toEqual({ amount: 0, currency: 'ZAR' });
    expect(fundStanding({ ...operating, balances: [{ amount: 90000, currency: 'ZAR' }] })!.progress).toBe(100);
    expect(fundStanding({ ...operating, balances: [{ amount: -50, currency: 'ZAR' }] })!.progress).toBe(0);
    expect(fundStanding({ ...operating, minimum: null, target: null })).toBeNull();
  });
});

describe('FundsComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  it('shows a fund below its minimum with its progress, and always sends the minimum and target when saving', async () => {
    TestBed.configureTestingModule({ imports: [FundsComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(FundsComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/funds`).flush([{ id: 1, name: 'Unallocated', isSystem: true, balances: [] }, operating]);
    http.expectOne(`${api}/currencies`).flush([{ code: 'ZAR', minorUnits: 2 }]);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent!.replace(/\s+/g, ' ')).toContain('Minimum ZAR 20,000.00 · Target ZAR 60,000.00');
    expect(el.textContent).toContain('Below minimum');
    expect(el.textContent).toContain('25% of the target');

    const c = fixture.componentInstance;
    c.startEdit(operating);
    c.form.controls.minimum.setValue(10000);
    c.save();
    const req = http.expectOne(`${api}/funds/2`);
    expect(req.request.body).toEqual({ name: 'Operating Capital', description: undefined, thresholdCurrency: 'ZAR', minimum: 10000, target: 60000 });
    req.flush(operating);
    http.expectOne(`${api}/funds`).flush([operating]);

    // The system fund has no minimum or target to set
    c.startEdit({ id: 1, name: 'Unallocated', isSystem: true, balances: [] });
    await fixture.whenStable();
    expect(el.querySelector('#fund-min')).toBeNull();
  });
});
