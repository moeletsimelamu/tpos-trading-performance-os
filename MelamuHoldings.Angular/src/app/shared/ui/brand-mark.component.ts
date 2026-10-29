import { Component, input } from '@angular/core';

// The Melamu Holdings monogram: an M and an H sharing their middle stem, joined by a brass bridge.
// `tone` picks the tile colour for the surface it sits on; the drawing itself never changes.
@Component({
  selector: 'app-brand-mark',
  standalone: true,
  host: { 'aria-hidden': 'true', '[style.width.px]': 'size()', '[style.height.px]': 'size()' },
  template: `
    <svg viewBox="0 0 32 32" focusable="false">
      <rect width="32" height="32" rx="7" [attr.fill]="tone() === 'on-dark' ? '#1c304d' : '#102036'" />
      <path d="M7 23V9l5 7 5-7v14M25 9v14" fill="none" stroke="#f6f2e9" stroke-width="2.6" />
      <path d="M17 16h8" stroke="#c9a35a" stroke-width="2.6" />
    </svg>
  `,
  styles: [`:host { display: inline-block; flex-shrink: 0; } svg { display: block; width: 100%; height: 100%; }`]
})
export class BrandMarkComponent {
  readonly size = input(32);
  readonly tone = input<'on-light' | 'on-dark'>('on-light');
}
