import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { startWith } from 'rxjs';
import { TradingApiService } from '../trading-api.service';
import { REQUIRED_FOR, SaveStatus, SaveTrade, Trade } from '../models/trade.model';
import { JournalAnswers, JournalSection, JournalTemplate, carryAnswers, isAnswered } from '../models/journal.model';
import { JournalFieldsComponent } from './journal-fields.component';
import { RULE_ANSWERS, RuleAnswer, RuleCategory, Strategy, groupByCategory } from '../models/strategy.model';
import { newIdempotencyKey, todayIso } from '../../finance/finance-api.service';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';

// Readable names for the fields a status can ask for
const LABELS: Partial<Record<keyof SaveTrade, string>> = {
  tradeDate: 'trade date', instrument: 'instrument', direction: 'direction', entryTime: 'entry time',
  entryPrice: 'entry price', exitPrice: 'exit price', pnL: 'realised P&L'
};

const SESSIONS = ['Asian', 'London', 'New York'];
const CURRENCIES = ['USD', 'ZAR', 'EUR', 'GBP'];

// A value that, when given, must be above zero (prices, size)
function positive(control: AbstractControl): ValidationErrors | null {
  const v = control.value;
  return v === null || v === '' || Number(v) > 0 ? null : { positive: 'Must be greater than zero.' };
}

function notNegative(control: AbstractControl): ValidationErrors | null {
  const v = control.value;
  return v === null || v === '' || Number(v) >= 0 ? null : { positive: 'Cannot be negative.' };
}

@Component({
  selector: 'app-trade-form',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, LoadErrorComponent, PageHeaderComponent, JournalFieldsComponent],
  templateUrl: './trade-form.component.html',
  styles: [`
    .section { padding: var(--space-5); }
    .section + .section { border-top: 1px solid var(--border-subtle); }
    .section h2 { font-size: var(--text-md); margin-bottom: var(--space-1); }
    .section > p { color: var(--text-secondary); font-size: var(--text-sm); margin-bottom: var(--space-4); }
    .check { flex-direction: row; align-items: center; gap: var(--space-2); }
    .check input { height: auto; }
    .wide { grid-column: 1 / -1; }
    .save-bar { display: flex; flex-wrap: wrap; gap: var(--space-2); align-items: center; padding: var(--space-4) var(--space-5); border-top: 1px solid var(--border-subtle); background: var(--surface-sunken); border-radius: 0 0 var(--radius-lg) var(--radius-lg); }
    .save-help { flex-basis: 100%; color: var(--text-muted); font-size: var(--text-xs); }
    textarea { min-height: 96px; }
    .picker { display: flex; flex-wrap: wrap; gap: var(--space-3); align-items: end; margin-bottom: var(--space-5); }
    .picker .field { min-width: 240px; }
    .rules { display: grid; gap: var(--space-2); }
    .rule-group { font-size: var(--text-sm); color: var(--text-secondary); margin: var(--space-3) 0 0; }
    .scope-note { margin: var(--space-2) 0 0; }
    .rule { display: flex; flex-wrap: wrap; justify-content: space-between; gap: var(--space-2) var(--space-4); align-items: center; padding: var(--space-2) 0; border-top: 1px solid var(--border-subtle); }
    .rule-text { display: flex; gap: var(--space-2); align-items: baseline; flex: 1 1 240px; }
    .answers { display: flex; gap: 2px; border: 1px solid var(--border); border-radius: var(--radius); padding: 2px; }
    .answers label { position: relative; display: flex; align-items: center; padding: 4px 10px; border-radius: var(--radius-sm); font-size: 13px; cursor: pointer; color: var(--text-secondary); }
    .answers input { position: absolute; inset: 0; opacity: 0; width: 100%; height: 100%; margin: 0; cursor: pointer; }
    .answers label:has(input:checked) { background: var(--primary); color: var(--text-inverse); }
    .answers label:has(input:focus-visible) { box-shadow: var(--focus-ring); }
  `]
})
export class TradeFormComponent {
  private api = inject(TradingApiService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);
  private fb = inject(FormBuilder);

  readonly sessions = SESSIONS;
  readonly currencies = CURRENCIES;
  readonly editingId = this.route.snapshot.paramMap.get('id') ? Number(this.route.snapshot.paramMap.get('id')) : null;

  readonly loadState = signal<'loading' | 'ready' | 'error'>(this.editingId ? 'loading' : 'ready');
  readonly loadError = signal('');
  readonly original = signal<Trade | null>(null);
  readonly saving = signal(false);
  // Which button is saving, so only that one says so
  readonly savingAs = signal<SaveStatus | 'changes' | null>(null);
  readonly saveError = signal('');
  readonly missing = signal<string[]>([]);

  // The journal: which template version the answers belong to, and the answers themselves
  readonly templates = signal<JournalTemplate[]>([]);
  readonly templatesError = signal('');
  readonly journalTemplateId = signal<number | null>(null);
  readonly journalVersionId = signal<number | null>(null);
  readonly journalSections = signal<JournalSection[]>([]);
  readonly answers = signal<JournalAnswers>({});
  readonly dropped = signal<string[]>([]);
  // Set when the answers or the version change, so an untouched edit leaves the trade's journal alone
  private journalChanged = false;

  // The strategy checklist: the rules of the strategy named on the form, answered per rule
  readonly ruleAnswers = RULE_ANSWERS;
  readonly strategies = signal<Strategy[]>([]);
  readonly strategiesError = signal('');
  readonly checks = signal<Record<number, RuleAnswer>>({});
  private checklistChanged = false;

  // A checklist already started keeps its own copy of the rules; otherwise the strategy's active rules
  readonly checklistRules = computed<{ ruleId: number; text: string; isMandatory: boolean; category: RuleCategory }[]>(() => {
    const name = (this.value().strategyName ?? '').trim().toLowerCase();
    if (!name) return [];
    const existing = this.original()?.checklist;
    if (existing && existing.strategyName.toLowerCase() === name) return existing.items;
    const strategy = this.strategies().find(s => s.name.toLowerCase() === name);
    return strategy?.rules.map(r => ({ ruleId: r.id, text: r.text, isMandatory: r.isMandatory, category: r.category })) ?? [];
  });

  // Setup, entry, exit and risk rules under their own headings
  readonly checklistGroups = computed(() => groupByCategory(this.checklistRules()));

  // The trader's strategy named on the form, if it is one of theirs
  readonly namedStrategy = computed(() => {
    const name = (this.value().strategyName ?? '').trim().toLowerCase();
    return name ? this.strategies().find(s => s.name.toLowerCase() === name) ?? null : null;
  });

  // A trade outside the strategy's instruments or sessions is allowed; the form only says so
  readonly scopeNote = computed(() => {
    const s = this.namedStrategy();
    if (!s) return '';
    const v = this.value();
    const instrument = (v.instrument ?? '').trim();
    const session = (v.session ?? '').trim();
    const same = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();
    const outside: string[] = [];
    if (instrument && s.instruments.length > 0 && !s.instruments.some(i => same(i, instrument))) outside.push(`it is set for ${s.instruments.join(', ')}, not ${instrument}`);
    if (session && s.sessions.length > 0 && !s.sessions.some(x => same(x, session))) outside.push(`for the ${s.sessions.join(', ')} ${s.sessions.length === 1 ? 'session' : 'sessions'}, not ${session}`);
    return outside.length ? `${s.name}: ${outside.join('; ')}. Check this is the strategy you meant.` : '';
  });

  // The named strategy's journal template, when it has one that is still active
  readonly strategyTemplate = computed(() => {
    const id = this.namedStrategy()?.defaultTemplateId;
    return id ? this.templates().find(t => t.id === id && t.status === 'Active') ?? null : null;
  });
  readonly templateOffer = computed(() => {
    const t = this.strategyTemplate();
    return t && t.id !== this.journalTemplateId() ? t : null;
  });
  // Set when the form moved to the strategy's template by itself, so it can say so
  readonly followedStrategy = signal('');
  // The trader chose a template (on the form or from the templates page): the strategy no longer decides
  private templatePicked = false;

  readonly strategyKnown = computed(() => {
    const name = (this.value().strategyName ?? '').trim().toLowerCase();
    return this.strategies().some(s => s.name.toLowerCase() === name) || this.original()?.checklist?.strategyName.toLowerCase() === name;
  });

  // The checklist kept from the strategy the trade had before the name was changed
  readonly leftChecklist = computed(() => {
    const existing = this.original()?.checklist;
    const name = (this.value().strategyName ?? '').trim().toLowerCase();
    return existing && existing.strategyName.toLowerCase() !== name ? existing.strategyName : null;
  });

  // The template list for the picker; an entry's archived template stays listed so it can be kept
  readonly templateChoices = computed(() => {
    const list: { id: number; name: string }[] = this.templates();
    const j = this.original()?.journal;
    return j && !list.some(t => t.id === j.templateId) ? [...list, { id: j.templateId, name: `${j.templateName} (archived)` }] : list;
  });

  readonly olderVersion = computed(() => {
    const j = this.original()?.journal;
    return j && this.journalVersionId() === j.templateVersionId && j.version < j.latestVersion ? j : null;
  });

  // One key per intended new trade: kept across retries of the same values, renewed when they change
  private key = newIdempotencyKey();

  readonly form = this.fb.group({
    tradeDate: [todayIso(), Validators.required],
    instrument: ['', [Validators.required, Validators.maxLength(50)]],
    direction: [null as 'Long' | 'Short' | null],
    session: ['', Validators.maxLength(50)],
    strategyName: ['', Validators.maxLength(100)],
    tradingAccount: ['', Validators.maxLength(100)],
    currency: [null as string | null],
    entryTime: [null as string | null],
    entryPrice: [null as number | null, positive],
    stopLoss: [null as number | null, positive],
    takeProfit: [null as number | null, positive],
    positionSize: [null as number | null, positive],
    riskAmount: [null as number | null, notNegative],
    exitDate: [null as string | null],
    exitTime: [null as string | null],
    exitPrice: [null as number | null, positive],
    pnL: [null as number | null],
    fees: [null as number | null, notNegative],
    rr: [null as number | null],
    notes: ['', Validators.maxLength(2000)],
    isManualOverride: [false],
    deviationNotes: ['', Validators.maxLength(2000)]
  });

  private readonly value = toSignal(this.form.valueChanges.pipe(startWith(this.form.getRawValue())), { requireSync: true });

  // R is calculated on the server from P&L ÷ risk when left empty; show the same figure while typing
  readonly calculatedR = computed(() => {
    const v = this.value();
    return v.rr == null && v.pnL != null && Number(v.riskAmount) > 0 ? (Number(v.pnL) / Number(v.riskAmount)).toFixed(2) : null;
  });

  readonly isReviewed = computed(() => this.original()?.status === 'Reviewed');
  readonly title = computed(() => this.editingId ? `Edit ${this.original()?.instrument ?? 'trade'}` : 'New trade');

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (!this.saving()) this.key = newIdempotencyKey();
      this.missing.set([]);
    });
    if (this.editingId) this.load();
    this.loadTemplates();
    // A new trade naming a strategy starts with that strategy's journal template, as long as nothing has been
    // answered yet and no template was chosen; after that the form only offers it
    effect(() => {
      const t = this.strategyTemplate();
      untracked(() => {
        const blank = !Object.values(this.answers()).some(isAnswered);
        if (!t || this.editingId || this.templatePicked || this.journalVersionId() === null || t.id === this.journalTemplateId() || !blank) return;
        this.switchTo(t.id, t.currentVersionId, t.sections);
        this.followedStrategy.set(this.namedStrategy()!.name);
      });
    });
    this.loadStrategies();
  }

  loadStrategies(): void {
    this.strategiesError.set('');
    this.api.getStrategies().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: s => this.strategies.set(s),
      error: err => this.strategiesError.set(describeHttpError(err))
    });
  }

  answerRule(ruleId: number, answer: RuleAnswer): void {
    this.checks.update(c => ({ ...c, [ruleId]: answer }));
    this.checklistChanged = true;
    if (!this.saving()) this.key = newIdempotencyKey();
  }

  loadTemplates(): void {
    this.templatesError.set('');
    this.api.getTemplates().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: t => { this.templates.set(t); this.startJournal(); },
      error: err => this.templatesError.set(describeHttpError(err))
    });
  }

  // An existing entry keeps the version it was written with; otherwise the default template's latest version
  private startJournal(): void {
    if (this.journalVersionId() !== null || (this.editingId && this.loadState() !== 'ready')) return;
    const j = this.original()?.journal;
    if (j) {
      this.useVersion(j.templateId, j.templateVersionId, j.sections, j.answers);
      return;
    }
    // A new trade started from the templates page uses that template; otherwise the default
    const chosen = Number(this.route.snapshot.queryParamMap?.get('template'));
    this.templatePicked ||= this.templates().some(x => x.id === chosen);
    const t = this.templates().find(x => x.id === chosen) ?? this.templates().find(x => x.isDefault) ?? this.templates()[0];
    if (t) this.useVersion(t.id, t.currentVersionId, t.sections, {});
  }

  chooseTemplate(id: number): void {
    this.templatePicked = true;
    this.followedStrategy.set('');
    const j = this.original()?.journal;
    const t = this.templates().find(x => x.id === id);
    if (j && j.templateId === id) this.switchTo(j.templateId, j.templateVersionId, j.sections);
    else if (t) this.switchTo(t.id, t.currentVersionId, t.sections);
  }

  useLatest(): void {
    const j = this.original()?.journal;
    if (!j) return;
    this.api.getTemplate(j.templateId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: t => this.switchTo(t.id, t.currentVersionId, t.sections),
      error: err => this.saveError.set(`Could not load the latest version: ${describeHttpError(err)}`)
    });
  }

  onAnswers(answers: JournalAnswers): void {
    this.answers.set(answers);
    this.journalChanged = true;
    if (!this.saving()) this.key = newIdempotencyKey();
  }

  // Answers move with their question where it still exists with the same type; the rest are listed as dropped
  private switchTo(templateId: number, versionId: number, sections: JournalSection[]): void {
    if (versionId === this.journalVersionId()) return;
    const { kept, dropped } = carryAnswers(this.answers(), this.journalSections(), sections);
    this.useVersion(templateId, versionId, sections, kept);
    this.dropped.set(dropped);
    this.journalChanged = true;
    if (!this.saving()) this.key = newIdempotencyKey();
  }

  private useVersion(templateId: number, versionId: number, sections: JournalSection[], answers: JournalAnswers): void {
    this.journalTemplateId.set(templateId);
    this.journalVersionId.set(versionId);
    this.journalSections.set(sections);
    this.answers.set(answers);
    this.dropped.set([]);
  }

  load(): void {
    this.loadState.set('loading');
    this.api.getTrade(this.editingId!).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: t => {
        this.original.set(t);
        this.form.reset({
          tradeDate: t.tradeDate, instrument: t.instrument, direction: t.direction ?? null, session: t.session ?? '',
          strategyName: t.strategyName ?? '', tradingAccount: t.tradingAccount ?? '', currency: t.currency ?? null,
          entryTime: t.entryTime?.slice(0, 5) ?? null, entryPrice: t.entryPrice ?? null, stopLoss: t.stopLoss ?? null,
          takeProfit: t.takeProfit ?? null, positionSize: t.positionSize ?? null, riskAmount: t.riskAmount ?? null,
          exitDate: t.exitDate ?? null, exitTime: t.exitTime?.slice(0, 5) ?? null, exitPrice: t.exitPrice ?? null,
          pnL: t.pnL ?? null, fees: t.fees ?? null, rr: t.rr ?? null, notes: t.notes ?? '',
          isManualOverride: t.isManualOverride, deviationNotes: t.deviationNotes ?? ''
        });
        this.checks.set(Object.fromEntries((t.checklist?.items ?? []).filter(i => i.answer).map(i => [i.ruleId, i.answer!])));
        this.loadState.set('ready');
        this.startJournal();
      },
      error: err => { this.loadError.set(describeHttpError(err)); this.loadState.set('error'); }
    });
  }

  // Saving a reviewed trade keeps it reviewed (no status is sent); otherwise the button chosen sets the status
  save(status: SaveStatus | null): void {
    if (this.saving()) return;
    const body = this.body(status);
    const needed = REQUIRED_FOR[status ?? 'Closed'].filter(f => body[f] === null || body[f] === undefined || body[f] === '');
    if (this.form.invalid || needed.length > 0) {
      this.form.markAllAsTouched();
      this.missing.set(needed.map(f => LABELS[f] ?? f));
      return;
    }

    this.saving.set(true);
    this.savingAs.set(status ?? 'changes');
    this.saveError.set('');
    const request = this.editingId ? this.api.updateTrade(this.editingId, body) : this.api.createTrade(body, this.key);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: trade => {
        this.saving.set(false);
        this.router.navigate(['/trading/trades', trade.id]);
      },
      error: err => { this.saving.set(false); this.savingAs.set(null); this.saveError.set(`Not saved: ${describeHttpError(err)}`); }
    });
  }

  private body(status: SaveStatus | null): SaveTrade {
    const v = this.form.getRawValue();
    const versionId = this.journalVersionId();
    const journal = versionId !== null && (!this.editingId || this.journalChanged) ? { templateVersionId: versionId, answers: this.answers() } : undefined;
    // Only answers to the rules now shown; nothing is sent unless the checklist was touched
    const checks = this.checks();
    const checklist = this.checklistChanged
      ? this.checklistRules().filter(r => checks[r.ruleId]).map(r => ({ ruleId: r.ruleId, answer: checks[r.ruleId] }))
      : [];
    const num = (x: number | null | undefined) => (x === null || x === undefined || (x as unknown) === '' ? null : Number(x));
    const text = (x: string | null | undefined) => (x?.trim() ? x.trim() : null);
    return {
      status: status ?? undefined,
      tradeDate: v.tradeDate!, instrument: v.instrument!.trim(), direction: v.direction,
      session: v.session?.trim() ?? '', strategyName: text(v.strategyName), tradingAccount: text(v.tradingAccount),
      currency: v.currency, entryTime: v.entryTime || null, entryPrice: num(v.entryPrice), stopLoss: num(v.stopLoss),
      takeProfit: num(v.takeProfit), positionSize: num(v.positionSize), riskAmount: num(v.riskAmount),
      exitDate: v.exitDate || null, exitTime: v.exitTime || null, exitPrice: num(v.exitPrice), pnL: num(v.pnL),
      fees: num(v.fees), rr: num(v.rr), notes: text(v.notes), isManualOverride: !!v.isManualOverride,
      deviationNotes: v.isManualOverride ? text(v.deviationNotes) : null,
      journal,
      checklist: checklist.length > 0 ? checklist : undefined
    };
  }
}
