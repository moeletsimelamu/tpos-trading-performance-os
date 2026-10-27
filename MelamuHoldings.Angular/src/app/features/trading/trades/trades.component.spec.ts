import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TradesComponent } from './trades.component';
import { Trade } from '../models/trade.model';
import { environment } from '../../../../environments/environment';

function trade(overrides: Partial<Trade>): Trade {
  return {
    id: 1, tradeDate: '2026-10-05', entryTime: '09:35:00', instrument: 'NQ', direction: 'Long',
    entryPrice: 100, exitPrice: 101, riskAmount: 100, pnL: 100, rr: 1.33, strategyName: 'ORB',
    session: 'New York', dayOfWeek: 'Monday', tradeDuration: 10, isManualOverride: false,
    ...overrides
  };
}

describe('TradesComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TradesComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function renderWith(trades: Trade[]) {
    const fixture = TestBed.createComponent(TradesComponent);
    await fixture.whenStable();
    http.expectOne(`${environment.apiUrl}/trades`).flush(trades);
    await fixture.whenStable();
    return fixture;
  }

  it('renders the RR column from the API `rr` property', async () => {
    const fixture = await renderWith([trade({ rr: 1.33 })]);

    const rrCell = fixture.nativeElement.querySelectorAll('tbody tr td')[7] as HTMLElement;
    expect(rrCell.textContent?.trim()).toBe('1.33R');
  });

  it('filters by result and direction', async () => {
    const fixture = await renderWith([
      trade({ id: 1, pnL: 100, direction: 'Long' }),
      trade({ id: 2, pnL: -50, direction: 'Short' }),
      trade({ id: 3, pnL: 0, direction: 'Long' })
    ]);
    const component = fixture.componentInstance;

    component.resultFilter.set('loss');
    expect(component.filtered().map(t => t.id)).toEqual([2]);

    component.resultFilter.set('');
    component.directionFilter.set('Long');
    expect(component.filtered().map(t => t.id)).toEqual([1, 3]);

    component.clearFilters();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('3 of 3 trades');
  });

  it('distinguishes an empty journal from an empty filter result', async () => {
    const empty = await renderWith([]);
    expect(empty.nativeElement.textContent).toContain('No trades recorded yet');
  });

  it('keeps the trade and shows an error when delete fails', async () => {
    const fixture = await renderWith([trade({ id: 7 })]);

    fixture.componentInstance.deleteTrade(7);
    http.expectOne(`${environment.apiUrl}/trades/7`).flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect(fixture.componentInstance.trades().length).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('Trade was not deleted');
  });

  it('asks before deleting: the first click only offers Confirm delete', async () => {
    const fixture = await renderWith([trade({ id: 7 })]);
    const el = fixture.nativeElement as HTMLElement;

    (el.querySelector('button[aria-label="Delete trade 7"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    http.expectNone(`${environment.apiUrl}/trades/7`);

    const confirm = [...el.querySelectorAll('button')].find(b => b.textContent?.trim() === 'Confirm delete') as HTMLButtonElement;
    confirm.click();
    http.expectOne(`${environment.apiUrl}/trades/7`).flush(null);
    await fixture.whenStable();

    expect(fixture.componentInstance.trades().length).toBe(0);
  });
});
