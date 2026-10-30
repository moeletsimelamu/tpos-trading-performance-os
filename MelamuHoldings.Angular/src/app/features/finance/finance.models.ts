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

// Minimum and target are in one currency and compared with the fund's balance in that currency only
export interface Fund { id: number; name: string; description?: string | null; isSystem: boolean; balances: Money[]; minimum?: Money | null; target?: Money | null; }

export interface SaveFund { name: string; description?: string; thresholdCurrency?: string | null; minimum?: number | null; target?: number | null; }

// Where a fund stands against its minimum and target: its balance in their currency (0 when it holds none),
// whether it is below the minimum, and how far it is towards the target (0 to 100)
export function fundStanding(fund: Fund): { balance: Money; belowMinimum: boolean; progress: number | null } | null {
  const currency = fund.minimum?.currency ?? fund.target?.currency;
  if (!currency) return null;
  const amount = fund.balances.find(b => b.currency === currency)?.amount ?? 0;
  const target = fund.target?.amount;
  return {
    balance: { amount, currency },
    belowMinimum: fund.minimum != null && amount < fund.minimum.amount,
    progress: target ? Math.max(0, Math.min(100, Math.round(amount / target * 1000) / 10)) : null
  };
}

// Obligations (Phase 7.1): what the business has to pay. Payments link expenses already in the ledger.
export type Recurrence = 'None' | 'Monthly' | 'Quarterly' | 'Yearly';
export const RECURRENCES: { value: Recurrence; label: string }[] = [
  { value: 'None', label: 'Once' }, { value: 'Monthly', label: 'Monthly' }, { value: 'Quarterly', label: 'Quarterly' }, { value: 'Yearly', label: 'Yearly' }
];
export type ObligationStanding = 'Upcoming' | 'DueSoon' | 'Overdue' | 'Completed' | 'Cancelled';
export interface ObligationPayment { id: number; transactionId: number; dueDate: string; paidOn: string; amount: Money; reversed: boolean; }
export interface Obligation {
  id: number; name: string; amount: Money; category?: string | null; recurrence: Recurrence; firstDueDate: string; dueDate: string;
  status: 'Active' | 'Completed' | 'Cancelled'; standing: ObligationStanding; daysUntilDue: number; notes?: string | null;
  payments: ObligationPayment[]; createdAt: string; updatedAt?: string | null;
}
export interface SaveObligation {
  name: string; amount: number; currency: string; category?: string | null; recurrence: Recurrence; firstDueDate: string; notes?: string | null;
}
// Continuity (Phase 7.2): months of essential expenses the reserve covers, from recorded balances only.
// Counted: the continuity fund's money in the settings currency in bank and cash accounts. Everything else is listed apart.
export interface ContinuitySettings {
  continuityFundId: number; continuityFundName: string; months: number; currency: string; monthlyEssentialExpense?: number | null;
  restorationFundId?: number | null; restorationFundName?: string | null; updatedAt?: string | null;
}
export interface SaveContinuitySettings { continuityFundId: number; months: number; monthlyEssentialExpense: number | null; restorationFundId: number | null; }
export interface ContinuityHolding { accountId: number; accountName: string; amount: Money; separated: boolean; }
export interface NotCounted { reason: string; amount: Money; accountName?: string | null; }
export interface Restoration {
  fundId: number; fundName: string; target?: Money | null; cash?: Money | null; shortfall?: Money | null; progress?: number | null; notCounted: NotCounted[];
}
export interface ContinuityReport {
  settings: ContinuitySettings; cash: Money; target?: Money | null; coverageMonths?: number | null; shortfall?: Money | null; belowTarget: boolean;
  earmarked: Money; separated: Money; holdings: ContinuityHolding[]; notCounted: NotCounted[]; restoration?: Restoration | null;
}

// Investments (Phase 7.3): valuations of brokerage accounts against what the ledger recorded. A valuation never moves money;
// unrealised is value − recorded balance on the valuation date, and is not cash or income.
export interface Valuation {
  id: number; accountId: number; valuedOn: string; value: Money; note?: string | null; recordedAt: string; recordedBalance: Money; unrealised: Money;
}
export interface InvestmentAccount {
  accountId: number; accountName: string; currency: string; institution?: string | null; ownership: Ownership; status: 'Active' | 'Closed';
  asOf: string; latest?: Valuation | null; netContributions: Money; recordedIncome: Money; recordedCosts: Money; recordedBalance: Money;
  unrealised?: Money | null; gainOnContributions?: Money | null; transactionsSinceValuation: number; movedSinceValuation: Money; currentRecordedBalance: Money;
}
export interface InvestmentTotal { currency: string; value: number; netContributions: number; unrealised: number; accounts: number; }
export interface Investments { accounts: InvestmentAccount[]; totals: InvestmentTotal[]; }
export interface RecordValuation { valuedOn: string; value: number; note?: string | null; }

export interface PaymentCandidate { transactionId: number; date: string; description: string; category?: string | null; amount: Money; }

// Principal entries move the money; fee entries record a charge taken along the way (kept visible, never netted)
export type EntryRole = 'Principal' | 'Fee';

export interface Entry { accountId: number; accountName: string; fundId: number; fundName: string; amount: number; currency: string; role: EntryRole; }

export interface Transaction {
  id: number;
  type: TransactionType;
  date: string;
  recordedAt: string;
  description: string;
  reference?: string | null;
  category?: string | null;
  exchangeRate?: number | null;   // effective: calculated from the amounts sent and received
  quotedRate?: number | null;     // as stated by the provider, entered by the operator; reference only
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
  // Only transactions touching a Bank or Cash account: the scope of the overview's figures
  cash?: boolean;
  page: number; pageSize: number;
}

// The Executive Overview's period figures (Phase 6). Summed from bank and cash ledger entries by transaction type;
// each line is the ledger list for that type and period. Net cash movement is cash movement, not profit.
export interface CashLine { type: TransactionType; label: string; amount: number; transactions: number; gross?: number | null; fees?: number | null; }
export interface CurrencyPeriod {
  currency: string; cashAtStart: number; movements: CashLine[]; netCashMovement: number; openingBalances: CashLine; cashAtEnd: number;
}
export interface MonthMovement { start: string; end: string; currency: string; income: number; expenses: number; netCashMovement: number; transactions: number; }
export interface PayoutsReceived { currency: string; credited: number; payouts: number; }
export interface Overview {
  from: string; to: string; currencies: CurrencyPeriod[]; months: MonthMovement[]; monthsTruncated: boolean; payoutsReceived: PayoutsReceived[];
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

// ---- Payouts and allocation (Phase 5) ----

export type PayoutStatus = 'Requested' | 'Approved' | 'Received' | 'Rejected';
export type AllocationStatus = 'Confirmed' | 'PartiallySettled' | 'Reconciled' | 'Cancelled';
export type SettlementMethod = 'Earmark' | 'Transfer' | 'Exchange';
export type FeeChargedTo = 'Sending' | 'Receiving';

export interface Payout {
  id: number; source: string; reference?: string | null; notes?: string | null; status: PayoutStatus;
  requested: Money; requestedOn: string;
  approved?: Money | null; approvedOn?: string | null;
  receivedOn?: string | null; receivedIntoAccountId?: number | null; receivedIntoAccountName?: string | null;
  credited?: Money | null; fee?: Money | null; effectiveRate?: number | null; incomeTransactionId?: number | null;
  rejectedOn?: string | null; rejectionReason?: string | null; createdAt: string;
  allocationId?: number | null; allocationStatus?: AllocationStatus | null;
}

export interface PolicyLine { fundId: number; fundName: string; percentage: number; }
export interface AllocationPolicy { id: number; version: number; isActive: boolean; createdAt: string; activatedAt?: string | null; lines: PolicyLine[]; }

export interface AllocationPreview {
  payoutId: number; policyId: number; policyVersion: number; sourceAccountId: number; sourceAccountName: string;
  total: Money; lines: { fundId: number; fundName: string; percentage: number; amount: Money }[];
}

export interface Settlement { id: number; transactionId: number; method: SettlementMethod; amount: Money; recordedAt: string; reversed: boolean; }

export interface AllocationLine {
  id: number; fundId: number; fundName: string; percentage: number;
  amount: Money; settled: Money; outstanding: Money; settlements: Settlement[];
}

export interface Allocation {
  id: number; payoutId: number; payoutSource: string; policyId: number; policyVersion: number;
  sourceAccountId: number; sourceAccountName: string; status: AllocationStatus;
  total: Money; settled: Money; outstanding: Money;
  confirmedAt: string; cancelledAt?: string | null; cancellationReason?: string | null;
  lines: AllocationLine[];
}

// One meaning per status everywhere it appears (docs/DESIGN_SYSTEM.md)
export const PAYOUT_STATUS: Record<PayoutStatus, { label: string; tag: string }> = {
  Requested: { label: 'Requested', tag: 'tag' },
  Approved: { label: 'Approved', tag: 'tag tag-info' },
  Received: { label: 'Received', tag: 'tag tag-positive' },
  Rejected: { label: 'Rejected', tag: 'tag tag-negative' }
};

export const ALLOCATION_STATUS: Record<AllocationStatus, { label: string; tag: string }> = {
  Confirmed: { label: 'Confirmed', tag: 'tag tag-info' },
  PartiallySettled: { label: 'Partially settled', tag: 'tag tag-warning' },
  Reconciled: { label: 'Reconciled', tag: 'tag tag-positive' },
  Cancelled: { label: 'Cancelled', tag: 'tag' }
};
