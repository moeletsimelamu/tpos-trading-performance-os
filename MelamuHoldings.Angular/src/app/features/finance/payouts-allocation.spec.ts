import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { PayoutDetailComponent } from './payouts/payout-detail.component';
import { AllocationDetailComponent } from './allocations/allocation-detail.component';
import { AllocationPolicyComponent } from './allocations/allocation-policy.component';
import { PayoutsComponent } from './payouts/payouts.component';
import { Allocation, AllocationPolicy, Payout } from './finance.models';
import { account, FUNDS } from './finance-testing';
import { environment } from '../../../environments/environment';

const api = environment.apiUrl;
const ACCOUNTS = [
  account({ id: 10, name: 'Operating ZAR', currency: 'ZAR' }),
  account({ id: 12, name: 'Payouts USD', currency: 'USD' }),
  account({ id: 13, name: 'Reserve USD', currency: 'USD' })
];
const POLICY: AllocationPolicy = {
  id: 1, version: 1, isActive: true, createdAt: '2026-10-27T00:00:00Z',
  lines: [{ fundId: 2, fundName: 'Operating Capital', percentage: 40 }, { fundId: 3, fundName: 'Business Continuity', percentage: 60 }]
};

function payout(overrides: Partial<Payout>): Payout {
  return {
    id: 7, source: 'Prop firm', status: 'Approved', requested: { amount: 1350, currency: 'USD' }, requestedOn: '2026-10-27',
    approved: { amount: 1350, currency: 'USD' }, approvedOn: '2026-10-27', createdAt: '2026-10-27T00:00:00Z', ...overrides
  };
}

function configure(component: unknown, id = '7') {
  TestBed.configureTestingModule({
    imports: [component as never],
    providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id }), queryParamMap: convertToParamMap({}) } } }
    ]
  });
  return TestBed.inject(HttpTestingController);
}

describe('PayoutDetailComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  async function render(p: Payout) {
    http = configure(PayoutDetailComponent);
    const fixture = TestBed.createComponent(PayoutDetailComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/payouts/7`).flush(p);
    http.expectOne(`${api}/financial-accounts`).flush(ACCOUNTS);
    http.expectOne(`${api}/allocation-policies`).flush([POLICY]);
    await fixture.whenStable();
    return fixture;
  }

  it('prefills the receipt with the approved amount and keeps its key for a retry', async () => {
    const fixture = await render(payout({}));
    const c = fixture.componentInstance;

    // Two USD accounts: the operator chooses; the amount defaults to what was approved
    expect(c.receiveForm.controls.accountId.value).toBeNull();
    expect(c.receiveForm.controls.amountCredited.value).toBe(1350);
    c.receiveForm.patchValue({ accountId: 12, amountCredited: 1340, fee: 10 });

    c.receive();
    const first = http.expectOne(`${api}/payouts/7/receive`);
    expect(first.request.body).toEqual({ accountId: 12, amountCredited: 1340, fee: 10, date: c.receiveForm.controls.date.value, reference: undefined });
    const key = first.request.headers.get('Idempotency-Key');
    first.flush(null, { status: 0, statusText: 'Unknown Error' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Not saved');

    c.receive();
    const retry = http.expectOne(`${api}/payouts/7/receive`);
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    retry.flush(payout({ status: 'Received', credited: { amount: 1340, currency: 'USD' } }));
    await fixture.whenStable();
    // A received payout without an allocation loads the preview straight away
    http.expectOne(`${api}/allocations/preview`).flush({
      payoutId: 7, policyId: 1, policyVersion: 1, sourceAccountId: 12, sourceAccountName: 'Payouts USD',
      total: { amount: 1340, currency: 'USD' }, lines: []
    });
  });

  it('shows the effective rate when the money lands in another currency', async () => {
    const fixture = await render(payout({ requested: { amount: 1000, currency: 'USD' }, approved: { amount: 1000, currency: 'USD' } }));
    const c = fixture.componentInstance;

    c.receiveForm.patchValue({ accountId: 10, amountCredited: 18100, fee: 150 });

    expect(c.impliedRate()).toBe('18.2500');
  });

  it('previews with the active policy and confirms the previewed policy with a key', async () => {
    const fixture = await render(payout({ status: 'Received', credited: { amount: 1350, currency: 'USD' }, receivedIntoAccountId: 12 }));
    const preview = http.expectOne(`${api}/allocations/preview`);
    expect(preview.request.body).toEqual({ payoutId: 7, policyId: 1 });
    preview.flush({
      payoutId: 7, policyId: 1, policyVersion: 1, sourceAccountId: 12, sourceAccountName: 'Payouts USD', total: { amount: 1350, currency: 'USD' },
      lines: [
        { fundId: 2, fundName: 'Operating Capital', percentage: 40, amount: { amount: 540, currency: 'USD' } },
        { fundId: 3, fundName: 'Business Continuity', percentage: 60, amount: { amount: 810, currency: 'USD' } }
      ]
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('table[aria-label="Allocation preview"]').textContent).toContain('USD 810.00');

    fixture.componentInstance.confirm();
    const confirm = http.expectOne(`${api}/allocations`);
    expect(confirm.request.body).toEqual({ payoutId: 7, policyId: 1 });
    expect(confirm.request.headers.get('Idempotency-Key')).toMatch(/^[a-f0-9]{32}$/);
    confirm.flush({ id: 3 });
    http.expectOne(`${api}/payouts/7`).flush(payout({ status: 'Received', allocationId: 3, allocationStatus: 'Confirmed' }));
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Open allocation #3');
  });
});

describe('AllocationDetailComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  const allocation: Allocation = {
    id: 3, payoutId: 7, payoutSource: 'Prop firm', policyId: 1, policyVersion: 1, sourceAccountId: 12, sourceAccountName: 'Payouts USD',
    status: 'Confirmed', total: { amount: 1000, currency: 'USD' }, settled: { amount: 0, currency: 'USD' }, outstanding: { amount: 1000, currency: 'USD' },
    confirmedAt: '2026-10-27T00:00:00Z',
    lines: [{
      id: 21, fundId: 2, fundName: 'Operating Capital', percentage: 40, amount: { amount: 400, currency: 'USD' },
      settled: { amount: 0, currency: 'USD' }, outstanding: { amount: 400, currency: 'USD' }, settlements: []
    }]
  };

  async function render() {
    http = configure(AllocationDetailComponent, '3');
    const fixture = TestBed.createComponent(AllocationDetailComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/allocations/3`).flush(allocation);
    http.expectOne(`${api}/financial-accounts`).flush(ACCOUNTS);
    await fixture.whenStable();
    return fixture;
  }

  it('earmarks in place without a destination account', async () => {
    const fixture = await render();
    const c = fixture.componentInstance;
    c.startSettling(allocation.lines[0]);

    expect(c.form.controls.amount.value).toBe(400);
    c.settle();
    const req = http.expectOne(`${api}/allocations/3/lines/21/settle`);
    expect(req.request.body).toEqual({ method: 'Earmark', amount: 400, date: c.form.controls.date.value, reference: undefined });
    req.flush({ ...allocation, status: 'PartiallySettled' });
  });

  it('offers same-currency accounts for a transfer and other currencies for an exchange', async () => {
    const fixture = await render();
    const c = fixture.componentInstance;
    c.startSettling(allocation.lines[0]);

    c.selectMethod('Transfer');
    expect(c.destinations().map(a => a.name)).toEqual(['Reserve USD']);
    c.selectMethod('Exchange');
    expect(c.destinations().map(a => a.name)).toEqual(['Operating ZAR']);
  });

  it('settles by exchange with the amount received, fee and quoted rate', async () => {
    const fixture = await render();
    const c = fixture.componentInstance;
    c.startSettling(allocation.lines[0]);
    c.selectMethod('Exchange');
    c.form.patchValue({ amount: 390, toAccountId: 10, amountReceived: 7117.5, fee: 10, feeChargedTo: 'Sending', quotedRate: 18.3 });

    expect(c.impliedRate()).toBe('18.2500');
    c.settle();
    const req = http.expectOne(`${api}/allocations/3/lines/21/settle`);
    expect(req.request.body).toMatchObject({ method: 'Exchange', amount: 390, toAccountId: 10, amountReceived: 7117.5, fee: 10, feeChargedTo: 'Sending', quotedRate: 18.3 });
    req.flush(allocation);
  });

  it('only offers cancelling while nothing has been settled', async () => {
    const fixture = await render();
    expect(fixture.componentInstance.canCancel()).toBe(true);

    fixture.componentInstance.allocation.set({ ...allocation, settled: { amount: 40, currency: 'USD' }, status: 'PartiallySettled' });
    expect(fixture.componentInstance.canCancel()).toBe(false);
  });
});

describe('AllocationPolicyComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  it('starts a new version from the active split and only saves at exactly 100%', async () => {
    http = configure(AllocationPolicyComponent);
    const fixture = TestBed.createComponent(AllocationPolicyComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/allocation-policies`).flush([POLICY]);
    http.expectOne(`${api}/funds`).flush([...FUNDS, { id: 2, name: 'Operating Capital', isSystem: false, balances: [] }]);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.startDraft();
    expect(c.nextVersion()).toBe(2);
    expect(c.draft.map(l => [l.fundName, l.percentage])).toEqual([['Business Continuity', 60], ['Operating Capital', 40]]);
    expect(c.total()).toBe(100);

    c.draft[0].percentage = 50;
    c.touch();
    expect(c.total()).toBe(90);
    c.save();
    http.expectNone(`${api}/allocation-policies`);

    c.draft[0].percentage = 33.33;
    c.draft[1].percentage = 66.67;
    c.touch();
    c.save();
    const req = http.expectOne(r => r.method === 'POST' && r.url === `${api}/allocation-policies`);
    expect(req.request.body).toEqual({ lines: [{ fundId: 3, percentage: 33.33 }, { fundId: 2, percentage: 66.67 }], activate: true });
  });
});

describe('PayoutsComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  it('totals expected payouts per currency, labelled as not cash', async () => {
    http = configure(PayoutsComponent);
    const fixture = TestBed.createComponent(PayoutsComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/payouts`).flush([
      payout({ id: 1, status: 'Requested', approved: null }),
      payout({ id: 2, status: 'Approved', approved: { amount: 600, currency: 'USD' } }),
      payout({ id: 3, status: 'Received', credited: { amount: 500, currency: 'USD' } })
    ]);
    http.expectOne(`${api}/currencies`).flush([{ code: 'USD', minorUnits: 2 }]);
    await fixture.whenStable();

    expect(fixture.componentInstance.expected()).toEqual([{ currency: 'USD', amount: 1950 }]);
    expect(fixture.nativeElement.textContent).toContain('Not cash');
  });
});
