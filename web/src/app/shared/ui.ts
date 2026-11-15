import { ChangeDetectionStrategy, Component, Pipe, PipeTransform, computed, input, output } from '@angular/core';
import { ApiError } from '../core/api.service';
import { MaterialRequestStatus, WorkOrderStatus, ErpTransmissionStatus } from '../core/models';
import { ICONS, IconName } from './icons.generated';

/** Inline SVG icon (Material Symbols outlined paths). Decorative unless `label` is given. */
@Component({
  selector: 'sr-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg
      viewBox="0 -960 960 960"
      [attr.width]="size()"
      [attr.height]="size()"
      [attr.aria-hidden]="label() ? null : 'true'"
      [attr.role]="label() ? 'img' : null"
      [attr.aria-label]="label() || null"
      focusable="false"
    >
      <path [attr.d]="path()" fill="currentColor" />
    </svg>
  `,
  styles: `
    :host {
      display: inline-flex;
      line-height: 0;
      flex: none;
    }
  `,
})
export class IconComponent {
  readonly name = input.required<IconName>();
  readonly size = input(20);
  readonly label = input<string>('');
  protected readonly path = computed(() => ICONS[this.name()]);
}

export const STATUS_LABEL: Record<WorkOrderStatus, string> = {
  Open: 'Open',
  InProgress: 'In progress',
  Blocked: 'Blocked',
  Completed: 'Completed',
  Closed: 'Closed',
  Cancelled: 'Cancelled',
};

const STATUS_ICON: Record<WorkOrderStatus, IconName> = {
  Open: 'radio_button_unchecked',
  InProgress: 'trending_up',
  Blocked: 'block',
  Completed: 'task_alt',
  Closed: 'lock',
  Cancelled: 'close',
};

@Pipe({ name: 'statusLabel' })
export class StatusLabelPipe implements PipeTransform {
  transform(value: WorkOrderStatus | string): string {
    return STATUS_LABEL[value as WorkOrderStatus] ?? value;
  }
}

/** Relative time ("3 h ago", "in 2 min") with the absolute timestamp available as a tooltip. */
@Pipe({ name: 'ago' })
export class AgoPipe implements PipeTransform {
  transform(value: string | null | undefined, now: number = Date.now()): string {
    if (!value) return '—';
    const diff = (new Date(value).getTime() - now) / 1000;
    const abs = Math.abs(diff);
    const fmt = (n: number, unit: string) => {
      const r = Math.round(n);
      return diff < 0 ? `${r} ${unit} ago` : `in ${r} ${unit}`;
    };
    if (abs < 45) return diff < 0 ? 'just now' : 'in a few seconds';
    if (abs < 3600) return fmt(abs / 60, 'min');
    if (abs < 86400) return fmt(abs / 3600, 'h');
    if (abs < 86400 * 45) return fmt(abs / 86400, abs / 86400 < 1.5 ? 'day' : 'days');
    return new Date(value).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
  }
}

@Pipe({ name: 'day' })
export class DayPipe implements PipeTransform {
  /** Formats a yyyy-MM-dd date without timezone shifting. */
  transform(value: string | null | undefined, style: 'short' | 'long' = 'short'): string {
    if (!value) return '—';
    const [y, m, d] = value.slice(0, 10).split('-').map(Number);
    const date = new Date(y, m - 1, d);
    return date.toLocaleDateString(undefined, style === 'long' ? { weekday: 'short', year: 'numeric', month: 'short', day: 'numeric' } : { month: 'short', day: 'numeric' });
  }
}

@Component({
  selector: 'sr-status',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="chip" [attr.data-status]="status()"><sr-icon [name]="icon()" [size]="12" />{{ label() }}</span>`,
  styles: `
    .chip { display: inline-flex; align-items: center; gap: 4px; height: 20px; padding: 0 6px; border-radius: var(--sr-radius); font-size: var(--sr-app-small, 0.75rem); font-weight: 500; line-height: 1; white-space: nowrap; border: 1px solid var(--sr-line); background: var(--sr-surface); color: var(--sr-ink-2); }
    [data-status='Open'] { background: var(--sr-st-open-bg); color: var(--sr-st-open); border-color: transparent; }
    [data-status='InProgress'] { background: var(--sr-st-progress-bg); color: var(--sr-st-progress); border-color: transparent; }
    [data-status='Blocked'] { background: var(--sr-st-blocked-bg); color: var(--sr-st-blocked); border-color: transparent; }
    [data-status='Completed'] { background: var(--sr-st-completed-bg); color: var(--sr-st-completed); border-color: transparent; }
    [data-status='Closed'] { background: var(--sr-st-closed-bg); color: var(--sr-st-closed); border-color: transparent; }
    [data-status='Cancelled'] { color: var(--sr-ink-3); }
  `,
})
export class StatusChipComponent {
  readonly status = input.required<WorkOrderStatus>();
  protected readonly label = computed(() => STATUS_LABEL[this.status()]);
  protected readonly icon = computed(() => STATUS_ICON[this.status()]);
}

const MR_LABEL: Record<MaterialRequestStatus, string> = { Submitted: 'Pending approval', Approved: 'Approved', Rejected: 'Rejected' };
const ERP_LABEL: Record<ErpTransmissionStatus, string> = {
  NotApplicable: 'Not sent',
  Queued: 'Queued for ERP',
  Retrying: 'Retrying',
  Sent: 'Acknowledged by ERP',
  Failed: 'ERP failed',
};

@Component({
  selector: 'sr-mr-status',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="chip" [attr.data-s]="status()">
    <sr-icon [name]="status() === 'Approved' ? 'check_circle' : status() === 'Rejected' ? 'block' : 'hourglass_top'" [size]="12" />{{ label() }}
  </span>`,
  styles: `
    .chip { display: inline-flex; align-items: center; gap: 4px; height: 20px; padding: 0 6px; border-radius: var(--sr-radius); font-size: var(--sr-app-small, 0.75rem); font-weight: 500; line-height: 1; white-space: nowrap; border: 1px solid var(--sr-line); background: var(--sr-surface); color: var(--sr-ink-2); }
    [data-s='Submitted'] { background: var(--sr-warn-soft); color: var(--sr-warn); border-color: transparent; }
    [data-s='Approved'] { background: var(--sr-good-soft); color: var(--sr-good); border-color: transparent; }
    [data-s='Rejected'] { background: var(--sr-critical-soft); color: var(--sr-critical); border-color: transparent; }
  `,
})
export class MaterialStatusComponent {
  readonly status = input.required<MaterialRequestStatus>();
  protected readonly label = computed(() => MR_LABEL[this.status()]);
}

@Component({
  selector: 'sr-erp-status',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="chip" [attr.data-s]="status()" [attr.data-testid]="'erp-' + status()">
    <sr-icon [name]="icon()" [size]="12" />{{ label() }}
  </span>`,
  styles: `
    .chip { display: inline-flex; align-items: center; gap: 4px; height: 20px; padding: 0 6px; border-radius: var(--sr-radius); font-size: var(--sr-app-small, 0.75rem); font-weight: 500; line-height: 1; white-space: nowrap; border: 1px solid var(--sr-line); background: var(--sr-surface); color: var(--sr-ink-2); }
    [data-s='Queued'] { background: var(--sr-info-soft); color: var(--sr-info); border-color: transparent; }
    [data-s='Retrying'] { background: var(--sr-warn-soft); color: var(--sr-warn); border-color: transparent; }
    [data-s='Sent'] { background: var(--sr-good-soft); color: var(--sr-good); border-color: transparent; }
    [data-s='Failed'] { background: var(--sr-critical-soft); color: var(--sr-critical); border-color: transparent; }
  `,
})
export class ErpStatusComponent {
  readonly status = input.required<ErpTransmissionStatus>();
  protected readonly label = computed(() => ERP_LABEL[this.status()]);
  protected readonly icon = computed(() => ERP_ICON[this.status()]);
}

const ERP_ICON: Record<ErpTransmissionStatus, IconName> = {
  NotApplicable: 'cloud_off',
  Queued: 'schedule',
  Retrying: 'sync',
  Sent: 'cloud_done',
  Failed: 'error',
};

@Component({
  selector: 'sr-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (kind()) {
      @case ('loading') {
        <div class="state" role="status" aria-live="polite">
          <span class="spinner" aria-hidden="true"></span>
          <span>{{ message() || 'Loading…' }}</span>
        </div>
      }
      @case ('error') {
        <div class="state error" role="alert">
          <div class="text">
            <strong>{{ error()?.title || 'Something went wrong' }}</strong>
            <span>{{ error()?.detail || message() }}</span>
          </div>
          @if (retryable()) {
            <button type="button" class="retry" (click)="retry.emit()">Try again</button>
          }
        </div>
      }
      @default {
        <div class="state empty">
          <div class="text">
            <strong>{{ title() || 'Nothing here yet' }}</strong>
            @if (message()) {
              <span>{{ message() }}</span>
            }
          </div>
          <ng-content />
        </div>
      }
    }
  `,
  styles: `
    .state { display: flex; align-items: center; gap: var(--sr-s3); flex-wrap: wrap; padding: var(--sr-s4); color: var(--sr-ink-2); }
    .text { display: flex; flex-direction: column; gap: 2px; flex: 1 1 200px; }
    .text strong { font-weight: 500; color: var(--sr-ink); }
    .text span { font-size: var(--sr-app-small, 0.75rem); color: var(--sr-ink-3); }
    .error { box-shadow: inset 2px 0 0 var(--sr-critical); }
    .retry { height: 32px; padding: 0 var(--sr-s3); border-radius: var(--sr-radius); border: 1px solid var(--sr-line-strong); background: var(--sr-surface); color: var(--sr-ink); font: inherit; font-weight: 500; cursor: pointer; }
    .spinner { width: 14px; height: 14px; border-radius: 50%; border: 2px solid var(--sr-line); border-top-color: var(--sr-ink-2); animation: spin 0.7s linear infinite; }
    @keyframes spin { to { transform: rotate(360deg); } }
    @media (max-width: 767px) { .retry { height: 44px; } }
  `,
})
export class StateComponent {
  readonly kind = input<'loading' | 'error' | 'empty'>('empty');
  readonly title = input('');
  readonly message = input('');
  readonly error = input<ApiError | null>(null);
  readonly retryable = input(true);
  readonly retry = output<void>();
}

export interface StatItem {
  label: string;
  value: string | number;
  hint?: string;
  tone?: 'neutral' | 'critical';
  testId?: string;
}

/** One quiet row of 3-4 key numbers (replaces rows of identical KPI tiles). */
@Component({
  selector: 'sr-stats',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dl class="stats" [style.--cols]="items().length">
      @for (s of items(); track s.label) {
        <div class="stat" [attr.data-tone]="s.tone ?? 'neutral'">
          <dt>{{ s.label }}</dt>
          <dd class="value" [attr.data-testid]="s.testId || null">{{ s.value }}</dd>
          @if (s.hint) {
            <dd class="hint">{{ s.hint }}</dd>
          }
        </div>
      }
    </dl>
  `,
  styles: `
    .stats { display: grid; grid-template-columns: repeat(var(--cols), minmax(0, 1fr)); margin: 0; background: var(--sr-surface); border: 1px solid var(--sr-line); border-radius: var(--sr-radius); }
    .stat { padding: var(--sr-s3) var(--sr-s4); border-left: 1px solid var(--sr-line); min-width: 0; }
    .stat:first-child { border-left: 0; }
    dt { font-size: var(--sr-app-small); color: var(--sr-ink-3); }
    dd { margin: 0; }
    .value { font-family: var(--sr-font-mono); font-size: var(--sr-app-title); font-weight: 500; line-height: 1.3; font-variant-numeric: tabular-nums; }
    [data-tone='critical'] .value { color: var(--sr-accent-strong); }
    .hint { font-size: var(--sr-app-small); color: var(--sr-ink-3); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    @media (max-width: 639px) {
      .stats { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .stat:nth-child(odd) { border-left: 0; }
      .stat:nth-child(n + 3) { border-top: 1px solid var(--sr-line); }
    }
  `,
})
export class StatsComponent {
  readonly items = input.required<StatItem[]>();
}

/** Labelled progress meter with an accessible value. */
@Component({
  selector: 'sr-meter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="meter" role="progressbar" [attr.aria-valuenow]="clamped()" aria-valuemin="0" aria-valuemax="100" [attr.aria-label]="label()">
      <div class="fill" [style.width.%]="clamped()" [attr.data-tone]="tone()"></div>
    </div>
  `,
  styles: `
    .meter { height: 4px; background: var(--sr-subtle); border-radius: 2px; overflow: hidden; }
    .fill { height: 100%; background: var(--sr-ink-2); border-radius: 2px; transition: width 200ms var(--sr-ease); }
    .fill[data-tone='good'] { background: var(--sr-good); }
    .fill[data-tone='critical'] { background: var(--sr-accent); }
  `,
})
export class MeterComponent {
  readonly value = input(0);
  readonly label = input('Progress');
  readonly tone = input<'info' | 'good' | 'critical'>('info');
  protected readonly clamped = computed(() => Math.max(0, Math.min(100, Math.round(this.value()))));
}

export const SHARED = [
  IconComponent,
  StatusChipComponent,
  MaterialStatusComponent,
  ErpStatusComponent,
  StatsComponent,
  StateComponent,
  MeterComponent,
  StatusLabelPipe,
  AgoPipe,
  DayPipe,
] as const;
