import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="auth-shell">
      <div class="auth-card">
        <div class="auth-logo">
          <span class="logo-mark">TD</span>
          <span class="auth-title">Trader Analytics</span>
        </div>

        <h2 class="form-heading">Sign in</h2>
        <p class="form-sub text-muted mono">Welcome back. Enter your credentials.</p>

        <div class="form-group">
          <label class="form-label">Email</label>
          <input class="form-input" type="email" [(ngModel)]="email" placeholder="trader@example.com" />
        </div>

        <div class="form-group">
          <label class="form-label">Password</label>
          <input class="form-input" type="password" [(ngModel)]="password" placeholder="••••••••"
            (keyup.enter)="login()" />
        </div>

        <p class="error-msg" *ngIf="errorMsg">{{ errorMsg }}</p>

        <button class="btn-primary" (click)="login()" [disabled]="loading">
          {{ loading ? 'Signing in...' : 'Sign in' }}
        </button>

        <p class="auth-switch mono">
          No account? <a routerLink="/register">Register here</a>
        </p>
      </div>
    </div>
  `,
  styles: [`
    .auth-shell {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      background: var(--bg-primary);
      padding: 2rem;
    }

    .auth-card {
      width: 100%;
      max-width: 420px;
      background: var(--bg-card);
      border: 1px solid var(--border-subtle);
      border-radius: var(--radius-lg);
      padding: 2.5rem;
    }

    .auth-logo {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      margin-bottom: 2rem;
    }

    .logo-mark {
      width: 36px;
      height: 36px;
      background: var(--accent-amber);
      color: var(--bg-primary);
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 800;
      font-size: 13px;
    }

    .auth-title {
      font-weight: 700;
      font-size: 15px;
      color: var(--text-primary);
    }

    .form-heading {
      font-family: var(--font-display);
      font-size: 1.5rem;
      font-weight: 800;
      margin-bottom: 0.35rem;
    }

    .form-sub {
      font-size: 12px;
      margin-bottom: 1.75rem;
    }

    .form-group {
      margin-bottom: 1.1rem;
    }

    .form-label {
      display: block;
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.07em;
      color: var(--text-secondary);
      margin-bottom: 0.4rem;
    }

    .form-input {
      width: 100%;
      background: var(--bg-secondary);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      padding: 0.65rem 0.85rem;
      color: var(--text-primary);
      font-family: var(--font-mono);
      font-size: 13px;
      outline: none;
      transition: border-color 0.15s;
      box-sizing: border-box;

      &:focus { border-color: var(--accent-amber); }
      &::placeholder { color: var(--text-muted); }
    }

    .error-msg {
      color: var(--accent-red);
      font-size: 12px;
      font-family: var(--font-mono);
      margin-bottom: 1rem;
    }

    .btn-primary {
      width: 100%;
      background: var(--accent-amber);
      color: var(--bg-primary);
      border: none;
      border-radius: var(--radius);
      padding: 0.75rem;
      font-family: var(--font-display);
      font-weight: 700;
      font-size: 14px;
      cursor: pointer;
      transition: opacity 0.15s;
      margin-top: 0.5rem;

      &:hover { opacity: 0.9; }
      &:disabled { opacity: 0.5; cursor: not-allowed; }
    }

    .auth-switch {
      text-align: center;
      font-size: 12px;
      color: var(--text-muted);
      margin-top: 1.25rem;

      a {
        color: var(--accent-amber);
        text-decoration: none;
        &:hover { text-decoration: underline; }
      }
    }
  `]
})
export class LoginComponent {
  email = '';
  password = '';
  loading = false;
  errorMsg = '';

  constructor(private auth: AuthService, private router: Router) {}

  login(): void {
    if (!this.email || !this.password) {
      this.errorMsg = 'Please enter your email and password.';
      return;
    }

    this.loading = true;
    this.errorMsg = '';

    this.auth.login({ email: this.email, password: this.password }).subscribe({
      next: () => this.router.navigate(['/dashboard']),
      error: () => {
        this.errorMsg = 'Invalid email or password.';
        this.loading = false;
      }
    });
  }
}