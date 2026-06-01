import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./features/dashboard/dashboard.component')
        .then(m => m.DashboardComponent)
  },
  {
    path: 'trades',
    loadComponent: () =>
      import('./features/trades/trades.component')
        .then(m => m.TradesComponent)
  },
  {
    path: 'upload',
    loadComponent: () =>
      import('./features/upload/upload.component')
        .then(m => m.UploadComponent)
  },
  {
    path: 'behaviour',
    loadComponent: () =>
      import('./features/behaviour/behaviour.component')
        .then(m => m.BehaviourComponent)
  }
];