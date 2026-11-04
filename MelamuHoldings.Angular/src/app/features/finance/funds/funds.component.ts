import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { FinanceApiService } from '../finance-api.service';
import { CurrencyInfo, Fund, fundStanding } from '../finance.models';
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
          @if (!editing()?.isSystem) {
            <fieldset class="thresholds">
              <legend>Minimum and target (optional)</legend>
              <p class="small secondary">The level the fund should stay above, and the level to build it to. They are compared with the fund's balance in this currency only; nothing is converted.</p>
              <div class="form-grid">
                <div class="field">
                  <label for="fund-currency">Currency</label>
                  <select id="fund-currency" formControlName="thresholdCurrency">
                    <option value="">Choose…</option>
                    @for (c of currencies(); track c.code) { <option [value]="c.code">{{ c.code }}</option> }
                  </select>
                </div>
                <div class="field"><label for="fund-min">Minimum</label><input id="fund-min" type="number" min="0" step="0.01" formControlName="minimum" /></div>
                <div class="field"><label for="fund-target">Target</label><input id="fund-target" type="number" min="0" step="0.01" formControlName="target" /></div>
              </div>
            </fieldset>
          }
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
              <thead><tr><th>Fund</th><th>Purpose</th><th class="num">Balance</th><th>Minimum and target</th><th><span class="visually-hidden">Actions</span></th></tr></thead>
              <tbody>
                @for (f of funds(); track f.id) {
                  <tr>
                    <td>
                      <a [routerLink]="['/finance/transactions']" [queryParams]="{ fundId: f.id }">{{ f.name }}</a>
                      @if (f.isSystem) { <span class="tag tag-info system">System</span> }
                    </td>
                    <td class="small">
                      {{ f.description ?? '' }}
                      @if (f.accounts?.length) {
                        <div class="muted">Accounts: @for (a of f.accounts; track a.id; let last = $last) { <a [routerLink]="['/finance/transactions']" [queryParams]="{ accountId: a.id }">{{ a.name }}</a>@if (!last) {, } }</div>
                      }
                    </td>
                    <td class="num">
                      @for (b of f.balances; track b.currency) { <div>{{ b.amount | money: b.currency }}</div> }
                      @empty { <span class="muted">Nothing recorded</span> }
                    </td>
                    <td class="small">
                      @if (standing(f); as s) {
                        <div>
                          @if (f.minimum) { Minimum {{ f.minimum.amount | money: f.minimum.currency }} }
                          @if (f.minimum && f.target) { · }
                          @if (f.target) { Target {{ f.target.amount | money: f.target.currency }} }
                        </div>
                        @if (s.belowMinimum) { <span class="tag tag-negative">Below minimum</span> }
                        @if (s.progress !== null) {
                          <div class="progress" [attr.aria-label]="s.progress + '% of the target'"><div class="fill" [style.width.%]="s.progress"></div></div>
                          <span class="muted">{{ s.progress }}% of the target</span>
                        }
                      } @else { <span class="muted">—</span> }
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
    .thresholds { border: 0; padding: 0; margin: var(--space-4) 0 0; }
    .thresholds legend { font-weight: 500; margin-bottom: var(--space-1); }
    .progress { height: 6px; border-radius: 3px; background: var(--surface-sunken); overflow: hidden; margin: var(--space-1) 0; max-width: 160px; }
    .progress .fill { height: 100%; background: var(--positive); }
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
  readonly currencies = signal<CurrencyInfo[]>([]);
  readonly standing = fundStanding;

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    description: ['', Validators.maxLength(300)],
    thresholdCurrency: [''],
    minimum: [null as number | null],
    target: [null as number | null]
  });

  constructor() {
    this.load();
    this.api.getCurrencies().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: c => this.currencies.set(c), error: () => {} });
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
    this.form.setValue({
      name: fund.name, description: fund.description ?? '',
      thresholdCurrency: fund.minimum?.currency ?? fund.target?.currency ?? '',
      minimum: fund.minimum?.amount ?? null, target: fund.target?.amount ?? null
    });
    this.formError.set('');
    this.editing.set(fund);
  }

  save(): void {
    if (this.form.invalid || this.saving()) return;
    const value = this.form.getRawValue();
    // The minimum and target are always sent: leaving them out clears them
    const body = {
      name: value.name, description: value.description || undefined,
      thresholdCurrency: value.thresholdCurrency || null, minimum: value.minimum ?? null, target: value.target ?? null
    };
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
