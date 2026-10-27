// Property names follow the API's System.Text.Json camelCase output: C# `RR` arrives as `rr`, `PnL` as `pnL`.
export interface Trade {
  id: number;
  tradeDate: string;
  entryTime: string;
  instrument: string;
  direction: 'Long' | 'Short';
  entryPrice: number;
  exitPrice: number;
  riskAmount: number;
  pnL: number;
  rr: number;
  strategyName?: string;
  session: string;
  dayOfWeek: string;
  tradeDuration: number;
  isManualOverride: boolean;
  deviationNotes?: string;
  notes?: string;
}

export interface CreateTrade {
  tradeDate: string;
  entryTime: string;
  instrument: string;
  direction: string;
  entryPrice: number;
  exitPrice: number;
  riskAmount: number;
  pnL: number;
  rr: number;
  strategyName?: string;
  session: string;
  dayOfWeek: string;
  tradeDuration: number;
  isManualOverride: boolean;
  deviationNotes?: string;
  notes?: string;
}
