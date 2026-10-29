import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';
import { describeHttpError } from '../../shared/http-error';
import { BrandMarkComponent } from '../../shared/ui/brand-mark.component';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule, BrandMarkComponent],
  template: `
    <div class="auth-shell">
      <aside class="brand-panel">
        <div class="lockup">
          <app-brand-mark [size]="44" tone="on-dark" />
          <div>
            <div class="wordmark">Melamu Holdings</div>
            <div class="product">Operating System</div>
          </div>
        </div>
        <p class="statement">Trading operations, financial management and capital allocation for Melamu Holdings, kept in one auditable record.</p>
        <p class="footnote">Private system. Access is limited to the operator.</p>
      </aside>

      <main class="form-panel">
        <form class="auth-form" (ngSubmit)="login()">
          <div class="mobile-lockup"><app-brand-mark [size]="32" /><span class="wordmark">Melamu Holdings</span></div>
          <h1>Sign in</h1>
          <p class="sub">Use the operator account created on the server.</p>

          <div class="field">
            <label for="login-email">Email</label>
            <input id="login-email" name="email" type="email" autocomplete="username" [(ngModel)]="email" />
          </div>

          <div class="field">
            <label for="login-password">Password</label>
            <input id="login-password" name="password" type="password" autocomplete="current-password" [(ngModel)]="password" />
          </div>

          @if (errorMsg()) {
            <p class="notice notice-error" role="alert">{{ errorMsg() }}</p>
          }

          <button type="submit" class="btn btn-primary submit" [disabled]="loading()">
            {{ loading() ? 'Signing in…' : 'Sign in' }}
          </button>
        </form>
      </main>
    </div>
  `,
  styles: [`
    .auth-shell { min-height: 100vh; display: grid; grid-template-columns: minmax(320px, 5fr) 7fr; }
    .brand-panel {
      background: var(--navy-900); color: var(--sidebar-text); padding: 48px;
      display: flex; flex-direction: column; gap: 24px;
    }
    .lockup { display: flex; align-items: center; gap: 14px; }
    .wordmark { font-weight: 600; font-size: 14px; letter-spacing: 0.14em; text-transform: uppercase; color: var(--sidebar-text-strong); }
    .product { font-size: 10.5px; letter-spacing: 0.2em; text-transform: uppercase; color: var(--sidebar-muted); margin-top: 2px; }
    .statement { margin-top: auto; max-width: 30ch; font-size: 22px; line-height: 1.4; color: var(--sidebar-text-strong); }
    .footnote { font-size: 12.5px; color: var(--sidebar-muted); }

    .form-panel { display: flex; align-items: center; justify-content: center; padding: 48px 24px; }
    .auth-form { width: 100%; max-width: 360px; display: flex; flex-direction: column; gap: 16px; }
    h1 { font-size: var(--text-xl); }
    .sub { color: var(--text-secondary); margin-top: -10px; }
    .field input { height: 40px; }
    .notice { margin: 0; }
    .submit { height: 40px; margin-top: 4px; }
    .mobile-lockup { display: none; }

    @media (max-width: 799px) {
      .auth-shell { grid-template-columns: 1fr; }
      .brand-panel { display: none; }
      .mobile-lockup { display: flex; align-items: center; gap: 10px; margin-bottom: 16px; .wordmark { color: var(--text-primary); } }
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
