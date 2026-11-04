import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { PropAccountLedgerComponent } from './prop-account-ledger.component';
import { PropAccountReportComponent } from './prop-account-report.component';
import { PropAccountEconomics, PropAccountLedger, PropAccountReport } from '../models/prop-account.model';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;
const usd = (amount: number) => ({ amount, currency: 'USD' });

const FEES_ONLY: PropAccountEconomics = {
  outcome: 'FeesWithoutPayouts', lines: [{ currency: 'USD', fees: 700, payoutsReceived: 0, net: -700 }], awaiting: [usd(1000)], feesCounted: 2, payoutsCounted: 0
};

function ledger(overrides: Partial<PropAccountLedger> = {}): PropAccountLedger {
  return { economics: { outcome: 'NothingLinked', lines: [], awaiting: [], feesCounted: 0, payoutsCounted: 0 }, fees: [], payouts: [], ...overrides };
}

const squash = (el: Element) => el.textContent!.replace(/\s+/g, ' ');

describe('PropAccountLedgerComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  async function render(first: PropAccountLedger | 'forbidden' | 'broken') {
    TestBed.configureTestingModule({ imports: [PropAccountLedgerComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PropAccountLedgerComponent);
    fixture.componentRef.setInput('accountId', 5);
    await fixture.whenStable();
    const req = http.expectOne(`${api}/prop-accounts/5/ledger`);
    if (first === 'forbidden') req.flush(null, { status: 403, statusText: 'Forbidden' });
    else if (first === 'broken') req.flush({ title: 'Boom' }, { status: 500, statusText: 'Server Error' });
    else req.flush(first);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement, c: fixture.componentInstance };
  }

  it('shows nothing at all to a user who is not the owner', async () => {
    const { el } = await render('forbidden');
    expect(el.querySelector('section')).toBeNull();
    expect(el.querySelector('[role="alert"]')).toBeNull();
  });

  it('says when the figures could not be loaded instead of showing none', async () => {
    const { el } = await render('broken');
    expect(el.querySelector('[role="alert"]')!.textContent).toContain('Could not load fees and payouts');
    expect(el.querySelector('table')).toBeNull();
  });

  it('flags fees with no payout, and keeps a payout on its way out of the figures', async () => {
    const { el } = await render(ledger({
      economics: FEES_ONLY,
      fees: [{ id: 1, kind: 'Reset', transactionId: 40, date: '2026-10-20', description: 'Reset fee', amount: usd(200), reversed: false }],
      payouts: [{ id: 9, source: 'Test Firm', status: 'Approved', amount: usd(1000), requestedOn: '2026-11-01' }]
    }));
    const text = squash(el);
    expect(text).toContain('Fees, no payout received');
    expect(text).toContain('USD 700.00');
    expect(text).toContain('USD -700.00');
    expect(text).toContain('Awaiting USD 1,000.00: requested or approved, not received, and not counted above.');
    expect(text).toContain('A reset');
    expect(text).toContain('Not received');
    expect(text).toContain('Reset fee #40');
    expect(el.querySelector('a[href="/finance/payouts/9"]')).not.toBeNull();
  });

  it('marks a reversed fee as not counted', async () => {
    const { el } = await render(ledger({ fees: [{ id: 1, kind: 'Purchase', transactionId: 40, date: '2026-10-20', description: 'Refunded', amount: usd(500), reversed: true }] }));
    expect(el.querySelector('tr.reversed')!.textContent).toContain('Reversed, not counted');
  });

  it('says each currency stands alone once there are two', async () => {
    const { el } = await render(ledger({ economics: { ...FEES_ONLY, outcome: 'PayoutsReceived', awaiting: [], lines: [
      { currency: 'USD', fees: 500, payoutsReceived: 0, net: -500 }, { currency: 'ZAR', fees: 0, payoutsReceived: 18000, net: 18000 }
    ] } }));
    expect(squash(el)).toContain('Each currency stands alone. Nothing is converted.');
    expect(squash(el)).toContain('ZAR +18,000.00');
  });

  it('links a chosen expense as a fee and says no money moves', async () => {
    const { fixture, el, c } = await render(ledger());
    c.startFee();
    http.expectOne(`${api}/prop-accounts/5/ledger/fee-candidates`).flush([
      { transactionId: 40, date: '2026-10-20', description: 'Challenge', amount: usd(500) }, { transactionId: 41, date: '2026-10-19', description: 'Other', amount: usd(90) }
    ]);
    await fixture.whenStable();

    const form = el.querySelector('form[aria-label="Link an expense"]')!;
    expect(form.textContent).toContain('No money moves and no balance changes.');
    expect(c.feeForm.getRawValue().transactionId).toBe(40);
    c.feeForm.patchValue({ transactionId: 41, kind: 'Monthly' });
    c.linkFee();

    const req = http.expectOne(`${api}/prop-accounts/5/ledger/fees`);
    expect(req.request.body).toEqual({ transactionId: 41, kind: 'Monthly' });
    req.flush(ledger({ fees: [{ id: 1, kind: 'Monthly', transactionId: 41, date: '2026-10-19', description: 'Other', amount: usd(90), reversed: false }] }));
    await fixture.whenStable();
    expect(el.querySelector('form')).toBeNull();
    expect(squash(el)).toContain('A monthly fee');
  });

  it('points to recording the expense first when none can be linked, and keeps a refused link on screen', async () => {
    const { fixture, el, c } = await render(ledger());
    c.startFee();
    http.expectOne(`${api}/prop-accounts/5/ledger/fee-candidates`).flush([]);
    await fixture.whenStable();
    expect(el.querySelector('form')!.textContent).toContain('Record the expense first');
    expect(el.querySelector('form button[type="submit"]')).toBeNull();

    c.startFee();
    http.expectOne(`${api}/prop-accounts/5/ledger/fee-candidates`).flush([{ transactionId: 40, date: '2026-10-20', description: 'Challenge', amount: usd(500) }]);
    await fixture.whenStable();
    c.linkFee();
    http.expectOne(`${api}/prop-accounts/5/ledger/fees`).flush({ title: 'Transaction 40 is already linked to a prop account.' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    expect(el.querySelector('form [role="alert"]')!.textContent).toContain('already linked');
  });

  it('links a payout, and removes a link only after asking', async () => {
    const payout = { id: 9, source: 'Test Firm', status: 'Received', amount: usd(1000), requestedOn: '2026-11-01', receivedOn: '2026-11-02', credited: usd(950) };
    const { fixture, el, c } = await render(ledger());
    c.startPayout();
    http.expectOne(`${api}/prop-accounts/5/ledger/payout-candidates`).flush([payout]);
    await fixture.whenStable();
    c.linkPayout();
    const link = http.expectOne(`${api}/prop-accounts/5/ledger/payouts/9`);
    expect(link.request.method).toBe('POST');
    link.flush(ledger({ payouts: [payout] }));
    await fixture.whenStable();
    expect(squash(el)).toContain('USD 950.00');

    const row = el.querySelector('.row-actions')!;
    (row.querySelector('button') as HTMLButtonElement).click();
    await fixture.whenStable();
    http.expectNone(`${api}/prop-accounts/5/ledger/payouts/9`);
    (el.querySelector('.row-actions .btn-danger') as HTMLButtonElement).click();
    const del = http.expectOne(`${api}/prop-accounts/5/ledger/payouts/9`);
    expect(del.request.method).toBe('DELETE');
    del.flush(ledger());
    await fixture.whenStable();
    expect(squash(el)).toContain('No payouts linked.');
  });
});

describe('PropAccountReportComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  async function render(report: PropAccountReport | 'forbidden') {
    TestBed.configureTestingModule({ imports: [PropAccountReportComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PropAccountReportComponent);
    await fixture.whenStable();
    const req = http.expectOne(`${api}/prop-accounts/fees-and-payouts`);
    if (report === 'forbidden') req.flush(null, { status: 403, statusText: 'Forbidden' });
    else req.flush(report);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('is hidden from a user who is not the owner', async () => {
    expect((await render('forbidden')).querySelector('section')).toBeNull();
  });

  it('answers which accounts cost money without paying back, per currency and unconverted', async () => {
    const el = await render({
      accounts: [
        { accountId: 1, firm: 'Firm A', nickname: 'Blown attempt', phase: 'Evaluation', status: 'Closed', economics: FEES_ONLY },
        { accountId: 2, firm: 'Firm A', nickname: 'Funded', phase: 'Funded', status: 'Active', economics: { outcome: 'PayoutsReceived', awaiting: [], feesCounted: 1, payoutsCounted: 1, lines: [{ currency: 'USD', fees: 500, payoutsReceived: 2000, net: 1500 }] } },
        { accountId: 3, firm: 'Firm B', nickname: 'New', phase: 'Evaluation', status: 'Active', economics: { outcome: 'NothingLinked', awaiting: [], feesCounted: 0, payoutsCounted: 0, lines: [] } }
      ],
      totals: [{ currency: 'USD', fees: 1200, payoutsReceived: 2000, net: 800 }],
      unlinkedReceivedPayouts: 2
    });

    const rows = [...el.querySelectorAll('tbody tr')].map(squash);
    expect(rows[0]).toContain('Blown attempt');
    expect(rows[0]).toContain('Fees, no payout received');
    expect(rows[0]).toContain('Awaiting USD 1,000.00, not counted');
    expect(rows[0]).toContain('USD -700.00');
    expect(rows[1]).toContain('Payouts received');
    expect(rows[1]).toContain('USD +1,500.00');
    expect(rows[2]).toContain('Nothing linked');
    expect(squash(el.querySelector('tfoot')!)).toContain('All accounts · USD');
    expect(squash(el.querySelector('tfoot')!)).toContain('USD +800.00');
    expect(squash(el)).toContain('nothing is converted');
    expect(squash(el)).toContain('2 received payouts are not linked to an account, so not counted here.');
  });

  it('leaves out the unlinked note and totals when there is nothing to say', async () => {
    const el = await render({ accounts: [], totals: [], unlinkedReceivedPayouts: 0 });
    expect(squash(el)).toContain('No prop firm accounts recorded.');
    expect(el.querySelector('tfoot')).toBeNull();
    expect(squash(el)).not.toContain('not linked');
  });
});
