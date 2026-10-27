import { Component } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from './services/auth.service';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, CommonModule],
  template: `
    <div class="shell">
      <nav class="sidebar">
        <div class="sidebar-logo">
          <span class="logo-mark">TD</span>
          <span class="logo-text">Trader<br><strong>Analytics</strong></span>
        </div>
        <ul class="nav-links">
          <li><a routerLink="/dashboard" routerLinkActive="active"><span class="nav-icon">◈</span> Dashboard</a></li>
          <li><a routerLink="/trades" routerLinkActive="active"><span class="nav-icon">◎</span> Trades</a></li>
          <li><a routerLink="/upload" routerLinkActive="active"><span class="nav-icon">⊕</span> Upload</a></li>
          <li><a routerLink="/behaviour" routerLinkActive="active"><span class="nav-icon">◉</span> Behaviour</a></li>
        </ul>
        <div class="sidebar-footer">
          <div class="user-row" *ngIf="auth.getUser()">
            <div class="user-avatar">
              {{ auth.getUser()?.displayName?.charAt(0)?.toUpperCase() }}
            </div>
            <div class="user-details">
              <div class="user-name">{{ auth.getUser()?.displayName }}</div>
              <div class="user-email mono">{{ auth.getUser()?.email }}</div>
            </div>
          </div>
          <button class="btn-logout" (click)="auth.logout()">Sign out</button>
        </div>
      </nav>
      <main class="content">
        <router-outlet />
      </main>
    </div>
  `,
  styles: [`
    .shell { display: flex; height: 100vh; overflow: hidden; }
    .sidebar {
      width: 220px; min-width: 220px;
      background: var(--bg-secondary);
      border-right: 1px solid var(--border-subtle);
      display: flex; flex-direction: column; padding: 1.5rem 0;
    }
    .sidebar-logo {
      display: flex; align-items: center; gap: 0.75rem;
      padding: 0 1.25rem 1.75rem;
      border-bottom: 1px solid var(--border-subtle);
      margin-bottom: 1rem;
    }
    .logo-mark {
      width: 36px; height: 36px;
      background: var(--accent-amber); color: var(--bg-primary);
      border-radius: 8px; display: flex; align-items: center;
      justify-content: center; font-weight: 800; font-size: 13px; flex-shrink: 0;
    }
    .logo-text { font-size: 12px; color: var(--text-secondary); line-height: 1.3; }
    .nav-links { list-style: none; flex: 1; padding: 0 0.75rem; }
    .nav-links li { margin-bottom: 2px; }
    .nav-links a {
      display: flex; align-items: center; gap: 0.65rem;
      padding: 0.6rem 0.75rem; border-radius: var(--radius);
      color: var(--text-secondary); text-decoration: none;
      font-size: 13.5px; font-weight: 500; transition: all 0.15s ease;
    }
    .nav-links a:hover { color: var(--text-primary); background: var(--bg-card); }
    .nav-links a.active { color: var(--accent-amber); background: rgba(245,158,11,0.08); }
    .nav-icon { font-size: 16px; color: var(--text-muted); }
    .sidebar-footer {
      padding: 1rem 1.25rem 0;
      border-top: 1px solid var(--border-subtle);
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }
    .user-info {
      font-size: 11px;
      color: var(--text-secondary);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .btn-logout {
      background: transparent;
      border: 1px solid var(--border);
      border-radius: var(--radius);
      padding: 0.4rem 0.75rem;
      color: var(--text-muted);
      font-size: 12px;
      cursor: pointer;
      text-align: left;
      transition: all 0.15s;
      &:hover { border-color: var(--accent-red); color: var(--accent-red); }
    }
    .user-row {
      display: flex;
      align-items: center;
      gap: 0.6rem;
      margin-bottom: 0.6rem;
    }

    .user-avatar {
      width: 28px;
      height: 28px;
      background: rgba(245, 158, 11, 0.15);
      color: var(--accent-amber);
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 12px;
      font-weight: 700;
      flex-shrink: 0;
    }

    .user-name {
      font-size: 12px;
      font-weight: 600;
      color: var(--text-primary);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .user-email {
      font-size: 10px;
      color: var(--text-muted);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
  `]
})
export class AppComponent {
  constructor(public auth: AuthService) {}
}