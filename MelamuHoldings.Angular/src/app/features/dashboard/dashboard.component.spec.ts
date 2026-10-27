import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { DashboardComponent } from './dashboard.component';
import { environment } from '../../../environments/environment';

const api = environment.apiUrl;

const emptySummary = {
  totalTrades: 0, winRate: 0, expectancy: 0, averageRR: 0, profitFactor: 0,
  totalPnL: 0, maxDrawdown: 0, winCount: 0, lossCount: 0, breakevenCount: 0
};

describe('DashboardComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render() {
    const fixture = TestBed.createComponent(DashboardComponent);
    await fixture.whenStable();
    return fixture;
  }

  function flushAll(summary: object) {
    http.expectOne(`${api}/analytics/summary`).flush(summary);
    for (const path of ['equity-curve', 'by-strategy', 'by-day', 'by-session'])
      http.expectOne(`${api}/analytics/${path}`).flush([]);
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
    expect(fixture.nativeElement.querySelector('a[href="/upload"]')).not.toBeNull();
  });

  it('shows an error with Retry when a request fails, and recovers on retry', async () => {
    const fixture = await render();
    http.expectOne(`${api}/analytics/summary`).flush(null, { status: 0, statusText: 'Unknown Error' });
    // forkJoin abandons the remaining calls once one fails
    const siblings = http.match(req => req.url.startsWith(`${api}/analytics/`));
    expect(siblings.length).toBe(4);
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
});
