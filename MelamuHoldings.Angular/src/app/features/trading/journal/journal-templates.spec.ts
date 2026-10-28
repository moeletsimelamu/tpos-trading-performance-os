import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { TemplateEditorComponent } from './template-editor.component';
import { TemplatesComponent } from './templates.component';
import { JournalFieldsComponent } from './journal-fields.component';
import { JournalAnswers, JournalSection, JournalTemplate, carryAnswers } from '../models/journal.model';
import { environment } from '../../../../environments/environment';

const api = environment.apiUrl;

const SECTIONS: JournalSection[] = [
  { id: 'pre-trade', title: 'Pre-trade', fields: [
    { id: 'checklist', label: 'Checklist', type: 'Checklist', options: ['Bias confirmed', 'Risk set'] },
    { id: 'tags', label: 'Tags', type: 'MultiSelect', options: ['A+', 'News', 'Late'] },
    { id: 'confidence', label: 'Confidence', type: 'Rating', required: true }
  ] }
];

function template(overrides: Partial<JournalTemplate> = {}): JournalTemplate {
  return {
    id: 3, name: 'Scalp', isDefault: false, status: 'Active', currentVersionId: 31, currentVersion: 1, sections: SECTIONS,
    createdAt: '2026-10-28T08:00:00Z', versions: [{ id: 31, version: 1, createdAt: '2026-10-28T08:00:00Z', entryCount: 4 }], ...overrides
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
  return TestBed.inject(HttpTestingController);
}

describe('carryAnswers', () => {
  it('keeps answers whose question still exists with the same type and names the rest', () => {
    const from: JournalSection[] = [{ id: 's', title: 'S', fields: [
      { id: 'a', label: 'A', type: 'ShortText' }, { id: 'b', label: 'B', type: 'Number' }, { id: 'c', label: 'C', type: 'ShortText' }
    ] }];
    const to: JournalSection[] = [{ id: 't', title: 'T', fields: [{ id: 'a', label: 'A', type: 'ShortText' }, { id: 'b', label: 'B', type: 'ShortText' }] }];

    const result = carryAnswers({ a: 'kept', b: 4, c: 'gone', d: '  ' }, from, to);

    expect(result.kept).toEqual({ a: 'kept' });
    expect(result.dropped).toEqual(['B', 'C']);   // empty answers are neither kept nor reported
  });
});

describe('JournalFieldsComponent', () => {
  async function render(answers: JournalAnswers) {
    TestBed.configureTestingModule({ imports: [JournalFieldsComponent] });
    const fixture = TestBed.createComponent(JournalFieldsComponent);
    fixture.componentRef.setInput('sections', SECTIONS);
    fixture.componentRef.setInput('answers', answers);
    const emitted: JournalAnswers[] = [];
    fixture.componentInstance.answersChange.subscribe(a => emitted.push(a));
    await fixture.whenStable();
    return { fixture, c: fixture.componentInstance, emitted };
  }

  it('records every checklist item once one is ticked, so unticked items are answers too', async () => {
    const { c, emitted } = await render({});
    c.tick(SECTIONS[0].fields[0], 'Risk set', true);
    expect(emitted.at(-1)).toEqual({ checklist: { 'Bias confirmed': false, 'Risk set': true } });
  });

  it('keeps multi-select choices in option order and removes the answer when none is left', async () => {
    const tags = SECTIONS[0].fields[1];
    const { c, emitted } = await render({ tags: ['Late'] });
    c.choose(tags, 'A+', true);
    expect(emitted.at(-1)).toEqual({ tags: ['A+', 'Late'] });

    const second = await (async () => { TestBed.resetTestingModule(); return render({ tags: ['Late'] }); })();
    second.c.choose(tags, 'Late', false);
    expect(second.emitted.at(-1)).toEqual({});
  });

  it('marks required questions and shows the chosen rating', async () => {
    const { fixture } = await render({ confidence: 4 });
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Confidence *');
    expect(el.querySelector('[role=radio][aria-checked=true]')?.textContent?.trim()).toBe('4');
  });
});

describe('TemplateEditorComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  it('creates a template with ids made from the titles and labels, and options one per line', async () => {
    http = configure(TemplateEditorComponent);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(TemplateEditorComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;
    c.name = ' Scalp ';
    c.sections[0].title = 'Before the trade';
    c.sections[0].fields[0].label = 'Market bias?';
    c.sections[0].fields[0].type = 'SingleSelect';
    c.sections[0].fields[0].optionsText = 'Bullish\n\n Bearish \n';
    c.addField(c.sections[0]);
    c.sections[0].fields[1].label = 'Market bias?';
    c.touch();

    expect(c.preview()[0].fields.map(f => f.id)).toEqual(['market-bias', 'market-bias-2']);
    c.save();
    const req = http.expectOne(`${api}/journal-templates`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      name: 'Scalp', description: null,
      sections: [{ id: 'before-the-trade', title: 'Before the trade', description: null, fields: [
        { id: 'market-bias', label: 'Market bias?', type: 'SingleSelect', required: false, help: null, options: ['Bullish', 'Bearish'] },
        { id: 'market-bias-2', label: 'Market bias?', type: 'ShortText', required: false, help: null, options: null }
      ] }]
    });
    req.flush(template({ id: 9 }));
    expect(navigate).toHaveBeenCalledWith(['/trading/journal-templates', 9]);
  });

  it('says what is missing instead of sending an incomplete template', async () => {
    http = configure(TemplateEditorComponent);
    const fixture = TestBed.createComponent(TemplateEditorComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;
    c.name = 'Scalp';
    c.sections[0].title = 'Quick';
    c.sections[0].fields[0].label = 'Setup';
    c.sections[0].fields[0].type = 'Checklist';

    c.save();
    http.expectNone(`${api}/journal-templates`);
    expect(c.saveError()).toBe("'Setup' needs its options, one per line.");
  });

  it('keeps existing ids when editing and reports the new version', async () => {
    http = configure(TemplateEditorComponent, 3);
    const fixture = TestBed.createComponent(TemplateEditorComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/journal-templates/3`).flush(template());
    await fixture.whenStable();
    const c = fixture.componentInstance;

    expect(fixture.nativeElement.textContent).toContain('4 trades written with v1 keep that version.');
    c.sections[0].fields[2].label = 'How confident were you?';
    c.move(c.sections[0].fields, 2, -1);
    c.save();
    const req = http.expectOne(`${api}/journal-templates/3`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.sections[0].fields.map((f: { id: string }) => f.id)).toEqual(['checklist', 'confidence', 'tags']);
    req.flush(template({ currentVersion: 2, currentVersionId: 32 }));
    await fixture.whenStable();

    expect(c.saved()).toBe('Saved as version 2.');
  });
});

describe('TemplatesComponent', () => {
  let http: HttpTestingController;
  afterEach(() => http.verify());

  it('lists templates with the trades journaled and opens a duplicate', async () => {
    http = configure(TemplatesComponent);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(TemplatesComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/journal-templates`).flush([
      template({ id: 1, name: 'Standard trade journal', isDefault: true }),
      template({ versions: [{ id: 30, version: 1, createdAt: '', entryCount: 2 }, { id: 31, version: 2, createdAt: '', entryCount: 3 }], currentVersion: 2 })
    ]);
    await fixture.whenStable();
    const text = fixture.nativeElement.textContent as string;
    const c = fixture.componentInstance;

    expect(text).toContain('Default');
    expect(c.used(c.templates()[1])).toBe(5);

    c.act(c.templates()[1], 'duplicate');
    http.expectOne(`${api}/journal-templates/3/duplicate`).flush(template({ id: 12, name: 'Scalp (copy)' }));
    expect(navigate).toHaveBeenCalledWith(['/trading/journal-templates', 12]);
  });

  it('reloads with archived templates when asked', async () => {
    http = configure(TemplatesComponent);
    const fixture = TestBed.createComponent(TemplatesComponent);
    await fixture.whenStable();
    http.expectOne(`${api}/journal-templates`).flush([]);

    fixture.componentInstance.toggleArchived();
    http.expectOne(`${api}/journal-templates?includeArchived=true`).flush([template({ status: 'Archived' })]);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Restore');
  });
});
