import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { FinanceApiService } from '../finance-api.service';
import { UnrestrictedCashReport } from '../finance.models';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

// How much cash is free to use, with the working behind each figure. Every figure comes from the API; each currency
// stands alone and nothing here moves money.
@Component({
  selector: 'app-finance-unrestricted',
  standalone: true,
  imports: [RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header section="Finance" title="Unrestricted cash"
        description="Cash that is free to use: Unallocated and Operating Capital in company bank and cash accounts, less what is already spoken for. Each currency stands alone and nothing is converted." />

      @switch (state()) {
        @case ('loading') { <div class="loading-state"><div class="spinner"></div><span class="loading-text">Working it out...</span></div> }
        @case ('error') { <app-load-error title="Could not work out unrestricted cash" [message]="error()" (retry)="load()" /> }
        @case ('ready') {
          @if (report(); as r) {
            @for (c of r.currencies; track c.currency) {
              <section class="card table-card working" [attr.aria-labelledby]="'working-' + c.currency">
                <div class="card-header">
                  <h2 [id]="'working-' + c.currency">{{ c.currency }}</h2>
                  <span class="figure" [class.text-negative]="c.amount < 0">{{ c.amount | money: c.currency }}</span>
                </div>
                @if (c.amount < 0) { <p class="notice notice-warning small">More is spoken for than is held.</p> }
                <table class="data-table">
                  <tbody>
                    @for (h of c.holdings; track h.accountId + '-' + h.fundId) {
                      <tr>
                        <td>{{ h.fundName }} <span class="small muted">· {{ h.accountName }}</span></td>
                        <td class="num">{{ h.amount.amount | money: h.amount.currency }}</td>
                      </tr>
                    }
                    <tr class="subtotal"><td>Held</td><td class="num">{{ c.held | money: c.currency }}</td></tr>
                    <tr><td>Less <a routerLink="/finance/funds">Operating Capital's minimum</a></td><td class="num">{{ c.operatingMinimum | money: c.currency }}</td></tr>
                    <tr><td>Less what <a routerLink="/finance/allocations">confirmed allocations</a> still owe other funds</td><td class="num">{{ c.owedToOtherFunds | money: c.currency }}</td></tr>
                    <tr><td>Less <a routerLink="/finance/payouts">received payouts</a> not allocated yet</td><td class="num">{{ c.payoutsNotAllocated | money: c.currency }}</td></tr>
                    <tr><td>Less <a routerLink="/finance/purchases">committed purchases</a></td><td class="num">{{ c.committedPurchases | money: c.currency }}</td></tr>
                    <tr class="total"><td>Unrestricted cash</td><td class="num" [class.text-negative]="c.amount < 0">{{ c.amount | money: c.currency }}</td></tr>
                  </tbody>
                </table>
                @if (c.obligationsDue > 0) {
                  <p class="beside small secondary">
                    {{ c.obligationsDue | money: c.currency }} of <a routerLink="/finance/obligations">obligations</a> fall due within 30 days, overdue ones included. They are not deducted;
                    {{ c.afterObligations | money: c.currency }} would be left if all were paid.
                  </p>
                }
              </section>
            } @empty {
              <div class="card empty-state">
                <h2>No cash to work out yet</h2>
                <p>Unrestricted cash comes from company bank and cash accounts holding Unallocated or Operating Capital money.</p>
                <a class="btn" routerLink="/finance/accounts">Set up accounts</a>
              </div>
            }

            @if (r.notCounted.length > 0) {
              <section class="card working" aria-labelledby="not-counted-heading">
                <div class="card-header"><h2 id="not-counted-heading">Shown, not counted</h2></div>
                <ul class="not-counted">
                  @for (n of r.notCounted; track $index) {
                    <li>
                      <span class="num">{{ n.amount.amount | money: n.amount.currency }}</span>
                      <span class="small secondary">{{ n.reason }}@if (n.accountName) { · {{ n.accountName }} }</span>
                    </li>
                  }
                </ul>
              </section>
            }
          }
        }
      }
    </div>
  `,
  styles: [`
    .working { margin-bottom: var(--space-5); }
    .figure { font-weight: 600; font-variant-numeric: tabular-nums; }
    .data-table td:first-child { white-space: normal; }
    .subtotal td { font-weight: 500; border-top: 1px solid var(--border-subtle); }
    .total td { font-weight: 600; border-top: 2px solid var(--border-subtle); }
    .beside { padding: var(--space-3) var(--space-5); }
    .notice { margin: var(--space-3) var(--space-5); }
    .not-counted { list-style: none; display: grid; gap: var(--space-3); padding: 0 var(--space-5) var(--space-4); }
    .not-counted li { display: flex; flex-direction: column; gap: var(--space-1); }
  `]
})
export class UnrestrictedComponent {
  private api = inject(FinanceApiService);
  private destroyRef = inject(DestroyRef);

  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly error = signal('');
  readonly report = signal<UnrestrictedCashReport | null>(null);

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.api.getUnrestrictedCash().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: r => { this.report.set(r); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }
}
