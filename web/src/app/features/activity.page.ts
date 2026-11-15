import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ActivityFeedComponent } from './activity-feed';

@Component({
  selector: 'sr-activity',
  imports: [ActivityFeedComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sr-page">
      <p class="sr-kicker">Activity</p>
      <h1>Activity and audit trail</h1>
      <p class="sr-muted intro">
        Every meaningful change is written to an append-only audit table in the same transaction as the change itself: who
        did it, when, and the before/after values of the fields that changed. You only see events for projects you can access.
      </p>
      <section class="sr-card">
        <sr-activity-feed [pageSize]="30" />
      </section>
    </div>
  `,
  styles: `.intro { max-width: 80ch; margin-bottom: 16px; }`,
})
export class ActivityPage {}
