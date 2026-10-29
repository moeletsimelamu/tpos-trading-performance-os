import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AccountsComponent } from './accounts/accounts.component';
import { TransactionsComponent } from './transactions/transactions.component';
import { account, FUNDS } from './finance-testing';
import { Transaction } from './finance.models';
import { environment } from '../../../environments/environment';

const api = environment.apiUrl;

describe('AccountsComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AccountsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(accounts = [
    account({ id: 1, name: 'Operating', balance: { amount: 16950.5, currency: 'ZAR' }, funds: [{ fundId: 1, fundName: 'Unallocated', balance: { amount: 16950.5, currency: 'ZAR' } }] }),
    account({ id: 2, name: 'Empty', balance: { amount: 0, currency: 'ZAR' } })
  ]) {
    const fixture = TestBed.createComponent(AccountsComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/currencies`).flush([{ code: 'ZAR', minorUnits: 2 }, { code: 'USD', minorUnits: 2 }]);
    http.expectOne(`${api}/balances`).flush({ accounts, funds: FUNDS, cashTotals: [{ amount: 16950.5, currency: 'ZAR' }] });
    await fixture.whenStable();
    return fixture;
  }

  it('shows cash totals and balances with their currency', async () => {
    const fixture = await render();
    const text = fixture.nativeElement.textContent;

    expect(text).toContain('ZAR 16,950.50');
    expect(text).toContain('Unallocated');
  });

  it('offers Close only for accounts at a zero balance, after confirmation', async () => {
    const fixture = await render();
    const rows = [...fixture.nativeElement.querySelectorAll('tbody tr')] as HTMLElement[];

    expect(rows[0].textContent).not.toContain('Close');
    const close = [...rows[1].querySelectorAll('button')].find(b => b.textContent?.trim() === 'Close') as HTMLButtonElement;
    close.click();
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('.btn-danger') as HTMLButtonElement).click();

    http.expectOne(`${api}/financial-accounts/2/close`).flush(account({ id: 2, status: 'Closed' }));
    http.expectOne(`${api}/balances`).flush({ accounts: [], funds: FUNDS, cashTotals: [] });
  });

  it('shows a conflict from the API when creating a duplicate account', async () => {
    const fixture = await render([]);
    const c = fixture.componentInstance;
    c.showForm.set(true);
    c.form.patchValue({ name: 'Operating', currency: 'ZAR' });

    c.create();
    http.expectOne(`${api}/financial-accounts`)
      .flush({ title: "An account named 'Operating' already exists." }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain("An account named 'Operating' already exists.");
  });
});

describe('TransactionsComponent', () => {
  let http: HttpTestingController;

  const reversible: Transaction = {
    id: 7, type: 'Income', date: '2026-10-27', recordedAt: '2026-10-27T10:00:00Z', description: 'Payout',
    entries: [{ accountId: 10, accountName: 'Operating', fundId: 1, fundName: 'Unallocated', amount: 1350, currency: 'ZAR', role: 'Principal' }]
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        provideRouter([{ path: 'finance/transactions', component: TransactionsComponent }])
      ]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function open(url: string, items: Transaction[] = [reversible]) {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    http.expectOne(`${api}/financial-accounts`).flush([]);
    http.expectOne(`${api}/funds`).flush(FUNDS);
    const list = http.expectOne(r => r.url === `${api}/transactions`);
    list.flush({ items, page: 1, pageSize: 25, totalCount: items.length, totalPages: 1 });
    await harness.fixture.whenStable();
    return { harness, request: list.request };
  }

  it('turns URL filters into API query parameters', async () => {
    const { request } = await open('/finance/transactions?accountId=10&type=Income&from=2026-10-01');

    expect(request.params.get('accountId')).toBe('10');
    expect(request.params.get('type')).toBe('Income');
    expect(request.params.get('from')).toBe('2026-10-01');
    expect(request.params.get('page')).toBe('1');
  });

  it('shows signed entries with their currency', async () => {
    const { harness } = await open('/finance/transactions');

    expect(harness.routeNativeElement?.textContent).toContain('ZAR +1,350.00');
  });

  it('reverses a transaction with a reason and an idempotency key', async () => {
    const { harness } = await open('/finance/transactions');
    const component = harness.fixture.debugElement.query(d => d.componentInstance instanceof TransactionsComponent).componentInstance as TransactionsComponent;

    component.toggle(7);
    component.reverseReason = 'Posted twice';
    component.reverse(reversible);

    const req = http.expectOne(`${api}/transactions/7/reverse`);
    expect(req.request.body.reason).toBe('Posted twice');
    expect(req.request.headers.get('Idempotency-Key')).toMatch(/^[a-f0-9]{32}$/);
    req.flush({ ...reversible, id: 8, type: 'Reversal', reversesTransactionId: 7 });
    http.expectOne(r => r.url === `${api}/transactions`).flush({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });
  });

  it('does not offer reversal for reversals or already reversed transactions', async () => {
    const { harness } = await open('/finance/transactions');
    const component = harness.fixture.debugElement.query(d => d.componentInstance instanceof TransactionsComponent).componentInstance as TransactionsComponent;

    expect(component.canReverse(reversible)).toBe(true);
    expect(component.canReverse({ ...reversible, reversedByTransactionId: 8 })).toBe(false);
    expect(component.canReverse({ ...reversible, type: 'Reversal' })).toBe(false);
  });

  it('applies filters by navigating, so the URL can be shared', async () => {
    const { harness } = await open('/finance/transactions');
    const component = harness.fixture.debugElement.query(d => d.componentInstance instanceof TransactionsComponent).componentInstance as TransactionsComponent;

    component.draft = { ...component.draft, fundId: 3 };
    component.applyFilters();
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/finance/transactions?fundId=3');
    http.expectOne(r => r.url === `${api}/transactions` && r.params.get('fundId') === '3')
      .flush({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });
  });
});
