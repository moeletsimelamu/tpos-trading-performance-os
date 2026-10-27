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
  status: string;
  errors: string[];
}