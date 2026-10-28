import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { StrategiesComponent } from './strategies.component';
import { StrategyEditorComponent } from './strategy-editor.component';
import { TradeFormComponent } from '../journal/trade-form.component';
import { TradeEntryComponent } from '../journal/trade-entry.component';
import { AdherenceReport, Strategy, TradeChecklist } from '../models/strategy.model';
import { Trade } from '../models/trade.model';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;

function strategy(overrides: Partial<Strategy> = {}): Strategy {
  return {
    id: 4, name: 'ORB', status: 'Active', tradeCount: 6, createdAt: '2026-10-28T08:00:00Z', retiredRules: [],
    rules: [
      { id: 41, text: 'Stop loss is set', isMandatory: true, sortOrder: 1 },
      { id: 42, text: 'News checked', isMandatory: false, sortOrder: 2 }
    ],
    ...overrides
  };
}

const group = (trades: number, netPnL: number) => ({ trades, wins: 1, netPnL, winRate: 50, averageR: 0.5, insufficientData: trades < 20 });

function report(overrides: Partial<AdherenceReport> = {}): AdherenceReport {
  return {
    completedTrades: 6, withChecklist: 3, incompleteChecklist: 1, withoutChecklist: 2, compliant: 2, nonCompliant: 1,
    adherenceRate: 66.67, minimumSample: 20, compliantResults: group(2, 80), nonCompliantResults: group(1, -90),
    mostViolated: [{ ruleId: 41, strategyName: 'ORB', text: 'Stop loss is set', timesNotMet: 1, timesAnswered: 3 }],
    byStrategy: [{ strategyId: 4, strategyName: 'ORB', completedTrades: 6, withChecklist: 3, compliant: 2, adherenceRate: 66.67 }],
    ...overrides
  };
}

function trade(overrides: Partial<Trade> = {}): Trade {
  return {
    id: 5, status: 'Closed', isImported: false, tradeDate: '2026-10-26', instrument: 'NQ', direction: 'Long',
    entryTime: '09:31:00', entryPrice: 100, exitPrice: 101, pnL: 50, riskAmount: 25, rr: 2, strategyName: 'ORB',
    session: 'London', dayOfWeek: 'Monday', isManualOverride: false, ...overrides
  };
}

function checklist(overrides: Partial<TradeChecklist> = {}): TradeChecklist {
  return {
    strategyId: 4, strategyName: 'ORB', createdAt: '2026-10-28T08:00:00Z',
    items: [
      { ruleId: 41, text: 'Stop loss was set', isMandatory: true, answer: 'NotMet' },
      { ruleId: 42, text: 'News checked', isMandatory: false, answer: 'Met' }
    ],
    isComplete: true, isCompliant: false, violations: ['Stop loss was set'],
    history: [
      { ruleId: 41, text: 'Stop loss was set', sequence: 1, answer: 'Met', answeredAt: '2026-10-28T08:00:00Z' },
      { ruleId: 42, text: 'News checked', sequence: 1, answer: 'Met', answeredAt: '2026-10-28T08:00:00Z' },
      { ruleId: 41, text: 'Stop loss was set', sequence: 2, answer: 'NotMet', answeredAt: '2026-10-28T09:15:00Z' }
    ],
    ...overrides
  };
}

function configure(component: unknown, id?: number) {
  TestBed.configureTestingModule({
    imports: [component as never],
    providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(id ? { id: String(id) } : {}) } } }
    ]
  });
  vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  return TestBed.inject(HttpTestingController);
}

describe('StrategiesComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  it('shows adherence with its denominator, results with sample sizes, and each strategy\'s rate', async () => {
    http = configure(StrategiesComponent);
    const fixture = TestBed.createComponent(StrategiesComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/strategies`).flush([strategy(), strategy({ id: 5, name: 'Scalp', rules: [], tradeCount: 0 })]);
    http.expectOne(`${api}/analytics/adherence`).flush(report());
    await fixture.whenStable();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('66.7%');
    expect(text).toContain('2 of 3 trades with a complete checklist followed every mandatory rule');
    expect(text).toContain('2 without a checklist · 1 unfinished, of 6 closed trades');
    expect(text).toContain('Insufficient data');
    expect(text).toContain('not met 1 of 3');
    expect(text).toContain('1 mandatory · 1 optional');
    expect(text).toContain('No rules yet');
    expect(text).toContain('(2/3)');
  });

  it('says there is no rate rather than showing zero', async () => {
    http = configure(StrategiesComponent);
    const fixture = TestBed.createComponent(StrategiesComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/strategies`).flush([]);
    http.expectOne(`${api}/analytics/adherence`).flush(report({ withChecklist: 0, compliant: 0, adherenceRate: null, mostViolated: [], byStrategy: [] }));
    await fixture.whenStable();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('No closed trade has a complete checklist yet');
    expect(text).not.toContain('0%');
    expect(text).not.toContain('Insufficient data');   // no comparison without scored trades
  });
});

describe('StrategyEditorComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  it('creates a strategy with its rules in order', async () => {
    http = configure(StrategyEditorComponent);
    const navigate = TestBed.inject(Router).navigate as unknown as ReturnType<typeof vi.fn>;
    const fixture = TestBed.createComponent(StrategyEditorComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;
    c.name = ' ORB ';
    c.rules[0].text = 'Stop loss is set';
    c.addRule();
    c.rules[1].text = 'News checked';
    c.rules[1].isMandatory = false;
    c.move(1, -1);

    c.save();
    const req = http.expectOne(`${api}/strategies`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      name: 'ORB', description: null,
      rules: [{ id: null, text: 'News checked', isMandatory: false }, { id: null, text: 'Stop loss is set', isMandatory: true }]
    });
    req.flush(strategy({ id: 9 }));
    expect(navigate).toHaveBeenCalledWith(['/trading/strategies', 9]);
  });

  it('refuses empty or repeated rules before sending', async () => {
    http = configure(StrategyEditorComponent);
    const fixture = TestBed.createComponent(StrategyEditorComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;
    c.name = 'ORB';

    c.save();
    expect(c.saveError()).toBe('Every rule needs its wording, or remove the empty one.');
    c.rules[0].text = 'Same';
    c.addRule();
    c.rules[1].text = 'same ';
    c.save();
    expect(c.saveError()).toBe('Two rules read the same.');
    http.expectNone(`${api}/strategies`);
  });

  it('keeps rule ids when editing, says removed rules are retired, and restores one', async () => {
    http = configure(StrategyEditorComponent, 4);
    const fixture = TestBed.createComponent(StrategyEditorComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/strategies/4`).flush(strategy());
    http.expectOne(`${api}/analytics/adherence?strategyId=4`).flush(report());
    await fixture.whenStable();
    const c = fixture.componentInstance;
    expect(fixture.nativeElement.textContent).toContain('Adherence on this strategy');

    c.rules[0].text = 'Stop placed beyond the range';
    c.remove(1);
    c.save();
    const req = http.expectOne(`${api}/strategies/4`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.rules).toEqual([{ id: 41, text: 'Stop placed beyond the range', isMandatory: true }]);
    const retired = { id: 42, text: 'News checked', isMandatory: false, sortOrder: 2, retiredAt: '2026-10-28T09:00:00Z' };
    req.flush(strategy({ rules: [{ id: 41, text: 'Stop placed beyond the range', isMandatory: true, sortOrder: 1 }], retiredRules: [retired] }));
    await fixture.whenStable();
    expect(c.saved()).toBe('Saved. 1 rule was retired; trades already checked keep it.');
    expect(fixture.nativeElement.textContent).toContain('Retired rules');

    c.restoreRule(42);
    http.expectOne(`${api}/strategies/4/rules/42/restore`).flush(strategy());
    expect(c.rules.map(r => r.id)).toEqual([41, 42]);
  });
});

describe('Trade form checklist', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  async function form(id?: number, existing?: Trade) {
    http = configure(TradeFormComponent, id);
    const fixture = TestBed.createComponent(TradeFormComponent);
    await fixture.whenStable();
    if (id) http.expectOne(`${api}/trades/${id}`).flush(existing!);
    http.expectOne(`${api}/journal-templates`).flush([]);
    http.expectOne(`${api}/strategies`).flush([strategy()]);
    await fixture.whenStable();
    return { fixture, c: fixture.componentInstance };
  }

  it('shows the named strategy\'s rules, offers N/A only on optional ones, and sends the answers', async () => {
    const { fixture, c } = await form();
    c.form.patchValue({ instrument: 'NQ', strategyName: 'orb' });
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Stop loss is set');
    expect(el.querySelectorAll('input[name="rule-41"]').length).toBe(2);   // Met, Not met
    expect(el.querySelectorAll('input[name="rule-42"]').length).toBe(3);   // plus N/A
    c.answerRule(41, 'Met');
    c.answerRule(42, 'NotApplicable');

    c.save('Draft');
    const req = http.expectOne(`${api}/trades`);
    expect(req.request.body.checklist).toEqual([{ ruleId: 41, answer: 'Met' }, { ruleId: 42, answer: 'NotApplicable' }]);
    req.flush(trade({ status: 'Draft' }));
  });

  it('explains a strategy with no rules, and a new one', async () => {
    const { fixture, c } = await form();
    c.form.patchValue({ strategyName: 'Brand new' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('A new strategy: it is created with the trade.');
  });

  it('keeps the checklist\'s own wording on edit and sends nothing when it is untouched', async () => {
    const { fixture, c } = await form(5, trade({ checklist: checklist() }));
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('Stop loss was set');   // the copy taken when the checklist started
    expect((el.querySelector('input[name="rule-41"][value="NotMet"]') as HTMLInputElement).checked).toBe(true);

    c.save(null);
    const req = http.expectOne(`${api}/trades/5`);
    expect(req.request.body.checklist).toBeUndefined();
    req.flush(trade());
  });
});

describe('Trade entry checklist', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  it('shows each answer, the violation and the change made after the first answer', async () => {
    http = configure(TradeEntryComponent, 5);
    const fixture = TestBed.createComponent(TradeEntryComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/trades/5`).flush(trade({ checklist: checklist() }));
    await fixture.whenStable();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('1 mandatory rule not met');
    expect(text).toContain('Not met');
    expect(text).toContain('Changed after first answer');
    expect(text).toContain('Stop loss was set: Met → Not met');
  });

  it('says when there is no checklist yet', async () => {
    http = configure(TradeEntryComponent, 5);
    const fixture = TestBed.createComponent(TradeEntryComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/trades/5`).flush(trade());
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('No checklist answered for ORB.');
  });
});
