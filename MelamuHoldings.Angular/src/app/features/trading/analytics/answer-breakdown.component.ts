import { Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TradingApiService } from '../trading-api.service';
import { AnalyticsFilter, AnalyticsFilterOptions } from '../models/analytics-filter.model';
import { AnswerBreakdown } from '../models/strategy.model';
import { describeHttpError } from '../../../shared/http-error';

// Results grouped by the answers to one journal question with fixed answers (emotion, rating, mistakes…), for the
// trades in view. Each row holds exactly the trades its answer filter keeps, so "Show" narrows the page to them.
@Component({
  selector: 'app-answer-breakdown',
  standalone: true,
  imports: [DecimalPipe],
  template: `
    @if (fields().length === 0) {
      <p class="muted pad">No journal question with fixed answers yet. Add a choice, rating or yes/no question to a template to compare results by answer.</p>
    } @else {
      <div class="pick">
        <label for="bd-question">Question</label>
        <select id="bd-question" (change)="selected.set($any($event.target).value)">
          @for (f of fields(); track f.id) { <option [value]="f.id" [selected]="f.id === field()">{{ f.label }}</option> }
        </select>
      </div>
      @if (error()) { <p class="notice notice-error pad" role="alert">{{ error() }}</p> }
      @if (data(); as d) {
        <table class="data-table">
          <thead>
            <tr><th>Answer</th><th class="num">Trades</th><th class="num">Win rate</th><th class="num">Average R</th><th class="num">P&amp;L</th><th><span class="visually-hidden">Trades with this answer</span></th></tr>
          </thead>
          <tbody>
            @for (a of d.answers; track a.value) {
              <tr [class.muted]="a.results.trades === 0">
                <td>{{ a.label }} @if (a.results.trades > 0 && a.results.insufficientData) { <span class="tag">small sample</span> }</td>
                <td class="num">{{ a.results.trades }}</td>
                <td class="num">{{ a.results.winRate == null ? '—' : (a.results.winRate | number: '1.1-1') + '%' }}</td>
                <td class="num">{{ a.results.averageR == null ? '—' : (a.results.averageR | number: '1.2-2') + 'R' }}</td>
                <td class="num" [class.text-positive]="a.results.netPnL > 0" [class.text-negative]="a.results.netPnL < 0">{{ a.results.trades === 0 ? '—' : signed(a.results.netPnL) }}</td>
                <td class="num">
                  @if (a.results.trades > 0 && a.value != null) {
                    <button type="button" class="btn-link small" (click)="drill.emit({ ...filter(), answerField: d.fieldId, answerValue: a.value })">Show</button>
                  }
                </td>
              </tr>
            }
            <tr class="not-answered">
              <td>Not answered</td>
              <td class="num">{{ d.notAnswered.results.trades }}</td>
              <td class="num">{{ d.notAnswered.results.winRate == null ? '—' : (d.notAnswered.results.winRate | number: '1.1-1') + '%' }}</td>
              <td class="num">{{ d.notAnswered.results.averageR == null ? '—' : (d.notAnswered.results.averageR | number: '1.2-2') + 'R' }}</td>
              <td class="num">{{ d.notAnswered.results.trades === 0 ? '—' : signed(d.notAnswered.results.netPnL) }}</td>
              <td></td>
            </tr>
          </tbody>
        </table>
        <p class="small muted pad">
          {{ d.completedTrades }} closed {{ d.completedTrades === 1 ? 'trade' : 'trades' }} in view.
          @if (d.countsTradesMoreThanOnce) { A trade with several answers counts under each, so the rows add up to more than that. }
          An answer with fewer than {{ d.minimumSample }} trades is too few to compare. This shows what happened with each answer, not why.
        </p>
      }
    }
  `,
  styles: [`
    .pick { display: flex; gap: var(--space-3); align-items: center; padding: var(--space-3) var(--space-5); flex-wrap: wrap; }
    .pick select { max-width: 100%; }
    .pad { padding: var(--space-3) var(--space-5); margin: 0; }
    .not-answered td { color: var(--text-secondary); border-top: 1px solid var(--border); }
  `]
})
export class AnswerBreakdownComponent {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);

  readonly filter = input<AnalyticsFilter>({});
  readonly options = input<AnalyticsFilterOptions | null>(null);
  readonly drill = output<AnalyticsFilter>();

  readonly fields = computed(() => this.options()?.journalFields ?? []);
  readonly selected = signal('');
  // The chosen question; until the trader picks, the question the page is filtered by (after "Show"), else the first
  readonly field = computed(() => {
    const ids = this.fields().map(f => f.id);
    const filtered = this.filter().answerField;
    return ids.includes(this.selected()) ? this.selected() : filtered && ids.includes(filtered) ? filtered : ids[0] ?? '';
  });
  readonly data = signal<AnswerBreakdown | null>(null);
  readonly error = signal('');

  constructor() {
    effect(() => {
      const field = this.field();
      const filter = this.filter();
      if (field) untracked(() => this.load(field, filter));
    });
  }

  private load(field: string, filter: AnalyticsFilter): void {
    this.error.set('');
    this.api.getAnswerBreakdown(field, filter).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: d => this.data.set(d),
      error: err => { this.data.set(null); this.error.set(`Could not load the breakdown: ${describeHttpError(err)}`); }
    });
  }

  signed(value: number): string {
    const formatted = Math.abs(value).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    return value > 0 ? `+${formatted}` : value < 0 ? `−${formatted}` : formatted;
  }
}
