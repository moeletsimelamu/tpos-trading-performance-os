import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, of } from 'rxjs';
import { TradingApiService } from '../trading-api.service';
import { AiJournalAnswer, AiReviewStatus, SUGGESTED_QUESTIONS } from '../models/ai-review.model';
import { AnalyticsFilter, AnalyticsFilterOptions, describeFilter } from '../models/analytics-filter.model';
import { describeHttpError } from '../../../shared/http-error';

// A question about the trades in view, answered by the optional AI assistant from the trader's own records.
// Hidden unless AI review is turned on. The answer keeps recorded facts, the app's figures and the model's
// interpretation apart, and nothing is saved.
@Component({
  selector: 'app-ai-journal-question',
  standalone: true,
  imports: [DatePipe, FormsModule, RouterLink],
  template: `
    @if (status()?.enabled) {
      <section class="card ask" aria-labelledby="ask-heading">
        <div class="card-header"><h2 id="ask-heading">Ask about these trades</h2><span class="tag">AI · optional</span></div>
        <div class="body">
          <p class="small muted">
            Sends your notes, journal answers and checklist results for up to {{ status()!.maxJournalTrades }} of the most recent closed trades
            in this view{{ scope() ? ' (' + scope() + ')' : '' }} to {{ status()!.model }}. Nothing is saved. Not advice: no trading signals and no diagnoses.
          </p>
          <div class="suggestions" role="group" aria-label="Suggested questions">
            @for (q of suggestions; track q) {
              <button type="button" class="chip" [class.selected]="question === q" (click)="question = q">{{ q }}</button>
            }
          </div>
          <form (ngSubmit)="ask()" class="ask-form">
            <div class="field grow">
              <label for="ask-question">Your question</label>
              <input id="ask-question" name="question" [(ngModel)]="question" maxlength="500" autocomplete="off" />
            </div>
            <button type="submit" class="btn btn-primary" [disabled]="busy() || question.trim().length < 3">{{ busy() ? 'Asking…' : 'Ask' }}</button>
          </form>
          @if (error()) { <p class="notice notice-error" role="alert">{{ error() }}</p> }

          @if (answer(); as a) {
            <div class="result" aria-live="polite">
              <p class="asked"><span class="muted">You asked:</span> {{ a.question }}</p>
              @for (w of a.warnings; track w) { <p class="notice notice-warning small">{{ w }}</p> }

              @if (a.answer) {
                <div class="part">
                  <h3>Answer <span class="tag tag-warning">AI · may be wrong</span></h3>
                  <p class="pre">{{ a.answer }}</p>
                </div>
              }

              @if (!a.sufficientData || a.missing.length > 0) {
                <div class="part">
                  <h3>{{ a.sufficientData ? 'What would help' : 'Not enough data' }}</h3>
                  <ul>@for (m of a.missing; track m) { <li>{{ m }}</li> }</ul>
                </div>
              }

              @if (a.facts.length > 0) {
                <div class="part">
                  <h3>Recorded in your journal</h3>
                  <ul>
                    @for (f of a.facts; track $index) {
                      <li>{{ f.text }}
                        <span class="small muted">— @for (id of f.tradeIds; track id; let last = $last) {<a [routerLink]="['/trading/trades', id]">trade {{ id }}</a>{{ last ? '' : ', ' }}}</span>
                      </li>
                    }
                  </ul>
                </div>
              }

              @if (a.metrics.length > 0) {
                <div class="part">
                  <h3>Calculated by the app</h3>
                  <table class="data-table compact">
                    <tbody>
                      @for (m of a.metrics; track m.label) {
                        <tr><th scope="row">{{ m.label }}</th><td class="num">{{ m.value }}</td><td class="small muted">{{ m.basis }}</td></tr>
                      }
                    </tbody>
                  </table>
                </div>
              }

              @if (a.interpretations.length > 0) {
                <div class="part">
                  <h3>AI interpretation <span class="tag tag-warning">may be wrong</span></h3>
                  <ul>@for (i of a.interpretations; track $index) { <li>{{ i }}</li> }</ul>
                </div>
              }

              @if (a.reflectionPrompts.length > 0) {
                <div class="part">
                  <h3>Questions to consider</h3>
                  <ul>@for (q of a.reflectionPrompts; track $index) { <li>{{ q }}</li> }</ul>
                </div>
              }

              <p class="small muted">
                @if (a.tradesInView > 0) {
                  Based on {{ a.tradeIds.length }} of {{ a.tradesInView }} closed {{ a.tradesInView === 1 ? 'trade' : 'trades' }} in this view,
                  {{ a.from | date: 'd MMM yyyy' }} to {{ a.to | date: 'd MMM yyyy' }}.
                }
                Answered by {{ a.model ?? 'the AI provider' }}. <button type="button" class="btn-link small" (click)="answer.set(null)">Clear</button>
              </p>
            </div>
          }
        </div>
      </section>
    }
  `,
  styles: [`
    .ask { margin-top: var(--space-6); padding: 0; }
    .body { padding: var(--space-5); }
    .suggestions { display: flex; flex-wrap: wrap; gap: var(--space-2); margin: var(--space-4) 0; }
    .chip {
      font: inherit; font-size: var(--text-sm); text-align: left; cursor: pointer; padding: var(--space-1) var(--space-3);
      border: 1px solid var(--border); border-radius: 999px; background: var(--surface); color: var(--text-primary);
      &.selected { border-color: var(--accent, var(--text-primary)); background: var(--surface-sunken); }
    }
    .ask-form { display: flex; gap: var(--space-3); align-items: flex-end; flex-wrap: wrap; }
    .grow { flex: 1 1 260px; margin: 0; }
    .result { margin-top: var(--space-5); padding: var(--space-4); border: 1px dashed var(--border); border-radius: var(--radius); background: var(--surface-sunken); }
    .asked { margin-bottom: var(--space-2); }
    h3 { font-size: var(--text-sm); margin-bottom: var(--space-2); display: flex; gap: var(--space-2); align-items: center; }
    .part { margin-top: var(--space-4); }
    ul { padding-left: var(--space-5); display: grid; gap: var(--space-1); }
    .pre { white-space: pre-wrap; }
    .compact th { font-weight: 400; color: var(--text-secondary); }
    .notice { margin: var(--space-3) 0 0; }
    // On a phone each figure is its label and value, with where it comes from underneath
    @media (max-width: 600px) {
      .compact, .compact tbody { display: block; }
      .compact tr { display: grid; grid-template-columns: 1fr auto; gap: 0 var(--space-3); padding: var(--space-2) 0; border-bottom: 1px solid var(--border-subtle); }
      .compact th, .compact td { padding: 0; border: 0; }
      .compact td.num { white-space: normal; text-align: right; max-width: 55vw; overflow-wrap: anywhere; }
      .compact td.small { grid-column: 1 / -1; }
    }
  `]
})
export class AiJournalQuestionComponent {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);

  readonly filter = input<AnalyticsFilter>({});
  readonly options = input<AnalyticsFilterOptions | null>(null);

  readonly suggestions = SUGGESTED_QUESTIONS;
  readonly status = signal<AiReviewStatus | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly answer = signal<AiJournalAnswer | null>(null);
  readonly scope = computed(() => describeFilter(this.filter(), this.options()).join(' · '));
  question = '';

  constructor() {
    this.api.getAiReviewStatus().pipe(catchError(() => of(null)), takeUntilDestroyed(this.destroyRef)).subscribe(s => this.status.set(s));
    // An answer describes the trades that were in view when it was asked; a new filter clears it
    effect(() => {
      this.filter();
      this.answer.set(null);
      this.error.set('');
    });
  }

  ask(): void {
    const question = this.question.trim();
    if (this.busy() || question.length < 3) return;
    this.busy.set(true);
    this.error.set('');
    this.api.askAboutJournal(question, this.filter()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: a => { this.busy.set(false); this.answer.set(a); },
      error: err => { this.busy.set(false); this.error.set(`No answer: ${describeHttpError(err)}`); }
    });
  }
}
