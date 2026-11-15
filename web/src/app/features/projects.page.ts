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
import { DayPipe, IconComponent, MeterComponent, StateComponent } from '../shared/ui';

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
  imports: [RouterLink, FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, IconComponent, StateComponent, MeterComponent, DayPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sr-page">
      <div class="head">
        <div>
          <p class="sr-kicker">Projects</p>
          <h1>Your projects</h1>
          <p class="sr-muted">Only projects you are a member of are listed{{ session.isAdmin() ? ' (admins see every project in the workspace)' : '' }}.</p>
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
      </div>

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
              <p class="sr-muted"><sr-icon name="location_on" [size]="16" /> {{ p.city || '—' }} · target {{ p.targetDate | day: 'long' }}</p>
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
    .head { display: flex; justify-content: space-between; align-items: flex-end; gap: 16px; flex-wrap: wrap; margin-bottom: 18px; }
    .search { width: 240px; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 16px; }
    .project { display: block; background: var(--sr-paper); border: 1px solid var(--sr-line); border-radius: var(--sr-radius-lg); padding: 18px; color: inherit; text-decoration: none; box-shadow: var(--sr-shadow); border-top: 4px solid var(--sr-safety); transition: transform 0.12s ease, box-shadow 0.12s ease; }
    .project:hover { transform: translateY(-2px); box-shadow: 0 8px 24px rgb(19 23 29 / 12%); }
    .top { display: flex; justify-content: space-between; align-items: center; }
    .code { font: 800 0.8rem/1 var(--sr-font-display); letter-spacing: 0.1em; background: var(--sr-ink); color: var(--sr-safety); padding: 4px 7px; border-radius: 4px; }
    .status { font-size: 0.78rem; font-weight: 650; padding: 2px 8px; border-radius: 999px; background: var(--sr-info-soft); color: var(--sr-info); }
    .status[data-status='Completed'] { background: var(--sr-good-soft); color: var(--sr-good); }
    .status[data-status='OnHold'] { background: var(--sr-warning-soft); color: var(--sr-warning); }
    h2 { margin: 12px 0 4px; font-size: 1.15rem; }
    p { display: flex; align-items: center; gap: 4px; font-size: 0.85rem; }
    .stats { display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; margin: 14px 0 0; }
    .stats div { background: var(--sr-concrete); border-radius: 6px; padding: 6px 8px; }
    .stats dt { font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.08em; color: var(--sr-ink-3); }
    .stats dd { margin: 0; font-weight: 750; font-size: 1.05rem; }
    .stats .bad dd { color: var(--sr-critical); }
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
