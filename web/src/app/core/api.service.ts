import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import {
  ActivityItem,
  BlockerSummary,
  Comment,
  ErpSimulatorMode,
  IntegrationHealth,
  MaterialRequest,
  MaterialRequestStatus,
  Milestone,
  MilestoneStatus,
  OutboxEvent,
  Overview,
  Paged,
  Persona,
  ProjectDashboard,
  ProjectDetail,
  ProjectRole,
  ProjectSummary,
  Scenario,
  Session,
  TenantSettings,
  TenantUser,
  WorkOrderDetail,
  WorkOrderListItem,
  WorkOrderPriority,
  WorkOrderQuery,
  WorkOrderTransition,
} from './models';

/** ProblemDetails from the API, normalized. `code` is the stable machine-readable reason. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly title: string,
    readonly detail: string,
    readonly fieldErrors: Record<string, string[]> = {},
    readonly extra: Record<string, unknown> = {},
  ) {
    super(detail || title);
  }

  get isConflict(): boolean {
    return this.status === 409;
  }

  static from(err: unknown): ApiError {
    if (err instanceof ApiError) return err;
    if (err instanceof HttpErrorResponse) {
      const body = (err.error ?? {}) as Record<string, unknown>;
      const status = err.status;
      const code = typeof body['code'] === 'string' ? body['code'] : status === 0 ? 'network' : `http-${status}`;
      const title = typeof body['title'] === 'string' ? body['title'] : status === 0 ? 'Network error' : 'Request failed';
      const detail =
        typeof body['detail'] === 'string'
          ? body['detail']
          : status === 0
            ? 'Could not reach SiteRoof. Check your connection and try again.'
            : status === 401
              ? 'Your session has ended. Start the demo again to continue.'
              : status === 429
                ? 'Too many requests. Wait a moment and try again.'
                : title;
      const errors = (body['errors'] ?? {}) as Record<string, string[]>;
      return new ApiError(status, code, title, detail, errors, body);
    }
    return new ApiError(0, 'unknown', 'Unexpected error', String(err));
  }

  /** First message across all field errors, for compact display. */
  get firstFieldError(): string | null {
    const first = Object.values(this.fieldErrors)[0];
    return first?.[0] ?? null;
  }
}

function params(query: object): HttpParams {
  let p = new HttpParams();
  for (const [k, v] of Object.entries(query)) {
    if (v === undefined || v === null || v === '' || v === false) continue;
    p = p.set(k, String(v));
  }
  return p;
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  private get<T>(url: string, query: object = {}): Observable<T> {
    return this.http.get<T>(url, { params: params(query) }).pipe(catchError((e) => throwError(() => ApiError.from(e))));
  }

  private send<T>(method: 'POST' | 'PATCH' | 'DELETE', url: string, body: unknown = {}): Observable<T> {
    return this.http.request<T>(method, url, { body }).pipe(catchError((e) => throwError(() => ApiError.from(e))));
  }

  // Session & demo
  me = () => this.get<Session>('/api/auth/me');
  login = (email: string, password: string) => this.send<Session>('POST', '/api/auth/login', { email, password });
  logout = () => this.send<void>('POST', '/api/auth/logout');
  startDemo = (persona: Persona) => this.send<Session>('POST', '/api/demo/start', { persona });
  switchPersona = (persona: Persona) => this.send<Session>('POST', '/api/demo/persona', { persona });
  resetDemo = () => this.send<Session>('POST', '/api/demo/reset');
  scenario = () => this.get<Scenario>('/api/demo/scenario');

  // Projects
  projects = () => this.get<ProjectSummary[]>('/api/projects');
  project = (id: string) => this.get<ProjectDetail>(`/api/projects/${id}`);
  createProject = (body: {
    code: string;
    title: string;
    description?: string;
    clientName?: string;
    siteAddress?: string;
    city?: string;
    startDate: string;
    targetDate: string;
  }) => this.send<ProjectDetail>('POST', '/api/projects', body);
  createMilestone = (projectId: string, body: { name: string; dueDate: string }) =>
    this.send<Milestone>('POST', `/api/projects/${projectId}/milestones`, body);
  updateMilestone = (id: string, body: { status?: MilestoneStatus; dueDate?: string; name?: string }) =>
    this.send<Milestone>('PATCH', `/api/milestones/${id}`, body);
  users = () => this.get<TenantUser[]>('/api/users');
  addMember = (projectId: string, body: { userId: string; role: ProjectRole; trade?: string }) =>
    this.send<ProjectDetail>('POST', `/api/projects/${projectId}/members`, body);
  dashboard = (projectId: string) => this.get<ProjectDashboard>(`/api/projects/${projectId}/dashboard`);
  blockerSummary = (projectId: string) => this.get<BlockerSummary>(`/api/projects/${projectId}/blocker-summary`);
  overview = () => this.get<Overview>('/api/overview');

  // Work orders
  workOrders = (projectId: string, query: WorkOrderQuery) =>
    this.get<Paged<WorkOrderListItem>>(`/api/projects/${projectId}/work-orders`, query);
  workOrder = (id: string) => this.get<WorkOrderDetail>(`/api/work-orders/${id}`);
  createWorkOrder = (
    projectId: string,
    body: {
      title: string;
      description?: string;
      workPackage: string;
      assigneeId?: string | null;
      milestoneId?: string | null;
      dueDate: string;
      priority: WorkOrderPriority;
      plannedQuantity: number;
      unit?: string;
    },
  ) => this.send<WorkOrderDetail>('POST', `/api/projects/${projectId}/work-orders`, body);
  updateWorkOrder = (id: string, body: Record<string, unknown> & { version: string }) =>
    this.send<WorkOrderDetail>('PATCH', `/api/work-orders/${id}`, body);
  transition = (id: string, transition: WorkOrderTransition, version: string, reason?: string) =>
    this.send<WorkOrderDetail>('POST', `/api/work-orders/${id}/transitions`, { transition, version, reason });
  addComment = (id: string, body: string) => this.send<Comment>('POST', `/api/work-orders/${id}/comments`, { body });
  addDependency = (id: string, dependsOnWorkOrderId: string) =>
    this.send<WorkOrderDetail>('POST', `/api/work-orders/${id}/dependencies`, { dependsOnWorkOrderId });
  removeDependency = (id: string, dependsOnId: string) =>
    this.send<WorkOrderDetail>('DELETE', `/api/work-orders/${id}/dependencies/${dependsOnId}`);
  addProgress = (id: string, body: { quantity: number; blocker?: string; note?: string }) =>
    this.send<WorkOrderDetail>('POST', `/api/work-orders/${id}/progress`, body);
  verifyProgress = (logId: string) => this.send<WorkOrderDetail>('POST', `/api/progress/${logId}/verify`);
  resolveBlocker = (logId: string) => this.send<WorkOrderDetail>('POST', `/api/progress/${logId}/resolve-blocker`);

  // Materials & integrations
  materialRequests = (query: { status?: MaterialRequestStatus | ''; projectId?: string; page?: number; pageSize?: number }) =>
    this.get<Paged<MaterialRequest>>('/api/material-requests', query);
  workOrderMaterials = (id: string) => this.get<MaterialRequest[]>(`/api/work-orders/${id}/material-requests`);
  submitMaterial = (
    workOrderId: string,
    body: { clientRequestId: string; itemName: string; quantity: number; unit: string; justification?: string },
  ) => this.send<MaterialRequest>('POST', `/api/work-orders/${workOrderId}/material-requests`, body);
  decide = (id: string, decision: 'Approve' | 'Reject', note?: string, version?: string) =>
    this.send<MaterialRequest>('POST', `/api/material-requests/${id}/decision`, { decision, note, version });
  integrationHealth = () => this.get<IntegrationHealth>('/api/integrations/health');
  retryOutbox = (id: string) => this.send<OutboxEvent>('POST', `/api/integrations/outbox/${id}/retry`);

  // Activity & admin
  activity = (query: { projectId?: string; entityType?: string; action?: string; page?: number; pageSize?: number }) =>
    this.get<Paged<ActivityItem>>('/api/activity', query);
  tenant = () => this.get<TenantSettings>('/api/admin/tenant');
  updateTenant = (body: { name?: string; erpSimulatorMode?: ErpSimulatorMode }) =>
    this.send<TenantSettings>('PATCH', '/api/admin/tenant', body);
}
