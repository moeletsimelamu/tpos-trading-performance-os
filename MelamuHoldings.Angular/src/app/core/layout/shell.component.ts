import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { BrandMarkComponent } from '../../shared/ui/brand-mark.component';
import { IconComponent } from '../../shared/ui/icon.component';
import { NAVIGATION } from './navigation';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, BrandMarkComponent, IconComponent],
  host: { '(document:keydown.escape)': 'menuOpen.set(false)' },
  template: `
    <div class="shell" [class.menu-open]="menuOpen()">
      <!-- Narrow screens: a top bar opens the navigation as a drawer -->
      <header class="topbar">
        <button type="button" class="menu-button" aria-controls="app-nav"
          [attr.aria-expanded]="menuOpen()" (click)="menuOpen.set(!menuOpen())">
          <app-icon [name]="menuOpen() ? 'close' : 'menu'" [size]="20" />
          <span class="visually-hidden">{{ menuOpen() ? 'Close navigation' : 'Open navigation' }}</span>
        </button>
        <a class="topbar-brand" routerLink="/"><app-brand-mark [size]="26" /><span class="wordmark">Melamu Holdings</span></a>
      </header>

      <nav id="app-nav" class="sidebar" aria-label="Main">
        <a class="brand" routerLink="/">
          <app-brand-mark [size]="34" tone="on-dark" />
          <span class="brand-text">
            <span class="wordmark">Melamu Holdings</span>
            <span class="brand-product">Operating System</span>
          </span>
        </a>

        <div class="nav-scroll">
          @for (section of navigation; track section.heading) {
            <div class="nav-section">
              <h2 class="nav-heading">{{ section.heading }}</h2>
              <ul>
                @for (item of section.items; track item.route) {
                  <li>
                    <a class="nav-link" [routerLink]="item.route" routerLinkActive="active" ariaCurrentWhenActive="page">
                      <app-icon [name]="item.icon" [size]="18" />
                      <span>{{ item.label }}</span>
                    </a>
                  </li>
                }
              </ul>
            </div>
          }
        </div>

        <div class="sidebar-footer">
          @if (auth.user(); as user) {
            <div class="user-avatar" aria-hidden="true">{{ user.displayName.charAt(0).toUpperCase() }}</div>
            <div class="user-details">
              <div class="user-name">{{ user.displayName }}</div>
              <div class="user-email">{{ user.email }}</div>
            </div>
          }
          <button type="button" class="sign-out" (click)="auth.logout()" title="Sign out">
            <app-icon name="sign-out" [size]="18" /><span class="visually-hidden">Sign out</span>
          </button>
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
    .sidebar {
      width: var(--sidebar-width); flex-shrink: 0; display: flex; flex-direction: column;
      background: var(--sidebar-bg); color: var(--sidebar-text);
    }
    .brand { display: flex; align-items: center; gap: 12px; padding: 20px 18px 18px; text-decoration: none; }
    .brand-text { display: flex; flex-direction: column; gap: 2px; line-height: 1.2; }
    .wordmark { font-weight: 600; font-size: 12.5px; letter-spacing: 0.14em; text-transform: uppercase; color: var(--sidebar-text-strong); }
    .brand-product { font-size: 10px; letter-spacing: 0.2em; text-transform: uppercase; color: var(--sidebar-muted); }

    .nav-scroll { flex: 1; overflow-y: auto; padding: 6px 10px 16px; }
    .nav-section { margin-top: 18px; }
    .nav-heading {
      font-size: 11px; font-weight: 600; letter-spacing: 0.08em; text-transform: uppercase;
      color: var(--sidebar-muted); padding: 0 10px 6px;
    }
    ul { list-style: none; }
    .nav-link {
      position: relative; display: flex; align-items: center; gap: 10px;
      padding: 7px 10px; margin: 1px 0; border-radius: var(--radius);
      font-size: 13.5px; color: var(--sidebar-text); text-decoration: none;
      &:hover { background: var(--sidebar-hover); color: var(--sidebar-text-strong); }
      &.active { background: var(--sidebar-active); color: var(--sidebar-text-strong); font-weight: 500; }
      &.active::before {
        content: ''; position: absolute; left: -10px; top: 7px; bottom: 7px; width: 3px;
        border-radius: 0 2px 2px 0; background: var(--brass-500);
      }
      &:focus-visible { box-shadow: 0 0 0 2px var(--brass-500); }
    }

    .sidebar-footer {
      display: flex; align-items: center; gap: 10px; padding: 14px 16px;
      border-top: 1px solid var(--sidebar-border);
    }
    .user-avatar {
      width: 30px; height: 30px; flex-shrink: 0; border-radius: 50%;
      background: var(--sidebar-active); color: var(--sidebar-text-strong);
      display: flex; align-items: center; justify-content: center; font-size: 12px; font-weight: 600;
    }
    .user-details { flex: 1; min-width: 0; line-height: 1.3; }
    .user-name, .user-email { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .user-name { font-size: 13px; font-weight: 500; color: var(--sidebar-text-strong); }
    .user-email { font-size: 11.5px; color: var(--sidebar-muted); }
    .sign-out {
      margin-left: auto; width: 32px; height: 32px; display: flex; align-items: center; justify-content: center;
      border: none; border-radius: var(--radius); background: transparent; color: var(--sidebar-text); cursor: pointer;
      &:hover { background: var(--sidebar-hover); color: var(--sidebar-text-strong); }
      &:focus-visible { box-shadow: 0 0 0 2px var(--brass-500); }
    }

    .content { flex: 1; min-width: 0; overflow-y: auto; padding: 32px 40px 48px; }
    .topbar, .backdrop { display: none; }

    @media (max-width: 899px) {
      .shell { flex-direction: column; }
      .topbar {
        display: flex; align-items: center; gap: 10px; height: 56px; padding: 0 16px; flex-shrink: 0;
        background: var(--surface); border-bottom: 1px solid var(--border-subtle);
      }
      .topbar-brand { display: flex; align-items: center; gap: 10px; text-decoration: none; }
      .topbar .wordmark { color: var(--text-primary); }
      .menu-button {
        width: 38px; height: 38px; display: flex; align-items: center; justify-content: center;
        border: 1px solid var(--border); border-radius: var(--radius); background: var(--surface);
        color: var(--text-primary); cursor: pointer;
      }
      .sidebar {
        position: fixed; inset: 0 auto 0 0; z-index: 20; width: 280px; max-width: 86vw;
        transform: translateX(-100%); transition: transform 0.2s ease; visibility: hidden; box-shadow: var(--shadow-lg);
      }
      .menu-open .sidebar { transform: none; visibility: visible; }
      .menu-open .backdrop { display: block; position: fixed; inset: 0; z-index: 10; background: rgba(11, 22, 38, 0.45); }
      .content { padding: 20px 16px 40px; }
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
