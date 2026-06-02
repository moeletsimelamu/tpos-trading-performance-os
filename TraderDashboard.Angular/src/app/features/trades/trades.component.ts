import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { Trade } from '../../models/trade.model';
import { NavigationService } from '../../services/navigation.service';

@Component({
  selector: 'app-trades',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="trades-page">
      <header class="page-header">
        <div>
          <h1>Trades</h1>
          <p class="text-muted mono">{{ filtered.length }} of {{ trades.length }} trades</p>
        </div>
      </header>

      <!-- Filters -->
      <div class="card filters-bar">
        <input
          class="filter-input"
          type="text"
          placeholder="Search instrument, strategy, session..."
          [(ngModel)]="searchTerm"
          (ngModelChange)="applyFilters()"
        />
        <select class="filter-select" [(ngModel)]="directionFilter" (ngModelChange)="applyFilters()">
          <option value="">All Directions</option>
          <option value="Long">Long</option>
          <option value="Short">Short</option>
        </select>
        <select class="filter-select" [(ngModel)]="resultFilter" (ngModelChange)="applyFilters()">
          <option value="">All Results</option>
          <option value="win">Wins</option>
          <option value="loss">Losses</option>
          <option value="breakeven">Breakeven</option>
        </select>
        <button class="btn-clear" (click)="clearFilters()">Clear</button>
      </div>

      <!-- Table -->
      <div class="card table-card">
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Date</th>
                <th>Instrument</th>
                <th>Direction</th>
                <th>Entry</th>
                <th>Exit</th>
                <th>Risk</th>
                <th>P&L</th>
                <th>RR</th>
                <th>Strategy</th>
                <th>Session</th>
                <th>Duration</th>
                <th>Override</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let trade of filtered" [class.row-win]="trade.pnL > 0" [class.row-loss]="trade.pnL < 0">
                <td class="mono">{{ trade.tradeDate }}</td>
                <td class="bold">{{ trade.instrument }}</td>
                <td>
                  <span class="tag" [class.tag-green]="trade.direction === 'Long'" [class.tag-red]="trade.direction === 'Short'">
                    {{ trade.direction }}
                  </span>
                </td>
                <td class="mono">{{ trade.entryPrice | number:'1.2-5' }}</td>
                <td class="mono">{{ trade.exitPrice | number:'1.2-5' }}</td>
                <td class="mono">{{ trade.riskAmount | number:'1.2-2' }}</td>
                <td class="mono" [class.text-green]="trade.pnL > 0" [class.text-red]="trade.pnL < 0">
                  {{ trade.pnL >= 0 ? '+' : '' }}{{ trade.pnL | number:'1.2-2' }}
                </td>
                <td class="mono" [class.text-green]="trade.rR >= 1" [class.text-red]="trade.rR < 0">
                  {{ trade.rR | number:'1.2-2' }}R
                </td>
                <td>
                  <span class="tag tag-blue" *ngIf="trade.strategyName">{{ trade.strategyName }}</span>
                  <span class="text-muted" *ngIf="!trade.strategyName">—</span>
                </td>
                <td>{{ trade.session }}</td>
                <td class="mono">{{ trade.tradeDuration }}m</td>
                <td>
                  <span class="tag tag-amber" *ngIf="trade.isManualOverride">Yes</span>
                  <span class="text-muted" *ngIf="!trade.isManualOverride">—</span>
                </td>
                <td>
                  <button class="btn-delete" (click)="deleteTrade(trade.id)">✕</button>
                </td>
              </tr>
              <tr *ngIf="filtered.length === 0">
                <td colspan="13" class="empty-state">No trades found.</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .trades-page { max-width: 1400px; }

    .page-header {
      margin-bottom: 1.5rem;
      h1 {
        font-family: var(--font-display);
        font-size: 1.75rem;
        font-weight: 800;
      }
    }

    .filters-bar {
      display: flex;
      gap: 0.75rem;
      align-items: center;
      margin-bottom: 1rem;
      flex-wrap: wrap;
    }

    .filter-input {
      flex: 1;
      min-width: 200px;
      background: var(--bg-secondary);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      padding: 0.5rem 0.75rem;
      color: var(--text-primary);
      font-family: var(--font-mono);
      font-size: 13px;
      outline: none;
      transition: border-color 0.15s;
      &:focus { border-color: var(--accent-amber); }
      &::placeholder { color: var(--text-muted); }
    }

    .filter-select {
      background: var(--bg-secondary);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      padding: 0.5rem 0.75rem;
      color: var(--text-primary);
      font-family: var(--font-mono);
      font-size: 13px;
      outline: none;
      cursor: pointer;
      &:focus { border-color: var(--accent-amber); }
    }

    .btn-clear {
      background: transparent;
      border: 1px solid var(--border);
      border-radius: var(--radius);
      padding: 0.5rem 1rem;
      color: var(--text-secondary);
      font-size: 13px;
      cursor: pointer;
      transition: all 0.15s;
      &:hover { border-color: var(--accent-red); color: var(--accent-red); }
    }

    .table-card { padding: 0; overflow: hidden; }

    .table-wrap { overflow-x: auto; }

    table {
      width: 100%;
      border-collapse: collapse;
      font-size: 13px;
    }

    thead tr {
      border-bottom: 1px solid var(--border);
    }

    th {
      padding: 0.75rem 1rem;
      text-align: left;
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--text-secondary);
      white-space: nowrap;
    }

    td {
      padding: 0.7rem 1rem;
      border-bottom: 1px solid var(--border-subtle);
      color: var(--text-data);
      white-space: nowrap;
    }

    tr:last-child td { border-bottom: none; }

    tbody tr {
      transition: background 0.1s;
      &:hover { background: var(--bg-card-hover); }
    }

    .row-win td:first-child { border-left: 2px solid var(--accent-green); }
    .row-loss td:first-child { border-left: 2px solid var(--accent-red); }

    .bold { font-weight: 600; color: var(--text-primary); }

    .btn-delete {
      background: transparent;
      border: 1px solid transparent;
      border-radius: 4px;
      color: var(--text-muted);
      cursor: pointer;
      padding: 2px 6px;
      font-size: 12px;
      transition: all 0.15s;
      &:hover { border-color: var(--accent-red); color: var(--accent-red); }
    }

    .empty-state {
      text-align: center;
      color: var(--text-muted);
      padding: 3rem;
      font-family: var(--font-mono);
    }
  `]
})
export class TradesComponent implements OnInit {
  trades: Trade[] = [];
  filtered: Trade[] = [];
  searchTerm = '';
  directionFilter = '';
  resultFilter = '';

  constructor(private api: ApiService, private nav: NavigationService) {}

  ngOnInit(): void {
    this.nav.onNavigateTo('trades', () => this.loadData());
  }

  loadData(): void {
    this.api.getTrades().subscribe(trades => {
      this.trades = trades;
      this.filtered = trades;
    });
  }

  applyFilters(): void {
    this.filtered = this.trades.filter(trade => {
      const term = this.searchTerm.toLowerCase();
      const matchesSearch = !term ||
        trade.instrument.toLowerCase().includes(term) ||
        trade.strategyName?.toLowerCase().includes(term) ||
        trade.session.toLowerCase().includes(term) ||
        trade.dayOfWeek.toLowerCase().includes(term);

      const matchesDirection = !this.directionFilter ||
        trade.direction === this.directionFilter;

      const matchesResult = !this.resultFilter ||
        (this.resultFilter === 'win' && trade.pnL > 0) ||
        (this.resultFilter === 'loss' && trade.pnL < 0) ||
        (this.resultFilter === 'breakeven' && trade.pnL === 0);

      return matchesSearch && matchesDirection && matchesResult;
    });
  }

  clearFilters(): void {
    this.searchTerm = '';
    this.directionFilter = '';
    this.resultFilter = '';
    this.filtered = [...this.trades];
  }

  deleteTrade(id: number): void {
    if (!confirm('Delete this trade?')) return;
    this.api.deleteTrade(id).subscribe(() => {
      this.trades = this.trades.filter(t => t.id !== id);
      this.applyFilters();
    });
  }
}