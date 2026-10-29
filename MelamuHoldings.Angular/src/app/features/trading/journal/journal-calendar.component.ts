import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CalendarDay, CalendarMonth, monthGrid } from '../models/journal-view.model';
import { IconComponent } from '../../../shared/ui/icon.component';

const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

// A month of trading by day. Totals count closed and reviewed trades; a day with only drafts or open trades is
// marked differently from a day with no trades at all. Each day with trades opens the trade log for that date.
@Component({
  selector: 'app-journal-calendar',
  standalone: true,
  imports: [RouterLink, IconComponent],
  template: `
    @let m = month();
    <div class="cal-head">
      <button type="button" class="icon-btn" (click)="previous.emit()" aria-label="Previous month"><app-icon name="chevron-left" [size]="18" /></button>
      <h2 id="cal-title">{{ title() }}</h2>
      <button type="button" class="icon-btn" (click)="next.emit()" aria-label="Next month"><app-icon name="chevron-right" [size]="18" /></button>
      <span class="cal-total small">
        @if (m.completedTrades > 0) {
          {{ m.completedTrades }} completed {{ m.completedTrades === 1 ? 'trade' : 'trades' }} ·
          <span [class.text-positive]="m.netPnL > 0" [class.text-negative]="m.netPnL < 0">{{ signed(m.netPnL) }}</span>
        } @else { <span class="muted">No completed trades this month</span> }
      </span>
    </div>
    <table class="cal" aria-labelledby="cal-title">
      <thead><tr>@for (d of weekdays; track d) { <th scope="col">{{ d }}</th> }</tr></thead>
      <tbody>
        @for (week of weeks(); track $index) {
          <tr>
            @for (date of week; track $index) {
              <td>
                @if (date) {
                  @if (byDate().get(date); as day) {
                    <a class="day" [class.unfinished]="day.completedTrades === 0" [class.today]="date === today"
                      [class.up]="day.netPnL > 0" [class.down]="day.netPnL < 0"
                      routerLink="/trading/trades" [queryParams]="{ date }" [attr.aria-label]="describe(date, day)">
                      <span class="num-day">{{ dayOf(date) }}</span>
                      @if (day.completedTrades > 0) {
                        <span class="count">{{ day.completedTrades }} {{ day.completedTrades === 1 ? 'trade' : 'trades' }}</span>
                        <span class="pnl full">{{ signed(day.netPnL) }}</span><span class="pnl short" aria-hidden="true">{{ compact(day.netPnL) }}</span>
                      }
                      @if (day.drafts + day.open > 0) { <span class="pending">{{ unfinished(day) }}</span> }
                    </a>
                  } @else {
                    <span class="day empty" [class.today]="date === today"><span class="num-day">{{ dayOf(date) }}</span></span>
                  }
                }
              </td>
            }
          </tr>
        }
      </tbody>
    </table>
    <p class="legend small muted">Totals count closed and reviewed trades. A dashed day has only drafts or open trades.</p>
  `,
  styles: [`
    .cal-head { display: flex; align-items: center; gap: var(--space-2); flex-wrap: wrap; margin-bottom: var(--space-3); }
    .cal-head h2 { font-size: var(--text-md); min-width: 9.5em; text-align: center; }
    .cal-total { margin-left: auto; }
    .icon-btn { display: flex; padding: 6px; border: 1px solid var(--border); background: var(--surface); border-radius: var(--radius); cursor: pointer; color: var(--text-secondary); }
    .icon-btn:hover { color: var(--text-primary); background: var(--surface-sunken); }
    .cal { width: 100%; table-layout: fixed; border-collapse: separate; border-spacing: 4px; }
    th { font-size: var(--text-xs); font-weight: 500; color: var(--text-muted); text-align: left; padding: 0 4px; }
    td { padding: 0; vertical-align: top; }
    .day { display: flex; flex-direction: column; gap: 1px; min-height: 64px; padding: 4px 6px; border-radius: var(--radius); border: 1px solid var(--border-subtle); background: var(--surface); color: var(--text-primary); text-decoration: none; font-size: 12px; overflow: hidden; }
    a.day:hover { border-color: var(--navy-700); }
    .day.empty { background: transparent; color: var(--text-muted); }
    .day.unfinished { border-style: dashed; background: var(--surface-sunken); }
    .day.up { border-left: 3px solid var(--positive); }
    .day.down { border-left: 3px solid var(--negative); }
    .day.today .num-day { font-weight: 700; color: var(--navy-900); text-decoration: underline; }
    .num-day { font-size: 12px; color: var(--text-secondary); }
    .count { color: var(--text-secondary); }
    .pnl { font-weight: 600; }
    .pnl.short { display: none; }
    .up .pnl { color: var(--positive); }
    .down .pnl { color: var(--negative); }
    .pending { color: var(--text-muted); font-style: italic; }
    .legend { margin-top: var(--space-2); }
    @media (max-width: 599px) {
      .cal { border-spacing: 2px; }
      .day { min-height: 48px; padding: 2px 3px; font-size: 10.5px; }
      .count, .pnl.full { display: none; }
      .pnl.short { display: inline; }
    }
  `]
})
export class JournalCalendarComponent {
  readonly month = input.required<CalendarMonth>();
  readonly previous = output<void>();
  readonly next = output<void>();

  readonly weekdays = WEEKDAYS;
  readonly today = new Date().toLocaleDateString('en-CA');

  readonly weeks = computed(() => monthGrid(this.month().year, this.month().month));
  readonly byDate = computed(() => new Map(this.month().days.map(d => [d.date, d])));
  readonly title = computed(() =>
    new Date(Date.UTC(this.month().year, this.month().month - 1, 1)).toLocaleDateString('en-GB', { month: 'long', year: 'numeric', timeZone: 'UTC' }));

  dayOf(date: string): number {
    return Number(date.slice(8, 10));
  }

  unfinished(day: CalendarDay): string {
    const parts = [];
    if (day.drafts) parts.push(`${day.drafts} ${day.drafts === 1 ? 'draft' : 'drafts'}`);
    if (day.open) parts.push(`${day.open} open`);
    return parts.join(', ');
  }

  describe(date: string, day: CalendarDay): string {
    const label = new Date(date + 'T00:00:00Z').toLocaleDateString('en-GB', { day: 'numeric', month: 'long', timeZone: 'UTC' });
    const done = day.completedTrades > 0 ? `${day.completedTrades} completed, ${this.signed(day.netPnL)}` : 'no completed trades';
    const pending = day.drafts + day.open > 0 ? `; ${this.unfinished(day)}` : '';
    return `${label}: ${done}${pending}`;
  }

  // Whole units, or thousands with one decimal, so a phone-width day cell is never cut off
  compact(value: number): string {
    const abs = Math.abs(value);
    const text = abs >= 1000 ? `${(abs / 1000).toFixed(1)}k` : `${Math.round(abs)}`;
    return value > 0 ? `+${text}` : value < 0 ? `−${text}` : text;
  }

  signed(value: number): string {
    const formatted = Math.abs(value).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    return value > 0 ? `+${formatted}` : value < 0 ? `−${formatted}` : formatted;
  }
}
