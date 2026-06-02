import { Injectable } from '@angular/core';
import { Router, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';

@Injectable({ providedIn: 'root' })
export class NavigationService {
  constructor(private router: Router) {}

  onNavigateTo(path: string, callback: () => void): void {
    // Fire immediately if already on the route
    if (this.router.url.includes(path)) {
      callback();
    }

    // Fire on every navigation to this route
    this.router.events.pipe(
      filter(event => event instanceof NavigationEnd),
      filter((event: any) => event.urlAfterRedirects.includes(path))
    ).subscribe(() => callback());
  }
}