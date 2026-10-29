import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AiJournalQuestionComponent } from './ai-journal-question.component';
import { AiJournalAnswer, SUGGESTED_QUESTIONS } from '../models/ai-review.model';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;

const answer: AiJournalAnswer = {
  sufficientData: true, question: 'Which rules have I struggled to follow?',
  answer: 'You missed "Stop set" once, on the losing trade of 5 January.', missing: [],
  facts: [{ text: 'Stop set was not met.', tradeIds: [15] }],
  metrics: [{ label: 'Followed every mandatory rule', value: '3 of 4 (75%)', basis: 'trades with a complete checklist' }],
  interpretations: ['It may relate to how you felt; four trades show little.'], reflectionPrompts: ['What happened before that entry?'],
  tradeIds: [13, 14, 15, 16], tradesInView: 6, from: '2034-01-03', to: '2034-01-05', model: 'local-model',
  generatedAt: '2034-01-06T10:00:00Z', warnings: ['Only 4 trades in this view: too few to show a pattern.']
};

describe('Asking about the trades in view', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  async function render(enabled: boolean) {
    TestBed.configureTestingModule({ imports: [AiJournalQuestionComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AiJournalQuestionComponent);
    fixture.componentRef.setInput('filter', { instrument: 'NQ', session: 'London' });
    await fixture.whenStable();
    http.expectOne(`${api}/ai-review/status`).flush({ enabled, model: enabled ? 'local-model' : null, maxComparableTrades: 10, maxJournalTrades: 30 });
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('is hidden when AI review is turned off', async () => {
    const { el } = await render(false);
    expect(el.textContent!.trim()).toBe('');
  });

  it('says what is sent, sends the question with the page filter, and keeps the parts of the answer apart', async () => {
    const { fixture, el } = await render(true);
    expect(el.textContent).toContain('up to 30 of the most recent closed trades in this view (NQ · London) to local-model');

    // A suggested question fills the box; nothing is asked until the trader presses Ask
    (el.querySelector('.chip') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect((el.querySelector('#ask-question') as HTMLInputElement).value).toBe(SUGGESTED_QUESTIONS[0]);
    http.expectNone(`${api}/ai-review/ask`);

    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    const req = http.expectOne(r => r.url === `${api}/ai-review/ask`);
    expect(req.request.body).toEqual({ question: SUGGESTED_QUESTIONS[0] });
    expect(req.request.params.get('instrument')).toBe('NQ');
    expect(req.request.params.get('session')).toBe('London');
    req.flush(answer);
    await fixture.whenStable();

    const text = el.textContent!.replace(/\s+/g, ' ');
    expect(text).toContain('Answer AI · may be wrong');
    expect(text).toContain('Recorded in your journal');
    expect(el.querySelector('a[href="/trading/trades/15"]')).not.toBeNull();
    expect(text).toContain('Calculated by the app');
    expect(text).toContain('3 of 4 (75%)');
    expect(text).toContain('AI interpretation');
    expect(text).toContain('Questions to consider');
    expect(text).toContain('too few to show a pattern');
    expect(text).toContain('Based on 4 of 6 closed trades in this view, 3 Jan 2034 to 5 Jan 2034.');
  });

  it('clears an answer when the filter changes, since it described other trades', async () => {
    const { fixture, el } = await render(true);
    (el.querySelector('#ask-question') as HTMLInputElement).value = 'Summarise my last trading session.';
    el.querySelector('#ask-question')!.dispatchEvent(new Event('input'));
    (el.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    http.expectOne(r => r.url === `${api}/ai-review/ask`).flush(answer);
    await fixture.whenStable();
    expect(el.textContent).toContain('You asked:');

    fixture.componentRef.setInput('filter', { instrument: 'ES' });
    await fixture.whenStable();
    expect(el.textContent).not.toContain('You asked:');
  });
});
