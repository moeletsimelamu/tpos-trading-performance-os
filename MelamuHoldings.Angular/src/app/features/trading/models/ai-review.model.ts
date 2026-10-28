// The optional AI-drafted review. A draft is never saved by the API: the trader edits it and saves it as their review.

export interface AiReviewStatus { enabled: boolean; model?: string | null; maxComparableTrades: number; }

export interface AiFact { text: string; tradeIds: number[]; }

// Figures calculated by the app, never by the model
export interface AiMetric { label: string; value: string; basis: string; }

export interface AiReviewDraft {
  sufficientData: boolean;
  missing: string[];
  facts: AiFact[];
  metrics: AiMetric[];
  interpretations: string[];
  suggestedReview?: string | null;
  comparableTradeIds: number[];
  model?: string | null;
  generatedAt: string;
  warnings: string[];
}
