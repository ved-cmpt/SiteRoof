import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { Observable, firstValueFrom } from 'rxjs';
import { ApiError, ApiService } from '../core/api.service';
import { Loader } from '../core/loader';
import {
  MaterialRequest,
  Paged,
  ProjectDetail,
  WorkOrderDetail,
  WorkOrderListItem,
  WorkOrderPriority,
  WorkOrderTransition,
} from '../core/models';
import { NotifyService } from '../core/notify.service';
import { SessionService } from '../core/session.service';
import { IconName } from '../shared/icons.generated';
import {
  AgoPipe,
  ErpStatusComponent,
  IconComponent,
  MaterialStatusComponent,
  MeterComponent,
  PriorityComponent,
  StateComponent,
  StatusChipComponent,
} from '../shared/ui';
import { DecisionDialogComponent, MaterialDialogComponent, ReasonDialogComponent, ReasonData } from './dialogs';

interface TransitionUi {
  label: string;
  icon: IconName;
  primary?: boolean;
  reason?: ReasonData;
}

const TRANSITIONS: Record<WorkOrderTransition, TransitionUi> = {
  Start: { label: 'Start work', icon: 'trending_up', primary: true },
  Block: { label: 'Mark blocked', icon: 'block', reason: { title: 'Mark as blocked', label: 'What is blocking the work?', confirm: 'Mark blocked', required: false } },
  Unblock: { label: 'Resume work', icon: 'trending_up', primary: true },
  Complete: { label: 'Mark complete', icon: 'task_alt', primary: true },
  Reopen: { label: 'Reopen', icon: 'lock_open', reason: { title: 'Reopen work order', label: 'Why is it being reopened?', confirm: 'Reopen', required: true } },
  Close: { label: 'Close out', icon: 'lock', primary: true },
  Cancel: { label: 'Cancel work order', icon: 'close', reason: { title: 'Cancel work order', label: 'Reason for cancelling', confirm: 'Cancel work order', required: true } },
};

@Component({
  selector: 'sr-work-order',
  imports: [
    RouterLink,
    FormsModule,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    IconComponent,
    StateComponent,
    StatusChipComponent,
    PriorityComponent,
    MeterComponent,
    MaterialStatusComponent,
    ErpStatusComponent,
    AgoPipe,

  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './work-order.page.html',
  styleUrl: './work-order.page.scss',
})
export class WorkOrderPage implements OnInit {
  readonly id = input.required<string>();
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly fb = inject(FormBuilder);
  protected readonly session = inject(SessionService);

  protected readonly wo = new Loader<WorkOrderDetail>();
  protected readonly materials = new Loader<MaterialRequest[]>();
  protected readonly project = new Loader<ProjectDetail>();
  protected readonly siblings = new Loader<Paged<WorkOrderListItem>>();
  protected readonly conflict = signal<ApiError | null>(null);
  protected readonly busy = signal(false);
  protected readonly editing = signal(false);
  protected readonly transitions = TRANSITIONS;
  protected readonly priorities: WorkOrderPriority[] = ['Low', 'Normal', 'High', 'Critical'];
  protected readonly newDependency = signal('');
  protected commentText = '';

  protected readonly progressForm = this.fb.group({
    quantity: [null as number | null, [Validators.min(0)]],
    blocker: [''],
    note: [''],
  });
  protected readonly editForm = this.fb.group({
    title: ['', [Validators.required, Validators.minLength(3)]],
    priority: ['Normal' as WorkOrderPriority],
    dueDate: ['', Validators.required],
    assigneeId: [null as string | null],
    plannedQuantity: [0, [Validators.min(0)]],
  });

  protected readonly dependencyOptions = computed(() => {
    const w = this.wo.data();
    const taken = new Set([w?.id, ...(w?.dependencies ?? []).map((d) => d.id)]);
    return (this.siblings.data()?.items ?? []).filter((s) => !taken.has(s.id) && s.status !== 'Cancelled');
  });
  protected readonly myId = computed(() => this.session.user()?.id);

  ngOnInit(): void {
    this.reload();
  }

  protected reload(): void {
    this.conflict.set(null);
    this.wo.load(this.api.workOrder(this.id()));
    this.materials.load(this.api.workOrderMaterials(this.id()));
  }

  protected startEdit(): void {
    const w = this.wo.data()!;
    this.editForm.reset({ title: w.title, priority: w.priority, dueDate: w.dueDate, assigneeId: w.assignee?.id ?? null, plannedQuantity: w.plannedQuantity });
    if (!this.project.data()) this.project.load(this.api.project(w.projectId));
    this.editing.set(true);
  }

  protected loadSiblings(): void {
    const w = this.wo.data();
    if (w && !this.siblings.data()) this.siblings.load(this.api.workOrders(w.projectId, { pageSize: 100 }));
  }

  /** Runs a mutation that returns the fresh work order; a 409 concurrency conflict shows the banner instead of a toast. */
  private async mutate(call: Observable<WorkOrderDetail>, success: string): Promise<boolean> {
    this.busy.set(true);
    try {
      const updated = await firstValueFrom(call);
      this.wo.data.set(updated);
      this.conflict.set(null);
      this.notify.success(success);
      return true;
    } catch (err) {
      const e = ApiError.from(err);
      if (e.code === 'concurrency-conflict') {
        this.conflict.set(e);
      } else {
        this.notify.error(e);
      }
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  protected async transition(t: WorkOrderTransition): Promise<void> {
    const w = this.wo.data()!;
    const ui = TRANSITIONS[t];
    let reason: string | undefined;
    if (ui.reason) {
      reason = await firstValueFrom(this.dialog.open(ReasonDialogComponent, { data: ui.reason }).afterClosed());
      if (reason === undefined) return;
    }
    await this.mutate(this.api.transition(w.id, t, w.version, reason || undefined), `${w.key}: ${ui.label.toLowerCase()} done.`);
  }

  protected async saveEdit(): Promise<void> {
    this.editForm.markAllAsTouched();
    if (this.editForm.invalid) return;
    const w = this.wo.data()!;
    const v = this.editForm.getRawValue();
    const body: Record<string, unknown> & { version: string } = {
      version: w.version,
      title: v.title,
      priority: v.priority,
      dueDate: v.dueDate,
      plannedQuantity: Number(v.plannedQuantity ?? 0),
    };
    if (v.assigneeId) body['assigneeId'] = v.assigneeId;
    else if (w.assignee) body['unassign'] = true;
    if (await this.mutate(this.api.updateWorkOrder(w.id, body), `${w.key} updated.`)) this.editing.set(false);
  }

  protected async reportProgress(): Promise<void> {
    this.progressForm.markAllAsTouched();
    const v = this.progressForm.getRawValue();
    const quantity = Number(v.quantity ?? 0);
    if (this.progressForm.invalid || quantity < 0) return;
    if (quantity === 0 && !v.blocker?.trim() && !v.note?.trim()) {
      this.notify.error(new ApiError(400, 'validation', 'Missing details', 'Report a quantity, a blocker or a note.'));
      return;
    }
    const w = this.wo.data()!;
    const ok = await this.mutate(
      this.api.addProgress(w.id, { quantity, blocker: v.blocker?.trim() || undefined, note: v.note?.trim() || undefined }),
      'Progress reported.',
    );
    if (ok) this.progressForm.reset({ quantity: null, blocker: '', note: '' });
  }

  protected verify(logId: string): void {
    void this.mutate(this.api.verifyProgress(logId), 'Progress verified.');
  }

  protected resolve(logId: string): void {
    void this.mutate(this.api.resolveBlocker(logId), 'Blocker resolved.');
  }

  protected async addComment(): Promise<void> {
    const body = this.commentText.trim();
    if (!body) return;
    try {
      const c = await firstValueFrom(this.api.addComment(this.id(), body));
      const w = this.wo.data()!;
      this.wo.data.set({ ...w, comments: [...w.comments, c] });
      this.commentText = '';
      this.notify.success('Comment added.');
    } catch (err) {
      this.notify.error(err);
    }
  }

  protected async addDependency(): Promise<void> {
    const dep = this.newDependency();
    if (!dep) return;
    if (await this.mutate(this.api.addDependency(this.id(), dep), 'Dependency added.')) this.newDependency.set('');
  }

  protected removeDependency(depId: string): void {
    void this.mutate(this.api.removeDependency(this.id(), depId), 'Dependency removed.');
  }

  protected requestMaterials(): void {
    const w = this.wo.data()!;
    this.dialog
      .open(MaterialDialogComponent, { data: { workOrderId: w.id, key: w.key } })
      .afterClosed()
      .subscribe((mr?: MaterialRequest) => {
        if (!mr) return;
        this.notify.success('Material request submitted for approval.');
        this.materials.load(this.api.workOrderMaterials(w.id));
      });
  }

  protected decide(request: MaterialRequest, decision: 'Approve' | 'Reject'): void {
    this.dialog
      .open(DecisionDialogComponent, { data: { request, decision } })
      .afterClosed()
      .subscribe((result?: MaterialRequest) => {
        if (!result) return;
        this.notify.success(decision === 'Approve' ? 'Approved and queued for the ERP.' : 'Request rejected.');
        this.materials.load(this.api.workOrderMaterials(this.id()));
      });
  }
}
