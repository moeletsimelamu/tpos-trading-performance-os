import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { NAVIGATION } from './navigation';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  host: { '(document:keydown.escape)': 'menuOpen.set(false)' },
  template: `
    <div class="shell" [class.menu-open]="menuOpen()">
      <!-- Narrow screens: a top bar opens the navigation as a drawer -->
      <header class="topbar">
        <button type="button" class="menu-button" aria-controls="app-nav"
          [attr.aria-expanded]="menuOpen()" (click)="menuOpen.set(!menuOpen())">
          <span class="menu-icon" aria-hidden="true"></span>
          <span class="visually-hidden">{{ menuOpen() ? 'Close navigation' : 'Open navigation' }}</span>
        </button>
        <span class="topbar-brand"><span class="logo-mark" aria-hidden="true">MH</span>Melamu Holdings OS</span>
      </header>

      <nav id="app-nav" class="sidebar" aria-label="Main">
        <a class="brand" routerLink="/">
          <span class="logo-mark" aria-hidden="true">MH</span>
          <span class="brand-text">
            <span class="brand-name">Melamu Holdings</span>
            <span class="brand-product">Operating System</span>
          </span>
        </a>

        <div class="nav-scroll">
          @for (section of navigation; track section.heading) {
            <div class="nav-section">
              <h2 class="nav-heading">{{ section.heading }}</h2>
              <ul>
                @for (item of section.items; track item.label) {
                  <li>
                    @if (item.route) {
                      <a class="nav-link" [routerLink]="item.route" routerLinkActive="active" ariaCurrentWhenActive="page">
                        {{ item.label }}
                      </a>
                    } @else {
                      <span class="nav-planned" [attr.title]="'Not built yet. Planned for Phase ' + item.plannedPhase + ' (see PLAN.md).'">
                        <span>{{ item.label }}</span>
                        <span class="planned-badge">Planned</span>
                      </span>
                    }
                  </li>
                }
              </ul>
            </div>
          }
        </div>

        <div class="sidebar-footer">
          @if (auth.user(); as user) {
            <div class="user-row">
              <div class="user-avatar" aria-hidden="true">{{ user.displayName.charAt(0).toUpperCase() }}</div>
              <div class="user-details">
                <div class="user-name">{{ user.displayName }}</div>
                <div class="user-email mono">{{ user.email }}</div>
              </div>
            </div>
          }
          <button type="button" class="btn-logout" (click)="auth.logout()">Sign out</button>
        </div>
      </nav>

      <div class="backdrop" aria-hidden="true" (click)="menuOpen.set(false)"></div>

      <main class="content">
        <router-outlet />
      </main>
    </div>
  `,
  styles: [`
    .shell { display: flex; height: 100vh; overflow: hidden; }

    .logo-mark {
      width: 32px; height: 32px; flex-shrink: 0;
      border: 1px solid var(--accent-amber); border-radius: var(--radius);
      color: var(--accent-amber);
      display: inline-flex; align-items: center; justify-content: center;
      font-weight: 700; font-size: 12px; letter-spacing: 0.04em;
    }

    .sidebar {
      width: 240px; min-width: 240px;
      background: var(--bg-secondary);
      border-right: 1px solid var(--border-subtle);
      display: flex; flex-direction: column;
    }

    .brand {
      display: flex; align-items: center; gap: 0.7rem;
      padding: 1.1rem 1.1rem 1rem;
      border-bottom: 1px solid var(--border-subtle);
      text-decoration: none;
    }
    .brand-text { display: flex; flex-direction: column; line-height: 1.25; }
    .brand-name { color: var(--text-primary); font-weight: 600; font-size: 14px; }
    .brand-product { color: var(--text-muted); font-size: 11px; letter-spacing: 0.04em; }

    .nav-scroll { flex: 1; overflow-y: auto; padding: 0.75rem 0.6rem; }
    .nav-section + .nav-section { margin-top: 1rem; }
    .nav-heading {
      font-size: 11px; font-weight: 600; letter-spacing: 0.08em; text-transform: uppercase;
      color: var(--text-muted); padding: 0 0.6rem; margin-bottom: 0.3rem;
    }
    ul { list-style: none; }

    .nav-link, .nav-planned {
      display: flex; align-items: center; justify-content: space-between; gap: 0.5rem;
      padding: 0.42rem 0.6rem; border-radius: var(--radius);
      font-size: 13.5px;
    }
    .nav-link {
      color: var(--text-secondary); text-decoration: none;
      border-left: 2px solid transparent;
      &:hover { color: var(--text-primary); background: var(--bg-card); }
      &.active { color: var(--text-primary); background: var(--bg-card); border-left-color: var(--accent-amber); font-weight: 500; }
    }
    .nav-planned { color: var(--text-muted); cursor: default; }
    .planned-badge {
      font-size: 10px; letter-spacing: 0.04em; text-transform: uppercase;
      border: 1px solid var(--border); border-radius: 4px; padding: 0 5px; color: var(--text-muted);
    }

    .sidebar-footer {
      padding: 0.9rem 1.1rem 1.1rem;
      border-top: 1px solid var(--border-subtle);
      display: flex; flex-direction: column; gap: 0.6rem;
    }
    .user-row { display: flex; align-items: center; gap: 0.6rem; min-width: 0; }
    .user-avatar {
      width: 28px; height: 28px; flex-shrink: 0;
      background: var(--bg-card); border: 1px solid var(--border); color: var(--text-primary);
      border-radius: 50%; display: flex; align-items: center; justify-content: center;
      font-size: 12px; font-weight: 600;
    }
    .user-details { min-width: 0; }
    .user-name, .user-email { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .user-name { font-size: 12.5px; font-weight: 600; color: var(--text-primary); }
    .user-email { font-size: 11px; color: var(--text-muted); }
    .btn-logout {
      background: transparent; border: 1px solid var(--border); border-radius: var(--radius);
      padding: 0.4rem 0.75rem; color: var(--text-secondary); font: inherit; font-size: 12.5px;
      cursor: pointer; text-align: left;
      &:hover { border-color: var(--accent-red); color: var(--accent-red); }
    }

    .content { flex: 1; overflow-y: auto; padding: 2rem; min-width: 0; }

    .topbar, .backdrop { display: none; }

    @media (max-width: 899px) {
      .shell { flex-direction: column; }
      .topbar {
        display: flex; align-items: center; gap: 0.75rem;
        height: 52px; padding: 0 1rem; flex-shrink: 0;
        background: var(--bg-secondary); border-bottom: 1px solid var(--border-subtle);
      }
      .topbar-brand { display: flex; align-items: center; gap: 0.6rem; font-weight: 600; }
      .topbar .logo-mark { width: 26px; height: 26px; font-size: 10px; }
      .menu-button {
        width: 36px; height: 36px; border: 1px solid var(--border); border-radius: var(--radius);
        background: transparent; cursor: pointer; display: flex; align-items: center; justify-content: center;
      }
      .menu-icon, .menu-icon::before, .menu-icon::after {
        display: block; width: 16px; height: 2px; background: var(--text-primary); position: relative;
      }
      .menu-icon::before, .menu-icon::after { content: ''; position: absolute; left: 0; }
      .menu-icon::before { top: -5px; }
      .menu-icon::after { top: 5px; }

      .sidebar {
        position: fixed; inset: 0 auto 0 0; z-index: 20; width: 272px; max-width: 85vw;
        transform: translateX(-100%); transition: transform 0.2s ease; visibility: hidden;
      }
      .menu-open .sidebar { transform: none; visibility: visible; }
      .menu-open .backdrop { display: block; position: fixed; inset: 0; z-index: 10; background: rgba(0, 0, 0, 0.55); }
      .content { padding: 1.25rem 1rem; }
    }

    @media (prefers-reduced-motion: reduce) {
      .sidebar { transition: none; }
    }
  `]
})
export class ShellComponent {
  readonly auth = inject(AuthService);
  readonly navigation = NAVIGATION;
  readonly menuOpen = signal(false);

  constructor() {
    // Following a link in the drawer closes it
    inject(Router).events
      .pipe(filter(e => e instanceof NavigationEnd), takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(() => this.menuOpen.set(false));
  }
}
