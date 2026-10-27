import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Title } from '@angular/platform-browser';
import { Router, TitleStrategy, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';
import { PageTitleStrategy } from './core/layout/page-title.strategy';

function signIn(): void {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600 }));
  localStorage.setItem('td_token', `header.${payload}.signature`);
}

describe('app routes', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        { provide: TitleStrategy, useClass: PageTitleStrategy },
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
  });

  it.each([
    ['/dashboard', '/trading/performance'],
    ['/trades', '/trading/trades'],
    ['/upload', '/trading/import'],
    ['/behaviour', '/trading/behaviour'],
    ['/', '/overview'],
    ['/trading', '/trading/performance'],
    ['/no-such-page', '/overview']
  ])('sends %s to %s when signed in', async (from, to) => {
    signIn();
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl(from);

    expect(TestBed.inject(Router).url).toBe(to);
  });

  it('sends signed-out visitors to the login page', async () => {
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/trading/trades');

    expect(TestBed.inject(Router).url).toBe('/login');
  });

  it('titles pages with the product name', async () => {
    signIn();
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/trading/import');

    expect(TestBed.inject(Title).getTitle()).toBe('Import trades · Melamu Holdings OS');
  });
});
