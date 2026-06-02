import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { UploadResult } from '../../models/analytics.model';
import { NavigationService } from '../../services/navigation.service';

@Component({
  selector: 'app-upload',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="upload-page">
      <header class="page-header">
        <div>
          <h1>Upload Journal</h1>
          <p class="text-muted mono">Import trades from a CSV file</p>
        </div>
      </header>

      <!-- Drop Zone -->
      <div class="card drop-zone"
        [class.dragging]="isDragging"
        [class.uploading]="isUploading"
        (dragover)="onDragOver($event)"
        (dragleave)="onDragLeave()"
        (drop)="onDrop($event)"
        (click)="fileInput.click()">

        <input #fileInput type="file" accept=".csv" style="display:none" (change)="onFileSelected($event)" />

        <div class="drop-icon">{{ isUploading ? '⟳' : '⊕' }}</div>
        <div class="drop-title">
          {{ isUploading ? 'Processing...' : 'Drop your CSV here' }}
        </div>
        <div class="drop-sub mono">
          {{ isUploading ? 'Parsing and storing trades' : 'or click to browse · .csv files only' }}
        </div>
      </div>

      <!-- Upload Result -->
      <div class="card result-card" *ngIf="lastResult">
        <div class="result-header">
          <span class="result-title">Upload Complete</span>
          <span class="tag" [class.tag-green]="lastResult.status === 'Completed'" [class.tag-red]="lastResult.status === 'Failed'">
            {{ lastResult.status }}
          </span>
        </div>
        <div class="result-stats">
          <div class="stat">
            <span class="stat-value mono">{{ lastResult.totalRows }}</span>
            <span class="stat-label">Total Rows</span>
          </div>
          <div class="stat">
            <span class="stat-value mono text-green">{{ lastResult.parsedRows }}</span>
            <span class="stat-label">Imported</span>
          </div>
          <div class="stat">
            <span class="stat-value mono text-red">{{ lastResult.failedRows }}</span>
            <span class="stat-label">Failed</span>
          </div>
        </div>
        <div class="error-list" *ngIf="lastResult && lastResult.errors && lastResult.errors.length > 0">
          <p class="error-heading">Parse errors:</p>
          <p class="error-item mono" *ngFor="let err of lastResult.errors">{{ err }}</p>
        </div>
      </div>

      <!-- CSV Format Guide -->
      <div class="card format-card">
        <div class="format-title">Expected CSV Format</div>
        <p class="text-muted" style="margin-bottom: 1rem; font-size: 13px;">
          Your CSV must include a header row with these column names:
        </p>
        <div class="columns-grid">
          <span class="col-tag mono" *ngFor="let col of expectedColumns">{{ col }}</span>
        </div>
      </div>

      <!-- Upload History -->
      <div class="card history-card">
        <div class="history-title">Upload History</div>
        <div class="table-wrap">
          <table *ngIf="logs.length > 0">
            <thead>
              <tr>
                <th>File</th>
                <th>Total</th>
                <th>Imported</th>
                <th>Failed</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let log of logs">
                <td class="mono">{{ log.fileName }}</td>
                <td class="mono">{{ log.totalRows }}</td>
                <td class="mono text-green">{{ log.parsedRows }}</td>
                <td class="mono text-red">{{ log.failedRows }}</td>
                <td>
                  <span class="tag"
                    [class.tag-green]="log.status === 'Completed'"
                    [class.tag-red]="log.status === 'Failed'"
                    [class.tag-amber]="log.status === 'Pending'">
                    {{ log.status }}
                  </span>
                </td>
              </tr>
            </tbody>
          </table>
          <p class="empty-state mono" *ngIf="logs.length === 0">No uploads yet.</p>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .upload-page { max-width: 900px; }

    .page-header {
      margin-bottom: 1.5rem;
      h1 { font-family: var(--font-display); font-size: 1.75rem; font-weight: 800; }
    }

    .drop-zone {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 3rem;
      border: 2px dashed var(--border);
      border-radius: var(--radius-lg);
      cursor: pointer;
      transition: all 0.2s ease;
      margin-bottom: 1rem;
      background: var(--bg-card);
      text-align: center;

      &:hover, &.dragging {
        border-color: var(--accent-amber);
        background: rgba(245, 158, 11, 0.04);
        box-shadow: var(--glow-amber);
      }

      &.uploading {
        border-color: var(--accent-blue);
        pointer-events: none;
      }
    }

    .drop-icon {
      font-size: 2.5rem;
      color: var(--accent-amber);
      margin-bottom: 0.75rem;
      line-height: 1;
    }

    .drop-title {
      font-size: 1.1rem;
      font-weight: 700;
      color: var(--text-primary);
      margin-bottom: 0.35rem;
    }

    .drop-sub {
      font-size: 12px;
      color: var(--text-muted);
    }

    .result-card { margin-bottom: 1rem; }

    .result-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 1.25rem;
    }

    .result-title { font-weight: 700; font-size: 15px; }

    .result-stats {
      display: flex;
      gap: 2rem;
      margin-bottom: 1rem;
    }

    .stat {
      display: flex;
      flex-direction: column;
      gap: 0.2rem;
    }

    .stat-value { font-size: 1.5rem; font-weight: 500; }
    .stat-label { font-size: 11px; color: var(--text-secondary); text-transform: uppercase; letter-spacing: 0.06em; }

    .error-heading { font-size: 12px; color: var(--accent-red); margin-bottom: 0.5rem; font-weight: 600; }
    .error-item { font-size: 12px; color: var(--text-secondary); margin-bottom: 0.25rem; }

    .format-card { margin-bottom: 1rem; }
    .format-title { font-weight: 700; font-size: 15px; margin-bottom: 0.75rem; }

    .columns-grid {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }

    .col-tag {
      background: var(--bg-secondary);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      padding: 3px 10px;
      font-size: 12px;
      color: var(--accent-amber);
    }

    .history-title { font-weight: 700; font-size: 15px; margin-bottom: 1rem; }

    .table-wrap { overflow-x: auto; }

    table { width: 100%; border-collapse: collapse; font-size: 13px; }
    th {
      padding: 0.6rem 0.75rem;
      text-align: left;
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--text-secondary);
      border-bottom: 1px solid var(--border);
    }
    td {
      padding: 0.65rem 0.75rem;
      border-bottom: 1px solid var(--border-subtle);
      color: var(--text-data);
    }
    tr:last-child td { border-bottom: none; }

    .empty-state { color: var(--text-muted); font-size: 13px; padding: 1rem 0; }
  `]
})
export class UploadComponent implements OnInit {
  isDragging = false;
  isUploading = false;
  lastResult: UploadResult | null = null;
  logs: UploadResult[] = [];

  expectedColumns = [
    'TradeDate', 'EntryTime', 'Instrument', 'Direction',
    'EntryPrice', 'ExitPrice', 'RiskAmount', 'PnL', 'RR',
    'Strategy', 'Session', 'DayOfWeek', 'TradeDuration',
    'Notes', 'IsManualOverride', 'DeviationNotes'
  ];

  constructor(private api: ApiService, private nav: NavigationService) {}

  ngOnInit(): void {
    this.nav.onNavigateTo('upload', () => this.loadLogs());
  }
  loadLogs(): void {
    this.api.getUploadLogs().subscribe(logs => this.logs = logs);
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.isDragging = true;
  }

  onDragLeave(): void {
    this.isDragging = false;
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDragging = false;
    const file = event.dataTransfer?.files[0];
    if (file) this.upload(file);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.upload(file);
  }

  upload(file: File): void {
    if (!file.name.endsWith('.csv')) {
      alert('Please select a .csv file.');
      return;
    }
    this.isUploading = true;
    this.api.uploadCsv(file).subscribe({
      next: result => {
        this.lastResult = result;
        this.isUploading = false;
        this.loadLogs();
      },
      error: () => {
        this.isUploading = false;
        alert('Upload failed. Check the API is running.');
      }
    });
  }
}