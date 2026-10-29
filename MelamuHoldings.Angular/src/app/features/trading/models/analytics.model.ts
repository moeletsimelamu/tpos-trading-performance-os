export interface AnalyticsSummary {
  totalTrades: number;
  winRate: number;
  expectancy: number;
  averageRR: number;
  profitFactor: number;
  totalPnL: number;
  maxDrawdown: number;
  winCount: number;
  lossCount: number;
  breakevenCount: number;
  // Below minimumSample trades the figures are labelled as insufficient data
  minimumSample: number;
  insufficientData: boolean;
}

export interface EquityPoint {
  date: string;
  cumulativePnL: number;
  dailyPnL: number;
  drawdown: number;
}

export interface StrategyPerformance {
  strategyName: string;
  totalTrades: number;
  winRate: number;
  averageRR: number;
  totalPnL: number;
  profitFactor: number;
}

export interface TimePerformance {
  label: string;
  totalTrades: number;
  winRate: number;
  totalPnL: number;
  averageRR: number;
}

export interface BehaviourAlert {
  alertType: string;
  severity: string;
  description: string;
  tradeIds: number[];
  detectedOnDate: string;
}

export interface BehaviourSummary {
  alerts: BehaviourAlert[];
  revengeTradingCount: number;
  overtradingDayCount: number;
  maxConsecutiveLosses: number;
  strategyDeviationCount: number;
  deviationPnLImpact: number;
}

export interface UploadResult {
  uploadLogId: number;
  fileName: string;
  totalRows: number;
  parsedRows: number;
  failedRows: number;
  // Not imported: already imported before, or matching a trade journaled by hand (waiting for a decision)
  duplicateRows: number;
  matchedRows: number;
  status: string;
  errors: string[];
}