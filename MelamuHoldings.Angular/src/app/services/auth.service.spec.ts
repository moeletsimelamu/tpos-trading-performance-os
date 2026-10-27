import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { AuthService } from './auth.service';
import { authGuard } from '../guards/auth.guard';
import { environment } from '../../environments/environment';

// A JWT whose payload only carries `exp`; the signature is irrelevant to the client-side expiry check
function fakeToken(expiresInSeconds: number): string {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + expiresInSeconds }));
  return `header.${payload}.signature`;
}

describe('AuthService and authGuard', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: 'login', children: [] }])]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('stores the session and exposes the user after login', () => {
    const auth = TestBed.inject(AuthService);
    const token = fakeToken(3600);

    auth.login({ email: 'owner@melamu.test', password: 'secret' }).subscribe();
    http.expectOne(`${environment.apiUrl}/auth/login`)
      .flush({ token, email: 'owner@melamu.test', displayName: 'Owner', expiresAt: '' });

    expect(auth.user()?.displayName).toBe('Owner');
    expect(auth.getToken()).toBe(token);
    expect(auth.isLoggedIn()).toBe(true);
  });

  it('clears the session on logout', () => {
    const auth = TestBed.inject(AuthService);
    localStorage.setItem('td_token', fakeToken(3600));

    auth.logout();

    expect(auth.getToken()).toBeNull();
    expect(auth.user()).toBeNull();
  });

  it('treats an expired token as signed out', () => {
    localStorage.setItem('td_token', fakeToken(-60));

    expect(TestBed.inject(AuthService).isLoggedIn()).toBe(false);
  });

  it('guard redirects to /login without a valid token', () => {
    const result = TestBed.runInInjectionContext(() => authGuard({} as never, {} as never));

    expect(result instanceof UrlTree).toBe(true);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/login');
  });

  it('guard allows navigation with a valid token', () => {
    localStorage.setItem('td_token', fakeToken(3600));

    expect(TestBed.runInInjectionContext(() => authGuard({} as never, {} as never))).toBe(true);
  });
});
