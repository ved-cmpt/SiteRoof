import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { Loader } from '../core/loader';
import { AttentionItem, Overview } from '../core/models';
import { SessionService } from '../core/session.service';
import { StatusChartComponent, StatusRow } from '../shared/charts';
import { IconName } from '../shared/icons.generated';
import { AgoPipe, DayPipe, IconComponent, KpiComponent, MeterComponent, StateComponent, StatusChipComponent } from '../shared/ui';

@Component({
  selector: 'sr-overview',
  imports: [
    RouterLink,
    FormsModule,
    MatFormFieldModule,
    MatSelectModule,
    IconComponent,
    KpiComponent,
    StateComponent,
    StatusChartComponent,
    StatusChipComponent,
    MeterComponent,
    AgoPipe,
    DayPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sr-page">
      <div class="head">
        <div>
          <p class="sr-kicker">Overview</p>
          <h1>Good to see you, {{ firstName() }}</h1>
          <p class="sr-muted">Everything below is calculated from the database when the page loads.</p>
        </div>
        @if (overview.data(); as o) {
          <mat-form-field appearance="outline" class="switcher" subscriptSizing="dynamic">
            <mat-label>Jump to project</mat-label>
            <mat-select (selectionChange)="open($event.value)" data-testid="project-switcher">
              @for (p of o.projects; track p.projectId) {
                <mat-option [value]="p.projectId">{{ p.code }} · {{ p.title }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        }
      </div>

      @if (overview.error(); as e) {
        <sr-state kind="error" [error]="e" (retry)="load()" />
      } @else if (!overview.data()) {
        <sr-state kind="loading" message="Loading the overview…" />
      } @else {
        @let o = overview.data()!;
        <section class="kpis" aria-label="Key figures">
          <sr-kpi label="Active projects" [value]="o.kpis.activeProjects" icon="apartment" tone="info" />
          <sr-kpi label="Open work orders" [value]="o.kpis.openWorkOrders" icon="checklist" testId="kpi-open" />
          <sr-kpi label="Overdue" [value]="o.kpis.overdueWorkOrders" icon="schedule" [tone]="o.kpis.overdueWorkOrders ? 'critical' : 'good'" hint="Past due and not done" testId="kpi-overdue" />
          <sr-kpi label="Open blockers" [value]="o.kpis.openBlockers" icon="block" [tone]="o.kpis.openBlockers ? 'warning' : 'good'" testId="kpi-blockers" />
          <sr-kpi label="Pending approvals" [value]="o.kpis.pendingApprovals" icon="fact_check" [tone]="o.kpis.pendingApprovals ? 'warning' : 'good'" testId="kpi-approvals" />
          <sr-kpi
            label="ERP issues"
            [value]="o.kpis.erpFailed + o.kpis.erpRetrying"
            icon="sync"
            [tone]="o.kpis.erpFailed ? 'critical' : o.kpis.erpRetrying ? 'warning' : 'good'"
            [hint]="o.kpis.erpFailed ? o.kpis.erpFailed + ' failed, ' + o.kpis.erpRetrying + ' retrying' : o.kpis.erpRetrying ? o.kpis.erpRetrying + ' retry scheduled' : 'All transmissions sent'"
            testId="kpi-erp"
          />
        </section>

        <div class="cols">
          <section class="sr-card" aria-labelledby="status-title">
            <div class="sr-card-title">
              <h2 id="status-title">Work orders by status</h2>
              <span class="sr-muted small">Overall {{ o.kpis.percentComplete }}% complete</span>
            </div>
            <sr-status-chart [rows]="rows()" />
            <ul class="projects">
              @for (p of o.projects; track p.projectId) {
                <li>
                  <a [routerLink]="['/app/projects', p.projectId]">{{ p.code }} · {{ p.title }}</a>
                  <span class="sr-muted small">{{ p.status }} · {{ p.overdue }} overdue</span>
                  <sr-meter [value]="p.percentComplete" [label]="p.title + ' completion'" [tone]="p.percentComplete === 100 ? 'good' : 'info'" />
                  <span class="sr-num small">{{ p.percentComplete }}%</span>
                </li>
              }
            </ul>
          </section>

          <section class="sr-card attention" aria-labelledby="att-title">
            <div class="sr-card-title">
              <h2 id="att-title">Needs attention</h2>
              <span class="count sr-num">{{ o.attention.length }}</span>
            </div>
            @if (o.attention.length === 0) {
              <sr-state title="Nothing needs attention" message="No overdue work, blockers, pending approvals or ERP issues." />
            } @else {
              <ul class="queue" data-testid="attention-queue">
                @for (a of o.attention; track $index) {
                  <li [attr.data-severity]="a.severity">
                    <sr-icon [name]="icon(a)" [size]="20" [label]="a.severity" />
                    <div>
                      <a [routerLink]="link(a)">{{ a.title }}</a>
                      <p>{{ a.detail }}@if (a.projectCode) { · {{ a.projectCode }} }</p>
                    </div>
                  </li>
                }
              </ul>
            }
          </section>
        </div>

        @if (o.myWork.length > 0) {
          <section class="sr-card mine" aria-labelledby="mine-title">
            <div class="sr-card-title"><h2 id="mine-title">My assigned work</h2></div>
            <div class="sr-table-wrap">
              <table class="sr-table">
                <thead><tr><th scope="col">Work order</th><th scope="col">Status</th><th scope="col">Due</th><th scope="col">Progress</th></tr></thead>
                <tbody>
                  @for (w of o.myWork; track w.id) {
                    <tr>
                      <td><a [routerLink]="['/app/work-orders', w.id]">{{ w.key }} · {{ w.title }}</a></td>
                      <td><sr-status [status]="w.status" /></td>
                      <td [class.late]="w.isOverdue">{{ w.dueDate | day }}@if (w.isOverdue) { · overdue }</td>
                      <td class="sr-num">{{ w.percentComplete }}%</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
        }
        <p class="sr-muted small stamp">Generated {{ o.generatedAt | ago }}.</p>
      }
    </div>
  `,
  styles: `
    .head { display: flex; justify-content: space-between; align-items: flex-end; gap: 16px; flex-wrap: wrap; margin-bottom: 18px; }
    .switcher { width: 320px; max-width: 100%; }
    .kpis { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); gap: 12px; margin-bottom: 16px; }
    .cols { display: grid; grid-template-columns: minmax(0, 1.4fr) minmax(0, 1fr); gap: 16px; }
    .small { font-size: 0.82rem; }
    .projects { list-style: none; margin: 18px 0 0; padding: 14px 0 0; border-top: 1px solid var(--sr-line); display: grid; gap: 12px; }
    .projects li { display: grid; grid-template-columns: minmax(0, 1.2fr) auto minmax(80px, 1fr) 40px; gap: 10px; align-items: center; }
    .projects a { font-weight: 600; }
    .count { background: var(--sr-ink); color: var(--sr-safety); border-radius: 999px; padding: 1px 9px; font-weight: 700; }
    .queue { list-style: none; margin: 0; padding: 0; display: grid; gap: 2px; max-height: 560px; overflow: auto; }
    .queue li { display: grid; grid-template-columns: 24px 1fr; gap: 10px; padding: 10px 8px; border-radius: 6px; border-left: 3px solid transparent; }
    .queue li:hover { background: var(--sr-concrete); }
    .queue li[data-severity='critical'] { border-left-color: var(--sr-critical); }
    .queue li[data-severity='critical'] sr-icon { color: var(--sr-critical); }
    .queue li[data-severity='warning'] { border-left-color: var(--sr-safety); }
    .queue li[data-severity='warning'] sr-icon { color: #a4470b; }
    .queue li[data-severity='info'] sr-icon { color: var(--sr-info); }
    .queue a { font-weight: 600; text-decoration: none; color: var(--sr-ink); }
    .queue a:hover { text-decoration: underline; }
    .queue p { margin: 2px 0 0; font-size: 0.82rem; color: var(--sr-ink-3); }
    .mine { margin-top: 16px; }
    .late { color: var(--sr-critical); font-weight: 600; }
    .stamp { margin-top: 14px; }
    @media (max-width: 1280px) { .kpis { grid-template-columns: repeat(3, minmax(0, 1fr)); } }
    @media (max-width: 1024px) { .cols { grid-template-columns: 1fr; } }
    @media (max-width: 560px) { .kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); } .projects li { grid-template-columns: 1fr 40px; } .projects li span:first-of-type { display: none; } }
  `,
})
export class OverviewPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly session = inject(SessionService);
  protected readonly overview = new Loader<Overview>();
  protected readonly firstName = computed(() => this.session.user()?.displayName.split(' ')[0] ?? '');
  protected readonly rows = computed<StatusRow[]>(() =>
    (this.overview.data()?.projects ?? []).map((p) => ({ id: p.projectId, label: p.code, sublabel: p.title, counts: p.counts })),
  );

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.overview.load(this.api.overview());
  }

  protected open(id: string): void {
    void this.router.navigate(['/app/projects', id]);
  }

  protected icon(a: AttentionItem): IconName {
    return ({ erp: 'sync', overdue: 'schedule', blocker: 'block', approval: 'fact_check', milestone: 'flag' } as const)[a.kind];
  }

  protected link(a: AttentionItem): unknown[] {
    return a.linkType === 'workOrder' ? ['/app/work-orders', a.linkId] : a.linkType === 'project' ? ['/app/projects', a.linkId] : ['/app/approvals'];
  }
}
