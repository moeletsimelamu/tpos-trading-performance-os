import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { TradingApiService } from '../trading-api.service';
import { OUTCOME_LABELS, PropAccountReport, STATUS_TAGS } from '../models/prop-account.model';
import { MoneyPipe } from '../../../shared/money.pipe';
import { describeHttpError } from '../../../shared/http-error';

// Which accounts cost money without paying anything back. Every attempt is listed, closed ones included, per currency
// and unconverted. The figures are the owner's: a signed-in user without the role does not see this section.
@Component({
  selector: 'app-prop-account-report',
  standalone: true,
  imports: [RouterLink, MoneyPipe],
  template: `
    @if (!hidden()) {
      <section class="card table-card report" aria-label="Fees against payouts">
        <h2>Fees against payouts</h2>
        @if (error()) {
          <p class="notice notice-error pad" role="alert">{{ error() }}</p>
        } @else if (report(); as r) {
          <p class="small secondary pad">Linked expenses against payouts actually received, including closed accounts. Each currency stands alone: nothing is converted.</p>
          @if (r.accounts.length === 0) {
            <p class="empty-state small">No prop firm accounts recorded.</p>
          } @else {
            <table class="data-table">
              <thead><tr><th>Account</th><th>Outcome</th><th class="num">Fees paid</th><th class="num">Payouts received</th><th class="num">Net</th></tr></thead>
              <tbody>
                @for (a of r.accounts; track a.accountId) {
                  <tr>
                    <td>
                      <a class="name" [routerLink]="['/trading/prop-accounts', a.accountId]">{{ a.nickname }}</a>
                      <div class="small muted">{{ a.firm }} · {{ a.phase }} · <span [class]="statusTags[a.status]">{{ a.status }}</span></div>
                    </td>
                    <td>
                      <span [class]="outcomes[a.economics.outcome].tag">{{ outcomes[a.economics.outcome].label }}</span>
                      @for (m of a.economics.awaiting; track m.currency) { <div class="small muted">Awaiting {{ m.amount | money: m.currency }}, not counted</div> }
                    </td>
                    <td class="num">@for (l of a.economics.lines; track l.currency) { <div>{{ l.fees | money: l.currency }}</div> }</td>
                    <td class="num">@for (l of a.economics.lines; track l.currency) { <div>{{ l.payoutsReceived | money: l.currency }}</div> }</td>
                    <td class="num">@for (l of a.economics.lines; track l.currency) { <div [class.text-negative]="l.net < 0">{{ l.net | money: l.currency : true }}</div> }</td>
                  </tr>
                }
              </tbody>
              @if (r.totals.length > 0) {
                <tfoot>
                  @for (t of r.totals; track t.currency) {
                    <tr>
                      <th scope="row" colspan="2">All accounts · {{ t.currency }}</th>
                      <td class="num">{{ t.fees | money: t.currency }}</td>
                      <td class="num">{{ t.payoutsReceived | money: t.currency }}</td>
                      <td class="num" [class.text-negative]="t.net < 0">{{ t.net | money: t.currency : true }}</td>
                    </tr>
                  }
                </tfoot>
              }
            </table>
          }
          @if (r.unlinkedReceivedPayouts > 0) {
            <p class="small secondary pad unlinked">
              {{ r.unlinkedReceivedPayouts }} received {{ r.unlinkedReceivedPayouts === 1 ? 'payout is' : 'payouts are' }} not linked to an account, so not counted here.
              <a routerLink="/finance/payouts">View payouts</a>
            </p>
          }
        }
      </section>
    }
  `,
  styles: [`
    .report { margin-top: var(--space-5); }
    h2 { font-size: var(--text-base); padding: var(--space-4) var(--space-5) 0; }
    .pad { padding: var(--space-2) var(--space-5) var(--space-3); margin: 0; }
    .unlinked { padding-top: var(--space-3); }
    .name { font-weight: 500; }
    .empty-state.small { padding: var(--space-6) var(--space-5); }
    tfoot th { text-align: left; font-weight: 600; }
    tfoot td { font-weight: 600; }
  `]
})
export class PropAccountReportComponent {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);

  readonly outcomes = OUTCOME_LABELS;
  readonly statusTags = STATUS_TAGS;
  readonly hidden = signal(false);
  readonly error = signal('');
  readonly report = signal<PropAccountReport | null>(null);

  constructor() {
    this.api.getPropAccountReport().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: r => this.report.set(r),
      error: (err: HttpErrorResponse) => {
        if (err.status === 403) this.hidden.set(true);
        else this.error.set(`Could not load fees against payouts: ${describeHttpError(err)}`);
      }
    });
  }
}
