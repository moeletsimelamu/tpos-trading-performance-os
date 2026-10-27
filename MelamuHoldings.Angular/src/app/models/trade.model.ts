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
  rR: number;
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
  rR: number;
  strategyName?: string;
  session: string;
  dayOfWeek: string;
  tradeDuration: number;
  isManualOverride: boolean;
  deviationNotes?: string;
  notes?: string;
}