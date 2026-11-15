import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ActivityFeedComponent } from './activity-feed';

@Component({
  selector: 'sr-activity',
  imports: [ActivityFeedComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sr-page">
      <header class="sr-head">
        <div>
          <nav class="crumbs" aria-label="Breadcrumb"><span>Audit trail</span></nav>
          <h1>Activity</h1>
          <div class="meta"><span>Every change with actor, time and before/after values, for projects you can access</span></div>
        </div>
      </header>
      <sr-activity-feed [pageSize]="30" />
    </div>
  `,
})
export class ActivityPage {}
