import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { ApiError, ApiService } from './api.service';

describe('ApiError.from', () => {
  it('keeps the ProblemDetails code, detail and field errors', () => {
    const err = ApiError.from(
      new HttpErrorResponse({
        status: 400,
        error: { title: 'One or more fields are invalid.', code: 'validation', errors: { Quantity: ['Quantity must be between 0 and 100,000.'] } },
      }),
    );
    expect(err.status).toBe(400);
    expect(err.code).toBe('validation');
    expect(err.firstFieldError).toBe('Quantity must be between 0 and 100,000.');
  });

  it('flags concurrency conflicts and keeps extension members', () => {
    const err = ApiError.from(
      new HttpErrorResponse({
        status: 409,
        error: { title: 'Conflict', code: 'concurrency-conflict', detail: 'Changed by someone else.', currentStatus: 'Blocked' },
      }),
    );
    expect(err.isConflict).toBe(true);
    expect(err.detail).toBe('Changed by someone else.');
    expect(err.extra['currentStatus']).toBe('Blocked');
  });

  it('gives readable messages for network failures, 401 and 429 without a body', () => {
    expect(ApiError.from(new HttpErrorResponse({ status: 0 })).code).toBe('network');
    expect(ApiError.from(new HttpErrorResponse({ status: 401 })).detail).toContain('session has ended');
    expect(ApiError.from(new HttpErrorResponse({ status: 429 })).detail).toContain('Too many requests');
  });
});

describe('ApiService', () => {
  let api: ApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('drops empty filters from work-order queries', async () => {
    const result = firstValueFrom(api.workOrders('p1', { status: '', overdue: true, mine: false, search: 'drywall', pageSize: 100 }));
    const req = http.expectOne((r) => r.url === '/api/projects/p1/work-orders');
    expect(req.request.params.keys().sort()).toEqual(['overdue', 'pageSize', 'search']);
    expect(req.request.params.get('overdue')).toBe('true');
    req.flush({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 });
    expect((await result).totalCount).toBe(0);
  });

  it('sends the version with status transitions and maps errors to ApiError', async () => {
    const result = firstValueFrom(api.transition('w1', 'Complete', 'AAAA'));
    const req = http.expectOne('/api/work-orders/w1/transitions');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ transition: 'Complete', version: 'AAAA', reason: undefined });
    req.flush({ code: 'dependencies-incomplete', detail: 'RBL-008 depends on RBL-006.' }, { status: 422, statusText: 'Unprocessable' });
    await expect(result).rejects.toMatchObject({ status: 422, code: 'dependencies-incomplete' });
  });

  it('submits material requests with the client idempotency key', () => {
    api.submitMaterial('w1', { clientRequestId: 'abc', itemName: 'Board', quantity: 40, unit: 'sheets' }).subscribe();
    const req = http.expectOne('/api/work-orders/w1/material-requests');
    expect(req.request.body.clientRequestId).toBe('abc');
    req.flush({});
  });
});
