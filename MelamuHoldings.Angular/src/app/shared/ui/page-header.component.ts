import { Component, input } from '@angular/core';

// The one page header: section label, title, a sentence of context, and an actions slot on the right.
@Component({
  selector: 'app-page-header',
  standalone: true,
  template: `
    <header class="page-header">
      <div class="page-heading">
        @if (section()) { <p class="page-section">{{ section() }}</p> }
        <h1>{{ title() }}</h1>
        @if (description()) { <p class="page-description">{{ description() }}</p> }
      </div>
      <div class="page-actions"><ng-content /></div>
    </header>
  `
})
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly section = input<string>();
  readonly description = input<string>();
}
