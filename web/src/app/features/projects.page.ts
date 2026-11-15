import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, ApiService } from '../core/api.service';
import { Loader } from '../core/loader';
import { ProjectDetail, ProjectSummary } from '../core/models';
import { NotifyService } from '../core/notify.service';
import { SessionService } from '../core/session.service';
import { StateComponent } from '../shared/ui';

@Component({
  selector: 'sr-project-dialog',
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>New project</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content class="grid">
        <mat-form-field appearance="outline">
          <mat-label>Code</mat-label>
          <input matInput formControlName="code" maxlength="20" required placeholder="e.g. WHS" />
          <mat-error>Use 2–20 uppercase letters, digits or dashes.</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Title</mat-label>
          <input matInput formControlName="title" maxlength="160" required />
          <mat-error>Enter a title.</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Start date</mat-label>
          <input matInput type="date" formControlName="startDate" required />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Target date</mat-label>
          <input matInput type="date" formControlName="targetDate" required />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Client (fictional)</mat-label>
          <input matInput formControlName="clientName" maxlength="160" />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>City</mat-label>
          <input matInput formControlName="city" maxlength="80" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full">
          <mat-label>Site address (fictional)</mat-label>
          <input matInput formControlName="siteAddress" maxlength="200" />
        </mat-form-field>
        @if (error()) {
          <p class="err full" role="alert">{{ error()!.firstFieldError || error()!.detail }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button mat-flat-button type="submit" [disabled]="busy()">Create project</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px; min-width: min(600px, 80vw); }
    .full { grid-column: 1 / -1; }
    .err { color: var(--sr-critical); }
  `,
})
export class ProjectDialogComponent {
  private readonly api = inject(ApiService);
  private readonly ref = inject(MatDialogRef<ProjectDialogComponent, ProjectDetail>);
  protected readonly busy = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/^[A-Z0-9-]{2,20}$/)]],
    title: ['', [Validators.required, Validators.minLength(3)]],
    startDate: [new Date().toISOString().slice(0, 10), Validators.required],
    targetDate: [new Date(Date.now() + 120 * 864e5).toISOString().slice(0, 10), Validators.required],
    clientName: [''],
    city: ['Edmonton, AB'],
    siteAddress: [''],
  });

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      this.ref.close(await firstValueFrom(this.api.createProject(this.form.getRawValue())));
    } catch (err) {
      this.error.set(ApiError.from(err));
    } finally {
      this.busy.set(false);
    }
  }
}

@Component({
  selector: 'sr-projects',
  imports: [RouterLink, FormsModule, MatButtonModule, StateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sr-page">
      <header class="sr-head">
        <div>
          <h1>Projects</h1>
          @if (projects.data(); as list) {
            <div class="meta"><span>{{ list.length }} projects</span></div>
          }
        </div>
        @if (canCreate()) {
          <div class="actions"><button mat-flat-button (click)="create()">New project</button></div>
        }
      </header>

      <div class="sr-toolbar">
        <label class="sr-field search">
          <span class="sr-only">Search projects</span>
          <input type="search" placeholder="Search by name, code or city" [ngModel]="search()" (ngModelChange)="search.set($event)" />
        </label>
      </div>

      <div class="sr-panel">
        @if (projects.error(); as e) {
          <sr-state kind="error" [error]="e" (retry)="load()" />
        } @else if (!projects.data()) {
          <sr-state kind="loading" />
        } @else if (filtered().length === 0) {
          <sr-state [title]="search() ? 'No projects match “' + search() + '”' : 'No projects yet'" />
        } @else {
          <table class="sr-dt stack-md">
            <thead>
              <tr>
                <th scope="col">Code</th>
                <th scope="col">Project</th>
                <th scope="col">Status</th>
                <th scope="col" class="m-hide">City</th>
                <th scope="col" class="num">Done</th>
                <th scope="col" class="num m-hide">Work orders</th>
                <th scope="col" class="num">Overdue</th>
                <th scope="col" class="num m-hide">Blocked</th>
                <th scope="col">Target</th>
              </tr>
            </thead>
            <tbody>
              @for (p of filtered(); track p.id) {
                <tr>
                  <td class="c-id">{{ p.code }}</td>
                  <td class="c-title"><a [routerLink]="['/app/projects', p.id]" [attr.data-testid]="'project-' + p.code">{{ p.title }}</a></td>
                  <td class="c-status">{{ p.status === 'OnHold' ? 'On hold' : p.status }}</td>
                  <td class="m-hide">{{ p.city }}</td>
                  <td class="num m-label" data-label="Done">{{ p.percentComplete }}%</td>
                  <td class="num m-hide">{{ p.doneWorkOrders }}/{{ p.totalWorkOrders }}</td>
                  <td class="num m-label" data-label="Overdue" [class.late]="p.overdueWorkOrders > 0">{{ p.overdueWorkOrders }}</td>
                  <td class="num m-hide">{{ p.blockedWorkOrders }}</td>
                  <td class="c-date m-label" data-label="Target">{{ p.targetDate }}</td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>
    </div>
  `,
  styles: `
    .search { width: 320px; max-width: 100%; }
    @media (max-width: 767px) { .search { width: 100%; } }
  `,
})
export class ProjectsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  protected readonly session = inject(SessionService);
  protected readonly projects = new Loader<ProjectSummary[]>();
  protected readonly search = signal('');
  protected readonly canCreate = computed(() => {
    const roles = this.session.user()?.roles ?? [];
    return roles.includes('Admin') || roles.includes('ProjectManager');
  });
  protected readonly filtered = computed(() => {
    const s = this.search().trim().toLowerCase();
    return (this.projects.data() ?? []).filter(
      (p) => !s || p.title.toLowerCase().includes(s) || p.code.toLowerCase().includes(s) || (p.city ?? '').toLowerCase().includes(s),
    );
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.projects.load(this.api.projects());
  }

  protected create(): void {
    this.dialog
      .open(ProjectDialogComponent)
      .afterClosed()
      .subscribe((created?: ProjectDetail) => {
        if (!created) return;
        this.notify.success(`Created ${created.code}.`);
        void this.router.navigate(['/app/projects', created.id]);
      });
  }
}
