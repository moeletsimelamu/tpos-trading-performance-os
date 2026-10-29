import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ShellComponent } from './shell.component';
import { NAVIGATION } from './navigation';

describe('ShellComponent', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    });
  });

  async function render() {
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('links every built page and nothing else', async () => {
    const el = await render();

    const links = [...el.querySelectorAll<HTMLAnchorElement>('nav .nav-link')].map(a => a.getAttribute('href'));
    const builtRoutes = NAVIGATION.flatMap(s => s.items).filter(i => i.route).map(i => i.route);

    expect(links).toEqual(builtRoutes);
  });

  it('lists only destinations that exist, each with an icon', async () => {
    const el = await render();

    const items = NAVIGATION.flatMap(s => s.items);
    expect(items.every(i => i.route.startsWith('/'))).toBe(true);
    expect(el.querySelectorAll('nav .nav-link app-icon').length).toBe(items.length);
    expect(el.textContent).not.toContain('Planned');
  });

  it('carries the product identity and a sign-out control', async () => {
    const el = await render();

    expect(el.querySelector('.brand')?.textContent).toContain('Melamu Holdings');
    expect(el.querySelector('.brand app-brand-mark svg')).not.toBeNull();
    expect([...el.querySelectorAll('button')].some(b => b.textContent?.includes('Sign out'))).toBe(true);
  });

  it('opens and closes the narrow-screen navigation drawer', async () => {
    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();
    const toggle = fixture.nativeElement.querySelector('.menu-button') as HTMLButtonElement;

    toggle.click();
    await fixture.whenStable();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await fixture.whenStable();
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
  });
});
