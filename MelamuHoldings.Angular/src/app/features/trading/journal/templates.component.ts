import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { TradingApiService } from '../trading-api.service';
import { JournalTemplate } from '../models/journal.model';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';
import { IconComponent } from '../../../shared/ui/icon.component';

@Component({
  selector: 'app-journal-templates',
  standalone: true,
  imports: [DatePipe, RouterLink, LoadErrorComponent, PageHeaderComponent, IconComponent],
  template: `
    <div class="page">
      <app-page-header section="Trading operations" title="Journal templates"
        description="The questions you answer for each trade. Editing a template's questions creates a new version; trades already journaled keep the version they were written with.">
        <a class="btn btn-primary" routerLink="/trading/journal-templates/new"><app-icon name="plus" [size]="16" />New template</a>
      </app-page-header>

      @switch (state()) {
        @case ('loading') { <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading templates...</span></div> }
        @case ('error') { <app-load-error title="Could not load journal templates" [message]="error()" (retry)="load()" /> }
        @case ('ready') {
          @if (actionError()) { <p class="notice notice-error" role="alert">{{ actionError() }}</p> }
          <div class="card table-card">
            <div class="toolbar">
              <label class="inline"><input type="checkbox" [checked]="showArchived()" (change)="toggleArchived()" /> Show archived</label>
            </div>
            <table class="data-table">
              <thead><tr><th>Template</th><th class="num">Version</th><th class="num">Trades journaled</th><th>Updated</th><th>Status</th><th><span class="visually-hidden">Actions</span></th></tr></thead>
              <tbody>
                @for (t of templates(); track t.id) {
                  <tr [class.archived]="t.status === 'Archived'">
                    <td class="name">
                      <a [routerLink]="['/trading/journal-templates', t.id]">{{ t.name }}</a>
                      @if (t.description) { <div class="small muted">{{ t.description }}</div> }
                    </td>
                    <td class="num">v{{ t.currentVersion }}</td>
                    <td class="num">{{ used(t) }}</td>
                    <td class="small secondary nowrap">{{ (t.updatedAt ?? t.createdAt) | date: 'd MMM yyyy' }}</td>
                    <td>
                      @if (t.isDefault) { <span class="tag tag-positive">Default</span> }
                      @else if (t.status === 'Archived') { <span class="tag">Archived</span> }
                      @else { <span class="tag tag-info">Active</span> }
                    </td>
                    <td class="actions">
                      <button type="button" class="btn-link small" [disabled]="busy()" (click)="act(t, 'duplicate')">Duplicate</button>
                      @if (t.status === 'Active' && !t.isDefault) {
                        <button type="button" class="btn-link small" [disabled]="busy()" (click)="act(t, 'make-default')">Make default</button>
                        <button type="button" class="btn-link small" [disabled]="busy()" (click)="act(t, 'archive')">Archive</button>
                      }
                      @if (t.status === 'Archived') {
                        <button type="button" class="btn-link small" [disabled]="busy()" (click)="act(t, 'restore')">Restore</button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <p class="small muted footnote">New trades start with the default template. Archived templates are hidden from new trades; entries written with them stay readable.</p>
        }
      }
    </div>
  `,
  styles: [`
    .inline { display: flex; gap: var(--space-2); align-items: center; font-size: 13.5px; }
    .archived td { color: var(--text-muted); }
    .name { min-width: 220px; }
    .actions { display: flex; gap: var(--space-3); justify-content: flex-end; white-space: nowrap; }
    .footnote { margin-top: var(--space-3); }
  `]
})
export class TemplatesComponent {
  private api = inject(TradingApiService);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly error = signal('');
  readonly templates = signal<JournalTemplate[]>([]);
  readonly showArchived = signal(false);
  readonly busy = signal(false);
  readonly actionError = signal('');

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.api.getTemplates(this.showArchived()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: t => { this.templates.set(t); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  toggleArchived(): void {
    this.showArchived.set(!this.showArchived());
    this.load();
  }

  used(t: JournalTemplate): number {
    return t.versions.reduce((sum, v) => sum + v.entryCount, 0);
  }

  act(t: JournalTemplate, action: 'duplicate' | 'archive' | 'restore' | 'make-default'): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set('');
    this.api.templateAction(t.id, action).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        this.busy.set(false);
        if (action === 'duplicate') this.router.navigate(['/trading/journal-templates', result.id]);
        else this.load();
      },
      error: err => { this.busy.set(false); this.actionError.set(`'${t.name}': ${describeHttpError(err)}`); }
    });
  }
}
