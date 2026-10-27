import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-load-error',
  standalone: true,
  template: `
    <div class="card load-error" role="alert">
      <div>
        <div class="load-error-title">{{ title() }}</div>
        <div class="load-error-text mono">{{ message() }}</div>
      </div>
      <button type="button" class="btn-retry" (click)="retry.emit()">Retry</button>
    </div>
  `,
  styles: [`
    .load-error {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
      border-left: 3px solid var(--accent-red);
    }
    .load-error-title { font-weight: 700; font-size: 14px; margin-bottom: 0.25rem; }
    .load-error-text { font-size: 12px; color: var(--text-secondary); }
    .btn-retry {
      background: transparent;
      border: 1px solid var(--border);
      border-radius: var(--radius);
      padding: 0.45rem 1rem;
      color: var(--text-primary);
      font-size: 13px;
      cursor: pointer;
      flex-shrink: 0;
      &:hover { border-color: var(--accent-amber); color: var(--accent-amber); }
    }
  `]
})
export class LoadErrorComponent {
  readonly title = input('Could not load data');
  readonly message = input.required<string>();
  readonly retry = output<void>();
}
