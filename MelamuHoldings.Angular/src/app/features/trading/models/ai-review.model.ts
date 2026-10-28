// The optional AI-drafted review. A draft is never saved by the API: the trader edits it and saves it as their review.

export interface AiReviewStatus { enabled: boolean; model?: string | null; maxComparableTrades: number; maxJournalTrades: number; }

export interface AiFact { text: string; tradeIds: number[]; }

// Figures calculated by the app, never by the model
export interface AiMetric { label: string; value: string; basis: string; }

export interface AiReviewDraft {
  sufficientData: boolean;
  missing: string[];
  facts: AiFact[];
  metrics: AiMetric[];
  interpretations: string[];
  reflectionPrompts: string[];
  suggestedReview?: string | null;
  comparableTradeIds: number[];
  model?: string | null;
  generatedAt: string;
  warnings: string[];
}

// An answer about the closed trades in an analytics view. Never saved; its figures come from the app.
export interface AiJournalAnswer {
  sufficientData: boolean;
  question: string;
  answer?: string | null;
  missing: string[];
  facts: AiFact[];
  metrics: AiMetric[];
  interpretations: string[];
  reflectionPrompts: string[];
  tradeIds: number[];
  tradesInView: number;
  from?: string | null;
  to?: string | null;
  model?: string | null;
  generatedAt: string;
  warnings: string[];
}

// Examples from the journal brief. They are starting points, not promises that the data can answer them.
export const SUGGESTED_QUESTIONS = [
  'Which rules have I struggled to follow?',
  'What patterns appear in my recent journal entries?',
  'How does my execution differ between winning and losing trades?',
  'Compare my recent rule adherence with my previous period.',
  'Summarise my last trading session.',
  'Is there any sign of revenge trading in these trades?'
];
