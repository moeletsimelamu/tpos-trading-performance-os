import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TradingApiService } from '../trading-api.service';
import { TRADE_STATUS, Trade } from '../models/trade.model';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

// One journal entry as a readable document: what was traded, the execution, the notes, and the review
@Component({
  selector: 'app-trade-entry',
  standalone: true,
  imports: [DatePipe, DecimalPipe, FormsModule, RouterLink, LoadErrorComponent, PageHeaderComponent],
  template: `
    <div class="page narrow">
      @switch (state()) {
        @case ('loading') {
          <app-page-header section="Trading operations · Trades" title="Trade" />
          <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading trade...</span></div>
        }
        @case ('error') {
          <app-page-header section="Trading operations · Trades" title="Trade" />
          <app-load-error title="Could not load the trade" [message]="error()" (retry)="load()" />
        }
        @case ('ready') {
          @if (trade(); as t) {
            <app-page-header section="Trading operations · Trades" [title]="heading()" [description]="subtitle()">
              <a class="btn" routerLink="/trading/trades">All trades</a>
              <a class="btn btn-primary" [routerLink]="['/trading/trades', t.id, 'edit']">Edit</a>
            </app-page-header>

            <div class="status-row">
              <span [class]="status[t.status].tag">{{ status[t.status].label }}</span>
              <span class="small muted">{{ statusNote() }}</span>
            </div>

            <section class="card flush" aria-labelledby="exec-heading">
              <div class="card-header"><h2 id="exec-heading">Execution</h2>@if (t.currency) { <span class="card-note">Amounts in {{ t.currency }}</span> }</div>
              <dl class="facts">
                <div><dt>Direction</dt><dd>{{ t.direction ?? '—' }}</dd></div>
                <div><dt>Entry</dt><dd>{{ price(t.entryPrice) }}<span class="muted">{{ t.entryTime ? ' at ' + t.entryTime.slice(0, 5) : '' }}</span></dd></div>
                <div><dt>Stop · target</dt><dd>{{ price(t.stopLoss) }} · {{ price(t.takeProfit) }}</dd></div>
                <div><dt>Size · risk</dt><dd>{{ t.positionSize ?? '—' }} · {{ money(t.riskAmount) }}</dd></div>
                <div><dt>Exit</dt><dd>{{ price(t.exitPrice) }}<span class="muted">{{ exitWhen() }}</span></dd></div>
                <div>
                  <dt>Realised P&amp;L</dt>
                  <dd [class.text-positive]="(t.pnL ?? 0) > 0" [class.text-negative]="(t.pnL ?? 0) < 0">{{ signed(t.pnL) }}</dd>
                </div>
                <div><dt>R-multiple</dt><dd>{{ t.rr != null ? (t.rr | number: '1.2-2') + 'R' : '—' }}</dd></div>
                <div><dt>Fees</dt><dd>{{ money(t.fees) }}</dd></div>
                <div><dt>Duration</dt><dd>{{ t.tradeDuration != null ? t.tradeDuration + ' min' : '—' }}</dd></div>
              </dl>
            </section>

            <section class="card flush" aria-labelledby="notes-heading">
              <div class="card-header"><h2 id="notes-heading">Notes</h2></div>
              <div class="prose">
                @if (t.notes) { <p class="pre">{{ t.notes }}</p> } @else { <p class="muted">No notes yet.</p> }
                @if (t.isManualOverride) {
                  <p class="notice notice-warning"><strong>Rule override.</strong>&nbsp;{{ t.deviationNotes ?? 'No explanation recorded.' }}</p>
                }
              </div>
            </section>

            <section class="card flush" aria-labelledby="review-heading">
              <div class="card-header">
                <h2 id="review-heading">Post-trade review</h2>
                @if (t.reviewedAt) { <span class="card-note">Reviewed {{ t.reviewedAt | date: 'd MMM yyyy' }}</span> }
              </div>
              <div class="prose">
                @switch (t.status) {
                  @case ('Reviewed') { <p class="pre">{{ t.reviewNotes }}</p> }
                  @case ('Closed') {
                    <form (ngSubmit)="review()" aria-label="Post-trade review">
                      <div class="field">
                        <label for="review-notes">Was the trade valid, was it executed as planned, and what would you do differently?</label>
                        <textarea id="review-notes" name="reviewNotes" [(ngModel)]="reviewNotes" maxlength="4000"></textarea>
                      </div>
                      @if (reviewError()) { <p class="notice notice-error" role="alert">{{ reviewError() }}</p> }
                      <div class="form-actions">
                        <button type="submit" class="btn btn-primary" [disabled]="busy() || !reviewNotes.trim()">{{ busy() ? 'Saving…' : 'Mark reviewed' }}</button>
                      </div>
                    </form>
                  }
                  @default { <p class="muted">Review the trade once it is closed.</p> }
                }
              </div>
            </section>

            <div class="danger-zone">
              @if (confirmingDelete()) {
                <button type="button" class="btn btn-danger" [disabled]="busy()" (click)="remove()">Confirm delete</button>
                <button type="button" class="btn-link small" (click)="confirmingDelete.set(false)">Keep</button>
              } @else {
                <button type="button" class="btn-link small" (click)="confirmingDelete.set(true)">Delete this trade</button>
              }
              @if (deleteError()) { <p class="notice notice-error" role="alert">{{ deleteError() }}</p> }
            </div>
          }
        }
      }
    </div>
  `,
  styles: [`
    .narrow { max-width: 920px; }
    .status-row { display: flex; gap: var(--space-3); align-items: center; margin: calc(-1 * var(--space-3)) 0 var(--space-5); }
    .flush { padding: 0; margin-bottom: var(--space-5); }
    .facts {
      display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: var(--space-4) var(--space-5); padding: var(--space-5);
      dt { font-size: var(--text-sm); color: var(--text-secondary); }
      dd { font-weight: 500; }
    }
    .prose { padding: var(--space-5); }
    .pre { white-space: pre-wrap; }
    .prose .notice { margin: var(--space-3) 0 0; }
    textarea { min-height: 120px; }
    .danger-zone { display: flex; gap: var(--space-3); align-items: center; flex-wrap: wrap; }
  `]
})
export class TradeEntryComponent {
  private api = inject(TradingApiService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  readonly status = TRADE_STATUS;
  private readonly id = Number(this.route.snapshot.paramMap.get('id'));

  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly error = signal('');
  readonly trade = signal<Trade | null>(null);
  readonly busy = signal(false);
  readonly reviewError = signal('');
  readonly deleteError = signal('');
  readonly confirmingDelete = signal(false);
  reviewNotes = '';

  readonly heading = computed(() => {
    const t = this.trade();
    return t ? `${t.instrument}${t.direction ? ' · ' + t.direction : ''}` : 'Trade';
  });

  readonly subtitle = computed(() => {
    const t = this.trade();
    if (!t) return '';
    const date = new Date(t.tradeDate + 'T00:00:00').toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
    return [date, t.session, t.strategyName, t.tradingAccount].filter(Boolean).join(' · ');
  });

  readonly statusNote = computed(() => {
    const t = this.trade();
    if (!t) return '';
    const source = t.isImported ? 'Imported from CSV. ' : '';
    switch (t.status) {
      case 'Draft': return source + 'Not counted in performance until it is closed.';
      case 'Open': return source + 'Position open: not counted in performance until it is closed.';
      case 'Closed': return source + 'Counted in performance. Write the review to complete the entry.';
      default: return source + 'Counted in performance, and reviewed.';
    }
  });

  readonly exitWhen = computed(() => {
    const t = this.trade();
    if (!t?.exitTime) return '';
    return ` at ${t.exitTime.slice(0, 5)}${t.exitDate && t.exitDate !== t.tradeDate ? ' on ' + t.exitDate : ''}`;
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.api.getTrade(this.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: t => { this.trade.set(t); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  review(): void {
    if (this.busy() || !this.reviewNotes.trim()) return;
    this.busy.set(true);
    this.reviewError.set('');
    this.api.reviewTrade(this.id, this.reviewNotes.trim()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: t => { this.busy.set(false); this.trade.set(t); },
      error: err => { this.busy.set(false); this.reviewError.set(`Not saved: ${describeHttpError(err)}`); }
    });
  }

  remove(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.api.deleteTrade(this.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.router.navigateByUrl('/trading/trades'),
      error: err => { this.busy.set(false); this.deleteError.set(`Not deleted: ${describeHttpError(err)}`); }
    });
  }

  price(value: number | null | undefined): string {
    return value == null ? '—' : value.toLocaleString('en-US', { maximumFractionDigits: 5 });
  }

  money(value: number | null | undefined): string {
    return value == null ? '—' : value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }

  signed(value: number | null | undefined): string {
    if (value == null) return '—';
    const formatted = this.money(Math.abs(value));
    return value > 0 ? `+${formatted}` : value < 0 ? `−${formatted}` : formatted;
  }
}
