// TypeScript mirrors of the API DTOs (api/SiteRoof.Api/Features/**). Enums are serialized as strings.

export type ProjectStatus = 'Planning' | 'Active' | 'OnHold' | 'Completed';
export type ProjectRole = 'Manager' | 'Supervisor' | 'Crew';
export type MilestoneStatus = 'Planned' | 'InProgress' | 'Completed';
export type WorkOrderPriority = 'Low' | 'Normal' | 'High' | 'Critical';
export type WorkOrderStatus = 'Open' | 'InProgress' | 'Blocked' | 'Completed' | 'Closed' | 'Cancelled';
export type WorkOrderTransition = 'Start' | 'Block' | 'Unblock' | 'Complete' | 'Reopen' | 'Close' | 'Cancel';
export type MaterialRequestStatus = 'Submitted' | 'Approved' | 'Rejected';
export type ErpTransmissionStatus = 'NotApplicable' | 'Queued' | 'Retrying' | 'Sent' | 'Failed';
export type OutboxStatus = 'Pending' | 'Processing' | 'Succeeded' | 'Failed';
export type ErpSimulatorMode = 'Normal' | 'FailFirstAttempt' | 'Unavailable';
export type Persona = 'manager' | 'supervisor' | 'crew';

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface SessionTenant {
  id: string;
  name: string;
  isDemo: boolean;
  expiresAt: string | null;
  erpSimulatorMode: ErpSimulatorMode;
}

export interface SessionUser {
  id: string;
  displayName: string;
  jobTitle: string | null;
  roles: string[];
  persona: Persona | null;
  tenant: SessionTenant;
}

export interface Session {
  authenticated: boolean;
  user: SessionUser | null;
}

export interface UserRef {
  id: string;
  displayName: string;
}

export interface ProjectSummary {
  id: string;
  code: string;
  title: string;
  status: ProjectStatus;
  city: string | null;
  startDate: string;
  targetDate: string;
  myRole: ProjectRole | null;
  totalWorkOrders: number;
  doneWorkOrders: number;
  overdueWorkOrders: number;
  blockedWorkOrders: number;
  percentComplete: number;
}

export interface Member {
  userId: string;
  displayName: string;
  jobTitle: string | null;
  role: ProjectRole;
  trade: string | null;
  openAssignments: number;
}

export interface Milestone {
  id: string;
  name: string;
  dueDate: string;
  status: MilestoneStatus;
  isOverdue: boolean;
  workOrderCount: number;
  doneWorkOrderCount: number;
  completedAt: string | null;
}

export interface ProjectDetail {
  id: string;
  code: string;
  title: string;
  description: string | null;
  clientName: string | null;
  siteAddress: string | null;
  city: string | null;
  status: ProjectStatus;
  startDate: string;
  targetDate: string;
  myRole: ProjectRole | null;
  permissions: { canManage: boolean; canSupervise: boolean; isMember: boolean };
  members: Member[];
  milestones: Milestone[];
}

export interface WorkOrderListItem {
  id: string;
  projectId: string;
  key: string;
  number: number;
  title: string;
  workPackage: string;
  status: WorkOrderStatus;
  priority: WorkOrderPriority;
  dueDate: string;
  isOverdue: boolean;
  assignee: UserRef | null;
  plannedQuantity: number;
  reportedQuantity: number;
  unit: string | null;
  percentComplete: number;
  openBlockers: number;
  pendingMaterialRequests: number;
}

export interface WorkOrderRef {
  id: string;
  key: string;
  title: string;
  status: WorkOrderStatus;
}

export interface ProgressLog {
  id: string;
  reporter: UserRef;
  quantity: number;
  blocker: string | null;
  note: string | null;
  createdAt: string;
  verifiedBy: UserRef | null;
  verifiedAt: string | null;
  blockerResolvedAt: string | null;
}

export interface Comment {
  id: string;
  author: UserRef;
  body: string;
  createdAt: string;
}

export interface WorkOrderPermissions {
  canEdit: boolean;
  canAssign: boolean;
  canReportProgress: boolean;
  canVerify: boolean;
  canComment: boolean;
  canManageDependencies: boolean;
  canRequestMaterials: boolean;
  canDecideMaterials: boolean;
}

export interface WorkOrderDetail {
  id: string;
  projectId: string;
  projectCode: string;
  projectTitle: string;
  key: string;
  number: number;
  title: string;
  description: string | null;
  workPackage: string;
  status: WorkOrderStatus;
  priority: WorkOrderPriority;
  dueDate: string;
  isOverdue: boolean;
  isLocked: boolean;
  assignee: UserRef | null;
  milestone: { id: string; name: string; dueDate: string } | null;
  plannedQuantity: number;
  reportedQuantity: number;
  unit: string | null;
  percentComplete: number;
  createdBy: UserRef;
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
  closedAt: string | null;
  version: string;
  availableTransitions: WorkOrderTransition[];
  permissions: WorkOrderPermissions;
  dependencies: WorkOrderRef[];
  dependents: WorkOrderRef[];
  progress: ProgressLog[];
  comments: Comment[];
}

export interface ErpStatus {
  status: ErpTransmissionStatus;
  attempts: number;
  nextRetryAt: string | null;
  lastError: string | null;
  externalReference: string | null;
  processedAt: string | null;
  outboxId: string | null;
}

export interface MaterialRequest {
  id: string;
  projectId: string;
  projectCode: string;
  workOrderId: string;
  workOrderKey: string;
  workOrderTitle: string;
  itemName: string;
  quantity: number;
  unit: string;
  justification: string | null;
  status: MaterialRequestStatus;
  requestedBy: UserRef;
  requestedAt: string;
  decidedBy: UserRef | null;
  decidedAt: string | null;
  decisionNote: string | null;
  externalReference: string | null;
  version: string;
  canDecide: boolean;
  erp: ErpStatus;
}

export interface ActivityItem {
  id: number;
  createdAt: string;
  actorId: string | null;
  actorName: string;
  entityType: string;
  entityId: string;
  action: string;
  summary: string;
  projectId: string | null;
  projectCode: string | null;
  before: Record<string, unknown> | null;
  after: Record<string, unknown> | null;
  link: { type: 'workOrder' | 'project'; id: string } | null;
}

export interface StatusCount {
  status: WorkOrderStatus;
  count: number;
}

export interface WorkPackage {
  workPackage: string;
  total: number;
  done: number;
  overdue: number;
  percentComplete: number;
}

export interface Blocker {
  progressLogId: string;
  workOrderId: string;
  workOrderKey: string;
  workOrderTitle: string;
  workOrderStatus: WorkOrderStatus;
  blocker: string;
  reportedBy: string;
  reportedAt: string;
  ageDays: number;
}

export interface PendingApproval {
  id: string;
  workOrderId: string;
  workOrderKey: string;
  itemName: string;
  quantity: number;
  unit: string;
  requestedBy: string;
  requestedAt: string;
  ageDays: number;
}

export interface ProjectDashboard {
  projectId: string;
  code: string;
  title: string;
  status: ProjectStatus;
  targetDate: string;
  kpis: {
    total: number;
    done: number;
    open: number;
    overdue: number;
    blocked: number;
    openBlockers: number;
    pendingApprovals: number;
    criticalOpen: number;
    percentComplete: number;
    daysToTarget: number;
  };
  statusBreakdown: StatusCount[];
  workPackages: WorkPackage[];
  overdueWork: WorkOrderListItem[];
  openBlockers: Blocker[];
  approvalBacklog: PendingApproval[];
  milestones: Milestone[];
  recentActivity: ActivityItem[];
  generatedAt: string;
}

export interface ProjectStatusRow {
  projectId: string;
  code: string;
  title: string;
  status: ProjectStatus;
  counts: StatusCount[];
  overdue: number;
  percentComplete: number;
}

export interface AttentionItem {
  kind: 'erp' | 'overdue' | 'blocker' | 'approval' | 'milestone';
  severity: 'critical' | 'warning' | 'info';
  title: string;
  detail: string;
  projectCode: string | null;
  linkType: 'workOrder' | 'approvals' | 'project';
  linkId: string;
  since: string | null;
}

export interface Overview {
  kpis: {
    activeProjects: number;
    openWorkOrders: number;
    overdueWorkOrders: number;
    blockedWorkOrders: number;
    openBlockers: number;
    pendingApprovals: number;
    erpRetrying: number;
    erpFailed: number;
    percentComplete: number;
  };
  projects: ProjectStatusRow[];
  attention: AttentionItem[];
  myWork: WorkOrderListItem[];
  generatedAt: string;
}

export interface BlockerSummary {
  generator: string;
  label: string;
  headline: string;
  points: { severity: 'critical' | 'warning' | 'info'; text: string; evidence: { type: string; id: string; label: string }[] }[];
  suggestedActions: string[];
  openBlockerCount: number;
  generatedAt: string;
  disclaimer: string;
}

export interface OutboxEvent {
  id: string;
  eventType: string;
  aggregateId: string;
  description: string | null;
  status: OutboxStatus;
  attempts: number;
  nextRetryAt: string;
  lastError: string | null;
  externalReference: string | null;
  createdAt: string;
  processedAt: string | null;
}

export interface IntegrationHealth {
  status: 'healthy' | 'attention' | 'degraded';
  dispatcherEnabled: boolean;
  dispatcherLastRunAt: string | null;
  mockErpReachable: boolean;
  simulatorMode: ErpSimulatorMode;
  maxAttempts: number;
  outbox: { pending: number; retrying: number; processing: number; succeeded: number; failed: number };
  recent: OutboxEvent[];
}

export interface ScenarioStep {
  order: number;
  title: string;
  description: string;
  persona: Persona;
  link: string;
  done: boolean | null;
}

export interface PersonaInfo {
  key: Persona;
  displayName: string;
  jobTitle: string;
  roleLabel: string;
  summary: string;
  active: boolean;
}

export interface Scenario {
  tenantId: string;
  tenantName: string;
  expiresAt: string | null;
  personas: PersonaInfo[];
  steps: ScenarioStep[];
  riverbendProjectId: string | null;
}

export interface TenantUser {
  id: string;
  displayName: string;
  jobTitle: string | null;
  roles: string[];
}

export interface AdminUser {
  id: string;
  displayName: string;
  jobTitle: string | null;
  roles: string[];
  persona: string | null;
  projects: string[];
}

export interface TenantSettings {
  id: string;
  name: string;
  isDemo: boolean;
  createdAt: string;
  expiresAt: string | null;
  erpSimulatorMode: ErpSimulatorMode;
  users: AdminUser[];
  usage: {
    projects: number;
    workOrders: number;
    progressLogs: number;
    materialRequests: number;
    auditEvents: number;
    maxProjects: number;
    maxWorkOrdersPerProject: number;
  };
}

export interface WorkOrderQuery {
  status?: WorkOrderStatus | '';
  assigneeId?: string;
  workPackage?: string;
  priority?: WorkOrderPriority | '';
  overdue?: boolean;
  mine?: boolean;
  search?: string;
  sort?: 'number' | 'due' | 'priority';
  page?: number;
  pageSize?: number;
}
