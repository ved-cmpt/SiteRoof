import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionService } from './session.service';

/** Client-side convenience only: every API call is authorized again on the server. */
export const authGuard: CanActivateFn = async (_route, state) => {
  const session = inject(SessionService);
  const router = inject(Router);
  const current = await session.ensureLoaded();
  return current.authenticated ? true : router.createUrlTree(['/signin'], { queryParams: { next: state.url } });
};

export const adminGuard: CanActivateFn = async () => {
  const session = inject(SessionService);
  const router = inject(Router);
  await session.ensureLoaded();
  return session.isAdmin() ? true : router.createUrlTree(['/app/forbidden']);
};
