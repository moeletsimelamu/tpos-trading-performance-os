import { Routes } from '@angular/router';
import { authGuard } from './guards/auth.guard';
import { ShellComponent } from './layout/shell.component';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./features/auth/login.component').then(m => m.LoginComponent)
  },
  {
    // Signed-in pages share the sidebar shell; the login page renders without it
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    canActivateChild: [authGuard],
    children: [
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
      {
        path: 'dashboard',
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent)
      },
      {
        path: 'trades',
        loadComponent: () =>
          import('./features/trades/trades.component').then(m => m.TradesComponent)
      },
      {
        path: 'upload',
        loadComponent: () =>
          import('./features/upload/upload.component').then(m => m.UploadComponent)
      },
      {
        path: 'behaviour',
        loadComponent: () =>
          import('./features/behaviour/behaviour.component').then(m => m.BehaviourComponent)
      }
    ]
  },
  { path: '**', redirectTo: '' }
];
