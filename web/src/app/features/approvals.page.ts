import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { Loader } from '../core/loader';
import { IntegrationHealth, MaterialRequest, MaterialRequestStatus, Paged } from '../core/models';
import { NotifyService } from '../core/notify.service';
import { AgoPipe, ErpStatusComponent, MaterialStatusComponent, StateComponent, StatItem, StatsComponent } from '../shared/ui';
import { DecisionDialogComponent } from './dialogs';

type Filter = MaterialRequestStatus | '';

@Component({
  selector: 'sr-approvals',
  imports: [RouterLink, MatButtonModule, StateComponent, StatsComponent, MaterialStatusComponent, ErpStatusComponent, AgoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './approvals.page.html',
  styleUrl: './approvals.page.scss',
})
export class ApprovalsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  protected readonly requests = new Loader<Paged<MaterialRequest>>();
  protected readonly health = new Loader<IntegrationHealth>();
  protected readonly filter = signal<Filter>('Submitted');
  protected readonly lastRefresh = signal<string | null>(null);
  private timer?: ReturnType<typeof setInterval>;

  /** While a transmission is queued or retrying, poll so the ERP status visibly moves without a reload. */
  protected readonly inFlight = computed(
    () =>
      (this.requests.data()?.items ?? []).some((r) => r.erp.status === 'Queued' || r.erp.status === 'Retrying') ||
      (this.health.data()?.outbox.pending ?? 0) + (this.health.data()?.outbox.retrying ?? 0) + (this.health.data()?.outbox.processing ?? 0) > 0,
  );

  protected readonly filterLabel = computed(() =>
    ({ Submitted: 'pending', Approved: 'approved', Rejected: 'rejected', '': 'requests' })[this.filter()],
  );

  protected readonly healthStats = computed<StatItem[]>(() => {
    const h = this.health.data();
    if (!h) return [];
    return [
      { label: 'Queued', value: h.outbox.pending + h.outbox.processing },
      { label: 'Retrying', value: h.outbox.retrying, testId: 'outbox-retrying' },
      { label: 'Sent', value: h.outbox.succeeded, testId: 'outbox-sent' },
      { label: 'Failed', value: h.outbox.failed, tone: h.outbox.failed ? 'critical' : 'neutral' },
    ];
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => clearInterval(this.timer));
  }

  ngOnInit(): void {
    this.load();
    this.timer = setInterval(() => {
      if (this.inFlight() && !document.hidden) this.load(true);
    }, 3000);
  }

  protected load(silent = false): void {
    if (!silent || !this.requests.loading()) {
      this.requests.load(this.api.materialRequests({ status: this.filter(), pageSize: 100 }));
      this.health.load(this.api.integrationHealth());
      this.lastRefresh.set(new Date().toISOString());
    }
  }

  protected setFilter(f: Filter): void {
    this.filter.set(f);
    this.load();
  }

  protected decide(request: MaterialRequest, decision: 'Approve' | 'Reject'): void {
    this.dialog
      .open(DecisionDialogComponent, { data: { request, decision } })
      .afterClosed()
      .subscribe((result?: MaterialRequest) => {
        if (!result) return;
        this.notify.success(
          decision === 'Approve' ? `Approved. The requisition is queued for the ERP (${result.workOrderKey}).` : 'Request rejected and the crew notified in the activity feed.',
        );
        this.load();
      });
  }

  protected async retry(outboxId: string): Promise<void> {
    try {
      await firstValueFrom(this.api.retryOutbox(outboxId));
      this.notify.success('Transmission re-queued.');
      this.load();
    } catch (err) {
      this.notify.error(err);
    }
  }
}
