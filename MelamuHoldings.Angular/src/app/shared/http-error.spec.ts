import { HttpErrorResponse } from '@angular/common/http';
import { describeHttpError } from './http-error';

describe('describeHttpError', () => {
  it('explains an unreachable API', () => {
    expect(describeHttpError(new HttpErrorResponse({ status: 0 })))
      .toBe('The API could not be reached. Check that it is running.');
  });

  it('uses the ProblemDetails title when the API sends one', () => {
    const error = new HttpErrorResponse({ status: 400, error: { title: 'Only CSV files are accepted.' } });

    expect(describeHttpError(error)).toBe('Only CSV files are accepted. (HTTP 400)');
  });

  it('falls back to the status code', () => {
    expect(describeHttpError(new HttpErrorResponse({ status: 503 }))).toBe('Request failed (HTTP 503).');
  });

  it('handles non-HTTP errors', () => {
    expect(describeHttpError(new Error('boom'))).toBe('Something went wrong. Please try again.');
  });
});
