import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { ApiError, ApiService } from '../core/api.service';
import { MaterialRequest, Member, Milestone, WorkOrderDetail, WorkOrderPriority } from '../core/models';
import { newId } from '../core/uuid';

// ----------------------------------------------------------------------------- confirm

export interface ConfirmData {
  title: string;
  body: string;
  confirm: string;
}

@Component({
  selector: 'sr-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content><p>{{ data.body }}</p></mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button [mat-dialog-close]="true" data-testid="confirm">{{ data.confirm }}</button>
    </mat-dialog-actions>
  `,
})
export class ConfirmDialogComponent {
  protected readonly data = inject<ConfirmData>(MAT_DIALOG_DATA);
}

// ----------------------------------------------------------------------------- reason

export interface ReasonData {
  title: string;
  label: string;
  confirm: string;
  required: boolean;
}

@Component({
  selector: 'sr-reason-dialog',
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      <mat-form-field appearance="outline">
        <mat-label>{{ data.label }}</mat-label>
        <textarea matInput [formControl]="reason" rows="3" maxlength="500" cdkFocusInitial></textarea>
        @if (reason.hasError('required')) {
          <mat-error>A reason is required.</mat-error>
        }
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button (click)="ok()" data-testid="reason-confirm">{{ data.confirm }}</button>
    </mat-dialog-actions>
  `,
})
export class ReasonDialogComponent {
  protected readonly data = inject<ReasonData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<ReasonDialogComponent, string>);
  protected readonly reason = inject(FormBuilder).nonNullable.control('', this.data.required ? Validators.required : []);

  protected ok(): void {
    this.reason.markAsTouched();
    if (this.reason.invalid) return;
    this.ref.close(this.reason.value.trim());
  }
}

// ----------------------------------------------------------------------------- work order form

export interface WorkOrderFormData {
  projectId: string;
  members: Member[];
  milestones: Milestone[];
  packages: string[];
}

@Component({
  selector: 'sr-work-order-dialog',
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>New work order</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content class="grid">
        <mat-form-field appearance="outline" class="full">
          <mat-label>Title</mat-label>
          <input matInput formControlName="title" maxlength="160" required data-testid="wo-title" />
          <mat-error>Enter a title of at least 3 characters.</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Work package</mat-label>
          <input matInput formControlName="workPackage" [attr.list]="'packages'" required data-testid="wo-package" />
          <datalist id="packages">
            @for (p of data.packages; track p) {
              <option [value]="p"></option>
            }
          </datalist>
          <mat-hint>e.g. Drywall, Electrical</mat-hint>
          <mat-error>Enter a work package.</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Assignee</mat-label>
          <mat-select formControlName="assigneeId" data-testid="wo-assignee">
            <mat-option [value]="null">Unassigned</mat-option>
            @for (m of data.members; track m.userId) {
              <mat-option [value]="m.userId">{{ m.displayName }} · {{ m.trade || m.role }}</mat-option>
            }
          </mat-select>
          <mat-hint>Only this project's roster can be assigned.</mat-hint>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Due date</mat-label>
          <input matInput type="date" formControlName="dueDate" required data-testid="wo-due" />
          <mat-error>Choose a due date.</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Priority</mat-label>
          <mat-select formControlName="priority">
            @for (p of priorities; track p) {
              <mat-option [value]="p">{{ p }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Planned quantity</mat-label>
          <input matInput type="number" min="0" formControlName="plannedQuantity" />
          <mat-error>Quantity can't be negative.</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Unit</mat-label>
          <input matInput formControlName="unit" maxlength="16" placeholder="m², lm, ea" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full">
          <mat-label>Milestone</mat-label>
          <mat-select formControlName="milestoneId">
            <mat-option [value]="null">None</mat-option>
            @for (m of data.milestones; track m.id) {
              <mat-option [value]="m.id">{{ m.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" class="full">
          <mat-label>Description</mat-label>
          <textarea matInput formControlName="description" rows="3" maxlength="4000"></textarea>
        </mat-form-field>
        @if (error()) {
          <p class="err full" role="alert">{{ error()!.firstFieldError || error()!.detail }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button mat-flat-button type="submit" [disabled]="busy()" data-testid="wo-save">Create work order</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px; min-width: min(640px, 80vw); }
    .full { grid-column: 1 / -1; }
    .err { color: var(--sr-critical); }
    @media (max-width: 600px) { .grid { grid-template-columns: 1fr; min-width: 0; } }
  `,
})
export class WorkOrderDialogComponent {
  protected readonly data = inject<WorkOrderFormData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<WorkOrderDialogComponent, WorkOrderDetail>);
  private readonly api = inject(ApiService);
  protected readonly priorities: WorkOrderPriority[] = ['Low', 'Normal', 'High', 'Critical'];
  protected readonly busy = signal(false);
  protected readonly error = signal<ApiError | null>(null);

  protected readonly form = inject(FormBuilder).group({
    title: ['', [Validators.required, Validators.minLength(3)]],
    workPackage: ['', [Validators.required, Validators.minLength(2)]],
    assigneeId: [null as string | null],
    milestoneId: [null as string | null],
    dueDate: [new Date(Date.now() + 7 * 864e5).toISOString().slice(0, 10), Validators.required],
    priority: ['Normal' as WorkOrderPriority],
    plannedQuantity: [0, [Validators.min(0)]],
    unit: [''],
    description: [''],
  });

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set(null);
    const v = this.form.getRawValue();
    try {
      const created = await firstValueFrom(
        this.api.createWorkOrder(this.data.projectId, {
          title: v.title!,
          workPackage: v.workPackage!,
          assigneeId: v.assigneeId,
          milestoneId: v.milestoneId,
          dueDate: v.dueDate!,
          priority: v.priority!,
          plannedQuantity: Number(v.plannedQuantity ?? 0),
          unit: v.unit || undefined,
          description: v.description || undefined,
        }),
      );
      this.ref.close(created);
    } catch (err) {
      this.error.set(ApiError.from(err));
    } finally {
      this.busy.set(false);
    }
  }
}

// ----------------------------------------------------------------------------- material request

@Component({
  selector: 'sr-material-dialog',
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Request materials · {{ data.key }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content class="grid">
        <mat-form-field appearance="outline" class="full">
          <mat-label>Item</mat-label>
          <input matInput formControlName="itemName" maxlength="160" required data-testid="mr-item" />
          <mat-error>Describe the item.</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Quantity</mat-label>
          <input matInput type="number" min="0.01" step="any" formControlName="quantity" required data-testid="mr-qty" />
          <mat-error>Quantity must be greater than zero.</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Unit</mat-label>
          <input matInput formControlName="unit" maxlength="16" required placeholder="sheets, pails, ea" data-testid="mr-unit" />
          <mat-error>Enter a unit.</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline" class="full">
          <mat-label>Why is it needed?</mat-label>
          <textarea matInput formControlName="justification" rows="2" maxlength="1000"></textarea>
        </mat-form-field>
        @if (error()) {
          <p class="err full" role="alert">{{ error()!.firstFieldError || error()!.detail }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button mat-flat-button type="submit" [disabled]="busy()" data-testid="mr-submit">Submit for approval</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px; min-width: min(520px, 80vw); }
    .full { grid-column: 1 / -1; }
    .err { color: var(--sr-critical); }
  `,
})
export class MaterialDialogComponent {
  protected readonly data = inject<{ workOrderId: string; key: string }>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<MaterialDialogComponent, MaterialRequest>);
  private readonly api = inject(ApiService);
  protected readonly busy = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  /** Generated once per dialog: a double submit or retry after a timeout cannot create a duplicate. */
  private readonly clientRequestId = newId();

  protected readonly form = inject(FormBuilder).group({
    itemName: ['', [Validators.required, Validators.minLength(2)]],
    quantity: [null as number | null, [Validators.required, Validators.min(0.01)]],
    unit: ['', Validators.required],
    justification: [''],
  });

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set(null);
    const v = this.form.getRawValue();
    try {
      const mr = await firstValueFrom(
        this.api.submitMaterial(this.data.workOrderId, {
          clientRequestId: this.clientRequestId,
          itemName: v.itemName!,
          quantity: Number(v.quantity),
          unit: v.unit!,
          justification: v.justification || undefined,
        }),
      );
      this.ref.close(mr);
    } catch (err) {
      this.error.set(ApiError.from(err));
    } finally {
      this.busy.set(false);
    }
  }
}

// ----------------------------------------------------------------------------- decision

@Component({
  selector: 'sr-decision-dialog',
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.decision === 'Approve' ? 'Approve' : 'Reject' }} material request</h2>
    <mat-dialog-content>
      <p>
        <strong>{{ data.request.quantity }} {{ data.request.unit }} {{ data.request.itemName }}</strong> for
        {{ data.request.workOrderKey }}, requested by {{ data.request.requestedBy.displayName }}.
      </p>
      @if (data.decision === 'Approve') {
        <p class="sr-muted">Approving records an audit entry and queues a requisition to the ERP in the same transaction. Decisions can't be reversed.</p>
      }
      <mat-form-field appearance="outline">
        <mat-label>{{ data.decision === 'Reject' ? 'Reason (required)' : 'Note (optional)' }}</mat-label>
        <textarea matInput [formControl]="note" rows="3" maxlength="1000" data-testid="decision-note"></textarea>
        @if (note.hasError('required')) {
          <mat-error>Give the crew a reason.</mat-error>
        }
      </mat-form-field>
      @if (error()) {
        <p class="err" role="alert">{{ error()!.detail }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button (click)="submit()" [disabled]="busy()" data-testid="decision-confirm">
        {{ data.decision === 'Approve' ? 'Approve request' : 'Reject request' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `.err { color: var(--sr-critical); }`,
})
export class DecisionDialogComponent {
  protected readonly data = inject<{ request: MaterialRequest; decision: 'Approve' | 'Reject' }>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<DecisionDialogComponent, MaterialRequest>);
  private readonly api = inject(ApiService);
  protected readonly busy = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly note = inject(FormBuilder).nonNullable.control('', this.data.decision === 'Reject' ? Validators.required : []);

  protected async submit(): Promise<void> {
    this.note.markAsTouched();
    if (this.note.invalid) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const result = await firstValueFrom(
        this.api.decide(this.data.request.id, this.data.decision, this.note.value.trim() || undefined, this.data.request.version),
      );
      this.ref.close(result);
    } catch (err) {
      this.error.set(ApiError.from(err));
    } finally {
      this.busy.set(false);
    }
  }
}
