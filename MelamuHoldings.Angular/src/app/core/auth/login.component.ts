import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';
import { describeHttpError } from '../../shared/http-error';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="auth-shell">
      <form class="auth-card" (ngSubmit)="login()">
        <div class="auth-logo">
          <span class="logo-mark" aria-hidden="true">MH</span>
          <span class="auth-title">Melamu Holdings OS</span>
        </div>

        <h1 class="form-heading">Sign in</h1>
        <p class="form-sub text-muted">Operator access only. Accounts are created on the server.</p>

        <div class="form-group">
          <label class="form-label" for="login-email">Email</label>
          <input id="login-email" name="email" class="form-input" type="email" autocomplete="username"
            [(ngModel)]="email" placeholder="you@example.com" />
        </div>

        <div class="form-group">
          <label class="form-label" for="login-password">Password</label>
          <input id="login-password" name="password" class="form-input" type="password" autocomplete="current-password"
            [(ngModel)]="password" placeholder="••••••••" />
        </div>

        @if (errorMsg()) {
          <p class="error-msg" role="alert">{{ errorMsg() }}</p>
        }

        <button type="submit" class="btn-primary" [disabled]="loading()">
          {{ loading() ? 'Signing in...' : 'Sign in' }}
        </button>
      </form>
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
      width: 32px;
      height: 32px;
      border: 1px solid var(--accent-amber);
      color: var(--accent-amber);
      border-radius: var(--radius);
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 700;
      font-size: 12px;
      letter-spacing: 0.04em;
    }

    .auth-title {
      font-weight: 600;
      font-size: 15px;
      color: var(--text-primary);
    }

    .form-heading {
      font-size: 1.4rem;
      font-weight: 700;
      margin-bottom: 0.35rem;
    }

    .form-sub {
      font-size: 13px;
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
      color: #14110a;
      border: none;
      border-radius: var(--radius);
      padding: 0.7rem;
      font-family: var(--font-display);
      font-weight: 600;
      font-size: 14px;
      cursor: pointer;
      transition: opacity 0.15s;
      margin-top: 0.5rem;

      &:hover { opacity: 0.9; }
      &:disabled { opacity: 0.5; cursor: not-allowed; }
    }
  `]
})
export class LoginComponent {
  private auth = inject(AuthService);
  private router = inject(Router);

  email = '';
  password = '';
  readonly loading = signal(false);
  readonly errorMsg = signal('');

  login(): void {
    if (this.loading()) return;
    if (!this.email || !this.password) {
      this.errorMsg.set('Please enter your email and password.');
      return;
    }

    this.loading.set(true);
    this.errorMsg.set('');

    this.auth.login({ email: this.email, password: this.password }).subscribe({
      next: () => this.router.navigateByUrl('/'),
      error: (error: HttpErrorResponse) => {
        this.errorMsg.set(loginErrorMessage(error));
        this.loading.set(false);
      }
    });
  }
}

function loginErrorMessage(error: HttpErrorResponse): string {
  switch (error.status) {
    case 400:
    case 401: return 'Invalid email or password.';
    case 429: return 'Too many sign-in attempts. Wait a minute and try again.';
    default: return describeHttpError(error);
  }
}