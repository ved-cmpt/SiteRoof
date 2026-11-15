import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { SessionService } from './session.service';

/** A 401 from any API call means the cookie is gone (expired or demo cleaned up): return to sign-in. */
export const sessionInterceptor: HttpInterceptorFn = (req, next) => {
  const session = inject(SessionService);
  const router = inject(Router);
  return next(req).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401 && req.url.startsWith('/api/') && !req.url.startsWith('/api/auth/')) {
        session.markSignedOut();
        void router.navigate(['/signin'], { queryParams: { expired: 1, next: router.url } });
      }
      return throwError(() => err);
    }),
  );
};
