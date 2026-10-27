import { HttpErrorResponse } from '@angular/common/http';

// Turns an HTTP failure into a sentence for the UI. The API returns RFC 7807 ProblemDetails.
export function describeHttpError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'Something went wrong. Please try again.';
  if (error.status === 0) return 'The API could not be reached. Check that it is running.';
  // Sessions started before the Owner role existed lack it until the next sign-in
  if (error.status === 403) return 'Your session does not have access to this. Sign out and sign in again; if it persists, your account lacks the Owner role.';

  const title = typeof error.error?.title === 'string' ? error.error.title : null;
  return title ? `${title} (HTTP ${error.status})` : `Request failed (HTTP ${error.status}).`;
}
