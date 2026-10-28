import { Component, computed, input } from '@angular/core';

// One stroke icon set drawn for this product: 24-unit grid, 1.6 stroke, round ends.
// Icons are decorative by default; give the surrounding control a text label.
const PATHS = {
  overview: 'M4 4h7v9H4zM13 4h7v5h-7zM13 11h7v9h-7zM4 15h7v5H4z',
  performance: 'M4 19h16M5 15l4-5 4 3 6-7',
  trades: 'M9 6h11M9 12h11M9 18h11M4.5 6h.01M4.5 12h.01M4.5 18h.01',
  template: 'M5 4h14v16H5zM8 8h8M8 12h8M8 16h5',
  strategy: 'M5 21V4M5 4h11l-2 4 2 4H5',
  journal: 'M6 4h11a1 1 0 0 1 1 1v15H7a2 2 0 0 1-2-2V5a1 1 0 0 1 1-1zM5 18a2 2 0 0 1 2-2h11M9 8h6',
  paperclip: 'M20 11.5l-8.2 8.2a5 5 0 0 1-7.1-7.1l8.5-8.5a3.3 3.3 0 0 1 4.7 4.7l-8.5 8.5a1.7 1.7 0 0 1-2.4-2.4l7.8-7.8',
  'chevron-left': 'M15 6l-6 6 6 6',
  'chevron-right': 'M9 6l6 6-6 6',
  up: 'M12 19V5M6 11l6-6 6 6',
  down: 'M12 5v14M6 13l6 6 6-6',
  import: 'M12 15V4M8 8l4-4 4 4M4 15v5h16v-5',
  behaviour: 'M3 12h4l2-5 4 10 2-5h6',
  accounts: 'M3 9l9-5 9 5M5.5 10.5v7M10 10.5v7M14 10.5v7M18.5 10.5v7M3 20h18',
  funds: 'M12 4l8 4-8 4-8-4 8-4zM4 12l8 4 8-4M4 16l8 4 8-4',
  payouts: 'M12 3v12M7.5 10.5L12 15l4.5-4.5M4 17v3h16v-3',
  allocation: 'M12 4v6M12 10l-6 5v5M12 10l6 5v5M12 10v10',
  transactions: 'M4 8h15l-3.5-3.5M20 16H5l3.5 3.5',
  audit: 'M9 3.5h6v3H9zM7 5H5v15h14V5h-2M9 13l2 2 4-4.5',
  'sign-out': 'M14 4h5v16h-5M10 8l-4 4 4 4M6 12h10',
  menu: 'M4 7h16M4 12h16M4 17h16',
  close: 'M6 6l12 12M18 6L6 18',
  plus: 'M12 5v14M5 12h14',
  'arrow-right': 'M5 12h14M13 6l6 6-6 6',
  alert: 'M12 4l9 16H3zM12 10v4M12 17h.01',
  check: 'M5 12.5l4.5 4.5L19 7',
  info: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 11v5M12 8h.01',
  refresh: 'M20 12a8 8 0 1 1-2.4-5.7M20 4v5h-5',
  trash: 'M5 7h14M10 7V4h4v3M7 7l1 13h8l1-13'
} as const;

export type IconName = keyof typeof PATHS;

@Component({
  selector: 'app-icon',
  standalone: true,
  host: { 'aria-hidden': 'true', '[style.width.px]': 'size()', '[style.height.px]': 'size()' },
  template: `
    <svg viewBox="0 0 24 24" focusable="false" fill="none" stroke="currentColor"
      stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">
      <path [attr.d]="path()" />
    </svg>
  `,
  styles: [`:host { display: inline-block; flex-shrink: 0; } svg { display: block; width: 100%; height: 100%; }`]
})
export class IconComponent {
  readonly name = input.required<IconName>();
  readonly size = input(18);
  readonly path = computed(() => PATHS[this.name()]);
}
