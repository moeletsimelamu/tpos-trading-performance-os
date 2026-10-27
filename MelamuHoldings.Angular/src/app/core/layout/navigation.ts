// The product's modules and their real implementation status. Planned items are shown so the
// structure is visible, but they render as labels, not links: nothing pretends to work before it does.
export interface NavItem {
  label: string;
  route?: string;       // present only for pages that exist
  plannedPhase?: number; // PLAN.md phase that delivers it
}

export interface NavSection {
  heading: string;
  items: NavItem[];
}

export const NAVIGATION: NavSection[] = [
  {
    heading: 'Overview',
    items: [
      { label: 'Executive Overview', plannedPhase: 5 }
    ]
  },
  {
    heading: 'Trading Operations',
    items: [
      { label: 'Performance', route: '/trading/performance' },
      { label: 'Trades', route: '/trading/trades' },
      { label: 'Import', route: '/trading/import' },
      { label: 'Behaviour', route: '/trading/behaviour' },
      { label: 'Prop Accounts', plannedPhase: 8 }
    ]
  },
  {
    heading: 'Finance',
    items: [
      { label: 'Financial Management', plannedPhase: 4 },
      { label: 'Capital Allocation', plannedPhase: 5 },
      { label: 'Wealth Management', plannedPhase: 7 },
      { label: 'Business Continuity', plannedPhase: 7 },
      { label: 'Business Planning', plannedPhase: 9 }
    ]
  },
  {
    heading: 'System',
    items: [
      { label: 'Settings', plannedPhase: 4 }
    ]
  }
];
