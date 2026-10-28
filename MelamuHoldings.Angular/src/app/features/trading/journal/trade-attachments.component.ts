import { Component, DestroyRef, OnInit, inject, input, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { TradingApiService } from '../trading-api.service';
import { ATTACHMENT_LIMITS, TradeAttachment } from '../models/journal-view.model';
import { describeHttpError } from '../../../shared/http-error';
import { IconComponent } from '../../../shared/ui/icon.component';

const ACCEPTED = /\.(png|jpe?g|webp|pdf)$/i;

// Screenshots and statements kept with a trade. Files are fetched with the session's token (they are never
// public), so images are shown from object URLs that are released when the page closes.
@Component({
  selector: 'app-trade-attachments',
  standalone: true,
  imports: [DatePipe, FormsModule, IconComponent],
  template: `
    @if (error()) { <p class="notice notice-error" role="alert">{{ error() }}</p> }
    @if (files().length > 0) {
      <ul class="files">
        @for (f of files(); track f.id) {
          <li>
            @if (previews()[f.id]; as url) {
              <button type="button" class="thumb" (click)="open(f)" [attr.aria-label]="'Open ' + f.fileName"><img [src]="url" [alt]="f.caption || f.fileName" /></button>
            } @else {
              <span class="thumb doc" aria-hidden="true"><app-icon [name]="isImage(f) ? 'paperclip' : 'import'" [size]="22" /></span>
            }
            <div class="meta">
              <button type="button" class="btn-link name" (click)="open(f)">{{ f.fileName }}</button>
              @if (f.caption) { <span class="small">{{ f.caption }}</span> }
              <span class="small muted">{{ size(f.sizeBytes) }} · {{ f.uploadedAt | date: 'd MMM yyyy' }}</span>
            </div>
            <div class="row-actions">
              @if (confirming() === f.id) {
                <button type="button" class="btn btn-sm btn-danger" [disabled]="busy()" (click)="remove(f)">Delete file</button>
                <button type="button" class="btn-link small" (click)="confirming.set(null)">Keep</button>
              } @else {
                <button type="button" class="btn-link small" (click)="confirming.set(f.id)">Delete</button>
              }
            </div>
          </li>
        }
      </ul>
    } @else {
      <p class="muted">No files yet. Add a chart screenshot or a platform statement.</p>
    }

    @if (files().length < limits.maxPerTrade) {
      <div class="add">
        <div class="field caption"><label for="att-caption">Caption (optional)</label><input id="att-caption" [(ngModel)]="caption" maxlength="200" /></div>
        <label class="btn" [class.disabled]="busy()">
          <app-icon name="paperclip" [size]="16" />{{ busy() ? 'Uploading…' : 'Attach a file' }}
          <input type="file" class="visually-hidden" [accept]="limits.accept" [disabled]="busy()" (change)="choose($event)" />
        </label>
        <span class="small muted">PNG, JPEG, WebP or PDF, up to 5 MB; {{ limits.maxPerTrade - files().length }} more allowed.</span>
      </div>
    } @else {
      <p class="small muted">This trade has the most files it can keep ({{ limits.maxPerTrade }}). Delete one to add another.</p>
    }
  `,
  styles: [`
    .files { list-style: none; display: grid; gap: var(--space-3); margin-bottom: var(--space-4); }
    .files li { display: grid; grid-template-columns: 72px 1fr auto; gap: var(--space-3); align-items: center; }
    .thumb { width: 72px; height: 54px; border: 1px solid var(--border); border-radius: var(--radius); background: var(--surface-sunken); padding: 0; overflow: hidden; display: flex; align-items: center; justify-content: center; color: var(--text-muted); cursor: pointer; }
    .thumb img { width: 100%; height: 100%; object-fit: cover; }
    .thumb.doc { cursor: default; }
    .meta { display: grid; gap: 2px; min-width: 0; }
    .name { text-align: left; overflow-wrap: anywhere; }
    .row-actions { display: flex; gap: var(--space-2); align-items: center; }
    .add { display: flex; flex-wrap: wrap; gap: var(--space-3); align-items: end; }
    .caption { min-width: 220px; flex: 1; }
    .btn.disabled { opacity: 0.6; pointer-events: none; }
    @media (max-width: 599px) { .files li { grid-template-columns: 56px 1fr; } .row-actions { grid-column: 2; } .thumb { width: 56px; height: 42px; } }
  `]
})
export class TradeAttachmentsComponent implements OnInit {
  private api = inject(TradingApiService);
  private destroyRef = inject(DestroyRef);

  readonly tradeId = input.required<number>();
  readonly initial = input<TradeAttachment[]>([]);
  // After a file is added or deleted
  readonly changed = output<void>();

  readonly limits = ATTACHMENT_LIMITS;
  readonly files = signal<TradeAttachment[]>([]);
  readonly previews = signal<Record<number, string>>({});
  readonly busy = signal(false);
  readonly error = signal('');
  readonly confirming = signal<number | null>(null);
  caption = '';

  constructor() {
    this.destroyRef.onDestroy(() => Object.values(this.previews()).forEach(url => URL.revokeObjectURL(url)));
  }

  ngOnInit(): void {
    this.files.set(this.initial());
    this.initial().filter(f => this.isImage(f)).forEach(f => this.loadPreview(f));
  }

  isImage(f: TradeAttachment): boolean {
    return f.contentType.startsWith('image/');
  }

  size(bytes: number): string {
    return bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
  }

  // Quick checks for a clear message; the server decides by the file's content
  choose(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file || this.busy()) return;
    this.error.set('');
    if (!ACCEPTED.test(file.name)) { this.error.set('Only PNG, JPEG, WebP images and PDF files can be attached.'); return; }
    if (file.size > this.limits.maxBytes) { this.error.set('The file is larger than 5 MB.'); return; }

    this.busy.set(true);
    this.api.uploadAttachment(this.tradeId(), file, this.caption).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: created => {
        this.busy.set(false);
        this.caption = '';
        this.files.update(list => [...list, created]);
        this.changed.emit();
        if (this.isImage(created)) this.loadPreview(created);
      },
      error: err => { this.busy.set(false); this.error.set(`Not attached: ${describeHttpError(err)}`); }
    });
  }

  // Images open in a new tab; PDFs download under their own name
  open(f: TradeAttachment): void {
    this.api.getAttachment(this.tradeId(), f.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        if (this.isImage(f)) window.open(url, '_blank', 'noopener');
        else {
          const link = document.createElement('a');
          link.href = url;
          link.download = f.fileName;
          link.click();
        }
        setTimeout(() => URL.revokeObjectURL(url), 60_000);
      },
      error: err => this.error.set(`Could not open ${f.fileName}: ${describeHttpError(err)}`)
    });
  }

  remove(f: TradeAttachment): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.api.deleteAttachment(this.tradeId(), f.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.busy.set(false);
        this.confirming.set(null);
        this.files.update(list => list.filter(x => x.id !== f.id));
        this.changed.emit();
        const url = this.previews()[f.id];
        if (url) URL.revokeObjectURL(url);
      },
      error: err => { this.busy.set(false); this.error.set(`Not deleted: ${describeHttpError(err)}`); }
    });
  }

  private loadPreview(f: TradeAttachment): void {
    this.api.getAttachment(this.tradeId(), f.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: blob => this.previews.update(p => ({ ...p, [f.id]: URL.createObjectURL(blob) })),
      error: () => { /* the file still lists; it opens on click if the server has it */ }
    });
  }
}
