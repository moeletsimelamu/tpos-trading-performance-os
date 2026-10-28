import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviourComponent } from '../behaviour/behaviour.component';
import { environment } from '../../../../environments/environment';
import { AnalyticsFilterBarComponent } from './analytics-filter-bar.component';
import { AnalyticsFilter, AnalyticsFilterOptions, describeFilter, filterFromQuery, filterParams, filterToQuery } from '../models/analytics-filter.model';

const options: AnalyticsFilterOptions = {
  strategies: [{ value: '4', label: 'ORB' }, { value: '5', label: 'Pullback' }],
  instruments: ['ES', 'NQ'],
  sessions: ['London', 'New York'],
  accounts: ['Prop A'],
  journalFields: [
    { id: 'mood', label: 'Mood', type: 'SingleSelect', options: [{ value: 'Calm', label: 'Calm' }, { value: 'Tense', label: 'Tense' }] },
    { id: 'quality', label: 'Quality', type: 'Rating', options: [{ value: '4', label: '4 of 5' }] }
  ]
};

describe('analytics filter model', () => {
  it('reads only well-formed values from the address', () => {
    const filter = filterFromQuery(convertToParamMap({
      from: '2026-10-01', to: 'yesterday', strategyId: '4x', session: ' London ', adherence: 'Sometimes', answerField: 'mood'
    }));
    expect(filter).toEqual({ from: '2026-10-01', session: 'London' });   // an answer without a question is dropped too
    expect(filterFromQuery(convertToParamMap({ instrument: ' NQ ' }))).toEqual({ instrument: 'NQ' });
  });

  it('sends the API the same names, without empty or half-chosen values', () => {
    const filter: AnalyticsFilter = { strategyId: 4, session: '', answerField: 'mood' };
    expect(filterToQuery(filter)).toEqual({ strategyId: '4' });
    expect(filterParams({ adherence: 'Broke', answerField: 'mood', answerValue: 'Calm' }).toString()).toBe('adherence=Broke&answerField=mood&answerValue=Calm');
  });

  it('describes the filters in plain words', () => {
    expect(describeFilter({ from: '2026-10-01', to: '2026-10-31', strategyId: 5, adherence: 'NotScored', answerField: 'quality', answerValue: '4' }, options))
      .toEqual(['1 Oct 2026 to 31 Oct 2026', 'Pullback', 'No complete checklist', 'Quality: 4 of 5']);
    // A question keeps its own punctuation
    expect(describeFilter({ answerField: 'valid', answerValue: 'Yes' }, { ...options, journalFields: [{ id: 'valid', label: 'Was it valid?', type: 'SingleSelect', options: [] }] }))
      .toEqual(['Was it valid? Yes']);
  });
});

describe('AnalyticsFilterBarComponent', () => {
  async function render(filter: AnalyticsFilter = {}) {
    TestBed.configureTestingModule({ imports: [AnalyticsFilterBarComponent] });
    const fixture = TestBed.createComponent(AnalyticsFilterBarComponent);
    fixture.componentRef.setInput('options', options);
    fixture.componentRef.setInput('filter', filter);
    const emitted: AnalyticsFilter[] = [];
    fixture.componentInstance.filterChange.subscribe(f => emitted.push(f));
    await fixture.whenStable();
    return { fixture, c: fixture.componentInstance, emitted };
  }

  it('applies a change at once and keeps the other filters', async () => {
    const { fixture, c, emitted } = await render({ session: 'London' });
    c.set('strategyId', '4');
    expect(emitted.at(-1)).toEqual({ session: 'London', strategyId: 4 });

    // The page passes the new filter back in; removing the session then leaves the strategy
    fixture.componentRef.setInput('filter', emitted.at(-1)!);
    c.set('session', '');
    expect(emitted.at(-1)).toEqual({ strategyId: 4 });
  });

  it('applies a journal question only once an answer is chosen', async () => {
    const { fixture, c, emitted } = await render();

    c.chooseQuestion('mood');
    await fixture.whenStable();
    expect(emitted).toEqual([]);
    expect(fixture.nativeElement.querySelector('#f-answer')).not.toBeNull();

    c.chooseAnswer('Tense');
    expect(emitted.at(-1)).toEqual({ answerField: 'mood', answerValue: 'Tense' });
  });

  it('offers instruments when there is more than one, and applies one', async () => {
    const { fixture, c, emitted } = await render();
    expect(fixture.nativeElement.querySelector('#f-instrument')).not.toBeNull();

    c.set('instrument', 'ES');
    expect(emitted.at(-1)).toEqual({ instrument: 'ES' });
  });

  it('shows what is applied and clears it', async () => {
    const { fixture, c, emitted } = await render({ account: 'Prop A', adherence: 'Followed' });
    expect(fixture.nativeElement.textContent).toContain('Showing only: Prop A · Followed every mandatory rule');

    c.clear();
    expect(emitted.at(-1)).toEqual({});
  });
});

describe('BehaviourComponent filters', () => {
  it('looks for patterns only among the filtered trades and says so', async () => {
    TestBed.configureTestingModule({
      imports: [BehaviourComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ session: 'New York' }) } } }]
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(BehaviourComponent);
    await fixture.whenStable();

    const req = http.expectOne(r => r.url === `${environment.apiUrl}/analytics/behaviour`);
    expect(req.request.params.get('session')).toBe('New York');
    req.flush({ alerts: [], revengeTradingCount: 0, overtradingDayCount: 0, maxConsecutiveLosses: 0, strategyDeviationCount: 0, deviationPnLImpact: 0 });
    http.expectOne(`${environment.apiUrl}/analytics/filter-options`).flush(options);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Patterns are looked for only among the trades that match the filters');
    http.verify();
  });
});
