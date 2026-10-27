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

  it('shows planned modules as labelled, non-interactive items', async () => {
    const el = await render();

    const planned = [...el.querySelectorAll<HTMLElement>('nav .nav-planned')];
    const plannedItems = NAVIGATION.flatMap(s => s.items).filter(i => !i.route);

    expect(planned.length).toBe(plannedItems.length);
    for (const item of planned) {
      expect(item.tagName).toBe('SPAN');
      expect(item.querySelector('a, button')).toBeNull();
      expect(item.textContent).toContain('Planned');
      expect(item.getAttribute('title')).toMatch(/Planned for Phase \d/);
    }
  });

  it('carries the product identity and a sign-out control', async () => {
    const el = await render();

    expect(el.querySelector('.brand')?.textContent).toContain('Melamu Holdings');
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
