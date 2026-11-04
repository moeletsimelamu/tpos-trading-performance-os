import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AccountsComponent, providerInitials } from './accounts/accounts.component';
import { TransactionsComponent } from './transactions/transactions.component';
import { account, FUNDS } from './finance-testing';
import { Account, Transaction, categoriesFor, otherCashText } from './finance.models';
import { environment } from '../../../environments/environment';

const api = environment.apiUrl;

describe('AccountsComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.removeItem('mhos.accounts.view');
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
    fixture.componentInstance.view.set('table');
    await fixture.whenStable();
    const rows = [...fixture.nativeElement.querySelectorAll('tbody tr')] as HTMLElement[];

    expect(rows[0].textContent).not.toContain('Close');
    const close = [...rows[1].querySelectorAll('button')].find(b => b.textContent?.trim() === 'Close') as HTMLButtonElement;
    close.click();
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('.btn-danger') as HTMLButtonElement).click();

    http.expectOne(`${api}/financial-accounts/2/close`).flush(account({ id: 2, status: 'Closed' }));
    http.expectOne(`${api}/balances`).flush({ accounts: [], funds: FUNDS, cashTotals: [] });
  });

  it('sends everything stated about a new account, and leaves what is blank empty', async () => {
    const fixture = await render([]);
    const c = fixture.componentInstance;
    c.toggleNew();
    c.form.patchValue({ name: ' EasyEquities TFSA ', type: 'Brokerage', currency: 'ZAR', ownership: 'Personal', category: 'Investment', taxTreatment: 'TaxFree', institution: ' EasyEquities ', reference: ' 1234 ', openedOn: '2026-01-15', homeFundId: 4, notes: ' ' });
    c.save();

    const req = http.expectOne(`${api}/financial-accounts`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      name: 'EasyEquities TFSA', type: 'Brokerage', currency: 'ZAR', ownership: 'Personal', category: 'Investment', taxTreatment: 'TaxFree',
      institution: 'EasyEquities', reference: '1234', openedOn: '2026-01-15', homeFundId: 4, notes: null
    });
    req.flush(account({}));
    http.expectOne(`${api}/balances`).flush({ accounts: [], funds: FUNDS, cashTotals: [] });
    await fixture.whenStable();
    expect(c.showForm()).toBe(false);
  });

  it('edits an account with its type and currency fixed, and sends a PUT without them', async () => {
    const existing: Account = account({ id: 2, name: 'FNB Aspire', ownership: 'Unspecified', category: 'Current', reference: '1234', homeFundId: 5 });
    const fixture = await render([existing]);
    const c = fixture.componentInstance;
    const edit = [...fixture.nativeElement.querySelectorAll('button')].find((b: HTMLButtonElement) => b.textContent?.trim() === 'Edit') as HTMLButtonElement;
    edit.click();
    await fixture.whenStable();

    expect(c.editing()?.id).toBe(2);
    expect([c.form.controls.type.disabled, c.form.controls.currency.disabled]).toEqual([true, true]);
    expect(c.form.controls.category.value).toBe('Current');
    expect(fixture.nativeElement.querySelector('form').getAttribute('aria-label')).toBe('Edit account');

    c.form.patchValue({ ownership: 'Personal' });
    c.save();
    const req = http.expectOne(`${api}/financial-accounts/2`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.ownership).toBe('Personal');
    expect(req.request.body.type).toBeUndefined();
    expect(req.request.body.currency).toBeUndefined();
    req.flush(existing);
    http.expectOne(`${api}/balances`).flush({ accounts: [], funds: FUNDS, cashTotals: [] });
    await fixture.whenStable();

    c.toggleNew();
    expect([c.editing(), c.form.controls.type.enabled, c.form.controls.name.value]).toEqual([null, true, '']);
  });

  it('offers only the categories a type can have, and drops one that no longer applies', async () => {
    const fixture = await render([]);
    const c = fixture.componentInstance;
    expect(categoriesFor('Bank')).toEqual(['NotStated', 'Current', 'Savings']);
    expect(categoriesFor('Brokerage')).toEqual(['NotStated', 'Investment']);
    expect(categoriesFor('Cash')).toEqual(['NotStated']);

    c.form.patchValue({ type: 'Bank', category: 'Savings' });
    c.form.controls.type.setValue('Brokerage');
    expect(c.form.controls.category.value).toBe('NotStated');
    expect(c.categories()).toEqual(['NotStated', 'Investment']);
  });

  it('says what an account is, flags money that is not the company\'s, and offers no home for unassigned money', async () => {
    const fixture = await render([
      account({ id: 3, name: 'TFSA', type: 'Brokerage', ownership: 'Personal', category: 'Investment', taxTreatment: 'TaxFree', institution: 'EasyEquities', reference: '1234', openedOn: '2026-01-15', homeFundName: 'Wealth Fund' }),
      account({ id: 4, name: 'Unknown', ownership: 'Unspecified' })
    ]);
    const c = fixture.componentInstance;
    c.view.set('table');
    await fixture.whenStable();
    const rows = [...fixture.nativeElement.querySelectorAll('tbody tr')].map((r: HTMLElement) => r.textContent!.replace(/\s+/g, ' '));

    expect(rows[0]).toContain('EasyEquities · Ref 1234 · Opened 2026-01-15');
    expect(rows[0]).toContain('Investment');
    expect(rows[0]).toContain('Personal');
    expect(rows[0]).toContain('Tax-free');
    expect(rows[0]).toContain('Mainly for Wealth Fund');
    expect(rows[1]).toContain('Not stated');
    expect(fixture.nativeElement.querySelectorAll('tbody .tag-warning').length).toBe(2);
    expect(c.homeFunds().every(f => !f.isSystem)).toBe(true);
  });

  it('keeps personal and unstated money out of the Cash figure and names it beside it', async () => {
    const fixture = TestBed.createComponent(AccountsComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/currencies`).flush([]);
    http.expectOne(`${api}/balances`).flush({
      accounts: [account({})], funds: FUNDS, cashTotals: [{ amount: 100, currency: 'ZAR' }],
      otherCash: [{ ownership: 'Personal', amount: { amount: 2000, currency: 'ZAR' } }, { ownership: 'Unspecified', amount: { amount: 700, currency: 'USD' } }]
    });
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('.metric-note')!.textContent).toBe('Company bank and cash accounts');
    expect(el.querySelector('.other-cash')!.textContent).toBe('Not counted as business cash: ZAR 2,000.00 in personal accounts; USD 700.00 in accounts with ownership not stated.');
  });

  it('writes nothing when all the cash is the company\'s', () => {
    expect(otherCashText(undefined, m => String(m.amount))).toBe('');
    expect(otherCashText([], m => String(m.amount))).toBe('');
    expect(otherCashText([{ ownership: 'Personal', amount: { amount: 1, currency: 'ZAR' } }, { ownership: 'Personal', amount: { amount: 2, currency: 'USD' } }], m => m.currency + m.amount))
      .toBe('ZAR1 and USD2 in personal accounts');
  });

  describe('panels', () => {
    const panels = (el: HTMLElement) => [...el.querySelectorAll('.panel')] as HTMLElement[];
    const text = (el: Element) => el.textContent!.replace(/\s+/g, ' ');

    it('shows what identifies each account, grouped with the company\'s first', async () => {
      const fixture = await render([
        account({ id: 1, name: 'FNB Aspire', institution: 'FNB', ownership: 'Personal', category: 'Current' }),
        account({
          id: 2, name: 'FNB Gold', institution: 'FNB', category: 'Current', reference: '1234', openedOn: '2026-01-15', homeFundName: 'Operating Capital',
          balance: { amount: 16950.5, currency: 'ZAR' }, funds: [{ fundId: 2, fundName: 'Operating Capital', balance: { amount: 16950.5, currency: 'ZAR' } }]
        }),
        account({ id: 3, name: 'Unknown', institution: null, ownership: 'Unspecified' })
      ]);
      const el = fixture.nativeElement as HTMLElement;

      expect([...el.querySelectorAll('.group h2')].map(h => h.textContent)).toEqual(['Business accounts', 'Personal accounts', 'Ownership not stated']);
      const gold = text(panels(el)[0]);
      for (const part of ['FNB', 'Bank · Current · ZAR', 'FNB Gold', 'ZAR 16,950.50', 'Recorded balance', 'Operating Capital', 'Mainly for', 'Reference1234', 'Opened2026-01-15', 'Transactions']) {
        expect(gold).toContain(part);
      }
      expect(panels(el)[0].querySelector('a.panel-name')!.getAttribute('href')).toBe('/finance/transactions?accountId=2');
      expect(text(el.querySelector('section[aria-label="Personal accounts"]')!)).toContain('Not counted as business cash.');
      expect(text(panels(el)[2])).toContain('Provider not stated');
    });

    it('never looks like or claims to be an issued card or a live connection', async () => {
      const fixture = await render();
      const el = fixture.nativeElement as HTMLElement;
      expect(text(el.querySelector('.honesty')!)).toContain('not bank cards or live connections');
      expect(el.querySelectorAll('.panel img').length).toBe(0);
      expect(text(panels(el)[0])).not.toMatch(/\d{4} \d{4}|Valid thru|Expires|Cardholder/i);
    });

    it('marks a tax-free account and tells it from a taxable one', async () => {
      const fixture = await render([
        account({ id: 1, name: 'TFSA', type: 'Brokerage', taxTreatment: 'TaxFree', category: 'Investment' }),
        account({ id: 2, name: 'Normal', type: 'Brokerage', taxTreatment: 'Taxable', category: 'Investment' })
      ]);
      const [normal, tfsa] = panels(fixture.nativeElement);
      expect(tfsa.querySelector('.tag-info')!.textContent).toBe('Tax-free');
      expect(text(normal)).toContain('TaxTaxable');
      expect(tfsa.getAttribute('data-type')).toBe('Brokerage');
    });

    it('shows a closed account after the open ones, with no actions', async () => {
      const fixture = await render([
        account({ id: 1, name: 'Aardvark', status: 'Closed', balance: { amount: 0, currency: 'ZAR' } }),
        account({ id: 2, name: 'Zebra' })
      ]);
      const [open, closed] = panels(fixture.nativeElement);
      expect(open.getAttribute('aria-label')).toBe('Zebra');
      expect(closed.classList.contains('closed')).toBe(true);
      expect(closed.querySelector('.tag')!.textContent).toBe('Closed');
      expect([...closed.querySelectorAll('button')].length).toBe(0);
    });

    it('remembers the choice of table, and shows panels when nothing is remembered', async () => {
      const fixture = await render();
      const c = fixture.componentInstance;
      const el = fixture.nativeElement as HTMLElement;
      expect(c.view()).toBe('panels');
      expect(el.querySelector('table')).toBeNull();

      ([...el.querySelectorAll('.view-bar button')].find(b => b.textContent === 'Table') as HTMLButtonElement).click();
      await fixture.whenStable();
      expect(el.querySelector('table')).not.toBeNull();
      expect(el.querySelector('.panel')).toBeNull();
      expect(localStorage.getItem('mhos.accounts.view')).toBe('table');
      localStorage.removeItem('mhos.accounts.view');
    });

    it('records a card with only what recognises it', async () => {
      const fixture = await render([account({ id: 2, name: 'FNB Gold', cards: [] })]);
      const c = fixture.componentInstance;
      const el = fixture.nativeElement as HTMLElement;
      expect(text(panels(el)[0])).toContain('Payment cardsNone recorded');

      ([...el.querySelectorAll('.panel button')].find(b => b.textContent === 'Add a card') as HTMLButtonElement).click();
      await fixture.whenStable();
      expect(text(el.querySelector('.card-form')!)).toContain('Never enter the full number, expiry, security code or cardholder name.');
      expect(el.querySelector('.card-form input[autocomplete="cc-number"]')).toBeNull();

      c.cardForm.patchValue({ lastFour: '123' });
      expect(c.cardForm.invalid).toBe(true);
      c.addCard(c.balances()!.accounts[0]);
      http.expectNone(`${api}/financial-accounts/2/cards`);

      c.cardForm.patchValue({ kind: 'Credit', lastFour: '1234', name: ' Fuel ' });
      c.addCard(c.balances()!.accounts[0]);
      const req = http.expectOne(`${api}/financial-accounts/2/cards`);
      expect(req.request.body).toEqual({ kind: 'Credit', lastFour: '1234', name: 'Fuel' });
      req.flush(account({ id: 2 }));
      http.expectOne(`${api}/balances`).flush({
        accounts: [account({ id: 2, name: 'FNB Gold', cards: [{ id: 9, kind: 'Credit', name: 'Fuel', lastFour: '1234', status: 'Active', addedAt: '2026-11-04T00:00:00Z' }] })],
        funds: FUNDS, cashTotals: []
      });
      await fixture.whenStable();
      expect(c.addingCardTo()).toBeNull();
      expect(text(panels(el)[0])).toContain('Fuel: Credit card ending 1234');
    });

    it('offers cards only on bank accounts', async () => {
      const fixture = await render([account({ id: 1, name: 'Broker', type: 'Brokerage' }), account({ id: 2, name: 'Till', type: 'Cash' })]);
      expect(text(fixture.nativeElement)).not.toContain('Payment cards');
    });

    it('records a card as cancelled after asking, and shows a refusal when adding one', async () => {
      const active = { id: 9, kind: 'Debit' as const, lastFour: '1234', status: 'Active' as const, addedAt: '2026-11-04T00:00:00Z' };
      const fixture = await render([account({ id: 2, name: 'FNB Gold', cards: [active] })]);
      const c = fixture.componentInstance;
      const el = fixture.nativeElement as HTMLElement;

      ([...el.querySelectorAll('.card-line button')].find(b => b.textContent === 'Cancel card') as HTMLButtonElement).click();
      await fixture.whenStable();
      (el.querySelector('.card-line .btn-danger') as HTMLButtonElement).click();
      http.expectOne(`${api}/financial-accounts/2/cards/9/cancel`).flush(account({ id: 2 }));
      http.expectOne(`${api}/balances`).flush({
        accounts: [account({ id: 2, name: 'FNB Gold', cards: [{ ...active, status: 'Cancelled', cancelledAt: '2026-11-04T01:00:00Z' } as never] })], funds: FUNDS, cashTotals: []
      });
      await fixture.whenStable();
      expect(el.querySelector('.card-line .tag')!.textContent).toBe('Cancelled');

      c.startCard(c.balances()!.accounts[0]);
      await fixture.whenStable();
      c.addCard(c.balances()!.accounts[0]);
      http.expectOne(`${api}/financial-accounts/2/cards`).flush({ title: 'This account already has an active card ending 1234.' }, { status: 422, statusText: 'Unprocessable' });
      await fixture.whenStable();
      expect(el.querySelector('.card-form [role="alert"]')!.textContent).toContain('already has an active card');
    });

    it('makes recognisable initials from a provider\'s name', () => {
      expect(['FNB', 'FNB Business', 'TymeBank', 'Easy Equities', 'First national bank', '  ', 'ABCD Bank'].map(providerInitials))
        .toEqual(['FNB', 'FNB', 'T', 'EE', 'FN', '?', 'AB']);
    });
  });

  it('shows a conflict from the API when creating a duplicate account', async () => {
    const fixture = await render([]);
    const c = fixture.componentInstance;
    c.showForm.set(true);
    c.form.patchValue({ name: 'Operating', currency: 'ZAR' });

    c.save();
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
