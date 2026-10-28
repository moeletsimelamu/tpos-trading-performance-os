import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink, ActivatedRoute } from '@angular/router';
import { TradingApiService } from '../trading-api.service';
import { AdherenceReport, SaveStrategy, Strategy } from '../models/strategy.model';
import { AdherencePanelComponent } from './adherence-panel.component';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';
import { IconComponent } from '../../../shared/ui/icon.component';

interface DraftRule { id: number | null; text: string; isMandatory: boolean; }

@Component({
  selector: 'app-strategy-editor',
  standalone: true,
  imports: [DatePipe, FormsModule, RouterLink, AdherencePanelComponent, LoadErrorComponent, PageHeaderComponent, IconComponent],
  templateUrl: './strategy-editor.component.html',
  styles: [`
    .card + .card, .card + section, section + .card { margin-top: var(--space-5); }
    .card h2 { font-size: var(--text-md); margin-bottom: var(--space-1); }
    .card > p { color: var(--text-secondary); font-size: var(--text-sm); margin-bottom: var(--space-4); }
    .rule { display: grid; grid-template-columns: 1fr auto; gap: var(--space-3); align-items: center; padding: var(--space-2) 0; border-top: 1px solid var(--border-subtle); }
    .tools { display: flex; gap: 2px; align-items: center; }
    .req { display: flex; gap: var(--space-2); align-items: center; font-size: 13px; margin-right: var(--space-2); white-space: nowrap; }
    .req input { height: auto; }
    .icon-btn { display: flex; padding: 6px; border: none; background: none; border-radius: var(--radius-sm); color: var(--text-secondary); cursor: pointer; }
    .icon-btn:hover:not(:disabled) { background: var(--surface-hover); color: var(--text-primary); }
    .icon-btn:disabled { opacity: 0.35; cursor: default; }
    .retired li { display: flex; justify-content: space-between; gap: var(--space-3); align-items: baseline; padding: var(--space-2) 0; border-top: 1px solid var(--border-subtle); }
    .retired ul { list-style: none; }
    .actions { display: flex; flex-wrap: wrap; gap: var(--space-2); margin-top: var(--space-4); }
    .narrow { max-width: 920px; }
    .adherence { margin-top: var(--space-6); }
    .adherence-title { font-size: var(--text-lg); margin-bottom: var(--space-3); }
    @media (max-width: 599px) { .rule { grid-template-columns: 1fr; } }
  `]
})
export class StrategyEditorComponent {
  private api = inject(TradingApiService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  readonly strategyId = this.route.snapshot.paramMap.get('id') ? Number(this.route.snapshot.paramMap.get('id')) : null;

  readonly state = signal<'loading' | 'ready' | 'error'>(this.strategyId ? 'loading' : 'ready');
  readonly error = signal('');
  readonly original = signal<Strategy | null>(null);
  readonly report = signal<AdherenceReport | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal('');
  readonly saved = signal('');

  name = '';
  description = '';
  rules: DraftRule[] = [{ id: null, text: '', isMandatory: true }];

  constructor() {
    if (this.strategyId) this.load();
  }

  load(): void {
    this.state.set('loading');
    this.api.getStrategy(this.strategyId!).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: s => { this.fill(s); this.state.set('ready'); this.loadAdherence(); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  // Adherence is extra: if it fails the editor still works
  private loadAdherence(): void {
    this.api.getAdherence({ strategyId: this.strategyId! }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: r => this.report.set(r),
      error: () => this.report.set(null)
    });
  }

  edited(): void {
    this.saved.set('');
  }

  addRule(): void {
    this.rules.push({ id: null, text: '', isMandatory: true });
    this.edited();
  }

  move(index: number, delta: number): void {
    const target = index + delta;
    if (target < 0 || target >= this.rules.length) return;
    [this.rules[index], this.rules[target]] = [this.rules[target], this.rules[index]];
    this.edited();
  }

  remove(index: number): void {
    this.rules.splice(index, 1);
    this.edited();
  }

  save(): void {
    if (this.saving()) return;
    const problem = this.check();
    if (problem) { this.saveError.set(problem); return; }

    const body: SaveStrategy = {
      name: this.name.trim(),
      description: this.description.trim() || null,
      rules: this.rules.map(r => ({ id: r.id, text: r.text.trim(), isMandatory: r.isMandatory }))
    };
    const retiring = this.original()?.rules.filter(r => !this.rules.some(d => d.id === r.id)).length ?? 0;
    this.saving.set(true);
    this.saveError.set('');
    this.api.saveStrategy(this.strategyId, body).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: s => {
        this.saving.set(false);
        if (!this.strategyId) { this.router.navigate(['/trading/strategies', s.id]); return; }
        this.fill(s);
        this.saved.set(retiring === 0 ? 'Saved.' : retiring === 1
          ? 'Saved. 1 rule was retired; trades already checked keep it.'
          : `Saved. ${retiring} rules were retired; trades already checked keep them.`);
      },
      error: err => { this.saving.set(false); this.saveError.set(`Not saved: ${describeHttpError(err)}`); }
    });
  }

  restoreRule(ruleId: number): void {
    this.act(this.api.restoreRule(this.strategyId!, ruleId), 'Rule restored at the end of the list.');
  }

  archive(): void {
    this.act(this.api.strategyAction(this.strategyId!, 'archive'), 'Archived. It no longer appears when choosing a strategy for a trade.');
  }

  restore(): void {
    this.act(this.api.strategyAction(this.strategyId!, 'restore'), 'Restored.');
  }

  private act(request: ReturnType<TradingApiService['restoreRule']>, message: string): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.saveError.set('');
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: s => { this.saving.set(false); this.fill(s); this.saved.set(message); },
      error: err => { this.saving.set(false); this.saveError.set(describeHttpError(err)); }
    });
  }

  private check(): string | null {
    if (!this.name.trim()) return 'Give the strategy a name.';
    if (this.rules.some(r => !r.text.trim())) return 'Every rule needs its wording, or remove the empty one.';
    const texts = this.rules.map(r => r.text.trim().toLowerCase());
    if (new Set(texts).size !== texts.length) return 'Two rules read the same.';
    return null;
  }

  private fill(s: Strategy): void {
    this.original.set(s);
    this.name = s.name;
    this.description = s.description ?? '';
    this.rules = s.rules.map(r => ({ id: r.id, text: r.text, isMandatory: r.isMandatory }));
  }
}
