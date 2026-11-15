import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../shared/ui';

export const GITHUB_URL = 'https://github.com/ved-cmpt/SiteRoof';

/** Wordmark with a simple geometric mark (a roof line over a plan square). */
@Component({
  selector: 'sr-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true">
      <rect x="4.5" y="10.5" width="15" height="10" rx="1" fill="none" stroke="currentColor" stroke-width="1.6" />
      <path d="M2.5 11.5 12 3.5l9.5 8" fill="none" stroke="#d9480f" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" />
      <path d="M10 20.5v-5h4v5" fill="none" stroke="currentColor" stroke-width="1.6" />
    </svg>
    <span>SiteRoof</span>
  `,
  styles: `
    :host { display: inline-flex; align-items: center; gap: 8px; font: 600 1rem/1 var(--sr-font-sans); letter-spacing: -0.02em; color: var(--sr-ink); }
  `,
})
export class LogoComponent {}

@Component({
  selector: 'sr-public-header',
  imports: [RouterLink, LogoComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a class="skip-link" href="#main">Skip to content</a>
    <header class="bar">
      <a routerLink="/" class="brand" aria-label="SiteRoof home"><sr-logo /></a>
      <nav id="primary-nav" aria-label="Primary" [class.open]="open()">
        <a routerLink="/" fragment="workflow" (click)="open.set(false)">Workflow</a>
        <a routerLink="/" fragment="capabilities" (click)="open.set(false)">Capabilities</a>
        <a routerLink="/" fragment="integration" (click)="open.set(false)">Integration</a>
        <a routerLink="/case-study" (click)="open.set(false)">Case study</a>
        <a [href]="github" rel="noopener" target="_blank" (click)="open.set(false)">GitHub</a>
        <a routerLink="/demo" class="sr-btn primary cta" data-testid="nav-try-demo" (click)="open.set(false)">Try demo</a>
      </nav>
      <button type="button" class="menu" [attr.aria-expanded]="open()" aria-controls="primary-nav" (click)="open.set(!open())">
        <sr-icon [name]="open() ? 'close' : 'menu'" [size]="22" />
        <span class="sr-only">Menu</span>
      </button>
    </header>
  `,
  styles: `
    :host { position: sticky; top: 0; z-index: 50; display: block; background: rgb(246 244 239 / 92%); backdrop-filter: blur(8px); border-bottom: 1px solid var(--sr-line); }
    .bar { max-width: 1200px; margin: 0 auto; padding: 0 24px; height: 60px; display: flex; align-items: center; gap: 24px; }
    .brand { text-decoration: none; }
    nav { margin-left: auto; display: flex; align-items: center; gap: 24px; }
    nav a:not(.cta) { color: var(--sr-ink-3); text-decoration: none; font-size: var(--sr-text-sm); font-weight: 500; transition: color var(--sr-fast) var(--sr-ease); }
    nav a:not(.cta):hover { color: var(--sr-ink); }
    .cta { height: 34px; padding: 0 14px; font-size: var(--sr-text-sm); }
    .menu { display: none; margin-left: auto; background: none; border: 0; color: var(--sr-ink); padding: 6px; cursor: pointer; }
    @media (max-width: 860px) {
      .menu { display: inline-flex; }
      nav { display: none; position: absolute; top: 100%; left: 0; right: 0; flex-direction: column; align-items: stretch; gap: 0; padding: 8px 24px 20px; background: var(--sr-bg); border-bottom: 1px solid var(--sr-line); }
      nav.open { display: flex; }
      nav a:not(.cta) { padding: 12px 0; font-size: var(--sr-text-base); border-bottom: 1px solid var(--sr-line); }
      .cta { margin-top: 14px; height: 42px; }
    }
  `,
})
export class PublicHeaderComponent {
  protected readonly open = signal(false);
  protected readonly github = GITHUB_URL;
}

/** Footer laid out like a drawing title block. */
@Component({
  selector: 'sr-public-footer',
  imports: [RouterLink, LogoComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <footer>
      <div class="inner">
        <div class="block">
          <div class="cell brand-cell">
            <sr-logo />
            <p>Keep site work, decisions, and progress in one place.</p>
          </div>
          <div class="cell">
            <span class="lbl">Project</span>
            <span class="val">Independent portfolio project</span>
            <p class="small">
              Not affiliated with, endorsed by, or built for any construction company. All projects, people, companies,
              addresses and records are fictional.
            </p>
          </div>
          <div class="cell">
            <span class="lbl">Sheets</span>
            <ul>
              <li><a routerLink="/demo">Live demo</a></li>
              <li><a routerLink="/case-study">Case study</a></li>
              <li><a [href]="github" rel="noopener" target="_blank">Source on GitHub</a></li>
              <li><a href="/openapi/v1.json">OpenAPI document</a></li>
            </ul>
          </div>
          <div class="cell">
            <span class="lbl">Built with</span>
            <p class="small">Angular · TypeScript · ASP.NET Core · C# · EF Core · SQL Server · Playwright</p>
            <span class="lbl">Credits</span>
            <p class="small">Type: Inter and JetBrains Mono (SIL OFL 1.1). Icons: Material Symbols (Apache 2.0). Code: MIT.</p>
          </div>
          <div class="cell dwg">
            <span class="lbl">Drawing no.</span>
            <span class="val sr-mono">SR-{{ sheet() }}</span>
            <span class="lbl">Rev</span>
            <span class="val sr-mono">2026-11</span>
          </div>
        </div>
      </div>
    </footer>
  `,
  styles: `
    footer { border-top: 1px solid var(--sr-line); background: var(--sr-bg); }
    .inner { max-width: 1200px; margin: 0 auto; padding: 40px 24px 48px; }
    .block { display: grid; grid-template-columns: 1.3fr 1.4fr 1fr 1.4fr 0.8fr; border: 1px solid var(--sr-ink); background: var(--sr-surface); }
    .cell { padding: 14px 16px; border-right: 1px solid var(--sr-line-strong); display: flex; flex-direction: column; gap: 4px; min-width: 0; }
    .cell:last-child { border-right: 0; }
    .lbl { font-family: var(--sr-font-mono); font-size: 0.625rem; letter-spacing: 0.12em; text-transform: uppercase; color: var(--sr-ink-3); }
    .lbl:not(:first-child) { margin-top: 8px; }
    .val { font-size: var(--sr-text-sm); font-weight: 500; }
    p { margin: 6px 0 0; font-size: var(--sr-text-sm); color: var(--sr-ink-2); }
    .small { font-size: 0.78rem; color: var(--sr-ink-3); line-height: 1.5; margin: 0; }
    ul { list-style: none; margin: 2px 0 0; padding: 0; display: grid; gap: 4px; font-size: var(--sr-text-sm); }
    @media (max-width: 960px) {
      .block { grid-template-columns: 1fr 1fr; }
      .cell { border-bottom: 1px solid var(--sr-line-strong); }
      .brand-cell { grid-column: 1 / -1; }
    }
    @media (max-width: 520px) { .block { grid-template-columns: 1fr; } .cell { border-right: 0; } }
  `,
})
export class PublicFooterComponent {
  /** Drawing number shown in the title block (e.g. "A-001" on the landing page). */
  readonly sheet = input('A-001');
  protected readonly github = GITHUB_URL;
}
