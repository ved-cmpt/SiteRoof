import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../shared/ui';
import { IconName } from '../shared/icons.generated';
import { GITHUB_URL, PublicFooterComponent, PublicHeaderComponent } from './public-chrome';

interface Feature {
  icon: IconName;
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

  protected readonly problems = [
    {
      title: 'The schedule lives in the office, the truth lives on site',
      body: 'Supervisors know drywall is short 40 sheets; the project manager finds out at the weekly meeting.',
    },
    {
      title: 'Approvals stall in inboxes',
      body: 'Material requests arrive by text and email, get approved twice or not at all, and never reach purchasing cleanly.',
    },
    {
      title: 'Dashboards are rebuilt by hand',
      body: 'Overdue counts and completion percentages are copied into spreadsheets and are stale the moment they are shared.',
    },
  ];

  protected readonly workflows = [
    {
      role: 'Site Supervisor',
      icon: 'engineering' as IconName,
      steps: [
        'Create a work order and assign it to someone on the project roster',
        'Set priority, due date, milestone and finish-to-start dependencies',
        'Verify crew progress and resolve blockers so work resumes',
      ],
    },
    {
      role: 'Crew Member',
      icon: 'construction' as IconName,
      steps: [
        'See only the work assigned to you, on the projects you belong to',
        'Report quantities installed, notes and blockers from the field',
        'Request missing materials against the work order',
      ],
    },
    {
      role: 'Project Manager',
      icon: 'fact_check' as IconName,
      steps: [
        'Approve or reject material requests — once, with an audit entry',
        'Watch the approval reach the ERP, including retries',
        'Track overdue work, blockers and completion by work package',
      ],
    },
  ];

  protected readonly features: Feature[] = [
    {
      icon: 'checklist',
      title: 'Work-order board with real rules',
      body: 'Status transitions, dependencies with cycle detection, closure checks, and closed work that cannot be silently edited.',
    },
    {
      icon: 'verified',
      title: 'Progress that is verified',
      body: 'Field quantities and blockers are logged by the crew and verified by someone other than the reporter.',
    },
    {
      icon: 'inventory_2',
      title: 'Material approvals, audited',
      body: 'Submitted once, decided once by the project manager. Every change records actor, time and before/after values.',
    },
    {
      icon: 'sync',
      title: 'Resilient ERP handoff',
      body: 'Approvals write a transactional outbox event. A background worker delivers it with bounded backoff and an idempotency key.',
    },
    {
      icon: 'dashboard',
      title: 'Dashboards from live records',
      body: 'Overdue work, completion by work package, open blockers and approval backlog are queried from SQL Server on every load.',
    },
    {
      icon: 'rule',
      title: 'Rules-based blocker summary',
      body: 'A deterministic summary of open blockers, every statement linked to its evidence. Clearly labelled; no AI involved.',
    },
    {
      icon: 'shield',
      title: 'Isolation by design',
      body: 'Tenant and project-membership checks on every API read and write. A crew member cannot open another project by editing the URL.',
    },
    {
      icon: 'history',
      title: 'Activity and audit trail',
      body: 'A readable activity feed backed by an append-only audit table, filterable by project and event type.',
    },
  ];

  protected readonly demoSteps = [
    { title: 'Click Try Demo', body: 'A private workspace is created for you with three fictional Edmonton-area projects. No sign-up, no passwords.' },
    { title: 'Switch roles', body: 'Use the persona switcher to act as the project manager, the site supervisor or a drywall crew lead.' },
    { title: 'Run the scenario', body: 'Riverbend Library is behind on drywall. Report progress, verify it, approve the missing board.' },
    { title: 'Watch it propagate', body: 'The ERP simulator rejects the first attempt; the retry succeeds. Dashboards update from the database.' },
  ];

  protected readonly stack = [
    'Angular 22',
    'TypeScript (strict)',
    'Angular Material',
    'ASP.NET Core 10',
    'C#',
    'EF Core 10',
    'SQL Server 2022',
    'T-SQL views',
    'xUnit',
    'Testcontainers',
    'Playwright',
    'Docker',
    'GitHub Actions',
  ];
}
