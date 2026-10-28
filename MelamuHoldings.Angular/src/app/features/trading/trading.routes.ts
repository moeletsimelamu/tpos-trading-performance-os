import { Routes } from '@angular/router';

// Trading Operations module, mounted at /trading
export const tradingRoutes: Routes = [
  { path: '', redirectTo: 'performance', pathMatch: 'full' },
  {
    path: 'performance',
    title: 'Trading performance',
    loadComponent: () =>
      import('./performance/performance.component').then(m => m.PerformanceComponent)
  },
  {
    path: 'trades',
    title: 'Trades',
    loadComponent: () =>
      import('./trades/trades.component').then(m => m.TradesComponent)
  },
  {
    path: 'trades/new',
    title: 'New trade',
    loadComponent: () =>
      import('./journal/trade-form.component').then(m => m.TradeFormComponent)
  },
  {
    path: 'trades/:id',
    title: 'Trade journal entry',
    loadComponent: () =>
      import('./journal/trade-entry.component').then(m => m.TradeEntryComponent)
  },
  {
    path: 'trades/:id/edit',
    title: 'Edit trade',
    loadComponent: () =>
      import('./journal/trade-form.component').then(m => m.TradeFormComponent)
  },
  {
    path: 'import',
    title: 'Import trades',
    loadComponent: () =>
      import('./import/import.component').then(m => m.ImportComponent)
  },
  {
    path: 'behaviour',
    title: 'Trading behaviour',
    loadComponent: () =>
      import('./behaviour/behaviour.component').then(m => m.BehaviourComponent)
  }
];
