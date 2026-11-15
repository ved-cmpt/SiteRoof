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
import { IconComponent, MeterComponent, StateComponent } from '../shared/ui';

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
          <mat-hint>Uppercase letters, digits, dashes</mat-hint>
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
  imports: [RouterLink, FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, IconComponent, StateComponent, MeterComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sr-page">
      <header class="sr-page-head">
        <div>
          <p class="sr-kicker">Projects</p>
          <h1>Your projects</h1>
          <p>Only projects you are a member of are listed{{ session.isAdmin() ? ' (admins see every project in the workspace)' : '' }}.</p>
        </div>
        <div class="sr-row">
          <mat-form-field appearance="outline" subscriptSizing="dynamic" class="search">
            <mat-label>Search projects</mat-label>
            <input matInput [ngModel]="search()" (ngModelChange)="search.set($event)" />
          </mat-form-field>
          @if (canCreate()) {
            <button mat-flat-button (click)="create()"><sr-icon name="add" [size]="18" /> New project</button>
          }
        </div>
      </header>

      @if (projects.error(); as e) {
        <sr-state kind="error" [error]="e" (retry)="load()" />
      } @else if (!projects.data()) {
        <sr-state kind="loading" />
      } @else if (filtered().length === 0) {
        <sr-state [title]="search() ? 'No projects match “' + search() + '”' : 'No projects yet'" message="Try a different search, or create a project." />
      } @else {
        <div class="grid">
          @for (p of filtered(); track p.id) {
            <a class="project" [routerLink]="['/app/projects', p.id]" [attr.data-testid]="'project-' + p.code">
              <div class="top">
                <span class="code">{{ p.code }}</span>
                <span class="status" [attr.data-status]="p.status">{{ p.status === 'OnHold' ? 'On hold' : p.status }}</span>
              </div>
              <h2>{{ p.title }}</h2>
              <p class="sr-muted"><sr-icon name="location_on" [size]="14" /> {{ p.city || '—' }} · target <span class="sr-mono">{{ p.targetDate }}</span></p>
              <sr-meter [value]="p.percentComplete" [label]="p.title + ' completion'" [tone]="p.percentComplete === 100 ? 'good' : 'info'" />
              <dl class="stats">
                <div><dt>Complete</dt><dd class="sr-num">{{ p.percentComplete }}%</dd></div>
                <div><dt>Done</dt><dd class="sr-num">{{ p.doneWorkOrders }}/{{ p.totalWorkOrders }}</dd></div>
                <div [class.bad]="p.overdueWorkOrders > 0"><dt>Overdue</dt><dd class="sr-num">{{ p.overdueWorkOrders }}</dd></div>
                <div [class.bad]="p.blockedWorkOrders > 0"><dt>Blocked</dt><dd class="sr-num">{{ p.blockedWorkOrders }}</dd></div>
              </dl>
              @if (p.myRole) {
                <span class="role">You: {{ p.myRole }}</span>
              }
            </a>
          }
        </div>
      }
    </div>
  `,
  styles: `
    .search { width: 240px; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 16px; }
    .project { display: block; background: var(--sr-surface); border: 1px solid var(--sr-line); border-radius: var(--sr-radius-lg); padding: 18px; color: inherit; text-decoration: none; transition: border-color var(--sr-fast) var(--sr-ease); }
    .project:hover { border-color: var(--sr-ink-3); }
    .top { display: flex; justify-content: space-between; align-items: center; }
    .code { font-family: var(--sr-font-mono); font-size: 0.75rem; color: var(--sr-ink-2); border: 1px solid var(--sr-line-strong); padding: 2px 6px; border-radius: var(--sr-radius-sm); }
    .status { font-size: 0.75rem; font-weight: 500; padding: 2px 8px; border-radius: 999px; background: var(--sr-info-soft); color: var(--sr-info); }
    .status[data-status='Completed'] { background: var(--sr-good-soft); color: var(--sr-good); }
    .status[data-status='OnHold'] { background: var(--sr-warn-soft); color: var(--sr-warn); }
    h2 { margin: 14px 0 4px; font-size: var(--sr-text-md); font-weight: 600; }
    p { display: flex; align-items: center; gap: 4px; font-size: 0.85rem; }
    .stats { display: grid; grid-template-columns: repeat(4, 1fr); margin: 14px 0 0; border-top: 1px solid var(--sr-line); }
    .stats div { padding: 8px 0 0; }
    .stats dt { font-family: var(--sr-font-mono); font-size: 0.625rem; text-transform: uppercase; letter-spacing: 0.1em; color: var(--sr-ink-3); }
    .stats dd { margin: 0; font-family: var(--sr-font-mono); font-weight: 500; font-size: 1rem; }
    .stats .bad dd { color: var(--sr-accent-strong); }
    .role { display: inline-block; margin-top: 12px; font-size: 0.78rem; color: var(--sr-ink-3); }
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
