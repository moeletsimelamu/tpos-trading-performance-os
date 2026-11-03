import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { InvestmentsComponent } from './investments.component';
import { InvestmentAccount, Investments, Valuation } from '../finance.models';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;
const zar = (amount: number) => ({ amount, currency: 'ZAR' });

const latest: Valuation = {
  id: 4, accountId: 7, valuedOn: '2026-10-25', value: zar(27000), note: 'Statement', recordedAt: '2026-10-25T10:00:00Z',
  recordedBalance: zar(25450), unrealised: zar(1550)
};

function account(overrides: Partial<InvestmentAccount> = {}): InvestmentAccount {
  return {
    accountId: 7, accountName: 'EasyEquities', currency: 'ZAR', institution: 'Broker', ownership: 'Company', status: 'Active', asOf: '2026-10-25',
    latest, netContributions: zar(25000), recordedIncome: zar(500), recordedCosts: zar(-50), recordedBalance: zar(25450),
    unrealised: zar(1550), gainOnContributions: zar(2000), transactionsSinceValuation: 1, movedSinceValuation: zar(3000), currentRecordedBalance: zar(28450),
    ...overrides
  };
}

describe('InvestmentsComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  async function render(data: Investments) {
    TestBed.configureTestingModule({ imports: [InvestmentsComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(InvestmentsComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/investments`).flush(data);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement, c: fixture.componentInstance };
  }

  const text = (el: Element) => el.textContent!.trim().replace(/\s+/g, ' ');
  const button = (el: HTMLElement, label: string) => [...el.querySelectorAll('button')].find(b => text(b) === label) as HTMLButtonElement;

  it('labels the gain unrealised, shows the ledger figures it is measured against, and flags later movements', async () => {
    const { el } = await render({
      accounts: [account(), account({ accountId: 8, accountName: 'Idle broker', latest: null, unrealised: null, gainOnContributions: null, transactionsSinceValuation: 0 })],
      totals: [{ currency: 'ZAR', value: 27000, netContributions: 25000, unrealised: 1550, accounts: 1 }]
    });

    expect([...el.querySelectorAll('.metric-strip .metric')].map(m => [...m.children].map(text))).toEqual([
      ['Valued · ZAR', 'ZAR 27,000.00', '1 account, at their latest valuations'],
      ['Unrealised · ZAR', 'ZAR +1,550.00', 'Not cash until sold and withdrawn']
    ]);
    const [valued, idle] = [...el.querySelectorAll('section.account')];
    expect([...valued.querySelectorAll('.figures > div')].map(d => [...d.children].map(text))).toEqual([
      ['Value on 2026-10-25', 'ZAR 27,000.00'], ['Net contributions', 'ZAR 25,000.00'], ['Recorded income', 'ZAR 500.00'],
      ['Recorded costs', 'ZAR -50.00'], ['Recorded in the ledger', 'ZAR 25,450.00'], ['Unrealised gain or loss', 'ZAR +1,550.00']
    ]);
    expect(text(valued)).toContain('Against what was put in, the account is ZAR +2,000.00');
    expect(text(valued.querySelector('.notice-warning')!)).toContain('1 transaction since this valuation (ZAR +3,000.00). They are not in the gain or loss');
    expect(text(idle)).toContain('Not valued yet, so there is no gain or loss to show.');
    expect(idle.querySelector('.lead')).toBeNull();
  });

  it('records a valuation, then reloads; deleting one asks first', async () => {
    const { fixture, el, c } = await render({ accounts: [account()], totals: [] });
    button(el, 'Record valuation').click();
    await fixture.whenStable();
    c.form.setValue({ valuedOn: '2026-10-30', value: 29000, note: ' Month end ' });
    c.record(c.data()!.accounts[0]);
    const req = http.expectOne(`${api}/investments/7/valuations`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ valuedOn: '2026-10-30', value: 29000, note: 'Month end' });
    req.flush({ ...latest, id: 5 });
    http.expectOne(`${api}/investments`).flush({ accounts: [account()], totals: [] });
    await fixture.whenStable();
    expect(c.recording()).toBeNull();

    button(el, 'All valuations').click();
    http.expectOne(`${api}/investments/7/valuations`).flush([latest]);
    await fixture.whenStable();
    expect([...el.querySelectorAll('tbody tr td')].map(text).slice(0, 4)).toEqual(['2026-10-25Statement', 'ZAR 27,000.00', 'ZAR 25,450.00', 'ZAR +1,550.00']);

    button(el, 'Delete').click();
    await fixture.whenStable();
    button(el, 'Delete it').click();
    const del = http.expectOne(`${api}/investments/7/valuations/4`);
    expect(del.request.method).toBe('DELETE');
    del.flush(null);
    http.expectOne(`${api}/investments`).flush({ accounts: [account({ latest: null, unrealised: null, gainOnContributions: null })], totals: [] });
    http.expectOne(`${api}/investments/7/valuations`).flush([]);
    await fixture.whenStable();
    expect(el.textContent).toContain('No valuations recorded.');
  });
});
