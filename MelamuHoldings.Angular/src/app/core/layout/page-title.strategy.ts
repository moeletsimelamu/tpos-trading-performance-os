import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';

export const PRODUCT_NAME = 'Melamu Holdings OS';

// Browser tab titles read "<page> · Melamu Holdings OS", or just the product name for untitled routes
@Injectable()
export class PageTitleStrategy extends TitleStrategy {
  private title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const page = this.buildTitle(snapshot);
    this.title.setTitle(page ? `${page} · ${PRODUCT_NAME}` : PRODUCT_NAME);
  }
}
