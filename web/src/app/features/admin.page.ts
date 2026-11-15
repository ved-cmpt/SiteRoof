import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { firstValueFrom, tap } from 'rxjs';
import { ApiService } from '../core/api.service';
import { Loader } from '../core/loader';
import { ErpSimulatorMode, TenantSettings } from '../core/models';
import { NotifyService } from '../core/notify.service';
import { SessionService } from '../core/session.service';
import { AgoPipe, StateComponent, StatItem, StatsComponent } from '../shared/ui';
import { ConfirmDialogComponent } from './dialogs';

@Component({
  selector: 'sr-admin',
  imports: [FormsModule, MatButtonModule, StateComponent, StatsComponent, AgoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sr-page">
      <header class="sr-head">
        <div>
          <nav class="crumbs" aria-label="Breadcrumb"><span>Admin</span></nav>
          <h1>Workspace settings</h1>
          @if (tenant.data(); as t) {
            <div class="meta">
              <span>{{ t.isDemo ? 'Demo workspace' : 'Workspace' }} created {{ t.createdAt | ago }}</span>
              @if (t.expiresAt) {
                <span>Deleted automatically {{ t.expiresAt | ago }}</span>
              }
            </div>
          }
        </div>
      </header>

      @if (tenant.error(); as e) {
        <div class="sr-panel"><sr-state kind="error" [error]="e" (retry)="load()" /></div>
      } @else if (!tenant.data()) {
        <div class="sr-panel"><sr-state kind="loading" /></div>
      } @else {
        @let t = tenant.data()!;
        <section class="sr-section" aria-label="Usage">
          <sr-stats [items]="usage()" />
        </section>

        <div class="two">
          <section class="sr-section" aria-labelledby="ws-title">
            <header><h2 id="ws-title">Workspace</h2></header>
            <form class="sr-panel pad sr-form" (ngSubmit)="saveName()">
              <label class="sr-f full">
                <span class="l">Name</span>
                <input name="name" [(ngModel)]="name" maxlength="120" required minlength="3" />
              </label>
              <div class="full sr-sticky-actions">
                <button mat-stroked-button type="submit" [disabled]="busy() || name.trim().length < 3">Save name</button>
              </div>
            </form>
          </section>

          <section class="sr-section" aria-labelledby="erp-title">
            <header><h2 id="erp-title">ERP simulator</h2></header>
            <fieldset class="sr-panel radios">
              <legend class="sr-only">How the mock ERP answers this workspace's requisitions</legend>
              @for (o of modes; track o.value) {
                <label class="radio">
                  <input type="radio" name="erp" [value]="o.value" [checked]="t.erpSimulatorMode === o.value" (change)="setMode(o.value)" [disabled]="busy()" />
                  <span>{{ o.label }}</span>
                </label>
              }
            </fieldset>
          </section>
        </div>

        <section class="sr-section" aria-labelledby="users-title">
          <header><h2 id="users-title">People</h2><span class="aside sr-mono">{{ t.users.length }}</span></header>
          <div class="sr-panel">
            <table class="sr-dt">
              <thead><tr><th scope="col">Name</th><th scope="col">Title</th><th scope="col">Workspace roles</th><th scope="col" class="m-hide">Projects</th><th scope="col">Persona</th></tr></thead>
              <tbody>
                @for (u of t.users; track u.id) {
                  <tr>
                    <td class="c-title">{{ u.displayName }}</td>
                    <td>{{ u.jobTitle }}</td>
                    <td>{{ u.roles.join(', ') }}</td>
                    <td class="m-hide muted">{{ u.projects.join(' · ') || '—' }}</td>
                    <td class="c-status muted">{{ u.persona || '' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>

        @if (t.isDemo) {
          <section class="sr-section" aria-labelledby="demo-title">
            <header><h2 id="demo-title">Demo workspace</h2></header>
            <div class="sr-panel pad demo">
              <span>Replace this workspace with a freshly seeded one. Other visitors are not affected.</span>
              <button mat-stroked-button (click)="reset()" [disabled]="busy()" data-testid="admin-reset">Reset my demo</button>
            </div>
          </section>
        }
      }
    </div>
  `,
  styles: `
    .two { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 var(--sr-s5); align-items: start; }
    .radios { margin: 0; padding: var(--sr-s2) 0; min-width: 0; }
    .radio { display: flex; align-items: center; gap: var(--sr-s3); min-height: 40px; padding: 0 var(--sr-s4); cursor: pointer; }
    .radio input { width: 16px; height: 16px; accent-color: var(--sr-accent-strong); }
    .demo { display: flex; align-items: center; justify-content: space-between; gap: var(--sr-s3); flex-wrap: wrap; }
    @media (max-width: 1023px) { .two { grid-template-columns: 1fr; } }
    @media (max-width: 767px) { .radio { min-height: 44px; } .demo button { width: 100%; } }
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
  protected readonly modes: { value: ErpSimulatorMode; label: string }[] = [
    { value: 'FailFirstAttempt', label: 'First attempt returns 503, then succeeds (default)' },
    { value: 'Normal', label: 'Always succeeds' },
    { value: 'Unavailable', label: 'Always returns 503 until the attempt limit' },
  ];
  protected readonly usage = computed<StatItem[]>(() => {
    const u = this.tenant.data()?.usage;
    if (!u) return [];
    return [
      { label: 'Projects', value: `${u.projects}/${u.maxProjects}` },
      { label: 'Work orders', value: u.workOrders },
      { label: 'Material requests', value: u.materialRequests },
      { label: 'Audit events', value: u.auditEvents },
    ];
  });

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
