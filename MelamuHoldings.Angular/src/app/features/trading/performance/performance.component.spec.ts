import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { PerformanceComponent } from './performance.component';
import { AnalyticsFilterOptions } from '../models/analytics-filter.model';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;
const FIGURES = ['equity-curve', 'by-strategy', 'by-day', 'by-session'];

const emptySummary = {
  totalTrades: 0, winRate: 0, expectancy: 0, averageRR: 0, profitFactor: 0,
  totalPnL: 0, maxDrawdown: 0, winCount: 0, lossCount: 0, breakevenCount: 0, minimumSample: 20, insufficientData: true
};

const options: AnalyticsFilterOptions = {
  strategies: [{ value: '4', label: 'ORB' }], instruments: ['NQ'], sessions: ['London', 'New York'], accounts: [],
  journalFields: [{ id: 'mood', label: 'Mood', type: 'SingleSelect', options: [{ value: 'Calm', label: 'Calm' }] }]
};

const emptyAdherence = {
  completedTrades: 0, withChecklist: 0, incompleteChecklist: 0, withoutChecklist: 0, compliant: 0, nonCompliant: 0, adherenceRate: null,
  minimumSample: 20, mostViolated: [], byStrategy: [],
  compliantResults: { trades: 0, wins: 0, netPnL: 0, insufficientData: true }, nonCompliantResults: { trades: 0, wins: 0, netPnL: 0, insufficientData: true }
};

describe('PerformanceComponent', () => {
  let http: HttpTestingController;

  function configure(query: Record<string, string> = {}) {
    TestBed.configureTestingModule({
      imports: [PerformanceComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        ...(Object.keys(query).length ? [{ provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } }] : [])
      ]
    });
    http = TestBed.inject(HttpTestingController);
  }

  afterEach(() => http.verify());

  async function render(query: Record<string, string> = {}) {
    configure(query);
    const fixture = TestBed.createComponent(PerformanceComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/analytics/filter-options`).flush(options);
    // The optional AI question panel asks whether AI is on; here it is off, so the panel stays hidden
    http.expectOne(`${api}/ai-review/status`).flush({ enabled: false, model: null, maxComparableTrades: 10, maxJournalTrades: 30 });
    return fixture;
  }

  function flushAll(summary: object, adherence: object = emptyAdherence) {
    http.expectOne(r => r.url === `${api}/analytics/summary`).flush(summary);
    for (const path of FIGURES) http.expectOne(r => r.url === `${api}/analytics/${path}`).flush([]);
    http.expectOne(r => r.url === `${api}/analytics/adherence`).flush(adherence);
  }

  it('shows a loading state until the data arrives', async () => {
    const fixture = await render();

    expect(fixture.nativeElement.textContent).toContain('Loading performance data...');
    flushAll(emptySummary);
  });

  it('shows an empty state with a link to import when there are no trades', async () => {
    const fixture = await render();
    flushAll(emptySummary);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('No trades recorded yet');
    expect(fixture.nativeElement.querySelector('a[href="/trading/import"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-analytics-filter-bar')).toBeNull();   // nothing to filter yet
  });

  it('leads with total P&L and switches the breakdown table between views', async () => {
    const fixture = await render();
    http.expectOne(`${api}/analytics/summary`).flush({
      ...emptySummary, totalTrades: 3, totalPnL: 425.5, winRate: 66.7, winCount: 2, lossCount: 1
    });
    http.expectOne(`${api}/analytics/equity-curve`).flush([
      { date: '2026-10-05', cumulativePnL: 100, dailyPnL: 100, drawdown: 0 },
      { date: '2026-10-06', cumulativePnL: 425.5, dailyPnL: 325.5, drawdown: 0 }
    ]);
    http.expectOne(`${api}/analytics/by-strategy`).flush([
      { strategyName: 'ORB', totalTrades: 2, winRate: 100, averageRR: 1.5, totalPnL: 525.5, profitFactor: 0 },
      { strategyName: 'Pullback', totalTrades: 1, winRate: 0, averageRR: -1, totalPnL: -100, profitFactor: 0 }
    ]);
    http.expectOne(`${api}/analytics/by-day`).flush([{ label: 'Monday', totalTrades: 3, winRate: 66.7, totalPnL: 425.5, averageRR: 0.67 }]);
    http.expectOne(`${api}/analytics/by-session`).flush([]);
    http.expectOne(`${api}/analytics/adherence`).flush(emptyAdherence);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('.metric-lead')?.textContent).toContain('+425.50');
    expect(el.textContent).toContain('from 5 Oct 2026 to 6 Oct 2026');
    expect(el.textContent).toContain('3 trades recorded: fewer than 20, so these figures are insufficient data');
    expect([...el.querySelectorAll('#breakdown-panel tbody tr')].map(r => r.querySelector('td')?.textContent))
      .toEqual(['ORB', 'Pullback']);
    expect(el.querySelector('#breakdown-panel tbody tr:last-child')?.textContent).toContain('−100.00');
    expect(el.textContent).toContain('Rule adherence');

    (el.querySelector('#tab-day') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(el.querySelector('#breakdown-panel thead th')?.textContent).toBe('Day');
    expect(el.querySelector('#breakdown-panel tbody')?.textContent).toContain('Monday');
  });

  it('shows an error with Retry when a request fails, and recovers on retry', async () => {
    const fixture = await render();
    http.expectOne(`${api}/analytics/summary`).flush(null, { status: 0, statusText: 'Unknown Error' });
    // forkJoin abandons the remaining calls once one fails
    const siblings = http.match(req => req.url.startsWith(`${api}/analytics/`));
    expect(siblings.length).toBe(5);
    expect(siblings.every(req => req.cancelled)).toBe(true);
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Could not load performance data');
    expect(text).toContain('The API could not be reached');

    (fixture.nativeElement.querySelector('.btn-retry') as HTMLButtonElement).click();
    await fixture.whenStable();
    flushAll(emptySummary);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('No trades recorded yet');
  });

  it('sends the filter from the address with every figure and says what is applied', async () => {
    const fixture = await render({ session: 'London', strategyId: '4', adherence: 'Followed', answerField: 'mood', answerValue: 'Calm', bogus: 'x' });
    const requests = http.match(r => r.url.startsWith(`${api}/analytics/`));
    expect(requests.length).toBe(6);
    for (const r of requests) {
      expect(r.request.params.get('session')).toBe('London');
      expect(r.request.params.get('strategyId')).toBe('4');
      expect(r.request.params.get('adherence')).toBe('Followed');
      expect(r.request.params.get('answerValue')).toBe('Calm');
      expect(r.request.params.has('bogus')).toBe(false);
    }
    requests.find(r => r.request.url.endsWith('/summary'))!.flush({ ...emptySummary, totalTrades: 2, totalPnL: 50 });
    requests.filter(r => !r.request.url.endsWith('/summary') && !r.request.url.endsWith('/adherence')).forEach(r => r.flush([]));
    requests.find(r => r.request.url.endsWith('/adherence'))!.flush(emptyAdherence);
    await fixture.whenStable();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Showing only: ORB · London · Followed every mandatory rule · Mood: Calm');
    expect(text).toContain('2 trades match these filters: fewer than 20');
    expect(text).toContain('closed trades matching the filters');
  });

  it('says when no trades match, and clearing the filters reloads everything and updates the address', async () => {
    const fixture = await render({ session: 'Tokyo' });
    flushAll(emptySummary);
    await fixture.whenStable();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    expect(fixture.nativeElement.textContent).toContain('No closed trades match these filters');
    (fixture.nativeElement.querySelector('.empty-state button') as HTMLButtonElement).click();

    expect(navigate).toHaveBeenCalledWith([], expect.objectContaining({ queryParams: {}, replaceUrl: true }));
    const summary = http.expectOne(r => r.url === `${api}/analytics/summary`);
    expect(summary.request.params.keys()).toEqual([]);
    summary.flush(emptySummary);
    for (const path of FIGURES) http.expectOne(r => r.url === `${api}/analytics/${path}`).flush([]);
    http.expectOne(r => r.url === `${api}/analytics/adherence`).flush(emptyAdherence);
  });
});
