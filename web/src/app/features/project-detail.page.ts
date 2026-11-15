import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTabsModule } from '@angular/material/tabs';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, ApiService } from '../core/api.service';
import { Loader } from '../core/loader';
import {
  BlockerSummary,
  MilestoneStatus,
  Paged,
  ProjectDashboard,
  ProjectDetail,
  ProjectRole,
  TenantUser,
  WorkOrderDetail,
  WorkOrderListItem,
  WorkOrderPriority,
  WorkOrderStatus,
} from '../core/models';
import { NotifyService } from '../core/notify.service';
import { PackageChartComponent } from '../shared/charts';
import {
  AgoPipe,
  DayPipe,
  IconComponent,
  KpiComponent,
  MeterComponent,
  PriorityComponent,
  StateComponent,
  StatusChipComponent,
  StatusLabelPipe,
} from '../shared/ui';
import { ActivityFeedComponent } from './activity-feed';
import { WorkOrderDialogComponent } from './dialogs';

const COLUMNS: WorkOrderStatus[] = ['Open', 'InProgress', 'Blocked', 'Completed', 'Closed'];

@Component({
  selector: 'sr-project-detail',
  imports: [
    RouterLink,
    FormsModule,
    ReactiveFormsModule,
    MatTabsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    IconComponent,
    KpiComponent,
    StateComponent,
    StatusChipComponent,
    PriorityComponent,
    MeterComponent,
    PackageChartComponent,
    ActivityFeedComponent,
    StatusLabelPipe,
    DayPipe,
    AgoPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './project-detail.page.html',
  styleUrl: './project-detail.page.scss',
})
export class ProjectDetailPage implements OnInit {
  readonly id = input.required<string>();
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly fb = inject(FormBuilder);

  protected readonly project = new Loader<ProjectDetail>();
  protected readonly board = new Loader<Paged<WorkOrderListItem>>();
  protected readonly dashboard = new Loader<ProjectDashboard>();
  protected readonly summary = new Loader<BlockerSummary>();
  protected readonly users = new Loader<TenantUser[]>();

  protected readonly tab = signal(0);
  protected readonly view = signal<'board' | 'list'>('board');
  protected readonly columns = COLUMNS;

  // Board filters
  protected readonly fStatus = signal<WorkOrderStatus | ''>('');
  protected readonly fPackage = signal('');
  protected readonly fAssignee = signal('');
  protected readonly fPriority = signal<WorkOrderPriority | ''>('');
  protected readonly fOverdue = signal(false);
  protected readonly fMine = signal(false);
  protected readonly fSearch = signal('');
  protected readonly sort = signal<'number' | 'due' | 'priority'>('number');

  protected readonly packages = computed(() => [...new Set((this.dashboard.data()?.workPackages ?? []).map((p) => p.workPackage))].sort());
  protected readonly byColumn = computed(() => {
    const items = this.board.data()?.items ?? [];
    return COLUMNS.map((status) => ({ status, items: items.filter((w) => w.status === status) }));
  });
  protected readonly cancelled = computed(() => (this.board.data()?.items ?? []).filter((w) => w.status === 'Cancelled'));
  protected readonly filterCount = computed(
    () =>
      [this.fStatus(), this.fPackage(), this.fAssignee(), this.fPriority(), this.fSearch()].filter(Boolean).length +
      (this.fOverdue() ? 1 : 0) +
      (this.fMine() ? 1 : 0),
  );
  protected readonly nonMembers = computed(() => {
    const members = new Set((this.project.data()?.members ?? []).map((m) => m.userId));
    return (this.users.data() ?? []).filter((u) => !members.has(u.id));
  });

  protected readonly milestoneForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(3)]],
    dueDate: ['', Validators.required],
  });
  protected readonly memberForm = this.fb.group({
    userId: ['', Validators.required],
    role: ['Crew' as ProjectRole, Validators.required],
    trade: [''],
  });
  protected readonly milestoneStatuses: MilestoneStatus[] = ['Planned', 'InProgress', 'Completed'];
  protected readonly priorities: WorkOrderPriority[] = ['Low', 'Normal', 'High', 'Critical'];
  protected readonly statuses: WorkOrderStatus[] = ['Open', 'InProgress', 'Blocked', 'Completed', 'Closed', 'Cancelled'];

  ngOnInit(): void {
    const q = this.route.snapshot.queryParamMap;
    if (q.get('overdue') === 'true') this.fOverdue.set(true);
    if (q.get('tab') === 'dashboard') this.tab.set(1);
    this.project.load(this.api.project(this.id()));
    this.loadBoard();
    this.loadDashboard();
  }

  protected forbidden(e: ApiError | null): boolean {
    return e?.status === 403;
  }

  protected notFound(e: ApiError | null): boolean {
    return e?.status === 404;
  }

  protected loadBoard(): void {
    this.board.load(
      this.api.workOrders(this.id(), {
        status: this.fStatus(),
        workPackage: this.fPackage(),
        assigneeId: this.fAssignee() || undefined,
        priority: this.fPriority(),
        overdue: this.fOverdue(),
        mine: this.fMine(),
        search: this.fSearch().trim(),
        sort: this.sort(),
        pageSize: 100,
      }),
    );
  }

  protected loadDashboard(): void {
    this.dashboard.load(this.api.dashboard(this.id()));
    this.summary.load(this.api.blockerSummary(this.id()));
  }

  protected clearFilters(): void {
    this.fStatus.set('');
    this.fPackage.set('');
    this.fAssignee.set('');
    this.fPriority.set('');
    this.fOverdue.set(false);
    this.fMine.set(false);
    this.fSearch.set('');
    void this.router.navigate([], { queryParams: {}, replaceUrl: true });
    this.loadBoard();
  }

  protected onTab(index: number): void {
    this.tab.set(index);
    if (index === 1) this.loadDashboard();
    if (index === 2 && this.project.data()?.permissions.canManage && !this.users.data()) this.users.load(this.api.users());
  }

  protected newWorkOrder(): void {
    const p = this.project.data();
    if (!p) return;
    this.dialog
      .open(WorkOrderDialogComponent, {
        data: { projectId: p.id, members: p.members, milestones: p.milestones, packages: this.packages() },
        autoFocus: 'first-tabbable',
      })
      .afterClosed()
      .subscribe((created?: WorkOrderDetail) => {
        if (!created) return;
        this.notify.success(`Created ${created.key}.`);
        this.loadBoard();
        this.loadDashboard();
        void this.router.navigate(['/app/work-orders', created.id]);
      });
  }

  protected async addMilestone(): Promise<void> {
    this.milestoneForm.markAllAsTouched();
    if (this.milestoneForm.invalid) return;
    try {
      await firstValueFrom(this.api.createMilestone(this.id(), this.milestoneForm.getRawValue()));
      this.milestoneForm.reset();
      this.notify.success('Milestone added.');
      this.project.load(this.api.project(this.id()));
    } catch (err) {
      this.notify.error(err);
    }
  }

  protected async setMilestoneStatus(id: string, status: MilestoneStatus): Promise<void> {
    try {
      await firstValueFrom(this.api.updateMilestone(id, { status }));
      this.notify.success('Milestone updated.');
      this.project.load(this.api.project(this.id()));
    } catch (err) {
      this.notify.error(err);
    }
  }

  protected async addMember(): Promise<void> {
    this.memberForm.markAllAsTouched();
    if (this.memberForm.invalid) return;
    const v = this.memberForm.getRawValue();
    try {
      const updated = await firstValueFrom(
        this.api.addMember(this.id(), { userId: v.userId!, role: v.role!, trade: v.trade || undefined }),
      );
      this.project.data.set(updated);
      this.memberForm.reset({ userId: '', role: 'Crew', trade: '' });
      this.notify.success('Added to the roster.');
    } catch (err) {
      this.notify.error(err);
    }
  }

  protected severityIcon(s: string) {
    return s === 'critical' ? 'error' : s === 'warning' ? 'warning' : 'info';
  }

  protected evidenceLink(e: { type: string; id: string }, blockers: ProjectDashboard['openBlockers']): unknown[] | null {
    if (e.type === 'workOrder') return ['/app/work-orders', e.id];
    if (e.type === 'progressLog') {
      const b = blockers.find((x) => x.progressLogId === e.id);
      return b ? ['/app/work-orders', b.workOrderId] : null;
    }
    if (e.type === 'materialRequest') return ['/app/approvals'];
    return null;
  }
}
