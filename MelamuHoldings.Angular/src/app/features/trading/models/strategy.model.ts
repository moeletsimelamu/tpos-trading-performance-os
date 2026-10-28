// A trader's strategies, their rules, a trade's checklist against them, and the adherence report.
// Only a mandatory rule that was not met is a violation; optional rules are informational.

export type RuleAnswer = 'Met' | 'NotMet' | 'NotApplicable';

export interface StrategyRule { id: number; text: string; isMandatory: boolean; sortOrder: number; retiredAt?: string | null; }

export interface Strategy {
  id: number;
  name: string;
  description?: string | null;
  status: 'Active' | 'Archived';
  rules: StrategyRule[];
  retiredRules: StrategyRule[];
  tradeCount: number;
  createdAt: string;
  updatedAt?: string | null;
}

export interface SaveStrategy {
  name: string;
  description?: string | null;
  rules: { id?: number | null; text: string; isMandatory: boolean }[];
}

export interface ChecklistItem { ruleId: number; text: string; isMandatory: boolean; answer?: RuleAnswer | null; answeredAt?: string | null; }
export interface ChecklistChange { ruleId: number; text: string; sequence: number; answer: RuleAnswer; answeredAt: string; }

export interface TradeChecklist {
  strategyId: number;
  strategyName: string;
  createdAt: string;
  items: ChecklistItem[];
  isComplete: boolean;
  isCompliant?: boolean | null;
  violations: string[];
  history: ChecklistChange[];
}

export interface AdherenceGroup { trades: number; wins: number; netPnL: number; winRate?: number | null; averageR?: number | null; insufficientData: boolean; }
export interface RuleViolation { ruleId: number; strategyName: string; text: string; timesNotMet: number; timesAnswered: number; }
export interface StrategyAdherence { strategyId: number; strategyName: string; completedTrades: number; withChecklist: number; compliant: number; adherenceRate?: number | null; }

export interface AdherenceReport {
  completedTrades: number;
  withChecklist: number;
  incompleteChecklist: number;
  withoutChecklist: number;
  compliant: number;
  nonCompliant: number;
  adherenceRate?: number | null;
  minimumSample: number;
  compliantResults: AdherenceGroup;
  nonCompliantResults: AdherenceGroup;
  mostViolated: RuleViolation[];
  byStrategy: StrategyAdherence[];
}

export const RULE_ANSWERS: { value: RuleAnswer; label: string }[] = [
  { value: 'Met', label: 'Met' },
  { value: 'NotMet', label: 'Not met' },
  { value: 'NotApplicable', label: 'N/A' }
];

export function answerLabel(answer: RuleAnswer | null | undefined): string {
  return RULE_ANSWERS.find(a => a.value === answer)?.label ?? 'Not answered';
}
