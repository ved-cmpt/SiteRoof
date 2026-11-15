import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { Loader } from '../core/loader';
import { AttentionItem, Overview } from '../core/models';
import { SessionService } from '../core/session.service';
import { StatusChartComponent, StatusRow } from '../shared/charts';
import { AgoPipe, StateComponent, StatItem, StatsComponent, StatusChipComponent } from '../shared/ui';

@Component({
  selector: 'sr-overview',
  imports: [RouterLink, StateComponent, StatsComponent, StatusChartComponent, StatusChipComponent, AgoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sr-page">
      <header class="sr-head">
        <div>
          <h1>Home</h1>
          @if (overview.data(); as o) {
            <div class="meta">
              <span>{{ o.projects.length }} project{{ o.projects.length === 1 ? '' : 's' }}</span>
              <span>{{ o.kpis.openWorkOrders }} open work order{{ o.kpis.openWorkOrders === 1 ? '' : 's' }}</span>
              <span>{{ o.kpis.percentComplete }}% complete overall</span>
              <span>Updated {{ o.generatedAt | ago }}</span>
            </div>
          }
        </div>
      </header>

      @if (overview.error(); as e) {
        <div class="sr-panel"><sr-state kind="error" [error]="e" (retry)="load()" /></div>
      } @else if (!overview.data()) {
        <div class="sr-panel"><sr-state kind="loading" /></div>
      } @else {
        @let o = overview.data()!;

        @if (o.myWork.length > 0) {
          <section class="sr-section" aria-labelledby="mine-title">
            <header><h2 id="mine-title">Assigned to me</h2><span class="aside sr-mono">{{ o.myWork.length }}</span></header>
            <div class="sr-panel">
              <table class="sr-dt" data-testid="my-work">
                <thead>
                  <tr><th scope="col">ID</th><th scope="col">Work order</th><th scope="col">Status</th><th scope="col">Due</th><th scope="col" class="num">Done</th></tr>
                </thead>
                <tbody>
                  @for (w of o.myWork; track w.id) {
                    <tr>
                      <td class="c-id">{{ w.key }}</td>
                      <td class="c-title"><a [routerLink]="['/app/work-orders', w.id]">{{ w.title }}</a></td>
                      <td class="c-status"><sr-status [status]="w.status" /></td>
                      <td class="c-date" [class.late]="w.isOverdue">{{ w.dueDate }}@if (w.isOverdue) { <span> overdue</span> }</td>
                      <td class="num">{{ w.percentComplete }}%</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
        }

        <section class="sr-section" aria-label="Key figures">
          <sr-stats [items]="stats()" />
        </section>

        <div class="cols">
          <section class="sr-section" aria-labelledby="att-title">
            <header>
              <h2 id="att-title">Needs attention</h2>
              <span class="aside sr-mono">{{ o.attention.length }}</span>
            </header>
            <div class="sr-panel">
              @if (o.attention.length === 0) {
                <sr-state title="Nothing needs attention" message="No overdue work, open blockers, pending approvals or ERP issues." />
              } @else {
                <table class="sr-dt" data-testid="attention-queue">
                  <thead>
                    <tr><th scope="col">Item</th><th scope="col">Project</th><th scope="col">Detail</th></tr>
                  </thead>
                  <tbody>
                    @for (a of visibleAttention(); track $index) {
                      <tr [attr.data-severity]="a.severity">
                        <td class="c-title">
                          <span class="sev" [attr.data-severity]="a.severity" aria-hidden="true"></span>
                          <span class="sr-only">{{ a.severity }}: </span>
                          <a [routerLink]="link(a)">{{ a.title }}</a>
                        </td>
                        <td class="c-id">{{ a.projectCode }}</td>
                        <td class="muted">{{ a.detail }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
                @if (o.attention.length > visibleAttention().length) {
                  <button type="button" class="more" (click)="showAll.set(true)">Show all {{ o.attention.length }}</button>
                }
              }
            </div>
          </section>

          <div>
            <section class="sr-section" aria-labelledby="proj-title">
              <header><h2 id="proj-title">Projects</h2><a routerLink="/app/projects" class="aside">All projects</a></header>
              <div class="sr-panel">
                <table class="sr-dt">
                  <thead>
                    <tr><th scope="col">Code</th><th scope="col">Project</th><th scope="col" class="num">Done</th><th scope="col" class="num">Overdue</th></tr>
                  </thead>
                  <tbody>
                    @for (p of o.projects; track p.projectId) {
                      <tr>
                        <td class="c-id">{{ p.code }}</td>
                        <td class="c-title"><a [routerLink]="['/app/projects', p.projectId]">{{ p.title }}</a></td>
                        <td class="num m-label" data-label="Done">{{ p.percentComplete }}%</td>
                        <td class="num m-label" data-label="Overdue" [class.late]="p.overdue > 0">{{ p.overdue }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </section>

            <section class="sr-section" aria-labelledby="status-title">
              <header><h2 id="status-title">Work orders by status</h2></header>
              <div class="sr-panel pad">
                <sr-status-chart [rows]="rows()" />
              </div>
            </section>
          </div>
        </div>
      }
    </div>
  `,
  styles: `
    .cols { display: grid; grid-template-columns: minmax(0, 1.5fr) minmax(0, 1fr); gap: 0 var(--sr-s5); align-items: start; }
    .sev { display: inline-block; width: 6px; height: 6px; margin-right: var(--sr-s2); vertical-align: middle; background: var(--sr-line-strong); }
    .sev[data-severity='critical'] { background: var(--sr-accent); }
    .more { width: 100%; height: 40px; border: 0; border-top: 1px solid var(--sr-line); background: var(--sr-surface); font: inherit; color: var(--sr-ink-2); cursor: pointer; }
    .more:hover { background: var(--sr-subtle); }
    @media (max-width: 1100px) { .cols { grid-template-columns: 1fr; } }
    @media (max-width: 767px) { .more { height: 44px; } }
  `,
})
export class OverviewPage implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly session = inject(SessionService);
  protected readonly overview = new Loader<Overview>();
  protected readonly showAll = signal(false);

  protected readonly rows = computed<StatusRow[]>(() =>
    (this.overview.data()?.projects ?? []).map((p) => ({ id: p.projectId, label: p.code, sublabel: p.title, counts: p.counts })),
  );

  protected readonly visibleAttention = computed(() => {
    const all = this.overview.data()?.attention ?? [];
    return this.showAll() ? all : all.slice(0, 8);
  });

  protected readonly stats = computed<StatItem[]>(() => {
    const k = this.overview.data()?.kpis;
    if (!k) return [];
    const erp = k.erpFailed + k.erpRetrying;
    return [
      { label: 'Overdue work orders', value: k.overdueWorkOrders, tone: k.overdueWorkOrders ? 'critical' : 'neutral', testId: 'kpi-overdue' },
      { label: 'Open blockers', value: k.openBlockers, testId: 'kpi-blockers' },
      { label: 'Pending approvals', value: k.pendingApprovals, testId: 'kpi-approvals' },
      {
        label: 'ERP issues',
        value: erp,
        tone: k.erpFailed ? 'critical' : 'neutral',
        hint: k.erpFailed ? `${k.erpFailed} failed` : k.erpRetrying ? `${k.erpRetrying} retry scheduled` : 'All sent',
        testId: 'kpi-erp',
      },
    ];
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.overview.load(this.api.overview());
  }

  protected link(a: AttentionItem): unknown[] {
    return a.linkType === 'workOrder' ? ['/app/work-orders', a.linkId] : a.linkType === 'project' ? ['/app/projects', a.linkId] : ['/app/approvals'];
  }
}
