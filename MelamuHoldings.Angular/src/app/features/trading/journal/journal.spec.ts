import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { TradeFormComponent } from './trade-form.component';
import { TradeEntryComponent } from './trade-entry.component';
import { TradesComponent } from '../trades/trades.component';
import { Trade } from '../models/trade.model';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;

function trade(overrides: Partial<Trade>): Trade {
  return {
    id: 5, status: 'Closed', isImported: false, tradeDate: '2026-10-26', instrument: 'EURUSD', direction: 'Short',
    entryTime: '09:31:00', entryPrice: 1.0825, exitPrice: 1.08, pnL: 375, riskAmount: 150, rr: 2.5,
    session: 'London', dayOfWeek: 'Monday', isManualOverride: false, ...overrides
  };
}

function configure(component: unknown, id?: number) {
  TestBed.configureTestingModule({
    imports: [component as never],
    providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(id ? { id: String(id) } : {}) } } }
    ]
  });
  return TestBed.inject(HttpTestingController);
}

describe('TradeFormComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  it('saves a draft with only date and instrument, sending empty fields as null, with an idempotency key', async () => {
    http = configure(TradeFormComponent);
    const fixture = TestBed.createComponent(TradeFormComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;
    c.form.patchValue({ tradeDate: '2026-10-26', instrument: ' EURUSD ', session: 'London' });

    c.save('Draft');
    const req = http.expectOne(`${api}/trades`);
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('Idempotency-Key')).toMatch(/^[a-f0-9]{32}$/);
    expect(req.request.body).toMatchObject({ status: 'Draft', instrument: 'EURUSD', direction: null, entryPrice: null, pnL: null, session: 'London' });
    req.flush(trade({ status: 'Draft' }));
  });

  it('names what a closed trade still needs instead of sending it', async () => {
    http = configure(TradeFormComponent);
    const fixture = TestBed.createComponent(TradeFormComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;
    c.form.patchValue({ instrument: 'EURUSD', direction: 'Short', entryTime: '09:31', entryPrice: 1.0825 });

    c.save('Closed');
    http.expectNone(`${api}/trades`);
    expect(c.missing()).toEqual(['exit price', 'realised P&L']);

    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Still needed for this status: exit price, realised P&L.');
  });

  it('keeps the key on a retry and shows the R it will calculate', async () => {
    http = configure(TradeFormComponent);
    const fixture = TestBed.createComponent(TradeFormComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;
    c.form.patchValue({ instrument: 'EURUSD', direction: 'Short', entryTime: '09:31', entryPrice: 1.0825, exitPrice: 1.08, pnL: 375, riskAmount: 150 });
    expect(c.calculatedR()).toBe('2.50');

    c.save('Closed');
    const first = http.expectOne(`${api}/trades`);
    const key = first.request.headers.get('Idempotency-Key');
    first.flush(null, { status: 0, statusText: 'Unknown Error' });

    c.save('Closed');
    const retry = http.expectOne(`${api}/trades`);
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    retry.flush(trade({}));
  });

  it('edits a reviewed trade without changing its status', async () => {
    http = configure(TradeFormComponent, 5);
    const fixture = TestBed.createComponent(TradeFormComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/trades/5`).flush(trade({ status: 'Reviewed', reviewNotes: 'Good trade' }));
    await fixture.whenStable();
    const c = fixture.componentInstance;

    expect(c.isReviewed()).toBe(true);
    expect(c.form.controls.entryTime.value).toBe('09:31');
    c.form.patchValue({ notes: 'Added context later' });
    c.save(null);
    const req = http.expectOne(`${api}/trades/5`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.status).toBeUndefined();
    expect(req.request.body.notes).toBe('Added context later');
    req.flush(trade({ status: 'Reviewed' }));
  });
});

describe('TradeEntryComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  async function render(t: Trade) {
    http = configure(TradeEntryComponent, 5);
    const fixture = TestBed.createComponent(TradeEntryComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/trades/5`).flush(t);
    await fixture.whenStable();
    return fixture;
  }

  it('shows missing execution values as dashes and says a draft does not count', async () => {
    const fixture = await render(trade({ status: 'Draft', direction: null, entryPrice: null, exitPrice: null, pnL: null, rr: null, riskAmount: null }));
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Not counted in performance until it is closed.');
    expect(text).toContain('Review the trade once it is closed.');
    expect(text).not.toContain('0.00');
  });

  it('marks a closed trade reviewed with the trader notes', async () => {
    const fixture = await render(trade({}));
    const c = fixture.componentInstance;
    c.reviewNotes = 'Valid setup; exited at target.';

    c.review();
    const req = http.expectOne(`${api}/trades/5/review`);
    expect(req.request.body).toEqual({ reviewNotes: 'Valid setup; exited at target.' });
    req.flush(trade({ status: 'Reviewed', reviewNotes: 'Valid setup; exited at target.', reviewedAt: '2026-10-28T10:00:00Z' }));
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Valid setup; exited at target.');
  });

  it('deletes only after confirmation and returns to the list', async () => {
    const fixture = await render(trade({ status: 'Draft' }));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture.componentInstance.confirmingDelete.set(true);

    fixture.componentInstance.remove();
    http.expectOne(`${api}/trades/5`).flush(null);

    expect(navigate).toHaveBeenCalledWith('/trading/trades');
  });
});

describe('TradesComponent status filter', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  it('filters by status and leaves results-less drafts out of a result filter', async () => {
    http = configure(TradesComponent);
    const fixture = TestBed.createComponent(TradesComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/trades`).flush([
      trade({ id: 1, status: 'Draft', pnL: null }), trade({ id: 2, status: 'Open', pnL: null }), trade({ id: 3 }), trade({ id: 4, status: 'Reviewed', pnL: -50 })
    ]);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.statusFilter.set('Draft');
    expect(c.filtered().map(t => t.id)).toEqual([1]);

    c.statusFilter.set('All');
    c.resultFilter.set('win');
    expect(c.filtered().map(t => t.id)).toEqual([3]);
    expect(c.count('Reviewed')).toBe(1);
  });
});
