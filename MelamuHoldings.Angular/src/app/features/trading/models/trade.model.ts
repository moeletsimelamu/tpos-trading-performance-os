import { JournalAnswers, TradeJournal } from './journal.model';
import { RuleAnswer, TradeChecklist } from './strategy.model';
import { TradeAttachment } from './journal-view.model';

// Property names follow the API's System.Text.Json camelCase output: C# `RR` arrives as `rr`, `PnL` as `pnL`.

// Draft → Open → Closed → Reviewed. Only Closed and Reviewed trades count in analytics.
export type TradeStatus = 'Draft' | 'Open' | 'Closed' | 'Reviewed';

// Execution values are null until recorded: a draft never carries placeholder zeros
export interface Trade {
  id: number;
  status: TradeStatus;
  isImported: boolean;
  tradeDate: string;
  instrument: string;
  tradingAccount?: string | null;
  direction?: 'Long' | 'Short' | null;
  entryTime?: string | null;
  entryPrice?: number | null;
  stopLoss?: number | null;
  takeProfit?: number | null;
  positionSize?: number | null;
  riskAmount?: number | null;
  exitDate?: string | null;
  exitTime?: string | null;
  exitPrice?: number | null;
  pnL?: number | null;
  fees?: number | null;
  rr?: number | null;
  currency?: string | null;
  strategyName?: string | null;
  session: string;
  dayOfWeek: string;
  tradeDuration?: number | null;
  isManualOverride: boolean;
  deviationNotes?: string | null;
  notes?: string | null;
  reviewNotes?: string | null;
  reviewedAt?: string | null;
  createdAt?: string;
  updatedAt?: string | null;
  journal?: TradeJournal | null;   // included when one trade is read
  checklist?: TradeChecklist | null;   // likewise: the checklist for the trade's strategy
  // From the checklist of the trade's strategy; included in lists. Absent when there is no checklist.
  ruleAdherence?: 'Followed' | 'Broke' | 'Unfinished' | null;
  attachments?: TradeAttachment[] | null;   // likewise: files kept with the trade
}

export type SaveStatus = 'Draft' | 'Open' | 'Closed';

// One saved change to a trade: each value before and after, as text. Null means it was empty.
export interface FieldChange {
  field: string;
  label: string;
  before: string | null;
  after: string | null;
}

export type RevisionKind = 'Created' | 'Edited' | 'Reviewed' | 'ImportLinked' | 'FileAdded' | 'FileRemoved';

export interface TradeRevision {
  id: number;
  occurredAt: string;
  kind: RevisionKind;
  status: TradeStatus;   // after the change
  changes: FieldChange[];
}

export function revisionTitle(r: TradeRevision): string {
  switch (r.kind) {
    case 'Created': return r.status === 'Draft' ? 'Created as a draft' : `Created as ${r.status === 'Open' ? 'an open' : 'a closed'} trade`;
    case 'Edited': return 'Edited';
    // The first review changes the status; a later one corrects the wording
    case 'Reviewed': return r.changes.some(c => c.field === 'Status') ? 'Marked reviewed' : 'Review corrected';
    case 'ImportLinked': return 'Filled in from a CSV import';
    case 'FileAdded': return 'File added';
    case 'FileRemoved': return 'File removed';
  }
}

// What the journal form sends. Status is the one the trader saves as; omitted, the trade keeps its status
// (used for a reviewed trade, which reaches Reviewed only through its own action).
export interface SaveTrade {
  status?: SaveStatus;
  tradeDate: string;
  instrument: string;
  tradingAccount?: string | null;
  direction?: string | null;
  entryTime?: string | null;
  entryPrice?: number | null;
  stopLoss?: number | null;
  takeProfit?: number | null;
  positionSize?: number | null;
  riskAmount?: number | null;
  exitDate?: string | null;
  exitTime?: string | null;
  exitPrice?: number | null;
  pnL?: number | null;
  fees?: number | null;
  rr?: number | null;
  currency?: string | null;
  strategyName?: string | null;
  session: string;
  isManualOverride: boolean;
  deviationNotes?: string | null;
  notes?: string | null;
  // Answers to a template version's questions; omitted, the trade's answers are left as they are
  journal?: { templateVersionId: number; answers: JournalAnswers };
  // Answers to the strategy's rules; omitted, the checklist is left as it is
  checklist?: { ruleId: number; answer: RuleAnswer }[];
}

// The fields each status needs; mirrors Trade.MissingFor on the server, which has the final say
export const REQUIRED_FOR: Record<SaveStatus, (keyof SaveTrade)[]> = {
  Draft: ['tradeDate', 'instrument'],
  Open: ['tradeDate', 'instrument', 'direction', 'entryTime', 'entryPrice'],
  Closed: ['tradeDate', 'instrument', 'direction', 'entryTime', 'entryPrice', 'exitPrice', 'pnL']
};

export const TRADE_STATUS: Record<TradeStatus, { label: string; tag: string }> = {
  Draft: { label: 'Draft', tag: 'tag' },
  Open: { label: 'Open', tag: 'tag tag-info' },
  Closed: { label: 'Closed', tag: 'tag tag-warning' },
  Reviewed: { label: 'Reviewed', tag: 'tag tag-positive' }
};
