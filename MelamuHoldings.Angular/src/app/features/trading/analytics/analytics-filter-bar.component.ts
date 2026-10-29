import { Component, computed, input, output, signal } from '@angular/core';
import { ADHERENCE_FILTERS, AdherenceFilter, AnalyticsFilter, AnalyticsFilterOptions, describeFilter, filterToQuery, isFilterEmpty } from '../models/analytics-filter.model';

// The filters every analytics page shares. Each change is applied at once; a journal question applies once an
// answer is chosen for it.
@Component({
  selector: 'app-analytics-filter-bar',
  standalone: true,
  template: `
    <section class="card filters" aria-label="Filters">
      <button type="button" class="btn btn-sm toggle" aria-controls="filter-grid" [attr.aria-expanded]="open()" (click)="open.set(!open())">
        {{ open() ? 'Hide filters' : 'Filters' }}{{ applied().length > 0 ? ' (' + applied().length + ' applied)' : '' }}
      </button>
      <div class="filter-grid" id="filter-grid" [class.open]="open()">
        <div class="field">
          <label for="f-from">From</label>
          <input id="f-from" type="date" [value]="filter().from ?? ''" [max]="filter().to ?? ''" (change)="set('from', $any($event.target).value)" />
        </div>
        <div class="field">
          <label for="f-to">To</label>
          <input id="f-to" type="date" [value]="filter().to ?? ''" [min]="filter().from ?? ''" (change)="set('to', $any($event.target).value)" />
        </div>
        <div class="field">
          <label for="f-strategy">Strategy</label>
          <select id="f-strategy" (change)="set('strategyId', $any($event.target).value)">
            <option value="" [selected]="!filter().strategyId">All strategies</option>
            @for (s of options()?.strategies ?? []; track s.value) { <option [value]="s.value" [selected]="s.value === '' + filter().strategyId">{{ s.label }}</option> }
          </select>
        </div>
        @if ((options()?.instruments ?? []).length > 1) {
          <div class="field">
            <label for="f-instrument">Instrument</label>
            <select id="f-instrument" (change)="set('instrument', $any($event.target).value)">
              <option value="" [selected]="!filter().instrument">All instruments</option>
              @for (i of options()!.instruments; track i) { <option [value]="i" [selected]="i === filter().instrument">{{ i }}</option> }
            </select>
          </div>
        }
        <div class="field">
          <label for="f-session">Session</label>
          <select id="f-session" (change)="set('session', $any($event.target).value)">
            <option value="" [selected]="!filter().session">All sessions</option>
            @for (s of options()?.sessions ?? []; track s) { <option [value]="s" [selected]="s === filter().session">{{ s }}</option> }
          </select>
        </div>
        @if ((options()?.accounts ?? []).length > 0) {
          <div class="field">
            <label for="f-account">Account</label>
            <select id="f-account" (change)="set('account', $any($event.target).value)">
              <option value="" [selected]="!filter().account">All accounts</option>
              @for (a of options()!.accounts; track a) { <option [value]="a" [selected]="a === filter().account">{{ a }}</option> }
            </select>
          </div>
        }
        <div class="field">
          <label for="f-adherence">Rule adherence</label>
          <select id="f-adherence" (change)="set('adherence', $any($event.target).value)">
            <option value="" [selected]="!filter().adherence">All trades</option>
            @for (a of adherence; track a.value) { <option [value]="a.value" [selected]="a.value === filter().adherence">{{ a.label }}</option> }
          </select>
        </div>
        @if ((options()?.journalFields ?? []).length > 0) {
          <div class="field">
            <label for="f-question">Journal answer</label>
            <select id="f-question" (change)="chooseQuestion($any($event.target).value)">
              <option value="" [selected]="!question()">Any answers</option>
              @for (f of options()!.journalFields; track f.id) { <option [value]="f.id" [selected]="f.id === question()">{{ f.label }}</option> }
            </select>
          </div>
          @if (questionField(); as q) {
            <div class="field">
              <label for="f-answer">{{ q.label }}</label>
              <select id="f-answer" (change)="chooseAnswer($any($event.target).value)">
                <option value="" [selected]="!filter().answerValue">Choose an answer</option>
                @for (o of q.options; track o.value) { <option [value]="o.value" [selected]="o.value === filter().answerValue">{{ o.label }}</option> }
              </select>
            </div>
          }
        }
      </div>
      @if (!empty()) {
        <p class="applied small">
          <span class="secondary">Showing only:</span> {{ applied().join(' · ') }}
          <button type="button" class="btn-link small" (click)="clear()">Clear filters</button>
        </p>
      }
    </section>
  `,
  styles: [`
    .filters { margin-bottom: var(--space-5); }
    .filter-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(170px, 1fr)); gap: var(--space-3); }
    .applied { margin-top: var(--space-3); display: flex; flex-wrap: wrap; gap: var(--space-2); align-items: baseline; }
    .toggle { display: none; }
    /* On a phone the filters fold away so the figures come first */
    @media (max-width: 599px) {
      .toggle { display: inline-flex; }
      .filter-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); margin-top: var(--space-3); }
      .filter-grid:not(.open) { display: none; }
    }
  `]
})
export class AnalyticsFilterBarComponent {
  readonly options = input<AnalyticsFilterOptions | null>(null);
  readonly filter = input<AnalyticsFilter>({});
  readonly filterChange = output<AnalyticsFilter>();

  readonly adherence = ADHERENCE_FILTERS;
  // A question chosen but not yet answered: shown, not applied
  private readonly pending = signal<string | null>(null);
  // Phone width only: whether the folded filters are shown
  readonly open = signal(false);

  readonly question = computed(() => this.pending() ?? this.filter().answerField ?? null);
  readonly questionField = computed(() => this.options()?.journalFields.find(f => f.id === this.question()) ?? null);
  readonly empty = computed(() => isFilterEmpty(this.filter()));
  readonly applied = computed(() => describeFilter(this.filter(), this.options()));

  set(key: 'from' | 'to' | 'strategyId' | 'instrument' | 'session' | 'account' | 'adherence', raw: string): void {
    const next: AnalyticsFilter = { ...this.filter() };
    if (!raw) delete next[key];
    else if (key === 'strategyId') next.strategyId = Number(raw);
    else if (key === 'adherence') next.adherence = raw as AdherenceFilter;
    else next[key] = raw;
    this.emit(next);
  }

  chooseQuestion(id: string): void {
    const next: AnalyticsFilter = { ...this.filter() };
    delete next.answerField;
    delete next.answerValue;
    this.pending.set(id || null);
    // Dropping a question, or switching from an applied one, changes the results at once
    if (!id || this.filter().answerField) this.emit(next);
  }

  chooseAnswer(value: string): void {
    const field = this.question();
    if (!field) return;
    const next: AnalyticsFilter = { ...this.filter() };
    if (value) { next.answerField = field; next.answerValue = value; }
    else { delete next.answerField; delete next.answerValue; }
    this.pending.set(value ? null : field);
    this.emit(next);
  }

  clear(): void {
    this.pending.set(null);
    this.emit({});
  }

  private emit(filter: AnalyticsFilter): void {
    // Only complete, non-empty values are passed on
    const clean = filterToQuery(filter);
    this.filterChange.emit({
      ...clean,
      ...(clean['strategyId'] ? { strategyId: Number(clean['strategyId']) } : {})
    } as AnalyticsFilter);
  }
}
