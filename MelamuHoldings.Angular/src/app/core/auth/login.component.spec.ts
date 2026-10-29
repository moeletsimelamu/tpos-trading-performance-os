import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { LoginComponent } from './login.component';
import { environment } from '../../../environments/environment';

describe('LoginComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: '', children: [] }])]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render() {
    const fixture = TestBed.createComponent(LoginComponent);
    await fixture.whenStable();
    fixture.componentInstance.email = 'owner@melamu.test';
    fixture.componentInstance.password = 'secret-password';
    return fixture;
  }

  it('asks for both fields before calling the API', async () => {
    const fixture = await render();
    fixture.componentInstance.password = '';

    fixture.componentInstance.login();
    await fixture.whenStable();

    http.expectNone(`${environment.apiUrl}/auth/login`);
    expect(fixture.nativeElement.textContent).toContain('Please enter your email and password.');
  });

  it('sends one request even if submitted twice', async () => {
    const fixture = await render();

    fixture.componentInstance.login();
    fixture.componentInstance.login();

    const requests = http.match(`${environment.apiUrl}/auth/login`);
    expect(requests.length).toBe(1);
    requests[0].flush(null, { status: 401, statusText: 'Unauthorized' });
  });

  it.each([
    [401, 'Invalid email or password.'],
    [429, 'Too many sign-in attempts. Wait a minute and try again.'],
    [0, 'The API could not be reached. Check that it is running.']
  ])('explains a %s response', async (status, message) => {
    const fixture = await render();

    fixture.componentInstance.login();
    http.expectOne(`${environment.apiUrl}/auth/login`).flush(null, { status, statusText: 'Error' });
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(message);
    expect(fixture.componentInstance.loading()).toBe(false);
  });
});
