import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ApiService } from '../../services/api.service';
import { UploadResult } from '../../models/analytics.model';
import { LoadErrorComponent } from '../../shared/load-error.component';
import { describeHttpError } from '../../shared/http-error';

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-upload',
  standalone: true,
  imports: [LoadErrorComponent],
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
        role="button"
        tabindex="0"
        aria-label="Choose a CSV file to import"
        [class.dragging]="isDragging()"
        [class.uploading]="isUploading()"
        (dragover)="onDragOver($event)"
        (dragleave)="isDragging.set(false)"
        (drop)="onDrop($event)"
        (click)="fileInput.click()"
        (keydown.enter)="fileInput.click()">

        <input #fileInput type="file" accept=".csv" hidden (change)="onFileSelected($event)" />

        <div class="drop-icon">{{ isUploading() ? '⟳' : '⊕' }}</div>
        <div class="drop-title">
          {{ isUploading() ? 'Processing...' : 'Drop your CSV here' }}
        </div>
        <div class="drop-sub mono">
          {{ isUploading() ? 'Parsing and storing trades' : 'or click to browse · .csv files only' }}
        </div>
      </div>

      @if (uploadError()) {
        <p class="upload-error mono" role="alert">{{ uploadError() }}</p>
      }

      <!-- Upload Result -->
      @if (lastResult(); as result) {
        <div class="card result-card">
          <div class="result-header">
            <span class="result-title">Upload {{ result.status === 'Failed' ? 'Failed' : 'Complete' }}</span>
            <span class="tag" [class.tag-green]="result.status === 'Completed'" [class.tag-red]="result.status === 'Failed'">
              {{ result.status }}
            </span>
          </div>
          <div class="result-stats">
            <div class="stat">
              <span class="stat-value mono">{{ result.totalRows }}</span>
              <span class="stat-label">Total Rows</span>
            </div>
            <div class="stat">
              <span class="stat-value mono text-green">{{ result.parsedRows }}</span>
              <span class="stat-label">Imported</span>
            </div>
            <div class="stat">
              <span class="stat-value mono text-red">{{ result.failedRows }}</span>
              <span class="stat-label">Failed</span>
            </div>
          </div>
          @if (result.errors.length > 0) {
            <div class="error-list">
              <p class="error-heading">Parse errors:</p>
              @for (err of result.errors; track $index) {
                <p class="error-item mono">{{ err }}</p>
              }
            </div>
          }
        </div>
      }

      <!-- CSV Format Guide -->
      <div class="card format-card">
        <div class="format-title">Expected CSV Format</div>
        <p class="text-muted format-intro">
          Your CSV must include a header row with these column names:
        </p>
        <div class="columns-grid">
          @for (col of expectedColumns; track col) {
            <span class="col-tag mono">{{ col }}</span>
          }
        </div>
      </div>

      <!-- Upload History -->
      <div class="card history-card">
        <div class="history-title">Upload History</div>

        @switch (logsState()) {
          @case ('loading') {
            <div class="loading-state">
              <div class="spinner"></div>
              <span class="loading-text">Loading history...</span>
            </div>
          }
          @case ('error') {
            <app-load-error title="Could not load upload history" [message]="logsError()" (retry)="loadLogs()" />
          }
          @case ('ready') {
            <div class="table-wrap">
              @if (logs().length > 0) {
                <table>
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
                    @for (log of logs(); track log.uploadLogId) {
                      <tr>
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
                    }
                  </tbody>
                </table>
              } @else {
                <p class="empty-state mono">No uploads yet.</p>
              }
            </div>
          }
        }
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

      &:hover, &.dragging, &:focus-visible {
        border-color: var(--accent-amber);
        background: rgba(245, 158, 11, 0.04);
        outline: none;
      }

      &.uploading { border-color: var(--accent-blue); pointer-events: none; }
    }

    .drop-icon { font-size: 2.5rem; color: var(--accent-amber); margin-bottom: 0.75rem; line-height: 1; }
    .drop-title { font-size: 1.1rem; font-weight: 700; color: var(--text-primary); margin-bottom: 0.35rem; }
    .drop-sub { font-size: 12px; color: var(--text-muted); }

    .upload-error { color: var(--accent-red); font-size: 12px; margin-bottom: 1rem; }

    .result-card { margin-bottom: 1rem; }
    .result-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 1.25rem; }
    .result-title { font-weight: 700; font-size: 15px; }
    .result-stats { display: flex; gap: 2rem; margin-bottom: 1rem; }
    .stat { display: flex; flex-direction: column; gap: 0.2rem; }
    .stat-value { font-size: 1.5rem; font-weight: 500; }
    .stat-label { font-size: 11px; color: var(--text-secondary); text-transform: uppercase; letter-spacing: 0.06em; }

    .error-heading { font-size: 12px; color: var(--accent-red); margin-bottom: 0.5rem; font-weight: 600; }
    .error-item { font-size: 12px; color: var(--text-secondary); margin-bottom: 0.25rem; }

    .format-card { margin-bottom: 1rem; }
    .format-title { font-weight: 700; font-size: 15px; margin-bottom: 0.75rem; }
    .format-intro { margin-bottom: 1rem; font-size: 13px; }
    .columns-grid { display: flex; flex-wrap: wrap; gap: 0.5rem; }
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
export class UploadComponent {
  private api = inject(ApiService);
  private destroyRef = inject(DestroyRef);

  readonly isDragging = signal(false);
  readonly isUploading = signal(false);
  readonly uploadError = signal('');
  readonly lastResult = signal<UploadResult | null>(null);
  readonly logsState = signal<LoadState>('loading');
  readonly logsError = signal('');
  readonly logs = signal<UploadResult[]>([]);

  readonly expectedColumns = [
    'TradeDate', 'EntryTime', 'Instrument', 'Direction',
    'EntryPrice', 'ExitPrice', 'RiskAmount', 'PnL', 'RR',
    'Strategy', 'Session', 'DayOfWeek', 'TradeDuration',
    'Notes', 'IsManualOverride', 'DeviationNotes'
  ];

  constructor() {
    this.loadLogs();
  }

  loadLogs(): void {
    this.logsState.set('loading');
    this.api.getUploadLogs()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: logs => {
          this.logs.set(logs);
          this.logsState.set('ready');
        },
        error: err => {
          this.logsError.set(describeHttpError(err));
          this.logsState.set('error');
        }
      });
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.isDragging.set(true);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDragging.set(false);
    const file = event.dataTransfer?.files[0];
    if (file) this.upload(file);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';  // allow choosing the same file again
    if (file) this.upload(file);
  }

  upload(file: File): void {
    if (this.isUploading()) return;
    if (!file.name.toLowerCase().endsWith('.csv')) {
      this.uploadError.set('Please choose a .csv file.');
      return;
    }

    this.uploadError.set('');
    this.isUploading.set(true);
    this.api.uploadCsv(file)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.lastResult.set(result);
          this.isUploading.set(false);
          this.loadLogs();
        },
        error: err => {
          this.uploadError.set(`Upload failed: ${describeHttpError(err)}`);
          this.isUploading.set(false);
        }
      });
  }
}
