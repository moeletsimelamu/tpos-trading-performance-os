import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { FinanceApiService } from '../finance-api.service';
import { Fund } from '../finance.models';
import { MoneyPipe } from '../../../shared/money.pipe';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-finance-funds',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, MoneyPipe, LoadErrorComponent, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header section="Finance" title="Funds"
        description="What money is earmarked for. A fund is a purpose, not a bank account; one account can hold several funds.">
        <button type="button" class="btn" (click)="startCreate()">New fund</button>
      </app-page-header>

      @if (editing() !== undefined) {
        <form class="card form-card" [formGroup]="form" (ngSubmit)="save()" [attr.aria-label]="editing() ? 'Edit fund' : 'New fund'">
          <div class="form-grid">
            <div class="field">
              <label for="fund-name">Name</label>
              <input id="fund-name" formControlName="name" />
            </div>
            <div class="field">
              <label for="fund-desc">Description (optional)</label>
              <input id="fund-desc" formControlName="description" />
            </div>
          </div>
          @if (formError()) { <p class="notice notice-error" role="alert">{{ formError() }}</p> }
          <div class="form-actions">
            <button type="submit" class="btn btn-primary" [disabled]="form.invalid || saving()">{{ saving() ? 'Saving…' : 'Save fund' }}</button>
            <button type="button" class="btn" (click)="editing.set(undefined)">Cancel</button>
          </div>
        </form>
      }

      @switch (state()) {
        @case ('loading') {
          <div class="loading-state"><div class="spinner"></div><span class="loading-text">Loading funds...</span></div>
        }
        @case ('error') {
          <app-load-error title="Could not load funds" [message]="error()" (retry)="load()" />
        }
        @case ('ready') {
          <div class="card table-card">
            <table class="data-table">
              <thead><tr><th>Fund</th><th>Purpose</th><th class="num">Balance</th><th><span class="visually-hidden">Actions</span></th></tr></thead>
              <tbody>
                @for (f of funds(); track f.id) {
                  <tr>
                    <td>
                      <a [routerLink]="['/finance/transactions']" [queryParams]="{ fundId: f.id }">{{ f.name }}</a>
                      @if (f.isSystem) { <span class="tag tag-info system">System</span> }
                    </td>
                    <td class="small">{{ f.description ?? '' }}</td>
                    <td class="num">
                      @for (b of f.balances; track b.currency) { <div>{{ b.amount | money: b.currency }}</div> }
                      @empty { <span class="muted">Nothing recorded</span> }
                    </td>
                    <td><button type="button" class="btn-link small" (click)="startEdit(f)">Edit</button></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    </div>
  `,
  styles: [`
    .form-card { margin-bottom: 1.25rem; }
    .system { margin-left: 0.5rem; }
  `]
})
export class FundsComponent {
  private api = inject(FinanceApiService);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);

  readonly state = signal<LoadState>('loading');
  readonly error = signal('');
  readonly funds = signal<Fund[]>([]);
  // undefined: no form; null: creating; a fund: editing it
  readonly editing = signal<Fund | null | undefined>(undefined);
  readonly saving = signal(false);
  readonly formError = signal('');

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    description: ['', Validators.maxLength(300)]
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.api.getFunds().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: f => { this.funds.set(f); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  startCreate(): void {
    this.form.reset();
    this.formError.set('');
    this.editing.set(null);
  }

  startEdit(fund: Fund): void {
    this.form.setValue({ name: fund.name, description: fund.description ?? '' });
    this.formError.set('');
    this.editing.set(fund);
  }

  save(): void {
    if (this.form.invalid || this.saving()) return;
    const value = this.form.getRawValue();
    const body = { name: value.name, description: value.description || undefined };
    const editing = this.editing();
    const request: Observable<Fund> = editing ? this.api.updateFund(editing.id, body) : this.api.createFund(body);

    this.saving.set(true);
    this.formError.set('');
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.saving.set(false); this.editing.set(undefined); this.load(); },
      error: err => { this.saving.set(false); this.formError.set(describeHttpError(err)); }
    });
  }
}
