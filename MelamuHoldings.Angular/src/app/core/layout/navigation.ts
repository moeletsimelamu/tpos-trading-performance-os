import { IconName } from '../../shared/ui/icon.component';

// The destinations that exist today. Modules still being built are not listed: the navigation
// describes the product as it is, and PLAN.md describes where it is going.
export interface NavItem {
  label: string;
  route: string;
  icon: IconName;
}

export interface NavSection {
  heading: string;
  items: NavItem[];
}

export const NAVIGATION: NavSection[] = [
  {
    heading: 'Overview',
    items: [
      { label: 'Executive overview', route: '/overview', icon: 'overview' }
    ]
  },
  {
    heading: 'Trading operations',
    items: [
      { label: 'Performance', route: '/trading/performance', icon: 'performance' },
      { label: 'Trades', route: '/trading/trades', icon: 'trades' },
      { label: 'Import', route: '/trading/import', icon: 'import' },
      { label: 'Behaviour', route: '/trading/behaviour', icon: 'behaviour' }
    ]
  },
  {
    heading: 'Finance',
    items: [
      { label: 'Accounts', route: '/finance/accounts', icon: 'accounts' },
      { label: 'Funds', route: '/finance/funds', icon: 'funds' },
      { label: 'Transactions', route: '/finance/transactions', icon: 'transactions' },
      { label: 'Audit log', route: '/finance/audit', icon: 'audit' }
    ]
  }
];
