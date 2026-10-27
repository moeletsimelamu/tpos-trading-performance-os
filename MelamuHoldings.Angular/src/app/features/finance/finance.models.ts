// Shapes returned by the finance API. Amounts are decimals from the server; the browser never computes a balance.

export type AccountType = 'Bank' | 'Cash' | 'Brokerage' | 'Other';
export type Ownership = 'Company' | 'Personal' | 'Unspecified';
export type TransactionType =
  'OpeningBalance' | 'Income' | 'Expense' | 'Transfer' | 'FundReallocation' | 'CurrencyExchange' | 'Reversal';

export interface Money { amount: number; currency: string; }
export interface FundPosition { fundId: number; fundName: string; balance: Money; }

export interface Account {
  id: number;
  name: string;
  type: AccountType;
  currency: string;
  ownership: Ownership;
  institution?: string | null;
  notes?: string | null;
  status: 'Active' | 'Closed';
  balance: Money;
  funds: FundPosition[];
  createdAt: string;
  closedAt?: string | null;
}

export interface Fund { id: number; name: string; description?: string | null; isSystem: boolean; balances: Money[]; }

export interface Entry { accountId: number; accountName: string; fundId: number; fundName: string; amount: number; currency: string; }

export interface Transaction {
  id: number;
  type: TransactionType;
  date: string;
  recordedAt: string;
  description: string;
  reference?: string | null;
  category?: string | null;
  exchangeRate?: number | null;
  reversesTransactionId?: number | null;
  reversedByTransactionId?: number | null;
  entries: Entry[];
}

export interface Paged<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number; }

export interface Balances { asOf?: string | null; accounts: Account[]; funds: Fund[]; cashTotals: Money[]; }

export interface AuditEvent {
  id: number; occurredAt: string; userId: number; action: string;
  entityType: string; entityId: string; summary: string; data?: string | null;
}

export interface CurrencyInfo { code: string; minorUnits: number; }

export interface TransactionFilter {
  from?: string; to?: string; type?: TransactionType | ''; accountId?: number | null; fundId?: number | null;
  page: number; pageSize: number;
}

// The kinds of movement a person can record, the endpoint each posts to, and how it reads in the UI
export type RecordKind = 'income' | 'expense' | 'transfer' | 'reallocation' | 'exchange' | 'opening-balance';

export const RECORD_KINDS: { kind: RecordKind; label: string; help: string }[] = [
  { kind: 'income', label: 'Income', help: 'Money received into an account, e.g. a payout that has arrived.' },
  { kind: 'expense', label: 'Expense', help: 'Money paid out of an account.' },
  { kind: 'transfer', label: 'Transfer', help: 'Money moved between two of your accounts in the same currency.' },
  { kind: 'reallocation', label: 'Earmark', help: 'Assign money inside one account to a different fund. No money leaves the account.' },
  { kind: 'exchange', label: 'Currency exchange', help: 'Money converted between accounts in different currencies, at the rate you actually got.' },
  { kind: 'opening-balance', label: 'Opening balance', help: 'What an account already held before you started recording. Once per account and fund.' }
];

export const TYPE_LABELS: Record<TransactionType, string> = {
  OpeningBalance: 'Opening balance',
  Income: 'Income',
  Expense: 'Expense',
  Transfer: 'Transfer',
  FundReallocation: 'Earmark',
  CurrencyExchange: 'Exchange',
  Reversal: 'Reversal'
};

export const UNALLOCATED_FUND_ID = 1;
