import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { Loader } from '../core/loader';
import { ActivityItem, Paged } from '../core/models';
import { AgoPipe, StateComponent } from '../shared/ui';

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
  imports: [RouterLink, FormsModule, MatButtonModule, StateComponent, AgoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sr-toolbar">
      <label class="sr-field">
        <span class="lbl">Show</span>
        <select [ngModel]="action()" (ngModelChange)="action.set($event); load(1)" data-testid="activity-filter">
          @for (a of actions; track a.value) {
            <option [value]="a.value">{{ a.label }}</option>
          }
        </select>
      </label>
      @if (feed.data(); as f) {
        <span class="sr-meta">{{ f.totalCount }} event{{ f.totalCount === 1 ? '' : 's' }}</span>
      }
    </div>

    <div class="sr-panel">
      @if (feed.error(); as e) {
        <sr-state kind="error" [error]="e" (retry)="load(page())" />
      } @else if (!feed.data()) {
        <sr-state kind="loading" />
      } @else if (feed.data()!.items.length === 0) {
        <sr-state title="No activity matches this filter" />
      } @else {
        <ol class="sr-list feed" data-testid="activity-feed">
          @for (a of feed.data()!.items; track a.id) {
            <li [attr.data-kind]="kind(a)">
              <p class="summary">
                @if (a.link) {
                  <a [routerLink]="a.link.type === 'workOrder' ? ['/app/work-orders', a.link.id] : ['/app/projects', a.link.id]">{{ a.summary }}</a>
                } @else {
                  <span>{{ a.summary }}</span>
                }
              </p>
              <p class="sr-meta">
                {{ a.actorName }} ·
                <time [attr.datetime]="a.createdAt" [attr.title]="a.createdAt">{{ a.createdAt | ago }}</time>
                @if (a.projectCode && showProject()) {
                  · <span class="sr-mono">{{ a.projectCode }}</span>
                }
              </p>
              @if (hasDiff(a)) {
                <details>
                  <summary class="sr-meta">Changed fields</summary>
                  <div class="diff-wrap">
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
                  </div>
                </details>
              }
            </li>
          }
        </ol>
        @if (feed.data()!.totalPages > 1) {
          <div class="pager">
            <button mat-stroked-button [disabled]="page() <= 1" (click)="load(page() - 1)">Newer</button>
            <span class="sr-meta sr-mono">{{ page() }} / {{ feed.data()!.totalPages }}</span>
            <button mat-stroked-button [disabled]="page() >= feed.data()!.totalPages" (click)="load(page() + 1)">Older</button>
          </div>
        }
      }
    </div>
  `,
  styles: `
    .lbl { font-size: var(--sr-app-small); color: var(--sr-ink-3); }
    .feed li { box-shadow: inset 2px 0 0 transparent; }
    .feed li[data-kind='bad'] { box-shadow: inset 2px 0 0 var(--sr-critical); }
    .summary { margin: 0; }
    .summary a { color: var(--sr-ink); text-decoration: none; }
    .summary a:hover { text-decoration: underline; }
    .sr-meta { margin: 2px 0 0; }
    details { margin-top: var(--sr-s1); }
    summary { cursor: pointer; }
    .diff-wrap { overflow-x: auto; max-width: 100%; }
    .diff { border-collapse: collapse; margin-top: var(--sr-s2); font-size: var(--sr-app-small); }
    .diff th, .diff td { border: 1px solid var(--sr-line); padding: var(--sr-s1) var(--sr-s2); text-align: left; vertical-align: top; max-width: 280px; overflow-wrap: anywhere; font-weight: 400; }
    .diff thead th { background: var(--sr-subtle); color: var(--sr-ink-3); }
    .pager { display: flex; align-items: center; gap: var(--sr-s3); justify-content: center; padding: var(--sr-s3); border-top: 1px solid var(--sr-line); }
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
}
