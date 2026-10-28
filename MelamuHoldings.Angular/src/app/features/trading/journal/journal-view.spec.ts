import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { JournalCalendarComponent } from './journal-calendar.component';
import { JournalOverviewComponent } from './journal-overview.component';
import { TradeAttachmentsComponent } from './trade-attachments.component';
import { JournalAnswersComponent } from './journal-fields.component';
import { TradesComponent } from '../trades/trades.component';
import { CalendarMonth, JournalOverview, TradeAttachment, monthGrid } from '../models/journal-view.model';
import { JournalSection } from '../models/journal.model';
import { Trade } from '../models/trade.model';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;

const OCTOBER: CalendarMonth = {
  year: 2026, month: 10, completedTrades: 2, netPnL: 70,
  days: [
    { date: '2026-10-05', completedTrades: 2, netPnL: 70, wins: 1, losses: 1, drafts: 1, open: 0 },
    { date: '2026-10-12', completedTrades: 0, netPnL: 0, wins: 0, losses: 0, drafts: 0, open: 1 }
  ]
};

function trade(id: number, overrides: Partial<Trade> = {}): Trade {
  return {
    id, status: 'Closed', isImported: false, tradeDate: '2026-10-05', instrument: `T${id}`, direction: 'Long', entryTime: '09:30:00',
    pnL: id * 10, rr: 1, session: 'London', dayOfWeek: 'Monday', isManualOverride: false, strategyName: 'ORB', ...overrides
  };
}

function configure(component: unknown, query: Record<string, string> = {}) {
  TestBed.configureTestingModule({
    imports: [component as never],
    providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({}), queryParamMap: convertToParamMap(query) } } }
    ]
  });
  return TestBed.inject(HttpTestingController);
}

describe('monthGrid', () => {
  it('starts weeks on Monday and pads days outside the month', () => {
    const weeks = monthGrid(2026, 10);   // 1 October 2026 is a Thursday
    expect(weeks[0]).toEqual([null, null, null, '2026-10-01', '2026-10-02', '2026-10-03', '2026-10-04']);
    expect(weeks.at(-1)!.filter(Boolean).at(-1)).toBe('2026-10-31');
    expect(weeks.every(w => w.length === 7)).toBe(true);
  });
});

describe('JournalCalendarComponent', () => {
  it('shows completed totals, marks a day with only unfinished trades, and links each day to the log', async () => {
    TestBed.configureTestingModule({ imports: [JournalCalendarComponent], providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(JournalCalendarComponent);
    fixture.componentRef.setInput('month', OCTOBER);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const days = [...el.querySelectorAll('a.day')] as HTMLAnchorElement[];

    expect(el.textContent).toContain('October 2026');
    expect(el.textContent).toContain('2 completed trades');
    expect(days.length).toBe(2);
    expect(days[0].getAttribute('aria-label')).toBe('5 October: 2 completed, +70.00; 1 draft');
    expect(days[0].getAttribute('href')).toBe('/trading/trades?date=2026-10-05');
    expect(days[1].classList.contains('unfinished')).toBe(true);
    expect(days[1].textContent).toContain('1 open');
    expect(el.querySelectorAll('.day.empty').length).toBe(29);
    expect(fixture.componentInstance.compact(1234.5)).toBe('+1.2k');
    expect(fixture.componentInstance.compact(-600)).toBe('−600');
  });
});

describe('JournalOverviewComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  const overview: JournalOverview = {
    drafts: 9, open: 1, awaitingReview: 20, reviewed: 2, openTrades: [], recent: [],
    draftTrades: [{ id: 3, status: 'Draft', tradeDate: '2026-10-05', instrument: 'NQ', isImported: false, lastChangedAt: '2026-10-05T08:00:00Z' }],
    awaitingReviewTrades: []
  };

  it('shows what needs finishing, the month, and rows waiting for an import decision', async () => {
    http = configure(JournalOverviewComponent);
    const fixture = TestBed.createComponent(JournalOverviewComponent);
    await fixture.whenStable();
    const now = new Date();
    http.expectOne(`${api}/journal/overview`).flush(overview);
    http.expectOne(r => r.url === `${api}/journal/calendar` && r.params.get('year') === String(now.getFullYear()) && r.params.get('month') === String(now.getMonth() + 1)).flush(OCTOBER);
    http.expectOne(`${api}/upload/matches`).flush([{ id: 1 }, { id: 2 }]);
    await fixture.whenStable();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Awaiting review20');
    expect(text).toContain('2 imported rows match trades you journaled');
    expect(text).toContain('All 9');   // more drafts than the list shows
    expect(text).toContain('No open positions.');

    fixture.componentInstance.shiftMonth(-1);
    const previous = new Date(now.getFullYear(), now.getMonth() - 1, 1);
    http.expectOne(r => r.url === `${api}/journal/calendar` && r.params.get('month') === String(previous.getMonth() + 1)
      && r.params.get('year') === String(previous.getFullYear())).flush({ ...OCTOBER, days: [] });
  });

  it('still loads when the import matches cannot be read', async () => {
    http = configure(JournalOverviewComponent);
    const fixture = TestBed.createComponent(JournalOverviewComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/journal/overview`).flush(overview);
    http.expectOne(r => r.url === `${api}/journal/calendar`).flush(OCTOBER);
    http.expectOne(`${api}/upload/matches`).flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect(fixture.componentInstance.state()).toBe('ready');
    expect(fixture.nativeElement.textContent).not.toContain('imported row');
  });
});

describe('TradesComponent journal filters', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  async function render(trades: Trade[], query: Record<string, string> = {}) {
    http = configure(TradesComponent, query);
    const fixture = TestBed.createComponent(TradesComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/trades`).flush(trades);
    await fixture.whenStable();
    return { fixture, c: fixture.componentInstance };
  }

  it('opens on the calendar day and status passed in the address', async () => {
    const { fixture, c } = await render([trade(1), trade(2, { tradeDate: '2026-10-06' }), trade(3, { status: 'Draft', pnL: null })], { date: '2026-10-05', status: 'Draft' });

    expect(c.filtered().map(t => t.id)).toEqual([3]);
    expect(fixture.nativeElement.textContent).toContain('Trades on Monday 5 October 2026');
    c.setFilter(c.dateFilter, '');
    expect(c.filtered().map(t => t.id)).toEqual([3]);
  });

  it('shows each trade\'s rule adherence', async () => {
    const { fixture } = await render([trade(1, { ruleAdherence: 'Followed' }), trade(2, { ruleAdherence: 'Broke' }), trade(3, { ruleAdherence: null })]);
    const cells = [...fixture.nativeElement.querySelectorAll('tbody tr')].map((r: Element) => r.querySelectorAll('td')[12].textContent!.trim());
    expect(cells.sort()).toEqual(['Broke a rule', 'Followed', '—']);
  });

  it('filters by strategy, session and account', async () => {
    const { c } = await render([trade(1), trade(2, { strategyName: 'Pullback', session: 'New York' }), trade(3, { tradingAccount: 'Prop 1' })]);

    expect(c.strategies()).toEqual(['ORB', 'Pullback']);
    expect(c.accounts()).toEqual(['Prop 1']);
    c.setFilter(c.sessionFilter, 'New York');
    expect(c.filtered().map(t => t.id)).toEqual([2]);
    c.clearFilters();
    c.setFilter(c.accountFilter, 'Prop 1');
    expect(c.filtered().map(t => t.id)).toEqual([3]);
  });

  it('sorts by P&L with trades that have none last, in either direction', async () => {
    const { c } = await render([trade(1, { pnL: -50 }), trade(2, { status: 'Draft', pnL: null }), trade(3, { pnL: 120 })]);

    c.sortBy('pnl');
    expect(c.sorted().map(t => t.id)).toEqual([3, 1, 2]);
    c.sortBy('pnl');
    expect(c.sorted().map(t => t.id)).toEqual([1, 3, 2]);
    expect(c.ariaSort('pnl')).toBe('ascending');
  });

  it('pages 25 at a time and goes back to the first page when a filter changes', async () => {
    const { fixture, c } = await render(Array.from({ length: 30 }, (_, i) => trade(i + 1, { session: i < 28 ? 'London' : 'Asian' })));

    expect(c.page().length).toBe(25);
    expect(fixture.nativeElement.textContent).toContain('Showing 1–25 of 30');
    c.pageIndex.set(1);
    expect(c.page().length).toBe(5);
    c.setFilter(c.sessionFilter, 'Asian');
    expect(c.pageIndex()).toBe(0);
    expect(c.page().length).toBe(2);
  });
});

describe('TradeAttachmentsComponent', () => {
  let http: HttpTestingController;
  afterEach(() => { http.verify(); vi.restoreAllMocks(); });

  const image: TradeAttachment = { id: 4, fileName: 'chart.png', contentType: 'image/png', sizeBytes: 2048, uploadedAt: '2026-10-28T08:00:00Z' };
  const pdf: TradeAttachment = { id: 5, fileName: 'statement.pdf', contentType: 'application/pdf', sizeBytes: 3 * 1024 * 1024, uploadedAt: '2026-10-28T08:00:00Z' };

  async function render(initial: TradeAttachment[]) {
    URL.createObjectURL = vi.fn(() => 'blob:preview');
    URL.revokeObjectURL = vi.fn();
    TestBed.configureTestingModule({ imports: [TradeAttachmentsComponent], providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TradeAttachmentsComponent);
    fixture.componentRef.setInput('tradeId', 9);
    fixture.componentRef.setInput('initial', initial);
    await fixture.whenStable();
    return { fixture, c: fixture.componentInstance };
  }

  it('loads previews for images only and lists sizes', async () => {
    const { fixture } = await render([image, pdf]);
    const preview = http.expectOne(`${api}/trades/9/attachments/4`);
    expect(preview.request.responseType).toBe('blob');
    preview.flush(new Blob(['x']));
    await fixture.whenStable();

    expect((fixture.nativeElement.querySelector('img') as HTMLImageElement).src).toBe('blob:preview');
    expect(fixture.nativeElement.textContent).toContain('3.0 MB');
    expect(fixture.nativeElement.textContent).toContain('8 more allowed');
  });

  it('refuses unsupported or oversized files before uploading, and uploads with the caption', async () => {
    const { c } = await render([]);
    const choose = (file: File) => c.choose({ target: { files: [file], value: 'x' } } as unknown as Event);

    choose(new File(['<svg/>'], 'chart.svg'));
    expect(c.error()).toBe('Only PNG, JPEG, WebP images and PDF files can be attached.');
    choose(new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'big.png'));
    expect(c.error()).toBe('The file is larger than 5 MB.');

    c.caption = 'Entry on the retest';
    choose(new File(['%PDF-'], 'statement.pdf'));
    const req = http.expectOne(`${api}/trades/9/attachments`);
    expect((req.request.body as FormData).get('caption')).toBe('Entry on the retest');
    req.flush(pdf);
    expect(c.files().map(f => f.id)).toEqual([5]);
  });

  it('deletes only after confirmation', async () => {
    const { fixture, c } = await render([pdf]);
    c.confirming.set(5);
    await fixture.whenStable();

    c.remove(pdf);
    http.expectOne(r => r.method === 'DELETE' && r.url === `${api}/trades/9/attachments/5`).flush(null);
    expect(c.files()).toEqual([]);
  });
});

describe('JournalAnswersComponent folding', () => {
  const sections: JournalSection[] = [
    { id: 'a', title: 'Context', fields: [{ id: 'mood', label: 'Mood', type: 'ShortText' }, { id: 'news', label: 'News', type: 'ShortText' }] },
    { id: 'b', title: 'Learning', fields: [{ id: 'lesson', label: 'Lesson', type: 'ShortText' }] }
  ];

  it('folds away unanswered questions and empty sections behind a button', async () => {
    TestBed.configureTestingModule({ imports: [JournalAnswersComponent] });
    const fixture = TestBed.createComponent(JournalAnswersComponent);
    fixture.componentRef.setInput('sections', sections);
    fixture.componentRef.setInput('answers', { mood: 'Calm' });
    fixture.componentRef.setInput('collapseUnanswered', true);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Mood');
    expect(el.textContent).not.toContain('Learning');
    const toggle = el.querySelector('button.toggle') as HTMLButtonElement;
    expect(toggle.textContent!.trim()).toBe('Show 2 unanswered questions');

    toggle.click();
    await fixture.whenStable();
    expect(el.textContent).toContain('Learning');
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
  });
});
