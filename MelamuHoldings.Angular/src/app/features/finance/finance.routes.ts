import { Routes } from '@angular/router';

// Financial Management module, mounted at /finance
export const financeRoutes: Routes = [
  { path: '', redirectTo: 'accounts', pathMatch: 'full' },
  {
    path: 'accounts',
    title: 'Accounts',
    loadComponent: () => import('./accounts/accounts.component').then(m => m.AccountsComponent)
  },
  {
    path: 'funds',
    title: 'Funds',
    loadComponent: () => import('./funds/funds.component').then(m => m.FundsComponent)
  },
  {
    path: 'transactions',
    title: 'Transactions',
    loadComponent: () => import('./transactions/transactions.component').then(m => m.TransactionsComponent)
  },
  {
    path: 'transactions/new',
    title: 'Record transaction',
    loadComponent: () => import('./transactions/record-transaction.component').then(m => m.RecordTransactionComponent)
  },
  {
    path: 'payouts',
    title: 'Payouts',
    loadComponent: () => import('./payouts/payouts.component').then(m => m.PayoutsComponent)
  },
  {
    path: 'payouts/:id',
    title: 'Payout',
    loadComponent: () => import('./payouts/payout-detail.component').then(m => m.PayoutDetailComponent)
  },
  {
    path: 'allocations',
    title: 'Capital allocation',
    loadComponent: () => import('./allocations/allocations.component').then(m => m.AllocationsComponent)
  },
  {
    path: 'allocations/:id',
    title: 'Allocation',
    loadComponent: () => import('./allocations/allocation-detail.component').then(m => m.AllocationDetailComponent)
  },
  {
    path: 'allocation-policy',
    title: 'Allocation policy',
    loadComponent: () => import('./allocations/allocation-policy.component').then(m => m.AllocationPolicyComponent)
  },
  {
    path: 'audit',
    title: 'Audit log',
    loadComponent: () => import('./audit/audit-log.component').then(m => m.AuditLogComponent)
  }
];
