import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDialog } from '@angular/material/dialog';
import { MatRadioModule } from '@angular/material/radio';
import { Router } from '@angular/router';
import { firstValueFrom, tap } from 'rxjs';
import { ApiService } from '../core/api.service';
import { Loader } from '../core/loader';
import { ErpSimulatorMode, TenantSettings } from '../core/models';
import { NotifyService } from '../core/notify.service';
import { SessionService } from '../core/session.service';
import { AgoPipe, StateComponent } from '../shared/ui';
import { ConfirmDialogComponent } from './dialogs';

@Component({
  selector: 'sr-admin',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatRadioModule, StateComponent, AgoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sr-page">
      <p class="sr-kicker">Admin</p>
      <h1>Workspace settings</h1>
      @if (tenant.error(); as e) {
        <sr-state kind="error" [error]="e" (retry)="load()" />
      } @else if (!tenant.data()) {
        <sr-state kind="loading" />
      } @else {
        @let t = tenant.data()!;
        <div class="grid">
          <section class="sr-card" aria-labelledby="ws-title">
            <h2 id="ws-title">Workspace</h2>
            <form (ngSubmit)="saveName()" class="row">
              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>Workspace name</mat-label>
                <input matInput name="name" [(ngModel)]="name" maxlength="120" required minlength="3" />
              </mat-form-field>
              <button mat-stroked-button type="submit" [disabled]="busy() || name.trim().length < 3">Save</button>
            </form>
            <p class="sr-muted small">
              {{ t.isDemo ? 'Demo workspace' : 'Workspace' }} created {{ t.createdAt | ago }}
              @if (t.expiresAt) {
                · expires {{ t.expiresAt | ago }} and is then deleted automatically
              }
            </p>
          </section>

          <section class="sr-card" aria-labelledby="erp-title">
            <h2 id="erp-title">ERP simulator</h2>
            <p class="sr-muted small">Controls how the self-hosted mock ERP answers this workspace's requisitions, to exercise retries and failure handling.</p>
            <mat-radio-group [ngModel]="t.erpSimulatorMode" (ngModelChange)="setMode($event)" class="radios" aria-label="ERP simulator mode">
              <mat-radio-button value="FailFirstAttempt">Fail the first attempt with 503, then succeed (default)</mat-radio-button>
              <mat-radio-button value="Normal">Always succeed</mat-radio-button>
              <mat-radio-button value="Unavailable">Always 503 (retries until the attempt limit, then fails)</mat-radio-button>
            </mat-radio-group>
          </section>

          <section class="sr-card" aria-labelledby="usage-title">
            <h2 id="usage-title">Usage and quotas</h2>
            <dl class="usage">
              <div><dt>Projects</dt><dd class="sr-num">{{ t.usage.projects }} / {{ t.usage.maxProjects }}</dd></div>
              <div><dt>Work orders</dt><dd class="sr-num">{{ t.usage.workOrders }}</dd></div>
              <div><dt>Progress logs</dt><dd class="sr-num">{{ t.usage.progressLogs }}</dd></div>
              <div><dt>Material requests</dt><dd class="sr-num">{{ t.usage.materialRequests }}</dd></div>
              <div><dt>Audit events</dt><dd class="sr-num">{{ t.usage.auditEvents }}</dd></div>
            </dl>
            <p class="sr-muted small">Demo workspaces allow up to {{ t.usage.maxWorkOrdersPerProject }} work orders per project; provisioning is rate-limited per visitor.</p>
          </section>

          @if (t.isDemo) {
            <section class="sr-card wide demo" aria-labelledby="demo-title">
              <h2 id="demo-title">Demo controls</h2>
              <p class="sr-muted small">
                Reset replaces this workspace with a freshly seeded one and deletes the current data. It only affects your
                workspace; other visitors have their own.
              </p>
              <button mat-stroked-button (click)="reset()" [disabled]="busy()" data-testid="admin-reset">Reset my demo</button>
            </section>
          }
          <section class="sr-card wide" aria-labelledby="users-title">
            <h2 id="users-title">People</h2>
            <div class="sr-table-wrap">
              <table class="sr-table">
                <thead><tr><th scope="col">Name</th><th scope="col">Title</th><th scope="col">Workspace roles</th><th scope="col">Project memberships</th><th scope="col">Demo persona</th></tr></thead>
                <tbody>
                  @for (u of t.users; track u.id) {
                    <tr>
                      <td>{{ u.displayName }}</td>
                      <td>{{ u.jobTitle }}</td>
                      <td>{{ u.roles.join(', ') }}</td>
                      <td>{{ u.projects.join(' · ') || '—' }}</td>
                      <td>{{ u.persona || '—' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <p class="sr-muted small">Demo people have no passwords; the persona switcher signs in as them within this workspace only.</p>
          </section>
        </div>
      }
    </div>
  `,
  styles: `
    .grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 16px; }
    .wide { grid-column: 1 / -1; }
    .demo { border-top: 4px solid var(--sr-safety); }
    .row { display: flex; gap: 10px; align-items: center; }
    .row mat-form-field { flex: 1; }
    .small { font-size: 0.82rem; }
    .radios { display: grid; gap: 6px; }
    .usage { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin: 0 0 8px; }
    .usage div { background: var(--sr-concrete); border-radius: 6px; padding: 6px 8px; }
    .usage dt { font-size: 0.72rem; color: var(--sr-ink-3); text-transform: uppercase; letter-spacing: 0.06em; }
    .usage dd { margin: 0; font-weight: 750; }
    @media (max-width: 1024px) { .grid { grid-template-columns: 1fr; } }
  `,
})
export class AdminPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);
  private readonly session = inject(SessionService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  protected readonly tenant = new Loader<TenantSettings>();
  protected readonly busy = signal(false);
  protected name = '';

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.tenant.load(this.api.tenant().pipe(tap((t) => (this.name = t.name))));
  }

  protected async saveName(): Promise<void> {
    await this.update({ name: this.name.trim() }, 'Workspace renamed.');
    await this.session.refresh();
  }

  protected setMode(mode: ErpSimulatorMode): void {
    void this.update({ erpSimulatorMode: mode }, 'ERP simulator updated.');
  }

  protected reset(): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: { title: 'Reset your demo workspace?', body: 'Everything you changed is discarded and a freshly seeded workspace replaces it.', confirm: 'Reset demo' },
      })
      .afterClosed()
      .subscribe(async (ok) => {
        if (!ok) return;
        this.busy.set(true);
        try {
          await this.session.resetDemo();
          this.notify.success('Your demo workspace was reset.');
          await this.router.navigateByUrl('/app/overview');
        } catch (err) {
          this.notify.error(err);
        } finally {
          this.busy.set(false);
        }
      });
  }

  private async update(body: { name?: string; erpSimulatorMode?: ErpSimulatorMode }, message: string): Promise<void> {
    this.busy.set(true);
    try {
      this.tenant.data.set(await firstValueFrom(this.api.updateTenant(body)));
      this.notify.success(message);
    } catch (err) {
      this.notify.error(err);
    } finally {
      this.busy.set(false);
    }
  }
}
