import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Session } from './models';
import { SessionService } from './session.service';

const demoSession = (persona: 'manager' | 'crew', id: string): Session => ({
  authenticated: true,
  user: {
    id,
    displayName: persona === 'manager' ? 'Dana Whitford' : 'Leah Tremblay',
    jobTitle: null,
    roles: persona === 'manager' ? ['Admin', 'ProjectManager'] : ['CrewMember'],
    persona,
    tenant: { id: 't1', name: 'Demo', isDemo: true, expiresAt: null, erpSimulatorMode: 'FailFirstAttempt' },
  },
});

describe('SessionService', () => {
  let session: SessionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    session = TestBed.inject(SessionService);
    http = TestBed.inject(HttpTestingController);
  });

  it('starts a demo and exposes role-derived state', async () => {
    const started = session.startDemo('manager');
    const req = http.expectOne('/api/demo/start');
    expect(req.request.body).toEqual({ persona: 'manager' });
    req.flush(demoSession('manager', 'u1'));
    await started;
    expect(session.isAuthenticated()).toBe(true);
    expect(session.isDemo()).toBe(true);
    expect(session.isAdmin()).toBe(true);
  });

  it('bumps the identity version when the persona changes so views reload', async () => {
    const started = session.startDemo('manager');
    http.expectOne('/api/demo/start').flush(demoSession('manager', 'u1'));
    await started;
    const before = session.identityVersion();

    const switched = session.switchPersona('crew');
    http.expectOne('/api/demo/persona').flush(demoSession('crew', 'u2'));
    await switched;
    expect(session.identityVersion()).toBe(before + 1);
    expect(session.isAdmin()).toBe(false);
    expect(session.persona()).toBe('crew');
  });

  it('treats a failing /me as signed out', async () => {
    const result = session.refresh();
    http.expectOne('/api/auth/me').flush({}, { status: 500, statusText: 'Server error' });
    expect((await result).authenticated).toBe(false);
    expect(session.loaded()).toBe(true);
  });
});
