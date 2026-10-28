import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TradeHistoryComponent } from './trade-history.component';
import { TradeRevision, revisionTitle } from '../models/trade.model';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;

const revisions: TradeRevision[] = [
  {
    id: 3, occurredAt: '2026-10-29T08:30:00Z', kind: 'Reviewed', status: 'Reviewed',
    changes: [{ field: 'Status', label: 'Status', before: 'Closed', after: 'Reviewed' }, { field: 'ReviewNotes', label: 'Review', before: null, after: 'Kept my stop.' }]
  },
  {
    id: 2, occurredAt: '2026-10-29T08:20:00Z', kind: 'Edited', status: 'Closed',
    changes: [{ field: 'Notes', label: 'Notes', before: 'First notes', after: 'Corrected notes' }, { field: 'rule.4', label: 'Rule: Stop set', before: 'Met', after: 'Not met' }]
  }
];

describe('Trade change history', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  async function render(stamp: unknown = 1) {
    TestBed.configureTestingModule({ imports: [TradeHistoryComponent], providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TradeHistoryComponent);
    fixture.componentRef.setInput('tradeId', 7);
    fixture.componentRef.setInput('stamp', stamp);
    fixture.componentRef.setInput('createdAt', '2026-10-20T09:00:00Z');
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const open = async () => {
      const details = el.querySelector('details')!;
      details.open = true;
      details.dispatchEvent(new Event('toggle'));
      await fixture.whenStable();
    };
    return { fixture, el, open };
  }

  it('loads nothing until it is opened, then shows each change before and after, newest first', async () => {
    const { fixture, el, open } = await render();
    http.expectNone(`${api}/trades/7/history`);

    await open();
    http.expectOne(`${api}/trades/7/history`).flush(revisions);
    await fixture.whenStable();

    const titles = [...el.querySelectorAll('.when strong')].map(e => e.textContent);
    expect(titles).toEqual(['Marked reviewed', 'Edited']);
    const rows = [...el.querySelectorAll('tbody tr')].map(r => [...r.querySelectorAll('th, td')].map(c => c.textContent!.replace(/^(Before|After)/, '').trim()));
    expect(rows).toEqual([
      ['Status', 'Closed', 'Reviewed'],
      ['Review', '—', 'Kept my stop.'],
      ['Notes', 'First notes', 'Corrected notes'],
      ['Rule: Stop set', 'Met', 'Not met']
    ]);
    // No creation record: says the earlier history was not kept rather than implying there was none
    expect(el.textContent).toContain('before changes were recorded');
  });

  it('reloads while open when the trade is saved again', async () => {
    const { fixture, open } = await render(1);
    await open();
    http.expectOne(`${api}/trades/7/history`).flush(revisions);

    fixture.componentRef.setInput('stamp', 2);
    await fixture.whenStable();
    http.expectOne(`${api}/trades/7/history`).flush([]);
  });

  it('names each kind of change in plain words', () => {
    const r = (kind: TradeRevision['kind'], status: TradeRevision['status'], changes: TradeRevision['changes'] = []) =>
      revisionTitle({ id: 1, occurredAt: '', kind, status, changes });
    expect(r('Created', 'Draft')).toBe('Created as a draft');
    expect(r('Created', 'Open')).toBe('Created as an open trade');
    expect(r('Reviewed', 'Reviewed')).toBe('Review corrected');
    expect(r('ImportLinked', 'Open')).toBe('Filled in from a CSV import');
    expect(r('FileRemoved', 'Closed')).toBe('File removed');
  });
});
