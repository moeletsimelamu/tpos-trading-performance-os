import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ImportComponent } from './import.component';
import { ImportMatch } from '../models/journal-view.model';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;

function match(overrides: Partial<ImportMatch> = {}): ImportMatch {
  return {
    id: 7, uploadLogId: 1, fileName: 'platform.csv', rowNumber: 2, status: 'Pending', createdAt: '2026-10-28T08:00:00Z',
    row: { rowNumber: 2, tradeDate: '2026-10-20', entryTime: '09:30:00', instrument: 'NQ', direction: 'Long', entryPrice: 18000, exitPrice: 18040, riskAmount: 200, pnL: 800, rr: 4, strategy: 'ORB', session: 'New York' },
    trade: { id: 31, status: 'Open', tradeDate: '2026-10-20', instrument: 'NQ', direction: 'Long', entryTime: '09:30:00', entryPrice: 18000, exitPrice: null, pnL: null },
    ...overrides
  };
}

describe('ImportComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ImportComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(matches: ImportMatch[] = []) {
    const fixture = TestBed.createComponent(ImportComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/upload/logs`).flush([]);
    http.expectOne(`${api}/upload/matches`).flush(matches);
    await fixture.whenStable();
    return fixture;
  }

  it('rejects a non-CSV file inline without calling the API', async () => {
    const fixture = await render();

    fixture.componentInstance.upload(new File(['x'], 'notes.txt'));
    await fixture.whenStable();

    http.expectNone(`${api}/upload`);
    expect(fixture.nativeElement.textContent).toContain('Please choose a .csv file.');
  });

  it('shows the import result, with skipped and matched rows, and refreshes history and matches', async () => {
    const fixture = await render();

    fixture.componentInstance.upload(new File(['TradeDate'], 'journal.csv'));
    http.expectOne(`${api}/upload`).flush({
      uploadLogId: 1, fileName: 'journal.csv', totalRows: 5, parsedRows: 2, failedRows: 1, duplicateRows: 1, matchedRows: 1,
      status: 'Completed', errors: ['Row 3: Direction must be \'Long\' or \'Short\'.']
    });
    http.expectOne(`${api}/upload/logs`).flush([]);
    http.expectOne(`${api}/upload/matches`).flush([match()]);
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Import complete');
    expect(text).toContain('Already imported');
    expect(text).toContain('Match your journal');
    expect(text).toContain('Row 3: Direction must be');
    expect(text).toContain('Rows matching your journal');
  });

  it('shows the API error when the upload fails', async () => {
    const fixture = await render();

    fixture.componentInstance.upload(new File(['x'], 'journal.csv'));
    http.expectOne(`${api}/upload`)
      .flush({ title: 'Only CSV files are accepted.' }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Upload failed: Only CSV files are accepted. (HTTP 400)');
    expect(fixture.componentInstance.isUploading()).toBe(false);
  });

  it('compares a matched row with the journaled trade and links it, naming what was filled', async () => {
    const fixture = await render([match()]);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Row 2 of platform.csv');
    expect(text).toContain('Your trade');

    fixture.componentInstance.decide(match(), 'link');
    http.expectOne(`${api}/upload/matches/7/link`).flush({ match: match({ status: 'Linked' }), filledFields: ['ExitPrice', 'PnL', 'RR'] });
    http.expectOne(`${api}/upload/logs`).flush([]);
    await fixture.whenStable();

    expect(fixture.componentInstance.matches()).toEqual([]);
    expect(fixture.nativeElement.textContent).toContain('Row 2 (NQ) was linked to your trade. Filled in: exit price, P&L, R-multiple.');
  });

  it('offers only import or leave out when the journaled trade was deleted', async () => {
    const fixture = await render([match({ trade: null })]);
    const buttons = [...fixture.nativeElement.querySelectorAll('.match-actions button')].map((b: Element) => b.textContent!.trim());

    expect(buttons).toEqual(['Import as a separate trade', 'Don\'t import']);
    fixture.componentInstance.decide(match({ trade: null }), 'dismiss');
    http.expectOne(`${api}/upload/matches/7/dismiss`).flush(match({ status: 'Dismissed' }));
    http.expectOne(`${api}/upload/logs`).flush([]);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Row 2 (NQ) was left out.');
  });
});
