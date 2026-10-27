import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TradingApiService } from '../trading-api.service';
import { UploadResult } from '../models/analytics.model';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';
import { IconComponent } from '../../../shared/ui/icon.component';

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-trading-import',
  standalone: true,
  imports: [LoadErrorComponent, PageHeaderComponent, IconComponent],
  template: `
    <div class="page narrow">
      <app-page-header section="Trading operations" title="Import trades"
        description="Bring trade history in from a CSV export. Valid rows are kept; rows that fail are listed with their row number." />

      <div class="drop-zone"
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

        @if (isUploading()) {
          <div class="spinner" aria-hidden="true"></div>
          <div class="drop-title">Importing…</div>
          <div class="drop-sub">Reading rows and saving trades</div>
        } @else {
          <app-icon name="import" [size]="28" />
          <div class="drop-title">Drop a CSV file here, or choose one</div>
          <div class="drop-sub">.csv only, up to 10 MB</div>
        }
      </div>

      @if (uploadError()) {
        <p class="notice notice-error" role="alert">{{ uploadError() }}</p>
      }

      @if (lastResult(); as result) {
        <section class="card result" aria-labelledby="result-heading">
          <div class="result-header">
            <h2 id="result-heading">{{ result.status === 'Failed' ? 'Import failed' : 'Import complete' }}</h2>
            <span class="tag" [class.tag-positive]="result.status === 'Completed'" [class.tag-negative]="result.status === 'Failed'">{{ result.status }}</span>
          </div>
          <dl class="result-stats">
            <div><dt>Rows read</dt><dd>{{ result.totalRows }}</dd></div>
            <div><dt>Imported</dt><dd>{{ result.parsedRows }}</dd></div>
            <div><dt>Failed</dt><dd [class.text-negative]="result.failedRows > 0">{{ result.failedRows }}</dd></div>
          </dl>
          @if (result.errors.length > 0) {
            <div class="error-list">
              <h3>Rows that were not imported</h3>
              <ul>
                @for (err of result.errors; track $index) { <li>{{ err }}</li> }
              </ul>
            </div>
          }
        </section>
      }

      <section class="card format" aria-labelledby="format-heading">
        <h2 id="format-heading">Expected columns</h2>
        <p class="secondary small">The file needs a header row with these names. <code>Direction</code> is Long or Short; prices must be above zero.</p>
        <div class="columns">
          @for (col of expectedColumns; track col) { <code>{{ col }}</code> }
        </div>
      </section>

      <section class="card table-card history" aria-labelledby="history-heading">
        <div class="card-header"><h2 id="history-heading">Import history</h2></div>

        @switch (logsState()) {
          @case ('loading') {
            <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading history...</span></div>
          }
          @case ('error') {
            <app-load-error title="Could not load upload history" [message]="logsError()" (retry)="loadLogs()" />
          }
          @case ('ready') {
            @if (logs().length > 0) {
              <table class="data-table">
                <thead>
                  <tr><th>File</th><th class="num">Rows</th><th class="num">Imported</th><th class="num">Failed</th><th>Status</th></tr>
                </thead>
                <tbody>
                  @for (log of logs(); track log.uploadLogId) {
                    <tr>
                      <td>{{ log.fileName }}</td>
                      <td class="num">{{ log.totalRows }}</td>
                      <td class="num">{{ log.parsedRows }}</td>
                      <td class="num">{{ log.failedRows }}</td>
                      <td>
                        <span class="tag" [class.tag-positive]="log.status === 'Completed'" [class.tag-negative]="log.status === 'Failed'"
                          [class.tag-warning]="log.status === 'Pending'">{{ log.status }}</span>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            } @else {
              <p class="empty-state small">No imports yet.</p>
            }
          }
        }
      </section>
    </div>
  `,
  styles: [`
    .narrow { max-width: 900px; }
    .drop-zone {
      display: flex; flex-direction: column; align-items: center; gap: var(--space-2);
      padding: var(--space-10) var(--space-6); text-align: center; cursor: pointer;
      background: var(--surface); border: 1.5px dashed var(--border); border-radius: var(--radius-lg);
      color: var(--text-secondary); transition: border-color var(--transition), background var(--transition);
      &:hover, &.dragging, &:focus-visible { border-color: var(--navy-700); background: var(--surface-sunken); }
      &.uploading { pointer-events: none; }
      app-icon { color: var(--navy-700); }
    }
    .spinner { width: 24px; height: 24px; border-radius: 50%; border: 2px solid var(--border); border-top-color: var(--navy-900); animation: spin 0.7s linear infinite; }
    .drop-title { font-size: var(--text-md); font-weight: 600; color: var(--text-primary); }
    .drop-sub { font-size: var(--text-sm); color: var(--text-muted); }
    .card { margin-top: var(--space-5); }
    h2 { font-size: var(--text-md); }
    .result-header { display: flex; align-items: center; justify-content: space-between; }
    .result-stats {
      display: flex; gap: var(--space-8); margin: var(--space-4) 0;
      dt { font-size: var(--text-sm); color: var(--text-secondary); }
      dd { font-size: var(--text-lg); font-weight: 600; }
    }
    .error-list {
      border-top: 1px solid var(--border-subtle); padding-top: var(--space-3);
      h3 { font-size: var(--text-sm); margin-bottom: var(--space-2); }
      ul { list-style: none; font-size: var(--text-sm); color: var(--text-secondary); display: grid; gap: 2px; }
    }
    .format p { margin: var(--space-1) 0 var(--space-3); }
    .columns { display: flex; flex-wrap: wrap; gap: 6px; }
    code { font-family: var(--font-mono); font-size: 12px; background: var(--neutral-bg); border-radius: var(--radius-sm); padding: 2px 6px; }
    .empty-state.small { padding: var(--space-6); }
  `]
})
export class ImportComponent {
  private api = inject(TradingApiService);
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
