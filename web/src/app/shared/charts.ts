import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { StatusCount, WorkOrderStatus, WorkPackage } from '../core/models';
import { STATUS_LABEL } from './ui';

export interface StatusRow {
  id: string;
  label: string;
  sublabel?: string;
  counts: StatusCount[];
}

const ORDER: WorkOrderStatus[] = ['Open', 'InProgress', 'Blocked', 'Completed', 'Closed'];
const COLOR: Record<string, string> = {
  Open: 'var(--sr-chart-open)',
  InProgress: 'var(--sr-chart-progress)',
  Blocked: 'var(--sr-chart-blocked)',
  Completed: 'var(--sr-chart-completed)',
  Closed: 'var(--sr-chart-closed)',
};

/**
 * Work-order status by project: one stacked horizontal bar per row (part-to-whole).
 * Identity never relies on color alone: legend + per-segment hover tooltip + a data table toggle.
 */
@Component({
  selector: 'sr-status-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <ul class="legend" aria-label="Legend">
        @for (s of order; track s) {
          <li><span class="swatch" [style.background]="color[s]"></span>{{ label[s] }}</li>
        }
      </ul>
      <button type="button" class="toggle" (click)="showTable.set(!showTable())" [attr.aria-pressed]="showTable()">
        {{ showTable() ? 'Show chart' : 'Show table' }}
      </button>
    </div>

    @if (!showTable()) {
      <div class="rows" role="img" [attr.aria-label]="summary()">
        @for (row of bars(); track row.id) {
          <div class="row">
            <div class="row-label">
              <strong>{{ row.label }}</strong>
              @if (row.sublabel) {
                <span>{{ row.sublabel }}</span>
              }
            </div>
            <div class="track" (mouseleave)="hover.set(null)">
              @if (row.total === 0) {
                <span class="none">No work orders</span>
              }
              @for (seg of row.segments; track seg.status) {
                <div
                  class="seg"
                  [style.width.%]="seg.pct"
                  [style.background]="color[seg.status]"
                  (mouseenter)="hover.set({ row: row.id, status: seg.status })"
                  [attr.title]="label[seg.status] + ': ' + seg.count"
                >
                  @if (seg.pct >= 12) {
                    <span class="seg-label" [class.dark]="seg.status === 'Completed'">{{ seg.count }}</span>
                  }
                  @if (hover()?.row === row.id && hover()?.status === seg.status) {
                    <span class="tip" role="tooltip">{{ label[seg.status] }} · {{ seg.count }} of {{ row.total }}</span>
                  }
                </div>
              }
            </div>
            <div class="total sr-num">{{ row.total }}</div>
          </div>
        }
      </div>
    } @else {
      <div class="sr-table-wrap">
        <table class="sr-dt">
          <caption class="sr-only">Work orders by status</caption>
          <thead>
            <tr>
              <th scope="col">Project</th>
              @for (s of order; track s) {
                <th scope="col">{{ label[s] }}</th>
              }
              <th scope="col">Total</th>
            </tr>
          </thead>
          <tbody>
            @for (row of bars(); track row.id) {
              <tr>
                <th scope="row">{{ row.label }}</th>
                @for (seg of row.all; track seg.status) {
                  <td class="num">{{ seg.count }}</td>
                }
                <td class="num">{{ row.total }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
  styles: `
    .head { display: flex; justify-content: space-between; gap: var(--sr-s3); align-items: center; flex-wrap: wrap; margin-bottom: var(--sr-s3); }
    .legend { display: flex; flex-wrap: wrap; gap: var(--sr-s1) var(--sr-s3); list-style: none; margin: 0; padding: 0; font-size: var(--sr-app-small); color: var(--sr-ink-3); }
    .legend li { display: inline-flex; align-items: center; gap: var(--sr-s1); }
    .swatch { width: 8px; height: 8px; border-radius: 2px; }
    .toggle { border: 1px solid var(--sr-line-strong); background: var(--sr-surface); border-radius: var(--sr-radius); height: 32px; padding: 0 var(--sr-s3); font: inherit; font-size: var(--sr-app-small); cursor: pointer; color: var(--sr-ink-2); }
    .rows { display: grid; gap: var(--sr-s3); }
    .row { display: grid; grid-template-columns: 48px 1fr 32px; gap: var(--sr-s3); align-items: center; }
    .row-label { min-width: 0; }
    .row-label strong { font-family: var(--sr-font-mono); font-weight: 400; font-size: var(--sr-app-small); color: var(--sr-ink-3); }
    .row-label span { display: none; }
    .track { display: flex; gap: 2px; height: 16px; background: var(--sr-subtle); border-radius: 2px; position: relative; }
    .seg { position: relative; height: 100%; min-width: 3px; display: flex; align-items: center; justify-content: center; cursor: default; }
    .seg:first-child { border-radius: 2px 0 0 2px; }
    .seg:last-child { border-radius: 0 2px 2px 0; }
    .seg:only-child { border-radius: 2px; }
    .seg-label { color: #fff; font-family: var(--sr-font-mono); font-size: var(--sr-app-small); line-height: 1; }
    .seg-label.dark { color: #0b0b0b; }
    .tip { position: absolute; bottom: calc(100% + 6px); left: 50%; transform: translateX(-50%); background: var(--sr-ink); color: #fff; font-size: var(--sr-app-small); padding: var(--sr-s1) var(--sr-s2); border-radius: var(--sr-radius); white-space: nowrap; z-index: 2; pointer-events: none; }
    .none { font-size: var(--sr-app-small); color: var(--sr-ink-3); padding: 0 var(--sr-s2); }
    .total { font-family: var(--sr-font-mono); text-align: right; }
    @media (max-width: 767px) { .toggle { height: 44px; } }
  `,
})
export class StatusChartComponent {
  readonly rows = input.required<StatusRow[]>();
  protected readonly order = ORDER;
  protected readonly color = COLOR;
  protected readonly label = STATUS_LABEL;
  protected readonly showTable = signal(false);
  protected readonly hover = signal<{ row: string; status: WorkOrderStatus } | null>(null);

  protected readonly bars = computed(() =>
    this.rows().map((r) => {
      const all = ORDER.map((status) => ({ status, count: r.counts.find((c) => c.status === status)?.count ?? 0 }));
      const total = all.reduce((s, x) => s + x.count, 0);
      return {
        ...r,
        all,
        total,
        segments: all.filter((x) => x.count > 0).map((x) => ({ ...x, pct: total === 0 ? 0 : (100 * x.count) / total })),
      };
    }),
  );

  protected readonly summary = computed(() =>
    'Work orders by status. ' +
    this.bars()
      .map((b) => `${b.label}: ${b.all.map((a) => `${a.count} ${STATUS_LABEL[a.status]}`).join(', ')}`)
      .join('. '),
  );
}

/** Completion by work package: one horizontal bar per package (single series, so no legend). */
@Component({
  selector: 'sr-package-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sr-table-wrap">
      <table class="pk">
        <caption class="sr-only">Completion by work package</caption>
        <thead class="sr-only">
          <tr><th scope="col">Work package</th><th scope="col">Completion</th><th scope="col">Done</th><th scope="col">Overdue</th></tr>
        </thead>
        <tbody>
          @for (p of packages(); track p.workPackage) {
            <tr>
              <th scope="row">{{ p.workPackage }}</th>
              <td class="bar-cell">
                <div class="track" [attr.title]="p.workPackage + ': ' + round(p.percentComplete) + '% complete'">
                  <div class="fill" [style.width.%]="p.percentComplete"></div>
                </div>
              </td>
              <td class="sr-num pct">{{ round(p.percentComplete) }}%</td>
              <td class="sr-num meta">{{ p.done }}/{{ p.total }} done</td>
              <td class="meta">
                @if (p.overdue > 0) {
                  <span class="late">{{ p.overdue }} overdue</span>
                }
              </td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styles: `
    .pk { width: 100%; border-collapse: collapse; font-size: var(--sr-app-body); }
    th[scope='row'] { text-align: left; font-weight: 400; padding: var(--sr-s2) var(--sr-s3) var(--sr-s2) 0; white-space: nowrap; }
    td { padding: var(--sr-s2) var(--sr-s2); }
    tr + tr th, tr + tr td { border-top: 1px solid var(--sr-line); }
    .bar-cell { width: 50%; min-width: 96px; }
    .track { height: 8px; background: var(--sr-subtle); border-radius: 2px; overflow: hidden; }
    .fill { height: 100%; background: var(--sr-ink-2); border-radius: 0 2px 2px 0; }
    .pct { font-family: var(--sr-font-mono); text-align: right; }
    .meta { color: var(--sr-ink-3); white-space: nowrap; font-size: var(--sr-app-small); font-family: var(--sr-font-mono); text-align: right; }
    .late { color: var(--sr-accent-strong); }
    @media (max-width: 639px) { .meta { display: none; } }
  `,
})
export class PackageChartComponent {
  readonly packages = input.required<WorkPackage[]>();
  protected round = Math.round;
}
