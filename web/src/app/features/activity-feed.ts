import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { Loader } from '../core/loader';
import { ActivityItem, Paged } from '../core/models';
import { AgoPipe, IconComponent, StateComponent } from '../shared/ui';
import { IconName } from '../shared/icons.generated';

const ACTIONS: { value: string; label: string }[] = [
  { value: '', label: 'All events' },
  { value: 'created', label: 'Created' },
  { value: 'status_changed', label: 'Status changes' },
  { value: 'assigned', label: 'Assignments' },
  { value: 'progress_reported', label: 'Progress reported' },
  { value: 'progress_verified', label: 'Progress verified' },
  { value: 'blocker_resolved', label: 'Blockers resolved' },
  { value: 'commented', label: 'Comments' },
  { value: 'submitted', label: 'Material requests' },
  { value: 'approved', label: 'Approvals' },
  { value: 'rejected', label: 'Rejections' },
  { value: 'erp_acknowledged', label: 'ERP acknowledged' },
  { value: 'erp_retry_scheduled', label: 'ERP retries' },
  { value: 'erp_failed', label: 'ERP failures' },
  { value: 'updated', label: 'Edits' },
  { value: 'settings_changed', label: 'Settings' },
];

/** Audit-backed activity feed with before/after details. Used on /app/activity and project pages. */
@Component({
  selector: 'sr-activity-feed',
  imports: [RouterLink, FormsModule, MatFormFieldModule, MatSelectModule, MatButtonModule, IconComponent, StateComponent, AgoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="filters">
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>Event type</mat-label>
        <mat-select [ngModel]="action()" (ngModelChange)="action.set($event); load(1)" data-testid="activity-filter">
          @for (a of actions; track a.value) {
            <mat-option [value]="a.value">{{ a.label }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      @if (feed.data(); as f) {
        <span class="sr-muted small">{{ f.totalCount }} event{{ f.totalCount === 1 ? '' : 's' }}</span>
      }
    </div>

    @if (feed.error(); as e) {
      <sr-state kind="error" [error]="e" (retry)="load(page())" />
    } @else if (!feed.data()) {
      <sr-state kind="loading" />
    } @else if (feed.data()!.items.length === 0) {
      <sr-state title="No activity matches this filter" />
    } @else {
      <ol class="feed" data-testid="activity-feed">
        @for (a of feed.data()!.items; track a.id) {
          <li>
            <span class="ico" [attr.data-kind]="kind(a)"><sr-icon [name]="icon(a)" [size]="18" /></span>
            <div class="body">
              <p class="summary">
                <strong>{{ a.actorName }}</strong>
                @if (a.link) {
                  <a [routerLink]="a.link.type === 'workOrder' ? ['/app/work-orders', a.link.id] : ['/app/projects', a.link.id]">{{ a.summary }}</a>
                } @else {
                  <span>{{ a.summary }}</span>
                }
              </p>
              <p class="meta">
                <time [attr.datetime]="a.createdAt" [attr.title]="a.createdAt">{{ a.createdAt | ago }}</time>
                @if (a.projectCode && showProject()) {
                  · {{ a.projectCode }}
                }
                · {{ a.entityType }} · {{ a.action }}
              </p>
              @if (hasDiff(a)) {
                <details>
                  <summary>Changed fields</summary>
                  <table class="diff">
                    <thead><tr><th scope="col">Field</th><th scope="col">Before</th><th scope="col">After</th></tr></thead>
                    <tbody>
                      @for (f of fields(a); track f) {
                        <tr>
                          <th scope="row">{{ f }}</th>
                          <td>{{ show(a.before?.[f]) }}</td>
                          <td>{{ show(a.after?.[f]) }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </details>
              }
            </div>
          </li>
        }
      </ol>
      @if (feed.data()!.totalPages > 1) {
        <div class="pager">
          <button mat-stroked-button [disabled]="page() <= 1" (click)="load(page() - 1)">Newer</button>
          <span class="sr-num">Page {{ page() }} of {{ feed.data()!.totalPages }}</span>
          <button mat-stroked-button [disabled]="page() >= feed.data()!.totalPages" (click)="load(page() + 1)">Older</button>
        </div>
      }
    }
  `,
  styles: `
    .filters { display: flex; align-items: center; gap: 12px; margin-bottom: 12px; flex-wrap: wrap; }
    .filters mat-form-field { width: 240px; }
    .small { font-size: 0.85rem; }
    .feed { list-style: none; margin: 0; padding: 0; }
    .feed li { display: grid; grid-template-columns: 34px 1fr; gap: 12px; padding: 12px 0; border-bottom: 1px solid var(--sr-line); }
    .ico { display: inline-grid; place-items: center; width: 32px; height: 32px; border-radius: 8px; background: var(--sr-concrete); color: var(--sr-ink-2); }
    .ico[data-kind='good'] { background: var(--sr-good-soft); color: var(--sr-good); }
    .ico[data-kind='bad'] { background: var(--sr-critical-soft); color: var(--sr-critical); }
    .ico[data-kind='warn'] { background: var(--sr-warning-soft); color: var(--sr-warning); }
    .summary { margin: 0; }
    .summary strong { margin-right: 6px; }
    .summary a { color: var(--sr-ink); }
    .meta { margin: 2px 0 0; font-size: 0.8rem; color: var(--sr-ink-3); }
    details { margin-top: 6px; font-size: 0.85rem; }
    summary { cursor: pointer; color: var(--sr-blueprint); }
    .diff { border-collapse: collapse; margin-top: 6px; }
    .diff th, .diff td { border: 1px solid var(--sr-line); padding: 4px 8px; text-align: left; vertical-align: top; max-width: 320px; overflow-wrap: anywhere; }
    .diff thead th { background: var(--sr-concrete); font-size: 0.75rem; }
    .pager { display: flex; align-items: center; gap: 12px; justify-content: center; margin-top: 14px; }
  `,
})
export class ActivityFeedComponent implements OnInit {
  readonly projectId = input<string | null>(null);
  readonly showProject = input(true);
  readonly pageSize = input(25);
  private readonly api = inject(ApiService);
  protected readonly feed = new Loader<Paged<ActivityItem>>();
  protected readonly actions = ACTIONS;
  protected readonly action = signal('');
  protected readonly page = signal(1);

  ngOnInit(): void {
    this.load(1);
  }

  load(page: number): void {
    this.page.set(page);
    this.feed.load(
      this.api.activity({ projectId: this.projectId() ?? undefined, action: this.action() || undefined, page, pageSize: this.pageSize() }),
    );
  }

  protected hasDiff(a: ActivityItem): boolean {
    return this.fields(a).length > 0;
  }

  protected fields(a: ActivityItem): string[] {
    return [...new Set([...Object.keys(a.before ?? {}), ...Object.keys(a.after ?? {})])];
  }

  protected show(v: unknown): string {
    if (v === null || v === undefined || v === '') return '—';
    return typeof v === 'object' ? JSON.stringify(v) : String(v);
  }

  protected kind(a: ActivityItem): string {
    if (['approved', 'erp_acknowledged', 'progress_verified', 'blocker_resolved'].includes(a.action)) return 'good';
    if (['rejected', 'erp_failed'].includes(a.action)) return 'bad';
    if (['erp_retry_scheduled'].includes(a.action) || a.summary.includes('to Blocked') || a.summary.includes('blocker')) return 'warn';
    return 'neutral';
  }

  protected icon(a: ActivityItem): IconName {
    const map: Record<string, IconName> = {
      created: 'add',
      updated: 'edit',
      status_changed: 'swap_horiz',
      assigned: 'person',
      commented: 'comment',
      progress_reported: 'trending_up',
      progress_verified: 'verified',
      blocker_resolved: 'task_alt',
      dependency_added: 'link',
      dependency_removed: 'link_off',
      submitted: 'inventory_2',
      approved: 'check_circle',
      rejected: 'block',
      erp_acknowledged: 'cloud_done',
      erp_retry_scheduled: 'sync',
      erp_failed: 'cloud_off',
      erp_manual_retry: 'refresh',
      settings_changed: 'tune',
    };
    return map[a.action] ?? 'history';
  }
}
