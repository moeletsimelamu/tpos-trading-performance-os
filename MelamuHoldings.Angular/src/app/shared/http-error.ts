import { HttpErrorResponse } from '@angular/common/http';

// Turns an HTTP failure into a sentence for the UI. The API returns RFC 7807 ProblemDetails.
export function describeHttpError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'Something went wrong. Please try again.';
  if (error.status === 0) return 'The API could not be reached. Check that it is running.';

  const title = typeof error.error?.title === 'string' ? error.error.title : null;
  return title ? `${title} (HTTP ${error.status})` : `Request failed (HTTP ${error.status}).`;
}
