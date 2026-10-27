import { Routes } from '@angular/router';

// Trading Operations module, mounted at /trading
export const tradingRoutes: Routes = [
  { path: '', redirectTo: 'performance', pathMatch: 'full' },
  {
    path: 'performance',
    title: 'Trading Performance',
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
    path: 'import',
    title: 'Import Journal',
    loadComponent: () =>
      import('./import/import.component').then(m => m.ImportComponent)
  },
  {
    path: 'behaviour',
    title: 'Behaviour Analytics',
    loadComponent: () =>
      import('./behaviour/behaviour.component').then(m => m.BehaviourComponent)
  }
];
