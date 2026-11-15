import { ChangeDetectionStrategy, Component, Pipe, PipeTransform, computed, input, output } from '@angular/core';
import { ApiError } from '../core/api.service';
import { MaterialRequestStatus, WorkOrderPriority, WorkOrderStatus, ErpTransmissionStatus } from '../core/models';
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
  template: `<span class="chip" [attr.data-status]="status()"><sr-icon [name]="icon()" [size]="15" />{{ label() }}</span>`,
  styles: `
    .chip {
      display: inline-flex;
      align-items: center;
      gap: 5px;
      padding: 2px 9px 2px 7px;
      border-radius: 999px;
      font-size: 0.78rem;
      font-weight: 650;
      white-space: nowrap;
      border: 1px solid transparent;
    }
    [data-status='Open'] { background: #f3ecf8; color: #5b3b78; border-color: #e0cdee; }
    [data-status='InProgress'] { background: var(--sr-info-soft); color: #1a4fa8; border-color: #c7d6fb; }
    [data-status='Blocked'] { background: var(--sr-critical-soft); color: var(--sr-critical); border-color: #f6c5c0; }
    [data-status='Completed'] { background: var(--sr-warning-soft); color: var(--sr-warning); border-color: #f1dca3; }
    [data-status='Closed'] { background: var(--sr-good-soft); color: var(--sr-good); border-color: #bfe3cc; }
    [data-status='Cancelled'] { background: var(--sr-concrete-2); color: var(--sr-ink-2); }
  `,
})
export class StatusChipComponent {
  readonly status = input.required<WorkOrderStatus>();
  protected readonly label = computed(() => STATUS_LABEL[this.status()]);
  protected readonly icon = computed(() => STATUS_ICON[this.status()]);
}

@Component({
  selector: 'sr-priority',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="p" [attr.data-p]="priority()">
    @if (priority() === 'Critical' || priority() === 'High') {
      <sr-icon name="flag" [size]="14" />
    }
    {{ priority() }}</span
  >`,
  styles: `
    .p { display: inline-flex; align-items: center; gap: 3px; font-size: 0.78rem; font-weight: 650; color: var(--sr-ink-3); }
    [data-p='Critical'] { color: var(--sr-critical); }
    [data-p='High'] { color: #a4470b; }
  `,
})
export class PriorityComponent {
  readonly priority = input.required<WorkOrderPriority>();
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
    <sr-icon [name]="status() === 'Approved' ? 'check_circle' : status() === 'Rejected' ? 'block' : 'hourglass_top'" [size]="15" />{{ label() }}
  </span>`,
  styles: `
    .chip { display: inline-flex; align-items: center; gap: 5px; padding: 2px 9px 2px 7px; border-radius: 999px; font-size: 0.78rem; font-weight: 650; white-space: nowrap; }
    [data-s='Submitted'] { background: var(--sr-warning-soft); color: var(--sr-warning); }
    [data-s='Approved'] { background: var(--sr-good-soft); color: var(--sr-good); }
    [data-s='Rejected'] { background: var(--sr-critical-soft); color: var(--sr-critical); }
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
    <sr-icon [name]="icon()" [size]="15" />{{ label() }}
  </span>`,
  styles: `
    .chip { display: inline-flex; align-items: center; gap: 5px; padding: 2px 9px 2px 7px; border-radius: 999px; font-size: 0.78rem; font-weight: 650; white-space: nowrap; background: var(--sr-concrete-2); color: var(--sr-ink-2); }
    [data-s='Queued'] { background: var(--sr-info-soft); color: var(--sr-info); }
    [data-s='Retrying'] { background: var(--sr-warning-soft); color: var(--sr-warning); }
    [data-s='Sent'] { background: var(--sr-good-soft); color: var(--sr-good); }
    [data-s='Failed'] { background: var(--sr-critical-soft); color: var(--sr-critical); }
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
  selector: 'sr-kpi',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="kpi" [attr.data-tone]="tone()">
      <div class="top">
        <span class="sr-kicker">{{ label() }}</span>
        <sr-icon [name]="icon()" [size]="20" />
      </div>
      <div class="value sr-num" [attr.data-testid]="testId() || null">{{ value() }}</div>
      @if (hint()) {
        <div class="hint">{{ hint() }}</div>
      }
    </div>
  `,
  styles: `
    .kpi { background: var(--sr-paper); border: 1px solid var(--sr-line); border-radius: var(--sr-radius-lg); padding: 14px 16px; height: 100%; border-top: 4px solid var(--sr-line-strong); box-shadow: var(--sr-shadow); }
    .kpi[data-tone='critical'] { border-top-color: var(--sr-critical); }
    .kpi[data-tone='warning'] { border-top-color: var(--sr-safety); }
    .kpi[data-tone='good'] { border-top-color: var(--sr-good); }
    .kpi[data-tone='info'] { border-top-color: var(--sr-blueprint); }
    .top { display: flex; justify-content: space-between; align-items: center; color: var(--sr-ink-3); }
    .value { font: 750 2rem/1.1 var(--sr-font-display); margin-top: 6px; color: var(--sr-ink); }
    .hint { font-size: 0.82rem; color: var(--sr-ink-3); margin-top: 4px; }
  `,
})
export class KpiComponent {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();
  readonly icon = input<IconName>('info');
  readonly hint = input<string>('');
  readonly tone = input<'neutral' | 'critical' | 'warning' | 'good' | 'info'>('neutral');
  readonly testId = input<string>('');
}

@Component({
  selector: 'sr-state',
  imports: [IconComponent],
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
          <sr-icon name="error" [size]="28" />
          <div>
            <strong>{{ error()?.title || 'Something went wrong' }}</strong>
            <p>{{ error()?.detail || message() }}</p>
          </div>
          @if (retryable()) {
            <button type="button" class="retry" (click)="retry.emit()">Try again</button>
          }
        </div>
      }
      @default {
        <div class="state empty">
          <sr-icon [name]="'checklist'" [size]="28" />
          <div>
            <strong>{{ title() || 'Nothing here yet' }}</strong>
            @if (message()) {
              <p>{{ message() }}</p>
            }
          </div>
          <ng-content />
        </div>
      }
    }
  `,
  styles: `
    .state { display: flex; align-items: center; gap: 14px; padding: 22px; color: var(--sr-ink-2); flex-wrap: wrap; }
    .state p { margin: 2px 0 0; color: var(--sr-ink-3); }
    .error { background: var(--sr-critical-soft); border-radius: var(--sr-radius); color: var(--sr-critical); }
    .error p { color: var(--sr-ink-2); }
    .empty { justify-content: flex-start; border: 1px dashed var(--sr-line-strong); border-radius: var(--sr-radius); }
    .retry { margin-left: auto; padding: 8px 14px; border-radius: var(--sr-radius); border: 1px solid currentColor; background: white; color: inherit; font-weight: 600; cursor: pointer; }
    .spinner { width: 18px; height: 18px; border-radius: 50%; border: 3px solid var(--sr-line); border-top-color: var(--sr-blueprint); animation: spin 0.8s linear infinite; }
    @keyframes spin { to { transform: rotate(360deg); } }
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
    .meter { height: 8px; background: var(--sr-concrete-2); border-radius: 999px; overflow: hidden; }
    .fill { height: 100%; background: var(--sr-blueprint); border-radius: 999px; transition: width 0.4s ease; }
    .fill[data-tone='good'] { background: var(--sr-good); }
    .fill[data-tone='critical'] { background: var(--sr-st-blocked); }
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
  PriorityComponent,
  MaterialStatusComponent,
  ErpStatusComponent,
  KpiComponent,
  StateComponent,
  MeterComponent,
  StatusLabelPipe,
  AgoPipe,
  DayPipe,
] as const;
