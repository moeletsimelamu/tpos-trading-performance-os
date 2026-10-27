import { Routes } from '@angular/router';
import { authGuard } from './guards/auth.guard';

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  {
    path: 'login',
    loadComponent: () =>
      import('./features/auth/login.component').then(m => m.LoginComponent)
  },
  {
    path: 'register',
    loadComponent: () =>
      import('./features/auth/register.component').then(m => m.RegisterComponent)
  },
  {
    path: 'dashboard',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent)
  },
  {
    path: 'trades',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/trades/trades.component').then(m => m.TradesComponent)
  },
  {
    path: 'upload',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/upload/upload.component').then(m => m.UploadComponent)
  },
  {
    path: 'behaviour',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/behaviour/behaviour.component').then(m => m.BehaviourComponent)
  }
];