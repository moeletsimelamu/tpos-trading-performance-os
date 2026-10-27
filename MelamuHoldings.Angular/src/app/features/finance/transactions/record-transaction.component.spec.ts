import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { RecordTransactionComponent } from './record-transaction.component';
import { account, FUNDS } from '../finance-testing';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;
const ACCOUNTS = [
  account({ id: 10, name: 'Operating', currency: 'ZAR' }),
  account({ id: 11, name: 'Reserve', currency: 'ZAR' }),
  account({ id: 12, name: 'Payouts', currency: 'USD' }),
  account({ id: 13, name: 'Old', currency: 'ZAR', status: 'Closed' })
];

describe('RecordTransactionComponent', () => {
  let http: HttpTestingController;

  async function render(query: Record<string, string> = {}) {
    TestBed.configureTestingModule({
      imports: [RecordTransactionComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } }
      ]
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(RecordTransactionComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/financial-accounts`).flush(ACCOUNTS);
    http.expectOne(`${api}/funds`).flush(FUNDS);
    await fixture.whenStable();
    return fixture;
  }

  afterEach(() => http.verify());

  function fillIncome(c: RecordTransactionComponent) {
    c.form.patchValue({ accountId: 10, fundId: 1, amount: 1350, date: '2026-10-27', description: 'Payout received' });
  }

  it('offers only active accounts and preselects from the query string', async () => {
    const fixture = await render({ type: 'opening-balance', accountId: '11' });
    const c = fixture.componentInstance;

    expect(c.kind()).toBe('opening-balance');
    expect(c.form.controls.accountId.value).toBe(11);
    expect(c.activeAccounts().map(a => a.name)).toEqual(['Operating', 'Reserve', 'Payouts']);
  });

  it('posts income with an Idempotency-Key and shows the recorded transaction', async () => {
    const fixture = await render();
    const c = fixture.componentInstance;
    fillIncome(c);

    c.submit();
    const req = http.expectOne(`${api}/transactions/income`);
    expect(req.request.headers.get('Idempotency-Key')).toMatch(/^[a-f0-9]{32}$/);
    expect(req.request.body).toEqual({
      date: '2026-10-27', description: 'Payout received', reference: undefined,
      accountId: 10, fundId: 1, amount: 1350, category: undefined
    });
    req.flush({ id: 42, entries: [{ accountId: 10 }] });
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Recorded transaction #42');
    expect(c.form.controls.amount.value).toBeNull();
  });

  it('retries with the same key after a failure, and uses a new key once the values change', async () => {
    const fixture = await render();
    const c = fixture.componentInstance;
    fillIncome(c);

    c.submit();
    const first = http.expectOne(`${api}/transactions/income`);
    const key = first.request.headers.get('Idempotency-Key');
    first.flush(null, { status: 0, statusText: 'Unknown Error' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Not recorded');

    c.submit();
    const retry = http.expectOne(`${api}/transactions/income`);
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    retry.flush(null, { status: 503, statusText: 'Unavailable' });

    c.form.patchValue({ amount: 1400 });
    c.submit();
    const changed = http.expectOne(`${api}/transactions/income`);
    expect(changed.request.headers.get('Idempotency-Key')).not.toBe(key);
    changed.flush({ id: 43, entries: [] });
  });

  it('sends one request even if submitted twice', async () => {
    const fixture = await render();
    const c = fixture.componentInstance;
    fillIncome(c);

    c.submit();
    c.submit();

    const requests = http.match(`${api}/transactions/income`);
    expect(requests.length).toBe(1);
    requests[0].flush({ id: 44, entries: [] });
  });

  it('does not submit invalid amounts', async () => {
    const fixture = await render();
    const c = fixture.componentInstance;
    fillIncome(c);

    for (const bad of [0, -5, 10.005]) {
      c.form.patchValue({ amount: bad });
      c.submit();
    }

    http.expectNone(`${api}/transactions/income`);
    expect(c.form.controls.amount.errors?.['amount']).toBe('Use at most 2 decimal places.');
  });

  it('allows a negative opening balance but not zero', async () => {
    const fixture = await render({ type: 'opening-balance' });
    const amount = fixture.componentInstance.form.controls.amount;

    amount.setValue(-150);
    expect(amount.valid).toBe(true);
    amount.setValue(0);
    expect(amount.errors?.['amount']).toBe('Must not be zero.');
  });

  it('limits transfers to the same currency and exchanges to a different one', async () => {
    const fixture = await render({ type: 'transfer' });
    const c = fixture.componentInstance;
    c.form.patchValue({ fromAccountId: 10 });

    expect(c.toAccountOptions().map(a => a.name)).toEqual(['Reserve']);

    c.selectKind('exchange');
    expect(c.toAccountOptions().map(a => a.name)).toEqual(['Payouts']);
  });

  it('previews the entries an earmark will post without moving money between accounts', async () => {
    const fixture = await render({ type: 'reallocation' });
    const c = fixture.componentInstance;
    c.form.patchValue({ accountId: 10, fromFundId: 1, toFundId: 3, amount: 540 });

    expect(c.preview()).toEqual([
      { account: 'Operating', fund: 'Unallocated', amount: -540, currency: 'ZAR' },
      { account: 'Operating', fund: 'Business Continuity', amount: 540, currency: 'ZAR' }
    ]);
  });

  it('shows the implied exchange rate', async () => {
    const fixture = await render({ type: 'exchange' });
    const c = fixture.componentInstance;
    c.form.patchValue({ fromAccountId: 12, toAccountId: 10, amountSent: 1000, amountReceived: 18250.5 });

    expect(c.impliedRate()).toBe('18.2505');
  });
});
