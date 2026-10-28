import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TradeEntryComponent } from './trade-entry.component';
import { AiReviewDraft } from '../models/ai-review.model';
import { Trade } from '../models/trade.model';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;

function trade(overrides: Partial<Trade> = {}): Trade {
  return {
    id: 5, status: 'Closed', isImported: false, tradeDate: '2026-10-26', instrument: 'NQ', direction: 'Long', entryTime: '09:31:00',
    entryPrice: 100, exitPrice: 101, pnL: 120, riskAmount: 50, rr: 2.4, session: 'London', dayOfWeek: 'Monday',
    isManualOverride: false, notes: 'Waited for the retest.', ...overrides
  };
}

const draft: AiReviewDraft = {
  sufficientData: true, missing: ['No screenshot of the setup.'], comparableTradeIds: [3, 4], model: 'local-model',
  generatedAt: '2026-10-28T10:00:00Z', warnings: ['Only 2 comparable trades: too few to show a pattern.'],
  facts: [{ text: 'You waited for the retest.', tradeIds: [5] }],
  metrics: [{ label: "This trade's P&L", value: '+120.00', basis: 'recorded' }, { label: 'Comparable net P&L', value: '+60.00', basis: '2 comparable trades' }],
  interpretations: ['Patience may have helped.'],
  suggestedReview: 'I waited for the retest and kept my stop.'
};

describe('AI review draft on the trade page', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  async function render(t: Trade, enabled: boolean) {
    TestBed.configureTestingModule({
      imports: [TradeEntryComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '5' }) } } }]
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TradeEntryComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/trades/5`).flush(t);
    http.expectOne(`${api}/ai-review/status`).flush({ enabled, model: enabled ? 'local-model' : null, maxComparableTrades: 10 });
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('is hidden when AI review is turned off', async () => {
    const { el } = await render(trade(), false);
    expect(el.querySelector('app-ai-review-draft')).toBeNull();
    expect(el.textContent).toContain('Mark reviewed');
  });

  it('is offered only while the review is being written', async () => {
    const { el } = await render(trade({ status: 'Reviewed', reviewNotes: 'Done.' }), true);
    expect(el.querySelector('app-ai-review-draft')).toBeNull();
  });

  it('keeps facts, the app\'s figures and the interpretation apart, and fills the review box only when asked', async () => {
    const { fixture, el } = await render(trade(), true);
    expect(el.textContent).toContain('up to 10 recent trades with the same strategy and session');

    (el.querySelector('app-ai-review-draft button') as HTMLButtonElement).click();
    const req = http.expectOne(`${api}/trades/5/ai-review`);
    expect(req.request.method).toBe('POST');
    req.flush(draft);
    await fixture.whenStable();

    const text = el.textContent!;
    expect(text).toContain('Recorded in your journal');
    expect(el.querySelector('a[href="/trading/trades/5"]')?.textContent).toBe('trade 5');
    expect(text).toContain('Calculated by the app');
    expect(text).toContain('+60.00');
    expect(text).toContain('AI interpretation may be wrong');
    expect(text).toContain('Only 2 comparable trades');
    expect(fixture.componentInstance.reviewNotes).toBe('');   // nothing changes until the trader chooses

    const use = [...el.querySelectorAll('app-ai-review-draft button')].find(b => b.textContent!.includes('Use as my review')) as HTMLButtonElement;
    use.click();
    expect(fixture.componentInstance.reviewNotes).toBe('I waited for the retest and kept my stop.');
    // No review was saved: http.verify() finds no request to /review
  });

  it('adds the draft below the trader\'s own words, never in their place', async () => {
    const { fixture } = await render(trade(), true);
    fixture.componentInstance.reviewNotes = 'My own thoughts.';

    fixture.componentInstance.useDraft('Drafted text.');

    expect(fixture.componentInstance.reviewNotes).toBe('My own thoughts.\n\nDrafted text.');
  });

  it('says what is missing when there is not enough data', async () => {
    const { fixture, el } = await render(trade({ notes: null }), true);
    (el.querySelector('app-ai-review-draft button') as HTMLButtonElement).click();
    http.expectOne(`${api}/trades/5/ai-review`).flush({
      ...draft, sufficientData: false, facts: [], interpretations: [], suggestedReview: null, warnings: [],
      missing: ['Your own notes, journal answers or checklist for this trade.']
    });
    await fixture.whenStable();

    expect(el.textContent).toContain('Not enough data');
    expect(el.textContent).toContain('Your own notes, journal answers or checklist');
    expect(el.textContent).not.toContain('Use as my review');
  });

  it('shows the provider failure and saves nothing', async () => {
    const { fixture, el } = await render(trade(), true);
    (el.querySelector('app-ai-review-draft button') as HTMLButtonElement).click();
    http.expectOne(`${api}/trades/5/ai-review`).flush({ title: 'The AI provider could not be reached. Nothing was saved.' }, { status: 502, statusText: 'Bad Gateway' });
    await fixture.whenStable();

    expect(el.textContent).toContain('No draft: The AI provider could not be reached. Nothing was saved.');
    expect(fixture.componentInstance.reviewNotes).toBe('');
  });
});
