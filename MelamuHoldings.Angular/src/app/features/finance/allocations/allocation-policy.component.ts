import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { FinanceApiService } from '../finance-api.service';
import { AllocationPolicy, Fund } from '../finance.models';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

type LoadState = 'loading' | 'ready' | 'error';

interface DraftLine { fundId: number; fundName: string; percentage: number | null; }

@Component({
  selector: 'app-allocation-policy',
  standalone: true,
  imports: [DatePipe, FormsModule, RouterLink, LoadErrorComponent, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header section="Finance · Capital allocation" title="Allocation policy"
        description="How received money is split between funds. Versions are never edited: a change is a new version, so every allocation keeps the rule it was confirmed under.">
        <a class="btn" routerLink="/finance/allocations">Allocations</a>
        @if (!drafting()) { <button type="button" class="btn btn-primary" (click)="startDraft()">New version</button> }
      </app-page-header>

      @switch (state()) {
        @case ('loading') {
          <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading policies...</span></div>
        }
        @case ('error') {
          <app-load-error title="Could not load allocation policies" [message]="error()" (retry)="load()" />
        }
        @case ('ready') {
          @if (drafting()) {
            <form class="card draft" (ngSubmit)="save()" aria-label="New policy version">
              <h2>Version {{ nextVersion() }}</h2>
              <p class="secondary small">Leave a fund at 0 to leave it out. The shares must add up to exactly 100%.</p>
              <table class="data-table">
                <thead><tr><th>Fund</th><th class="num">Share %</th></tr></thead>
                <tbody>
                  @for (line of draft; track line.fundId) {
                    <tr>
                      <td><label [for]="'pct-' + line.fundId">{{ line.fundName }}</label></td>
                      <td class="num"><input class="pct" [id]="'pct-' + line.fundId" type="number" step="0.01" min="0" max="100"
                        [name]="'pct-' + line.fundId" [(ngModel)]="line.percentage" (ngModelChange)="touch()" /></td>
                    </tr>
                  }
                  <tr class="total-row" [class.off]="total() !== 100">
                    <td>Total</td><td class="num">{{ total() }}%</td>
                  </tr>
                </tbody>
              </table>
              @if (total() !== 100) { <p class="error small" role="status">The shares add up to {{ total() }}%. They must add up to 100%.</p> }
              <label class="activate"><input type="checkbox" name="activate" [(ngModel)]="activate" /> Use this version for new allocations straight away</label>
              @if (formError()) { <p class="notice notice-error" role="alert">{{ formError() }}</p> }
              <div class="form-actions">
                <button type="submit" class="btn btn-primary" [disabled]="saving() || total() !== 100">{{ saving() ? 'Saving…' : 'Save version ' + nextVersion() }}</button>
                <button type="button" class="btn-link small" (click)="drafting.set(false)">Cancel</button>
              </div>
            </form>
          }

          <div class="card table-card">
            <table class="data-table" aria-label="Policy versions">
              <thead><tr><th>Version</th><th>Split</th><th>Created</th><th>Status</th><th><span class="visually-hidden">Actions</span></th></tr></thead>
              <tbody>
                @for (p of policies(); track p.id) {
                  <tr>
                    <td class="strong">v{{ p.version }}</td>
                    <td>
                      @for (l of p.lines; track l.fundId) { <div class="small">{{ l.fundName }} <span class="secondary">{{ l.percentage }}%</span></div> }
                    </td>
                    <td class="secondary small nowrap">{{ p.createdAt | date: 'yyyy-MM-dd' }}{{ p.version === 1 ? ' (default)' : '' }}</td>
                    <td>@if (p.isActive) { <span class="tag tag-positive">Active</span> } @else { <span class="tag">Not in use</span> }</td>
                    <td class="actions">
                      @if (!p.isActive) { <button type="button" class="btn btn-sm" [disabled]="saving()" (click)="activateVersion(p)">Make active</button> }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (actionError()) { <p class="notice notice-error" role="alert">{{ actionError() }}</p> }
        }
      }
    </div>
  `,
  styles: [`
    .draft { margin-bottom: var(--space-5); padding: var(--space-5) 0 var(--space-4); }
    .draft > h2, .draft > p, .draft > .activate, .draft > .form-actions, .draft > .notice { margin-left: var(--space-5); margin-right: var(--space-5); }
    .draft > h2 { font-size: var(--text-md); }
    .draft table { margin: var(--space-3) 0; }
    .pct { width: 96px; height: 32px; text-align: right; border: 1px solid var(--border); border-radius: var(--radius); padding: 0 8px; font: inherit; }
    .total-row td { font-weight: 600; border-top: 1px solid var(--border); }
    .total-row.off td { color: var(--negative); }
    .activate { display: flex; gap: var(--space-2); align-items: center; font-size: 13.5px; margin-top: var(--space-3); }
    .error { color: var(--negative); margin: 0 var(--space-5); }
    .strong { font-weight: 600; }
    .actions { text-align: right; }
  `]
})
export class AllocationPolicyComponent {
  private api = inject(FinanceApiService);
  private destroyRef = inject(DestroyRef);

  readonly state = signal<LoadState>('loading');
  readonly error = signal('');
  readonly policies = signal<AllocationPolicy[]>([]);
  readonly funds = signal<Fund[]>([]);
  readonly drafting = signal(false);
  readonly saving = signal(false);
  readonly formError = signal('');
  readonly actionError = signal('');

  draft: DraftLine[] = [];
  activate = true;
  // Bumped on every edit so the total recomputes (the draft rows are plain objects bound with ngModel)
  private readonly edits = signal(0);

  readonly nextVersion = computed(() => Math.max(0, ...this.policies().map(p => p.version)) + 1);

  readonly total = computed(() => {
    this.edits();
    // Summed in hundredths so 33.33 + 33.33 + 33.34 is exactly 100
    return Math.round(this.draft.reduce((sum, l) => sum + Math.round(Number(l.percentage ?? 0) * 100), 0)) / 100;
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    forkJoin({ policies: this.api.getPolicies(), funds: this.api.getFunds() }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ policies, funds }) => { this.policies.set(policies); this.funds.set(funds); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  // Starts from the active version's split, with every purpose fund listed
  startDraft(): void {
    const active = this.policies().find(p => p.isActive);
    this.draft = this.funds().filter(f => !f.isSystem).map(f => ({
      fundId: f.id, fundName: f.name, percentage: active?.lines.find(l => l.fundId === f.id)?.percentage ?? 0
    }));
    this.activate = true;
    this.formError.set('');
    this.touch();
    this.drafting.set(true);
  }

  touch(): void {
    this.edits.update(n => n + 1);
  }

  save(): void {
    if (this.saving() || this.total() !== 100) return;
    const lines = this.draft.filter(l => Number(l.percentage) > 0).map(l => ({ fundId: l.fundId, percentage: Number(l.percentage) }));
    this.saving.set(true);
    this.formError.set('');
    this.api.createPolicy({ lines, activate: this.activate }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.saving.set(false); this.drafting.set(false); this.load(); },
      error: err => { this.saving.set(false); this.formError.set(`Not saved: ${describeHttpError(err)}`); }
    });
  }

  activateVersion(policy: AllocationPolicy): void {
    this.saving.set(true);
    this.actionError.set('');
    this.api.activatePolicy(policy.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.saving.set(false); this.load(); },
      error: err => { this.saving.set(false); this.actionError.set(`v${policy.version} was not activated: ${describeHttpError(err)}`); }
    });
  }
}
