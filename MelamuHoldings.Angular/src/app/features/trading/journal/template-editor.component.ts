import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TradingApiService } from '../trading-api.service';
import { FIELD_TYPES, JournalAnswers, JournalFieldType, JournalSection, JournalTemplate } from '../models/journal.model';
import { JournalFieldsComponent } from './journal-fields.component';
import { LoadErrorComponent } from '../../../shared/load-error.component';
import { describeHttpError } from '../../../shared/http-error';
import { PageHeaderComponent } from '../../../shared/ui/page-header.component';
import { IconComponent } from '../../../shared/ui/icon.component';

// Editable copies of sections and fields. Existing ids are kept so answers stay linked across versions;
// new items get an id from their label when saved.
interface DraftField { id: string; label: string; type: JournalFieldType; required: boolean; help: string; optionsText: string; }
interface DraftSection { id: string; title: string; description: string; fields: DraftField[]; }

function slug(text: string, fallback: string): string {
  const s = text.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 36);
  return s || fallback;
}

function unique(base: string, taken: Set<string>): string {
  let id = base;
  for (let n = 2; taken.has(id); n++) id = `${base.slice(0, 36)}-${n}`;
  taken.add(id);
  return id;
}

@Component({
  selector: 'app-template-editor',
  standalone: true,
  imports: [FormsModule, RouterLink, JournalFieldsComponent, LoadErrorComponent, PageHeaderComponent, IconComponent],
  templateUrl: './template-editor.component.html',
  styles: [`
    .layout { display: grid; grid-template-columns: minmax(0, 3fr) minmax(0, 2fr); gap: var(--space-5); align-items: start; }
    @media (max-width: 1099px) { .layout { grid-template-columns: minmax(0, 1fr); } }
    .card + .card { margin-top: var(--space-4); }
    .sec-head, .fld { display: grid; gap: var(--space-3); }
    .sec-head { grid-template-columns: 1fr 1fr auto; align-items: end; }
    .fld { grid-template-columns: 2fr 1fr auto; align-items: end; padding: var(--space-3) 0; border-top: 1px solid var(--border-subtle); }
    .fld-extra { grid-column: 1 / -1; display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-3); }
    @media (max-width: 699px) { .sec-head, .fld, .fld-extra { grid-template-columns: 1fr; } }
    .tools { display: flex; gap: 2px; align-items: center; }
    .icon-btn { display: flex; padding: 6px; border: none; background: none; border-radius: var(--radius-sm); color: var(--text-secondary); cursor: pointer; }
    .icon-btn:hover:not(:disabled) { background: var(--surface-hover); color: var(--text-primary); }
    .icon-btn:disabled { opacity: 0.35; cursor: default; }
    .req { display: flex; gap: var(--space-2); align-items: center; font-size: 13px; }
    .req input { height: auto; }
    textarea { min-height: 72px; }
    .preview { position: sticky; top: 0; }
  `]
})
export class TemplateEditorComponent {
  private api = inject(TradingApiService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  readonly fieldTypes = FIELD_TYPES;
  readonly templateId = this.route.snapshot.paramMap.get('id') ? Number(this.route.snapshot.paramMap.get('id')) : null;

  readonly state = signal<'loading' | 'ready' | 'error'>(this.templateId ? 'loading' : 'ready');
  readonly error = signal('');
  readonly original = signal<JournalTemplate | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal('');
  readonly saved = signal('');
  readonly previewAnswers = signal<JournalAnswers>({});

  name = '';
  description = '';
  sections: DraftSection[] = [];
  // Bumped on every edit so the preview re-renders (the draft is plain objects bound with ngModel)
  private readonly revision = signal(0);

  readonly preview = computed<JournalSection[]>(() => {
    this.revision();
    return this.toSections();
  });

  readonly versionNote = computed(() => {
    const t = this.original();
    if (!t) return 'Saving creates version 1.';
    const current = t.versions.find(v => v.version === t.currentVersion);
    const used = current?.entryCount ?? 0;
    return `Saving changed questions creates version ${t.currentVersion + 1}. ` +
      (used > 0 ? `${used === 1 ? '1 trade' : used + ' trades'} written with v${t.currentVersion} ${used === 1 ? 'keeps' : 'keep'} that version.` : 'No trade uses the current version yet.');
  });

  constructor() {
    if (this.templateId) this.load();
    else {
      this.sections = [this.newSection()];
      this.touch();
    }
  }

  load(): void {
    this.state.set('loading');
    this.api.getTemplate(this.templateId!).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: t => { this.original.set(t); this.fill(t); this.state.set('ready'); },
      error: err => { this.error.set(describeHttpError(err)); this.state.set('error'); }
    });
  }

  touch(): void {
    this.revision.update(n => n + 1);
    this.saved.set('');
  }

  hasOptions(type: JournalFieldType): boolean {
    return !!FIELD_TYPES.find(t => t.type === type)?.hasOptions;
  }

  addSection(): void {
    this.sections.push(this.newSection());
    this.touch();
  }

  addField(section: DraftSection): void {
    section.fields.push(this.newField());
    this.touch();
  }

  move<T>(list: T[], index: number, delta: number): void {
    const target = index + delta;
    if (target < 0 || target >= list.length) return;
    [list[index], list[target]] = [list[target], list[index]];
    this.touch();
  }

  remove<T>(list: T[], index: number): void {
    list.splice(index, 1);
    this.touch();
  }

  save(): void {
    if (this.saving()) return;
    const problem = this.check();
    if (problem) { this.saveError.set(problem); return; }

    const sections = this.toSections();
    const before = this.original()?.currentVersion ?? 0;
    this.saving.set(true);
    this.saveError.set('');
    this.api.saveTemplate(this.templateId, { name: this.name.trim(), description: this.description.trim() || null, sections })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: t => {
          this.saving.set(false);
          if (!this.templateId) { this.router.navigate(['/trading/journal-templates', t.id]); return; }
          this.original.set(t);
          this.fill(t);
          this.saved.set(t.currentVersion > before ? `Saved as version ${t.currentVersion}.` : 'Saved. The questions did not change, so the version stays the same.');
        },
        error: err => { this.saving.set(false); this.saveError.set(`Not saved: ${describeHttpError(err)}`); }
      });
  }

  // The draft as the API expects it; new sections and fields get ids from their titles and labels
  private toSections(): JournalSection[] {
    const sectionIds = new Set(this.sections.filter(s => s.id).map(s => s.id));
    const fieldIds = new Set(this.sections.flatMap(s => s.fields).filter(f => f.id).map(f => f.id));
    return this.sections.map((s, i) => ({
      id: s.id || unique(slug(s.title, `section-${i + 1}`), sectionIds),
      title: s.title.trim(),
      description: s.description.trim() || null,
      fields: s.fields.map((f, j) => ({
        id: f.id || unique(slug(f.label, `field-${i + 1}-${j + 1}`), fieldIds),
        label: f.label.trim(),
        type: f.type,
        required: f.required,
        help: f.help.trim() || null,
        options: this.hasOptions(f.type) ? f.optionsText.split('\n').map(o => o.trim()).filter(Boolean) : null
      }))
    }));
  }

  private check(): string | null {
    if (!this.name.trim()) return 'Give the template a name.';
    if (this.sections.length === 0) return 'Add at least one section.';
    for (const s of this.sections) {
      if (!s.title.trim()) return 'Every section needs a title.';
      if (s.fields.length === 0) return `Section '${s.title}' needs at least one question.`;
      for (const f of s.fields) {
        if (!f.label.trim()) return `A question in '${s.title}' has no label.`;
        if (this.hasOptions(f.type) && !f.optionsText.trim()) return `'${f.label}' needs its options, one per line.`;
      }
    }
    return null;
  }

  private fill(t: JournalTemplate): void {
    this.name = t.name;
    this.description = t.description ?? '';
    this.sections = t.sections.map(s => ({
      id: s.id, title: s.title, description: s.description ?? '',
      fields: s.fields.map(f => ({ id: f.id, label: f.label, type: f.type, required: !!f.required, help: f.help ?? '', optionsText: (f.options ?? []).join('\n') }))
    }));
    this.touch();
  }

  private newSection(): DraftSection {
    return { id: '', title: '', description: '', fields: [this.newField()] };
  }

  private newField(): DraftField {
    return { id: '', label: '', type: 'ShortText', required: false, help: '', optionsText: '' };
  }
}
