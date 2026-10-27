import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { ShellComponent } from './core/layout/shell.component';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in',
    loadComponent: () =>
      import('./core/auth/login.component').then(m => m.LoginComponent)
  },
  {
    // Signed-in pages share the sidebar shell; the login page renders without it
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    canActivateChild: [authGuard],
    children: [
      // The Executive Overview becomes the landing page once it exists (Phase 5)
      { path: '', redirectTo: 'trading/performance', pathMatch: 'full' },
      {
        path: 'trading',
        loadChildren: () => import('./features/trading/trading.routes').then(m => m.tradingRoutes)
      },
      {
        path: 'finance',
        loadChildren: () => import('./features/finance/finance.routes').then(m => m.financeRoutes)
      },

      // Paths used before the Melamu Holdings OS navigation; kept so bookmarks still work
      { path: 'dashboard', redirectTo: 'trading/performance', pathMatch: 'full' },
      { path: 'trades', redirectTo: 'trading/trades', pathMatch: 'full' },
      { path: 'upload', redirectTo: 'trading/import', pathMatch: 'full' },
      { path: 'behaviour', redirectTo: 'trading/behaviour', pathMatch: 'full' }
    ]
  },
  { path: '**', redirectTo: '' }
];
