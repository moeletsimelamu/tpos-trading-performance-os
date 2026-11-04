export interface MoneyAmount {
  amount: number;
  currency: string;
}

export type PropAccountPhase = 'Evaluation' | 'Verification' | 'Funded';
export type PropAccountStatus = 'Active' | 'Pending' | 'Breached' | 'Closed';
export type PropAccountEventKind = 'Opened' | 'PhaseChanged' | 'StatusChanged' | 'Reset' | 'RulesChanged' | 'DetailsChanged' | 'BalanceRecorded' | 'BalanceRemoved';
export type DrawdownModel = 'None' | 'Static' | 'TrailingEndOfDay' | 'TrailingIntraday';

export const PHASES: PropAccountPhase[] = ['Evaluation', 'Verification', 'Funded'];

export type PropAccountFeeKind = 'Purchase' | 'Reset' | 'Monthly' | 'Other';
export type PropAccountOutcome = 'FeesWithoutPayouts' | 'PayoutsReceived' | 'NothingLinked';

export const OUTCOME_LABELS: Record<PropAccountOutcome, { label: string; tag: string }> = {
  FeesWithoutPayouts: { label: 'Fees, no payout received', tag: 'tag tag-warning' },
  PayoutsReceived: { label: 'Payouts received', tag: 'tag tag-positive' },
  NothingLinked: { label: 'Nothing linked', tag: 'tag' }
};

export interface PropAccountCurrencyLine { currency: string; fees: number; payoutsReceived: number; net: number; }
export interface PropAccountEconomics { outcome: PropAccountOutcome; lines: PropAccountCurrencyLine[]; awaiting: MoneyAmount[]; feesCounted: number; payoutsCounted: number; }
export interface PropAccountFee { id: number; kind: PropAccountFeeKind; transactionId: number; date: string; description: string; category?: string | null; amount: MoneyAmount; reversed: boolean; }
export interface PropAccountPayout { id: number; source: string; status: string; amount: MoneyAmount; requestedOn: string; receivedOn?: string | null; credited?: MoneyAmount | null; }
export interface PropAccountLedger { economics: PropAccountEconomics; fees: PropAccountFee[]; payouts: PropAccountPayout[]; }
export interface PropAccountFeeCandidate { transactionId: number; date: string; description: string; category?: string | null; amount: MoneyAmount; }
export interface PropAccountReportRow { accountId: number; firm: string; nickname: string; phase: PropAccountPhase; status: PropAccountStatus; economics: PropAccountEconomics; }
export interface PropAccountReport { accounts: PropAccountReportRow[]; totals: PropAccountCurrencyLine[]; unlinkedReceivedPayouts: number; }

export const STATUS_TAGS: Record<PropAccountStatus, string> = {
  Active: 'tag tag-positive',
  Pending: 'tag tag-warning',
  Breached: 'tag tag-negative',
  Closed: 'tag'
};

export const DRAWDOWN_MODELS: { value: DrawdownModel; label: string }[] = [
  { value: 'None', label: 'No drawdown limit' },
  { value: 'Static', label: 'Static (from the starting balance)' },
  { value: 'TrailingEndOfDay', label: 'Trailing, end of day' },
  { value: 'TrailingIntraday', label: 'Trailing, intraday' }
];

export function drawdownLabel(model: DrawdownModel): string {
  return DRAWDOWN_MODELS.find(m => m.value === model)!.label;
}

export const EVENT_LABELS: Record<PropAccountEventKind, string> = {
  Opened: 'Opened',
  PhaseChanged: 'Phase changed',
  StatusChanged: 'Status changed',
  Reset: 'Reset',
  RulesChanged: 'Rules changed',
  DetailsChanged: 'Details corrected',
  BalanceRecorded: 'Balance recorded',
  BalanceRemoved: 'Balance removed'
};

// Limits are in the account's currency; empty means the firm sets none
export interface PropAccountRules {
  drawdownModel: DrawdownModel;
  maxDrawdown: number | null;
  trailingLocksAtStart: boolean;
  dailyLossLimit: number | null;
  profitTarget: number | null;
  minimumTradingDays: number | null;
  payoutSplitPercent: number | null;
  purchaseFee: number | null;
  resetFee: number | null;
  monthlyFee: number | null;
  notes: string | null;
}

export interface PropAccountRuleSet {
  version: number;
  phase: PropAccountPhase;
  effectiveFrom: string;
  rules: PropAccountRules;
  createdAt: string;
}

export interface PropAccountEvent {
  id: number;
  kind: PropAccountEventKind;
  occurredOn: string;
  phase: PropAccountPhase;
  status: PropAccountStatus;
  login?: string | null;
  startingBalance?: MoneyAmount | null;
  note?: string | null;
  recordedAt: string;
}

export type RuleStanding = 'Clear' | 'Near' | 'Reached';

export const RULE_STANDING: Record<RuleStanding, { label: string; tag: string }> = {
  Clear: { label: 'Clear', tag: 'tag tag-positive' },
  Near: { label: 'Near the limit', tag: 'tag tag-warning' },
  Reached: { label: 'At the limit', tag: 'tag tag-negative' }
};

// The app's figures for the current run, from linked closed trades and balances entered. Not the firm's figures.
export interface PropAccountFigures {
  balance: number;
  profit: number;
  tradesCounted: number;
  tradingDays: number;
  drawdown?: { model: DrawdownModel; limit: number; level: number; room: number; standing: RuleStanding } | null;
  dailyLoss?: {
    limit: number; latestDay?: string | null; latestDayPnL?: number | null; worstDay?: string | null; worstLoss?: number | null;
    daysPastLimit: string[]; standing: RuleStanding;
  } | null;
  target?: { target: number; profit: number; remaining: number; reached: boolean } | null;
  minimumDays?: { required: number; done: number; remaining: number; reached: boolean } | null;
  targetMet: boolean;
}

export interface PropAccountStanding {
  figures: PropAccountFigures;
  linkedTrades: number;
  openTrades: number;
  tradesBeforeRun: number;
}

// Expected and difference: what the recorded trades came to on that date, for a balance in the current run
export interface PropAccountBalance {
  id: number;
  asOf: string;
  balance: MoneyAmount;
  drawdownLevel?: MoneyAmount | null;
  note?: string | null;
  expected?: number | null;
  difference?: number | null;
  recordedAt: string;
}

export interface PropAccount {
  id: number;
  firm: string;
  nickname: string;
  accountSize: MoneyAmount;
  phase: PropAccountPhase;
  status: PropAccountStatus;
  login?: string | null;
  notes?: string | null;
  openedOn: string;
  runStartedOn: string;
  startingBalance: MoneyAmount;
  rules: PropAccountRuleSet;
  ruleHistory: PropAccountRuleSet[];
  history: PropAccountEvent[];
  standing: PropAccountStanding;
  balances: PropAccountBalance[];
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreatePropAccount {
  firm: string;
  nickname: string;
  accountSize: number;
  currency: string;
  phase: PropAccountPhase;
  login: string | null;
  openedOn: string;
  rules: PropAccountRules;
  notes: string | null;
}

export interface UpdatePropAccount {
  firm: string;
  nickname: string;
  accountSize: number;
  login: string | null;
  notes: string | null;
}
