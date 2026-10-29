import { Account, Fund } from './finance.models';

// Test fixtures shaped like API responses
export function account(overrides: Partial<Account>): Account {
  return {
    id: 1, name: 'Operating', type: 'Bank', currency: 'ZAR', ownership: 'Company', status: 'Active',
    balance: { amount: 0, currency: overrides.currency ?? 'ZAR' }, funds: [], createdAt: '2026-10-27T00:00:00Z',
    ...overrides
  };
}

export const FUNDS: Fund[] = [
  { id: 1, name: 'Unallocated', isSystem: true, balances: [] },
  { id: 3, name: 'Business Continuity', isSystem: false, balances: [] }
];
