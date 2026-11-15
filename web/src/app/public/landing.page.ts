import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../shared/ui';
import { GITHUB_URL, PublicFooterComponent, PublicHeaderComponent } from './public-chrome';

interface Callout {
  n: number;
  /** Position on the screenshot, in percent of its width/height. */
  x: number;
  y: number;
  title: string;
  body: string;
}

@Component({
  selector: 'sr-landing',
  imports: [RouterLink, IconComponent, PublicHeaderComponent, PublicFooterComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './landing.page.html',
  styleUrl: './landing.page.scss',
})
export class LandingPage {
  protected readonly github = GITHUB_URL;

  /** Numbered callouts over the work-order screenshot (positions match docs/screenshots/work-order.png). */
  protected readonly callouts: Callout[] = [
    { n: 1, x: 69.6, y: 12, title: 'Status follows a state machine', body: 'Start, block, complete, close. Closed work is locked; reopening needs a manager and a reason.' },
    { n: 2, x: 18.2, y: 30, title: 'Crew report from the field', body: 'Quantity installed, a note and an optional blocker. A blocker moves the work order to Blocked.' },
    { n: 3, x: 72.9, y: 65.6, title: 'Someone else verifies', body: 'Progress is verified by a supervisor who did not report it. Blockers are resolved explicitly.' },
    { n: 4, x: 92.6, y: 61.8, title: 'Materials, approved once', body: 'Requests are tied to the work order and decided once by the project manager, with an audit entry.' },
  ];

  protected readonly capabilities = [
    { area: 'Work orders', what: 'Assign within the project roster, prioritize, set due dates and milestones, add finish-to-start dependencies.', how: 'Server-side roster check; dependency cycles rejected; completion blocked until predecessors are done.' },
    { area: 'Progress', what: 'Quantities, notes and blockers reported by the crew.', how: 'Non-negative quantities enforced in the API and by CHECK constraints; verification by a different person.' },
    { area: 'Approvals', what: 'Material requests submitted once and approved or rejected by the project manager.', how: 'Client idempotency key with a unique index; repeated decisions are no-ops; contradictions return 409.' },
    { area: 'Concurrency', what: 'Two people editing the same work order.', how: 'SQL Server rowversion; a stale edit returns 409 and the UI says so instead of overwriting.' },
    { area: 'Dashboards', what: 'Overdue work, completion by work package, open blockers, approval backlog.', how: 'Two documented T-SQL views, queried on every load. Nothing is cached or typed in.' },
    { area: 'Audit', what: 'Activity feed for every meaningful change.', how: 'Actor, time and before/after values written in the same transaction as the change.' },
    { area: 'Isolation', what: 'Each demo visitor gets a private workspace.', how: 'Tenant query filters plus per-project membership checks on every read and write.' },
    { area: 'Blocker summary', what: 'A short summary of open blockers with links to the records behind each statement.', how: 'Deterministic rules, labelled “Rules-based summary”. No AI model is used.' },
  ];

  protected readonly roles = [
    {
      role: 'Project managers',
      does: 'See overdue work, open blockers and the approval backlog for every project, and approve materials once, with a record of who decided.',
    },
    {
      role: 'Site supervisors',
      does: 'Create work orders, assign them to the project team, verify reported progress and clear blockers.',
    },
    {
      role: 'Crews',
      does: 'See assigned work on a phone, log quantities and blockers from the field, and request materials against the work order.',
    },
  ];

  /** Real results from docs/STATUS.md. Update both together. */
  protected readonly metrics = [
    { value: '60', label: 'backend tests', detail: 'xUnit against SQL Server 2022 in a disposable container' },
    { value: '14', label: 'frontend unit tests', detail: 'Angular services and components' },
    { value: '16', label: 'browser tests', detail: 'Playwright, including the full demo scenario' },
    { value: '33 ms', label: 'dashboard median', detail: 'Measured locally against the Docker container, not a hosted service' },
  ];
}
