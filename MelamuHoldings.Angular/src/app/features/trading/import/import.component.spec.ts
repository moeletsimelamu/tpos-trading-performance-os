import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ImportComponent } from './import.component';
import { environment } from '../../../../environments/environment';

describe('ImportComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ImportComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render() {
    const fixture = TestBed.createComponent(ImportComponent);
    await fixture.whenStable();
    http.expectOne(`${environment.apiUrl}/upload/logs`).flush([]);
    await fixture.whenStable();
    return fixture;
  }

  it('rejects a non-CSV file inline without calling the API', async () => {
    const fixture = await render();

    fixture.componentInstance.upload(new File(['x'], 'notes.txt'));
    await fixture.whenStable();

    http.expectNone(`${environment.apiUrl}/upload`);
    expect(fixture.nativeElement.textContent).toContain('Please choose a .csv file.');
  });

  it('shows the import result and refreshes history', async () => {
    const fixture = await render();

    fixture.componentInstance.upload(new File(['TradeDate'], 'journal.csv'));
    http.expectOne(`${environment.apiUrl}/upload`).flush({
      uploadLogId: 1, fileName: 'journal.csv', totalRows: 3, parsedRows: 2, failedRows: 1,
      status: 'Completed', errors: ['Row 3: Direction must be \'Long\' or \'Short\'.']
    });
    http.expectOne(`${environment.apiUrl}/upload/logs`).flush([]);
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Upload Complete');
    expect(text).toContain('Row 3: Direction must be');
  });

  it('shows the API error when the upload fails', async () => {
    const fixture = await render();

    fixture.componentInstance.upload(new File(['x'], 'journal.csv'));
    http.expectOne(`${environment.apiUrl}/upload`)
      .flush({ title: 'Only CSV files are accepted.' }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Upload failed: Only CSV files are accepted. (HTTP 400)');
    expect(fixture.componentInstance.isUploading()).toBe(false);
  });
});
