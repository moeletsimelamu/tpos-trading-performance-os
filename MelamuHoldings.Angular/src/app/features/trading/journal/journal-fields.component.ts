import { Component, computed, input, output, signal } from '@angular/core';
import { JournalAnswer, JournalAnswers, JournalField, JournalSection, isAnswered } from '../models/journal.model';

// Renders a template version's questions as inputs. Answers flow in and every change flows out as a new
// object; the parent decides when to save. Also used as the live preview in the template editor.
@Component({
  selector: 'app-journal-fields',
  standalone: true,
  template: `
    @for (section of sections(); track section.id) {
      <section class="j-section" [attr.aria-labelledby]="idPrefix() + section.id">
        <h3 [id]="idPrefix() + section.id">{{ section.title }}</h3>
        @if (section.description) { <p class="j-desc">{{ section.description }}</p> }
        <div class="form-grid">
          @for (f of section.fields; track f.id) {
            <div class="field" [class.wide]="wide(f)">
              @switch (f.type) {
                @case ('Checkbox') {
                  <label class="inline"><input type="checkbox" [checked]="answers()[f.id] === true" (change)="set(f, $any($event.target).checked)" />{{ f.label }}{{ f.required ? ' *' : '' }}</label>
                }
                @case ('Checklist') {
                  <span class="label" [id]="key(f)">{{ f.label }}{{ f.required ? ' *' : '' }}</span>
                  <div class="choices" role="group" [attr.aria-labelledby]="key(f)">
                    @for (item of f.options ?? []; track item) {
                      <label class="inline"><input type="checkbox" [checked]="ticked(f, item)" (change)="tick(f, item, $any($event.target).checked)" />{{ item }}</label>
                    }
                  </div>
                }
                @case ('MultiSelect') {
                  <span class="label" [id]="key(f)">{{ f.label }}{{ f.required ? ' *' : '' }}</span>
                  <div class="choices row" role="group" [attr.aria-labelledby]="key(f)">
                    @for (option of f.options ?? []; track option) {
                      <label class="inline"><input type="checkbox" [checked]="chosen(f, option)" (change)="choose(f, option, $any($event.target).checked)" />{{ option }}</label>
                    }
                  </div>
                }
                @case ('Rating') {
                  <span class="label" [id]="key(f)">{{ f.label }}{{ f.required ? ' *' : '' }}</span>
                  <div class="rating" role="radiogroup" [attr.aria-labelledby]="key(f)">
                    @for (n of [1, 2, 3, 4, 5]; track n) {
                      <button type="button" class="btn btn-sm" role="radio" [attr.aria-checked]="answers()[f.id] === n" (click)="set(f, answers()[f.id] === n ? '' : n)">{{ n }}</button>
                    }
                  </div>
                }
                @default {
                  <label [for]="key(f)">{{ f.label }}{{ f.required ? ' *' : '' }}</label>
                  @switch (f.type) {
                    @case ('LongText') { <textarea [id]="key(f)" [value]="text(f)" (input)="set(f, $any($event.target).value)"></textarea> }
                    @case ('SingleSelect') {
                      <select [id]="key(f)" (change)="set(f, $any($event.target).value)">
                        <option value="" [selected]="!answers()[f.id]">Not answered</option>
                        @for (option of f.options ?? []; track option) { <option [value]="option" [selected]="answers()[f.id] === option">{{ option }}</option> }
                      </select>
                    }
                    @case ('Number') { <input [id]="key(f)" type="number" step="any" [value]="text(f)" (input)="setNumber(f, $any($event.target).value)" /> }
                    @case ('DateTime') { <input [id]="key(f)" type="datetime-local" [value]="text(f)" (input)="set(f, $any($event.target).value)" /> }
                    @case ('Url') { <input [id]="key(f)" type="url" placeholder="https://" [value]="text(f)" (input)="set(f, $any($event.target).value)" /> }
                    @default { <input [id]="key(f)" [value]="text(f)" maxlength="200" (input)="set(f, $any($event.target).value)" /> }
                  }
                }
              }
              @if (f.help) { <span class="hint">{{ f.help }}</span> }
            </div>
          }
        </div>
      </section>
    }
  `,
  styles: [`
    .j-section + .j-section { margin-top: var(--space-6); }
    h3 { font-size: var(--text-md); margin-bottom: 2px; }
    .j-desc { color: var(--text-secondary); font-size: var(--text-sm); margin-bottom: var(--space-3); }
    .wide { grid-column: 1 / -1; }
    .label { font-size: var(--text-sm); font-weight: 500; color: var(--text-secondary); }
    .choices { display: grid; gap: 6px; }
    .choices.row { display: flex; flex-wrap: wrap; gap: 6px var(--space-4); }
    .inline { display: flex; align-items: center; gap: var(--space-2); font-size: 13.5px; color: var(--text-primary); font-weight: 400; }
    .inline input { height: auto; }
    .rating { display: flex; gap: 6px; }
    .rating [aria-checked='true'] { background: var(--primary); border-color: var(--primary); color: var(--text-inverse); }
    textarea { min-height: 84px; }
  `]
})
export class JournalFieldsComponent {
  readonly sections = input.required<JournalSection[]>();
  readonly answers = input<JournalAnswers>({});
  readonly idPrefix = input('j-');
  readonly answersChange = output<JournalAnswers>();

  key(f: JournalField): string {
    return this.idPrefix() + f.id;
  }

  wide(f: JournalField): boolean {
    return f.type === 'LongText' || f.type === 'Checklist' || f.type === 'MultiSelect';
  }

  text(f: JournalField): string {
    const value = this.answers()[f.id];
    return value === undefined || value === null ? '' : String(value);
  }

  ticked(f: JournalField, item: string): boolean {
    return (this.answers()[f.id] as Record<string, boolean> | undefined)?.[item] === true;
  }

  chosen(f: JournalField, option: string): boolean {
    return ((this.answers()[f.id] as string[] | undefined) ?? []).includes(option);
  }

  set(f: JournalField, value: JournalAnswer | ''): void {
    const next = { ...this.answers() };
    if (value === '' || value === null) delete next[f.id];
    else next[f.id] = value;
    this.answersChange.emit(next);
  }

  setNumber(f: JournalField, raw: string): void {
    this.set(f, raw === '' || !Number.isFinite(Number(raw)) ? '' : Number(raw));
  }

  // A checklist is recorded in full once touched: every item ticked or not, so "not ticked" is an answer too
  tick(f: JournalField, item: string, checked: boolean): void {
    const current = { ...Object.fromEntries((f.options ?? []).map(o => [o, false])), ...((this.answers()[f.id] as Record<string, boolean>) ?? {}) };
    current[item] = checked;
    this.set(f, current);
  }

  choose(f: JournalField, option: string, checked: boolean): void {
    const current = new Set((this.answers()[f.id] as string[] | undefined) ?? []);
    if (checked) current.add(option); else current.delete(option);
    const ordered = (f.options ?? []).filter(o => current.has(o));
    this.set(f, ordered.length ? ordered : '');
  }
}

// The same questions, read-only: what was answered, and a dash for what was not. With collapseUnanswered the
// unanswered questions (and sections with none answered) are folded away behind a button.
@Component({
  selector: 'app-journal-answers',
  standalone: true,
  template: `
    @for (section of visibleSections(); track section.id) {
      <section class="a-section">
        <h3>{{ section.title }}</h3>
        <dl>
          @for (f of section.fields; track f.id) {
            <div [class.wide]="f.type === 'LongText' || f.type === 'Checklist'">
              <dt>{{ f.label }}</dt>
              <dd>
                @switch (f.type) {
                  @case ('Checklist') {
                    @if (checklist(f.id); as items) {
                      <ul class="checks">
                        @for (item of f.options ?? []; track item) {
                          <li><span class="mark" [class.yes]="items[item]" aria-hidden="true">{{ items[item] ? '✓' : '–' }}</span>{{ item }}<span class="visually-hidden">{{ items[item] ? ': yes' : ': no' }}</span></li>
                        }
                      </ul>
                    } @else { <span class="muted">Not answered</span> }
                  }
                  @default { <span [class.muted]="display(f) === null" class="pre">{{ display(f) ?? '—' }}</span> }
                }
              </dd>
            </div>
          }
        </dl>
      </section>
    } @empty {
      <p class="muted">No questions answered yet.</p>
    }
    @if (collapseUnanswered() && hiddenCount() > 0) {
      <button type="button" class="btn-link small toggle" [attr.aria-expanded]="showAll()" (click)="showAll.set(!showAll())">
        {{ showAll() ? 'Hide unanswered questions' : 'Show ' + hiddenCount() + ' unanswered ' + (hiddenCount() === 1 ? 'question' : 'questions') }}
      </button>
    }
  `,
  styles: [`
    .toggle { margin-top: var(--space-4); }
    .a-section + .a-section { margin-top: var(--space-5); padding-top: var(--space-5); border-top: 1px solid var(--border-subtle); }
    h3 { font-size: var(--text-md); margin-bottom: var(--space-3); }
    dl { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: var(--space-3) var(--space-5); }
    .wide { grid-column: 1 / -1; }
    dt { font-size: var(--text-sm); color: var(--text-secondary); }
    dd { font-weight: 500; }
    .pre { white-space: pre-wrap; }
    .checks { list-style: none; display: grid; gap: 2px; font-weight: 400; }
    .mark { display: inline-block; width: 18px; color: var(--text-muted); }
    .mark.yes { color: var(--positive); font-weight: 600; }
  `]
})
export class JournalAnswersComponent {
  readonly sections = input.required<JournalSection[]>();
  readonly answers = input<Record<string, JournalAnswer>>({});
  readonly collapseUnanswered = input(false);
  readonly showAll = signal(false);

  readonly hiddenCount = computed(() =>
    this.sections().flatMap(s => s.fields).filter(f => !isAnswered(this.answers()[f.id])).length);

  readonly visibleSections = computed<JournalSection[]>(() => {
    if (!this.collapseUnanswered() || this.showAll()) return this.sections();
    return this.sections()
      .map(s => ({ ...s, fields: s.fields.filter(f => isAnswered(this.answers()[f.id])) }))
      .filter(s => s.fields.length > 0);
  });

  checklist(id: string): Record<string, boolean> | null {
    const value = this.answers()[id];
    return value && typeof value === 'object' && !Array.isArray(value) ? value : null;
  }

  display(f: JournalField): string | null {
    const value = this.answers()[f.id];
    if (value === undefined || value === null || value === '') return null;
    if (Array.isArray(value)) return value.join(', ');
    if (typeof value === 'boolean') return value ? 'Yes' : 'No';
    if (f.type === 'Rating') return `${value} / 5`;
    if (f.type === 'DateTime') return String(value).replace('T', ' ');
    return String(value);
  }
}
