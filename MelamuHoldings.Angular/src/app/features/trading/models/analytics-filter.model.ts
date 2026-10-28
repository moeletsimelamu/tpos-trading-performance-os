import { HttpParams } from '@angular/common/http';
import { ParamMap } from '@angular/router';
import { JournalFieldType } from './journal.model';

// One filter for every analytics figure. It only narrows the trades; the calculations never change.
export type AdherenceFilter = 'Followed' | 'Broke' | 'NotScored';

export interface AnalyticsFilter {
  from?: string;
  to?: string;
  strategyId?: number;
  instrument?: string;
  session?: string;
  account?: string;
  adherence?: AdherenceFilter;
  answerField?: string;
  answerValue?: string;
}

export interface FilterOption { value: string; label: string; }
export interface JournalFilterField { id: string; label: string; type: JournalFieldType; options: FilterOption[]; }

export interface AnalyticsFilterOptions {
  strategies: FilterOption[];
  instruments: string[];
  sessions: string[];
  accounts: string[];
  journalFields: JournalFilterField[];
}

export const ADHERENCE_FILTERS: { value: AdherenceFilter; label: string }[] = [
  { value: 'Followed', label: 'Followed every mandatory rule' },
  { value: 'Broke', label: 'Broke a mandatory rule' },
  { value: 'NotScored', label: 'No complete checklist' }
];

const KEYS: (keyof AnalyticsFilter)[] = ['from', 'to', 'strategyId', 'instrument', 'session', 'account', 'adherence', 'answerField', 'answerValue'];
const DATE = /^\d{4}-\d{2}-\d{2}$/;

export function isFilterEmpty(filter: AnalyticsFilter): boolean {
  return KEYS.every(k => filter[k] === undefined || filter[k] === '');
}

// The same names the API takes, and the page address uses, so a filtered view can be bookmarked
export function filterToQuery(filter: AnalyticsFilter): Record<string, string> {
  const query: Record<string, string> = {};
  for (const k of KEYS) {
    const v = filter[k];
    if (v !== undefined && v !== '') query[k] = String(v);
  }
  // A question without an answer (or the reverse) filters nothing
  if (!query['answerField'] || !query['answerValue']) { delete query['answerField']; delete query['answerValue']; }
  return query;
}

export function filterParams(filter: AnalyticsFilter = {}): HttpParams {
  return new HttpParams({ fromObject: filterToQuery(filter) });
}

// Reads a filter from the address, ignoring anything malformed rather than sending it on
export function filterFromQuery(query: ParamMap): AnalyticsFilter {
  const text = (k: string) => query.get(k)?.trim() || undefined;
  const filter: AnalyticsFilter = {
    from: DATE.test(query.get('from') ?? '') ? query.get('from')! : undefined,
    to: DATE.test(query.get('to') ?? '') ? query.get('to')! : undefined,
    strategyId: /^\d+$/.test(query.get('strategyId') ?? '') ? Number(query.get('strategyId')) : undefined,
    instrument: text('instrument'),
    session: text('session'),
    account: text('account'),
    adherence: ADHERENCE_FILTERS.some(a => a.value === query.get('adherence')) ? query.get('adherence') as AdherenceFilter : undefined,
    answerField: text('answerField'),
    answerValue: text('answerValue')
  };
  if (!filter.answerField || !filter.answerValue) { filter.answerField = undefined; filter.answerValue = undefined; }
  return Object.fromEntries(Object.entries(filter).filter(([, v]) => v !== undefined)) as AnalyticsFilter;
}

// Plain words for what is applied, e.g. ['From 1 Oct 2026', 'London', 'Mood: Calm']
export function describeFilter(filter: AnalyticsFilter, options: AnalyticsFilterOptions | null): string[] {
  const day = (d: string) => new Date(d + 'T00:00:00Z').toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
  const parts: string[] = [];
  if (filter.from && filter.to) parts.push(`${day(filter.from)} to ${day(filter.to)}`);
  else if (filter.from) parts.push(`From ${day(filter.from)}`);
  else if (filter.to) parts.push(`Until ${day(filter.to)}`);
  if (filter.strategyId) parts.push(options?.strategies.find(s => s.value === String(filter.strategyId))?.label ?? `Strategy ${filter.strategyId}`);
  if (filter.instrument) parts.push(filter.instrument);
  if (filter.session) parts.push(filter.session);
  if (filter.account) parts.push(filter.account);
  if (filter.adherence) parts.push(ADHERENCE_FILTERS.find(a => a.value === filter.adherence)!.label);
  if (filter.answerField && filter.answerValue) {
    const field = options?.journalFields.find(f => f.id === filter.answerField);
    const answer = field?.options.find(o => o.value === filter.answerValue)?.label ?? filter.answerValue;
    const label = field?.label ?? filter.answerField;
    parts.push(label.endsWith('?') ? `${label} ${answer}` : `${label}: ${answer}`);
  }
  return parts;
}
