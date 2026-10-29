import { Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TradingApiService } from '../trading-api.service';
import { AiReviewDraft } from '../models/ai-review.model';
import { describeHttpError } from '../../../shared/http-error';

// Asks the optional AI assistant for a draft review of one closed trade. The answer keeps recorded facts, the app's
// own figures and the model's interpretation apart; the suggested text is only used when the trader chooses to,
// and it is added to the review box, never saved by itself.
@Component({
  selector: 'app-ai-review-draft',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="ai" aria-labelledby="ai-heading">
      <div class="ai-head">
        <h3 id="ai-heading">AI draft</h3>
        @if (!draft()) {
          <button type="button" class="btn btn-sm" [disabled]="busy()" (click)="ask()">{{ busy() ? 'Drafting…' : 'Draft a review with AI' }}</button>
        }
      </div>
      <p class="small muted">
        Reads only this trade, its journal answers and checklist, and up to {{ maxComparable() }} recent trades with the same strategy and session.
        Nothing is saved unless you put it in your review and save it. Not advice: no trading signals and no diagnoses.
      </p>
      @if (error()) { <p class="notice notice-error" role="alert">{{ error() }}</p> }

      @if (draft(); as d) {
        @for (w of d.warnings; track w) { <p class="notice notice-warning small">{{ w }}</p> }

        @if (!d.sufficientData || d.missing.length > 0) {
          <div class="part">
            <h4>{{ d.sufficientData ? 'What would help' : 'Not enough data' }}</h4>
            <ul>@for (m of d.missing; track m) { <li>{{ m }}</li> }</ul>
          </div>
        }

        @if (d.facts.length > 0) {
          <div class="part">
            <h4>Recorded in your journal</h4>
            <ul>
              @for (f of d.facts; track $index) {
                <li>{{ f.text }}
                  <span class="small muted">— @for (id of f.tradeIds; track id; let last = $last) {<a [routerLink]="['/trading/trades', id]">trade {{ id }}</a>{{ last ? '' : ', ' }}}</span>
                </li>
              }
            </ul>
          </div>
        }

        <div class="part">
          <h4>Calculated by the app</h4>
          <table class="data-table compact">
            <tbody>
              @for (m of d.metrics; track m.label) {
                <tr><th scope="row">{{ m.label }}</th><td class="num">{{ m.value }}</td><td class="small muted">{{ m.basis }}</td></tr>
              }
            </tbody>
          </table>
        </div>

        @if (d.interpretations.length > 0) {
          <div class="part interpretation">
            <h4>AI interpretation <span class="tag tag-warning">may be wrong</span></h4>
            <ul>@for (i of d.interpretations; track $index) { <li>{{ i }}</li> }</ul>
          </div>
        }

        @if (d.reflectionPrompts.length > 0) {
          <div class="part">
            <h4>Questions to consider</h4>
            <ul>@for (q of d.reflectionPrompts; track $index) { <li>{{ q }}</li> }</ul>
          </div>
        }

        @if (d.suggestedReview; as text) {
          <div class="part">
            <h4>Suggested review</h4>
            <blockquote class="pre">{{ text }}</blockquote>
            <div class="actions">
              <button type="button" class="btn btn-sm btn-primary" (click)="use.emit(text)">{{ hasNotes() ? 'Add below my notes' : 'Use as my review' }}</button>
              <button type="button" class="btn-link small" (click)="discard()">Discard</button>
            </div>
            <p class="small muted">Edit it before you mark the trade reviewed; it is your review, not the AI's.</p>
          </div>
        } @else {
          <div class="actions"><button type="button" class="btn-link small" (click)="discard()">Close</button></div>
        }
        <p class="small muted">Drafted by {{ d.model ?? 'the AI provider' }}.</p>
      }
    </section>
  `,
  styles: [`
    .ai { margin-top: var(--space-5); padding: var(--space-4); border: 1px dashed var(--border); border-radius: var(--radius); background: var(--surface-sunken); }
    .ai-head { display: flex; justify-content: space-between; align-items: center; gap: var(--space-3); }
    h3 { font-size: var(--text-md); }
    h4 { font-size: var(--text-sm); margin-bottom: var(--space-2); display: flex; gap: var(--space-2); align-items: center; }
    .part { margin-top: var(--space-4); }
    ul { padding-left: var(--space-5); display: grid; gap: var(--space-1); }
    blockquote { border-left: 3px solid var(--border); padding-left: var(--space-3); margin: 0 0 var(--space-3); }
    .pre { white-space: pre-wrap; }
    .actions { display: flex; gap: var(--space-3); align-items: center; margin-top: var(--space-2); }
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
export class AiReviewDraftComponent {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);

  readonly tradeId = input.required<number>();
  readonly maxComparable = input(10);
  // Whether the review box already holds the trader's words: the draft is then added below, never in their place
  readonly hasNotes = input(false);
  readonly use = output<string>();

  readonly busy = signal(false);
  readonly error = signal('');
  readonly draft = signal<AiReviewDraft | null>(null);

  ask(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.api.draftAiReview(this.tradeId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: d => { this.busy.set(false); this.draft.set(d); },
      error: err => { this.busy.set(false); this.error.set(`No draft: ${describeHttpError(err)}`); }
    });
  }

  discard(): void {
    this.draft.set(null);
  }
}
