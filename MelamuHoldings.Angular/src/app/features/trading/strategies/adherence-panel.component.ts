import { Component, computed, input } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { AdherenceGroup, AdherenceReport } from '../models/strategy.model';

// Rule adherence with its denominator always in view, and the results of trades that followed the mandatory
// rules next to those that did not. Groups under the minimum sample are labelled; nothing claims a cause.
@Component({
  selector: 'app-adherence-panel',
  standalone: true,
  imports: [DecimalPipe],
  template: `
    @let r = report();
    <section class="metric-strip" aria-label="Rule adherence">
      <div class="metric">
        <span class="metric-label">Rule adherence</span>
        <span class="metric-value">{{ r.adherenceRate != null ? (r.adherenceRate | number: '1.0-1') + '%' : '—' }}</span>
        <span class="metric-note">
          @if (r.withChecklist > 0) { {{ r.compliant }} of {{ r.withChecklist }} trades with a complete checklist followed every mandatory rule }
          @else { No closed trade has a complete checklist yet }
        </span>
      </div>
      <div class="metric">
        <span class="metric-label">Not scored</span>
        <span class="metric-value">{{ r.withoutChecklist + r.incompleteChecklist }}</span>
        <span class="metric-note">{{ r.withoutChecklist }} without a checklist · {{ r.incompleteChecklist }} unfinished, of {{ r.completedTrades }} closed trades</span>
      </div>
    </section>

    @if (r.withChecklist > 0) {
      <div class="card table-card compare">
        <table class="data-table">
          <caption class="visually-hidden">Results of trades that followed the mandatory rules and trades that broke one</caption>
          <thead><tr><th>Trades</th><th class="num">Count</th><th class="num">Win rate</th><th class="num">Net P&amp;L</th><th class="num">Average R</th><th></th></tr></thead>
          <tbody>
            @for (row of rows(); track row.label) {
              <tr>
                <th scope="row">{{ row.label }}</th>
                <td class="num">{{ row.group.trades }}</td>
                <td class="num">{{ row.group.winRate != null ? (row.group.winRate | number: '1.0-1') + '%' : '—' }}</td>
                <td class="num" [class.text-positive]="row.group.netPnL > 0" [class.text-negative]="row.group.netPnL < 0">{{ signed(row.group.netPnL) }}</td>
                <td class="num">{{ row.group.averageR != null ? formatR(row.group.averageR) : '—' }}</td>
                <td>@if (row.group.insufficientData) { <span class="tag tag-warning">Insufficient data</span> }</td>
              </tr>
            }
          </tbody>
        </table>
        <p class="small muted note">
          Fewer than {{ r.minimumSample }} trades in a group is labelled insufficient data. This compares what happened;
          it does not show that following the rules caused the difference.
        </p>
      </div>
    }

    @if (r.mostViolated.length > 0) {
      <div class="card violations">
        <h3>Mandatory rules most often not met</h3>
        <ul>
          @for (v of r.mostViolated; track v.ruleId) {
            <li>
              <span>{{ v.text }} @if (v.category && v.category !== 'General') { <span class="tag">{{ v.category }}</span> } @if (showStrategy()) { <span class="muted">· {{ v.strategyName }}</span> }</span>
              <span class="small secondary nowrap">not met {{ v.timesNotMet }} of {{ v.timesAnswered }}</span>
            </li>
          }
        </ul>
      </div>
    }
  `,
  styles: [`
    .compare, .violations { margin-top: var(--space-4); }
    .note { padding: var(--space-3) var(--space-5) var(--space-4); }
    .violations h3 { font-size: var(--text-md); margin-bottom: var(--space-3); }
    .violations ul { list-style: none; display: grid; gap: var(--space-2); }
    .violations li { display: flex; justify-content: space-between; gap: var(--space-4); align-items: baseline; }
  `]
})
export class AdherencePanelComponent {
  readonly report = input.required<AdherenceReport>();
  readonly showStrategy = input(true);

  readonly rows = computed<{ label: string; group: AdherenceGroup }[]>(() => [
    { label: 'Followed every mandatory rule', group: this.report().compliantResults },
    { label: 'Broke a mandatory rule', group: this.report().nonCompliantResults }
  ]);

  // R with a true minus sign, like the P&L beside it
  formatR(value: number): string {
    const formatted = Math.abs(value).toFixed(2) + 'R';
    return value < 0 ? `−${formatted}` : formatted;
  }

  signed(value: number): string {
    const formatted = Math.abs(value).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    return value > 0 ? `+${formatted}` : value < 0 ? `−${formatted}` : formatted;
  }
}
