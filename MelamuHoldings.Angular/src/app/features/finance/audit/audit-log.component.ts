import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FinanceApiService } from '../finance-api.service';
import { AuditEvent, Paged } from '../finance.models';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-finance-audit-log',
  standalone: true,
  imports: [DatePipe, LoadErrorComponent, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header section="Finance" title="Audit log"
        description="Every change to accounts, funds and the ledger, newest first. Entries cannot be edited or removed."></app-page-header>

      @switch (state()) {
        @case ('loading') {
          <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading audit log...</span></div>
        }
        @case ('error') {
          <app-load-error title="Could not load the audit log" [message]="error()" (retry)="load(page())" />
        }
        @case ('ready') {
          @if (events(); as e) {
            @if (e.totalCount === 0) {
              <div class="card">Nothing recorded yet.</div>
            } @else {
              <div class="card table-card">
                <table class="data-table">
                  <thead><tr><th>When</th><th>Action</th><th>What happened</th></tr></thead>
                  <tbody>
                    @for (event of e.items; track event.id) {
                      <tr>
                        <td class="small nowrap secondary">{{ event.occurredAt | date: 'yyyy-MM-dd HH:mm' }}</td>
                        <td class="mono small">{{ event.action }}</td>
                        <td>{{ event.summary }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
              <nav class="pager" aria-label="Audit log pages">
                <button type="button" class="btn" [disabled]="e.page <= 1" (click)="load(e.page - 1)">Previous</button>
                <span class="small muted">Page {{ e.page }} of {{ e.totalPages }} · {{ e.totalCount }} events</span>
                <button type="button" class="btn" [disabled]="e.page >= e.totalPages" (click)="load(e.page + 1)">Next</button>
              </nav>
            }
          }
        }
      }
    </div>
  `,
  styles: [`
    .nowrap { white-space: nowrap; }
    .pager { display: flex; align-items: center; gap: 1rem; margin-top: 1rem; }
  `]
})
export class AuditLogComponent {
  private api = inject(FinanceApiService);
  private destroyRef = inject(DestroyRef);

  readonly state = signal<LoadState>('loading');
  readonly error = signal('');
  readonly page = signal(1);
  readonly events = signal<Paged<AuditEvent> | null>(null);

  constructor() {
    this.load(1);
  }

  load(page: number): void {
    this.page.set(page);
    this.state.set('loading');
    this.api.getAuditEvents(page, 50).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: e => { this.events.set(e); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }
}
