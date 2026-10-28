import { TradeStatus } from './trade.model';

// The journal at a glance, the calendar, attachments, and CSV rows that matched journaled trades

export interface TradeSummary {
  id: number;
  status: TradeStatus;
  tradeDate: string;
  instrument: string;
  direction?: string | null;
  strategyName?: string | null;
  pnL?: number | null;
  rr?: number | null;
  isImported: boolean;
  lastChangedAt: string;
}

export interface JournalOverview {
  drafts: number;
  open: number;
  awaitingReview: number;
  reviewed: number;
  draftTrades: TradeSummary[];
  openTrades: TradeSummary[];
  awaitingReviewTrades: TradeSummary[];
  recent: TradeSummary[];
}

// P&L, wins and losses count closed and reviewed trades only; drafts and open trades are counted beside them
export interface CalendarDay { date: string; completedTrades: number; netPnL: number; wins: number; losses: number; drafts: number; open: number; }
export interface CalendarMonth { year: number; month: number; completedTrades: number; netPnL: number; days: CalendarDay[]; }

export interface TradeAttachment { id: number; fileName: string; contentType: string; sizeBytes: number; caption?: string | null; uploadedAt: string; }

export const ATTACHMENT_LIMITS = { maxBytes: 5 * 1024 * 1024, maxPerTrade: 10, accept: '.png,.jpg,.jpeg,.webp,.pdf,image/png,image/jpeg,image/webp,application/pdf' };

export type ImportMatchStatus = 'Pending' | 'Linked' | 'ImportedSeparately' | 'Dismissed';
export type MatchDecision = 'link' | 'import' | 'dismiss';

export interface ImportedRow {
  rowNumber: number; tradeDate: string; entryTime: string; instrument: string; direction: string;
  entryPrice: number; exitPrice: number; riskAmount: number; pnL: number; rr: number; strategy?: string | null; session: string;
}

export interface MatchedTrade {
  id: number; status: TradeStatus; tradeDate: string; instrument: string; direction?: string | null; entryTime?: string | null;
  entryPrice?: number | null; exitPrice?: number | null; pnL?: number | null; strategyName?: string | null;
}

export interface ImportMatch {
  id: number; uploadLogId: number; fileName: string; rowNumber: number; status: ImportMatchStatus;
  row: ImportedRow; trade?: MatchedTrade | null; createdAt: string; resolvedAt?: string | null; createdTradeId?: number | null;
}

export interface LinkResult { match: ImportMatch; filledFields: string[]; }

// "2026-10-05" → the month grid's weeks, Monday first; days outside the month are null
export function monthGrid(year: number, month: number): (string | null)[][] {
  const first = new Date(Date.UTC(year, month - 1, 1));
  const daysInMonth = new Date(Date.UTC(year, month, 0)).getUTCDate();
  const offset = (first.getUTCDay() + 6) % 7;
  const cells: (string | null)[] = Array(offset).fill(null);
  for (let d = 1; d <= daysInMonth; d++) cells.push(`${year}-${String(month).padStart(2, '0')}-${String(d).padStart(2, '0')}`);
  while (cells.length % 7 !== 0) cells.push(null);
  const weeks: (string | null)[][] = [];
  for (let i = 0; i < cells.length; i += 7) weeks.push(cells.slice(i, i + 7));
  return weeks;
}
