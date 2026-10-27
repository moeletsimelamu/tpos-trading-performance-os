import { Component, input, output } from '@angular/core';
import { IconComponent } from './ui/icon.component';

@Component({
  selector: 'app-load-error',
  standalone: true,
  imports: [IconComponent],
  template: `
    <div class="load-error" role="alert">
      <app-icon name="alert" [size]="20" />
      <div class="load-error-body">
        <div class="load-error-title">{{ title() }}</div>
        <div class="load-error-text">{{ message() }}</div>
      </div>
      <button type="button" class="btn btn-retry" (click)="retry.emit()"><app-icon name="refresh" [size]="16" />Retry</button>
    </div>
  `,
  styles: [`
    .load-error {
      display: flex; align-items: flex-start; gap: var(--space-3);
      background: var(--surface); border: 1px solid #f0c9c5; border-radius: var(--radius-lg);
      padding: var(--space-4) var(--space-5);
      > app-icon { color: var(--negative); margin-top: 1px; }
    }
    .load-error-body { flex: 1; min-width: 0; }
    .load-error-title { font-weight: 600; margin-bottom: 2px; }
    .load-error-text { font-size: var(--text-sm); color: var(--text-secondary); }
  `]
})
export class LoadErrorComponent {
  readonly title = input('Could not load data');
  readonly message = input.required<string>();
  readonly retry = output<void>();
}
