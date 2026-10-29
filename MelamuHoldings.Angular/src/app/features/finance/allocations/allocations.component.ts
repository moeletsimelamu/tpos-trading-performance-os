import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { FinanceApiService } from '../finance-api.service';
import { ALLOCATION_STATUS, Allocation, Money } from '../finance.models';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-finance-allocations',
  standalone: true,
  imports: [DatePipe, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header section="Finance" title="Capital allocation"
        description="Confirmed splits of received payouts, and how much of each has really been moved or earmarked.">
        <a class="btn" routerLink="/finance/allocation-policy">Allocation policy</a>
        <a class="btn btn-primary" routerLink="/finance/payouts">Payouts</a>
      </app-page-header>

      @switch (state()) {
        @case ('loading') {
          <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading allocations...</span></div>
        }
        @case ('error') {
          <app-load-error title="Could not load allocations" [message]="error()" (retry)="load()" />
        }
        @case ('ready') {
          @if (outstanding().length > 0) {
            <section class="metric-strip summary" aria-label="Outstanding allocations">
              @for (m of outstanding(); track m.currency) {
                <div class="metric">
                  <span class="metric-label">Still to settle · {{ m.currency }}</span>
                  <span class="metric-value">{{ m.amount | money: m.currency }}</span>
                  <span class="metric-note">Confirmed, but not yet earmarked or moved</span>
                </div>
              }
            </section>
          }

          @if (allocations().length === 0) {
            <div class="card empty-state">
              <h2>No allocations yet</h2>
              <p>Allocate a payout once it has been received: open it from the payouts list and confirm the split.</p>
              <a class="btn" routerLink="/finance/payouts">Go to payouts</a>
            </div>
          } @else {
            <div class="card table-card">
              <table class="data-table">
                <thead>
                  <tr><th>Confirmed</th><th>Payout</th><th>Policy</th><th class="num">Total</th><th class="num">Settled</th><th class="num">Outstanding</th><th>Status</th></tr>
                </thead>
                <tbody>
                  @for (a of allocations(); track a.id) {
                    <tr [class.cancelled]="a.status === 'Cancelled'">
                      <td class="nowrap secondary">{{ a.confirmedAt | date: 'yyyy-MM-dd' }}</td>
                      <td>
                        <a [routerLink]="['/finance/allocations', a.id]">Allocation #{{ a.id }}</a>
                        <div class="small muted">Payout #{{ a.payoutId }} · {{ a.payoutSource }} · in {{ a.sourceAccountName }}</div>
                      </td>
                      <td class="secondary">v{{ a.policyVersion }}</td>
                      <td class="num">{{ a.total.amount | money: a.total.currency }}</td>
                      <td class="num">{{ a.settled.amount | money: a.settled.currency }}</td>
                      <td class="num">{{ a.outstanding.amount | money: a.outstanding.currency }}</td>
                      <td><span [class]="status[a.status].tag">{{ status[a.status].label }}</span></td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }
      }
    </div>
  `,
  styles: [`
    .summary { margin-bottom: var(--space-5); }
    .cancelled td { color: var(--text-muted); }
  `]
})
export class AllocationsComponent {
  private api = inject(FinanceApiService);
  private destroyRef = inject(DestroyRef);

  readonly status = ALLOCATION_STATUS;
  readonly state = signal<LoadState>('loading');
  readonly error = signal('');
  readonly allocations = signal<Allocation[]>([]);

  // Per currency, display only: the server's per-allocation outstanding figures added up
  readonly outstanding = computed<Money[]>(() => {
    const totals = new Map<string, number>();
    for (const a of this.allocations().filter(a => a.outstanding.amount > 0))
      totals.set(a.outstanding.currency, Math.round(((totals.get(a.outstanding.currency) ?? 0) + a.outstanding.amount) * 100) / 100);
    return [...totals].map(([currency, amount]) => ({ currency, amount }));
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.api.getAllocations().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: a => { this.allocations.set(a); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }
}
