import { Component } from '@angular/core';

@Component({
  selector: 'app-trades',
  standalone: true,
  template: `
    <h1 style="color: var(--text-primary); font-family: var(--font-display); font-size: 1.75rem; font-weight: 800;">
      Trades
    </h1>
    <p style="color: var(--text-secondary); margin-top: 0.5rem;">Trade table coming in Phase 6.</p>
  `
})
export class TradesComponent {}