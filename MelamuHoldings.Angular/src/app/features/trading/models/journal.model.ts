// Journal templates: numbered, immutable versions of questions. A trade's answers are tied to the version
// they were written against, so later edits to a template never change what an old entry says.

export type JournalFieldType =
  'ShortText' | 'LongText' | 'Number' | 'DateTime' | 'SingleSelect' | 'MultiSelect' | 'Checkbox' | 'Checklist' | 'Rating' | 'Url';

export interface JournalField {
  id: string;
  label: string;
  type: JournalFieldType;
  required?: boolean;
  help?: string | null;
  options?: string[] | null;
}

export interface JournalSection {
  id: string;
  title: string;
  description?: string | null;
  fields: JournalField[];
}

export interface JournalVersion { id: number; version: number; createdAt: string; entryCount: number; }

export interface JournalTemplate {
  id: number;
  name: string;
  description?: string | null;
  isDefault: boolean;
  status: 'Active' | 'Archived';
  currentVersionId: number;
  currentVersion: number;
  sections: JournalSection[];
  createdAt: string;
  updatedAt?: string | null;
  versions: JournalVersion[];
}

// An answer's shape follows its field type: text, number, choice(s), yes/no, or ticks per checklist item
export type JournalAnswer = string | number | boolean | string[] | Record<string, boolean>;
export type JournalAnswers = Record<string, JournalAnswer>;

export interface TradeJournal {
  templateId: number;
  templateName: string;
  templateVersionId: number;
  version: number;
  latestVersion: number;
  sections: JournalSection[];
  answers: JournalAnswers;
}

export const FIELD_TYPES: { type: JournalFieldType; label: string; hasOptions?: boolean }[] = [
  { type: 'ShortText', label: 'Short text' },
  { type: 'LongText', label: 'Long text' },
  { type: 'Number', label: 'Number' },
  { type: 'DateTime', label: 'Date and time' },
  { type: 'SingleSelect', label: 'Choose one', hasOptions: true },
  { type: 'MultiSelect', label: 'Choose several', hasOptions: true },
  { type: 'Checkbox', label: 'Yes / no' },
  { type: 'Checklist', label: 'Checklist', hasOptions: true },
  { type: 'Rating', label: 'Rating 1–5' },
  { type: 'Url', label: 'Web link' }
];

// True when an answer holds something; empty answers are not sent (the server drops them anyway)
export function isAnswered(value: JournalAnswer | undefined): boolean {
  if (value === undefined || value === null) return false;
  if (typeof value === 'string') return value.trim().length > 0;
  if (Array.isArray(value)) return value.length > 0;
  return true;
}

// Carries answers to another version where the question (same id) still exists with the same type.
// Returns what was kept and the labels of answers that have no place in the new version.
export function carryAnswers(answers: JournalAnswers, from: JournalSection[], to: JournalSection[]): { kept: JournalAnswers; dropped: string[] } {
  const target = new Map(to.flatMap(s => s.fields).map(f => [f.id, f]));
  const source = new Map(from.flatMap(s => s.fields).map(f => [f.id, f]));
  const kept: JournalAnswers = {};
  const dropped: string[] = [];
  for (const [id, value] of Object.entries(answers)) {
    if (!isAnswered(value)) continue;
    const before = source.get(id);
    const after = target.get(id);
    if (after && before && after.type === before.type) kept[id] = value;
    else dropped.push(before?.label ?? id);
  }
  return { kept, dropped };
}
