import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./features/dashboard/dashboard.component')
        .then(m => m.DashboardComponent),
    runGuardsAndResolvers: 'always'
  },
  {
    path: 'trades',
    loadComponent: () =>
      import('./features/trades/trades.component')
        .then(m => m.TradesComponent),
    runGuardsAndResolvers: 'always'
  },
  {
    path: 'upload',
    loadComponent: () =>
      import('./features/upload/upload.component')
        .then(m => m.UploadComponent),
    runGuardsAndResolvers: 'always'
  },
  {
    path: 'behaviour',
    loadComponent: () =>
      import('./features/behaviour/behaviour.component')
        .then(m => m.BehaviourComponent),
    runGuardsAndResolvers: 'always'
  }
];