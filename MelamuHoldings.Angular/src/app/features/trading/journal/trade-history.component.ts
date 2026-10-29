import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TradingApiService } from '../trading-api.service';
import { TradeRevision, revisionTitle } from '../models/trade.model';
import { describeHttpError } from '../../../shared/http-error';

// Every saved change to a trade, newest first, with each value before and after. Loaded when opened,
// and again whenever the trade changes while it is open.
@Component({
  selector: 'app-trade-history',
  standalone: true,
  imports: [DatePipe],
  template: `
    <details (toggle)="toggled($event)">
      <summary>Show what changed, and when</summary>
      @if (state() === 'loading') { <p class="muted" role="status">Loading the history…</p> }
      @if (state() === 'error') {
        <p class="notice notice-error" role="alert">Could not load the history: {{ error() }} <button type="button" class="btn-link" (click)="load()">Try again</button></p>
      }
      @if (state() === 'ready') {
        @if (revisions().length > 0) {
          <ol class="revisions">
            @for (r of revisions(); track r.id) {
              <li>
                <p class="when"><strong>{{ title(r) }}</strong><span class="muted"> · {{ r.occurredAt | date: 'd MMM yyyy, HH:mm' }}</span></p>
                @if (r.changes.length > 0) {
                  <table class="diff">
                    <caption class="visually-hidden">Changes saved {{ r.occurredAt | date: 'd MMM yyyy, HH:mm' }}</caption>
                    <thead><tr><th scope="col">What</th><th scope="col">Before</th><th scope="col">After</th></tr></thead>
                    <tbody>
                      @for (c of r.changes; track c.field) {
                        <tr>
                          <th scope="row">{{ c.label }}</th>
                          <td [class.muted]="c.before == null"><span class="cell-label">Before</span>{{ c.before ?? '—' }}</td>
                          <td [class.muted]="c.after == null"><span class="cell-label">After</span>{{ c.after ?? '—' }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                }
              </li>
            }
          </ol>
        }
        @if (!revisions().some(r => r.kind === 'Created')) {
          <p class="small muted">{{ origin() }}</p>
        }
      }
    </details>
  `,
  styles: [`
    summary { cursor: pointer; color: var(--text-link, inherit); font-weight: 500; }
    .revisions { list-style: none; display: grid; gap: var(--space-5); margin-top: var(--space-4); }
    .when { margin-bottom: var(--space-2); }
    .diff { width: 100%; border-collapse: collapse; font-size: var(--text-sm); }
    .diff th, .diff td { text-align: left; vertical-align: top; padding: var(--space-2) var(--space-3); border-top: 1px solid var(--border-subtle); }
    .diff thead th { color: var(--text-secondary); font-weight: 500; border-top: 0; }
    .diff tbody th { font-weight: 500; width: 28%; }
    .diff td { white-space: pre-wrap; overflow-wrap: anywhere; width: 36%; }
    .cell-label { display: none; }
    @media (max-width: 600px) {
      .diff thead { display: none; }
      .diff tr { display: grid; padding: var(--space-2) 0; border-top: 1px solid var(--border-subtle); }
      .diff tbody th, .diff td { border: 0; padding: var(--space-1) 0; width: auto; }
      .diff td { display: grid; grid-template-columns: 4.5em 1fr; }
      .cell-label { display: block; color: var(--text-secondary); }
    }
  `]
})
export class TradeHistoryComponent {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);

  readonly tradeId = input.required<number>();
  // Changes whenever the trade is saved; an open history reloads
  readonly stamp = input<unknown>(null);
  readonly createdAt = input<string | undefined>(undefined);
  readonly isImported = input(false);

  readonly state = signal<'closed' | 'loading' | 'ready' | 'error'>('closed');
  readonly error = signal('');
  readonly revisions = signal<TradeRevision[]>([]);
  readonly title = revisionTitle;

  constructor() {
    effect(() => {
      this.stamp();
      if (untracked(this.state) !== 'closed') untracked(() => this.load());
    });
  }

  toggled(event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (!open) this.state.set('closed');
    else if (this.state() === 'closed') this.load();
  }

  load(): void {
    this.state.set('loading');
    this.api.getTradeHistory(this.tradeId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: list => { this.revisions.set(list); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  // Trades imported or saved before the history was kept have no creation record
  origin(): string {
    const at = this.createdAt();
    const when = at ? ` on ${new Date(at).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })}` : '';
    return this.isImported()
      ? `Imported from CSV${when}. Changes since then are listed above.`
      : `Created${when}, before changes were recorded: only later changes are listed.`;
  }
}
