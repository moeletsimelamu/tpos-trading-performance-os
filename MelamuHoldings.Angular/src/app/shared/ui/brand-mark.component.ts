import { Component, computed, input } from '@angular/core';
import { MARK_BAR, MARK_GOLD_BODY, MARK_GOLD_WICK, MARK_GREY_BODY, MARK_GREY_WICK, MARK_M, MARK_TONES, MARK_VIEW_BOX, NAME_ONE_LINE, NAME_STACKED } from './brand-art';

let nextId = 0;

// The Melamu Holdings mark: the M, with two candlesticks joined by a gold bar to form the H.
// `tone` picks the colours for the surface it sits on; the drawing itself never changes.
@Component({
  selector: 'app-brand-mark',
  standalone: true,
  host: { 'aria-hidden': 'true', '[style.width.px]': 'size()', '[style.height.px]': 'size()' },
  template: `
    <svg [attr.viewBox]="viewBox" focusable="false">
      <defs>
        <!-- A gradient per instance: one defined in a hidden copy of the mark would leave the visible one unfilled -->
        <linearGradient [attr.id]="gradientId" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" [attr.stop-color]="colours().silver[0]" />
          <stop offset=".55" [attr.stop-color]="colours().silver[1]" />
          <stop offset="1" [attr.stop-color]="colours().silver[2]" />
        </linearGradient>
      </defs>
      <path [attr.d]="m" [attr.fill]="'url(#' + gradientId + ')'" />
      <rect [attr.x]="greyWick.x" [attr.y]="greyWick.y" [attr.width]="greyWick.width" [attr.height]="greyWick.height" [attr.fill]="colours().greyWick" />
      <rect [attr.x]="greyBody.x" [attr.y]="greyBody.y" [attr.width]="greyBody.width" [attr.height]="greyBody.height" [attr.fill]="colours().grey" />
      <rect [attr.x]="bar.x" [attr.y]="bar.y" [attr.width]="bar.width" [attr.height]="bar.height" [attr.fill]="colours().gold" />
      <rect [attr.x]="goldWick.x" [attr.y]="goldWick.y" [attr.width]="goldWick.width" [attr.height]="goldWick.height" [attr.fill]="colours().goldWick" />
      <rect [attr.x]="goldBody.x" [attr.y]="goldBody.y" [attr.width]="goldBody.width" [attr.height]="goldBody.height" [attr.fill]="colours().gold" />
    </svg>
  `,
  styles: [`:host { display: inline-block; flex-shrink: 0; } svg { display: block; width: 100%; height: 100%; }`]
})
export class BrandMarkComponent {
  readonly size = input(32);
  readonly tone = input<'on-light' | 'on-dark'>('on-light');

  readonly viewBox = MARK_VIEW_BOX;
  readonly m = MARK_M;
  readonly greyWick = MARK_GREY_WICK;
  readonly greyBody = MARK_GREY_BODY;
  readonly bar = MARK_BAR;
  readonly goldWick = MARK_GOLD_WICK;
  readonly goldBody = MARK_GOLD_BODY;
  readonly gradientId = `brand-silver-${nextId++}`;
  readonly colours = computed(() => MARK_TONES[this.tone()]);
}

// The company name as drawn lettering, in the colour of its surroundings. It carries its own label, so it reads as text.
@Component({
  selector: 'app-brand-name',
  standalone: true,
  host: { '[style.height.px]': 'height()' },
  template: `
    <svg [attr.viewBox]="art().viewBox" role="img" aria-label="Melamu Holdings" focusable="false">
      <title>Melamu Holdings</title>
      <path [attr.d]="art().path" fill="currentColor" />
    </svg>
  `,
  styles: [`:host { display: inline-block; flex-shrink: 0; line-height: 0; } svg { display: block; height: 100%; width: auto; }`]
})
export class BrandNameComponent {
  readonly layout = input<'stacked' | 'line'>('line');
  readonly height = input(14);
  readonly art = computed(() => this.layout() === 'stacked' ? NAME_STACKED : NAME_ONE_LINE);
}
