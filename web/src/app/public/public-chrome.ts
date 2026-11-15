import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../shared/ui';

export const GITHUB_URL = 'https://github.com/ved-cmpt/SiteRoof';

@Component({
  selector: 'sr-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg viewBox="0 0 64 64" width="30" height="30" aria-hidden="true">
      <rect width="64" height="64" rx="12" fill="#1f2732" />
      <path d="M10 34 32 14l22 20" fill="none" stroke="#f5b800" stroke-width="7" stroke-linecap="round" stroke-linejoin="round" />
      <path d="M18 36v14h28V36" fill="none" stroke="#fff" stroke-width="5" stroke-linejoin="round" />
      <path d="M26 50V40h12v10" fill="#f5b800" />
    </svg>
    <span>Site<b>Roof</b></span>
  `,
  styles: `
    :host { display: inline-flex; align-items: center; gap: 9px; font: 800 1.2rem/1 var(--sr-font-display); letter-spacing: -0.02em; }
    b { color: var(--sr-safety); font-weight: 800; }
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
        <a routerLink="/" fragment="product" (click)="open.set(false)">Product</a>
        <a routerLink="/" fragment="workflows" (click)="open.set(false)">Workflows</a>
        <a routerLink="/" fragment="demo" (click)="open.set(false)">How the demo works</a>
        <a routerLink="/" fragment="engineering" (click)="open.set(false)">Engineering</a>
        <a routerLink="/case-study" (click)="open.set(false)">Case study</a>
        <a routerLink="/demo" class="sr-btn-safety cta" data-testid="nav-try-demo" (click)="open.set(false)">Try Demo</a>
      </nav>
      <button type="button" class="menu" [attr.aria-expanded]="open()" aria-controls="primary-nav" (click)="open.set(!open())">
        <sr-icon [name]="open() ? 'close' : 'menu'" [size]="24" />
        <span class="sr-only">Menu</span>
      </button>
    </header>
  `,
  styles: `
    :host { position: sticky; top: 0; z-index: 50; display: block; background: rgb(19 23 29 / 94%); backdrop-filter: blur(8px); color: #fff; border-bottom: 1px solid rgb(255 255 255 / 8%); }
    .bar { max-width: 1200px; margin: 0 auto; padding: 12px 20px; display: flex; align-items: center; gap: 20px; }
    .brand { color: #fff; text-decoration: none; }
    nav { margin-left: auto; display: flex; align-items: center; gap: 22px; }
    nav a:not(.cta) { color: #d6dbe2; text-decoration: none; font-weight: 550; font-size: 0.93rem; }
    nav a:not(.cta):hover { color: #fff; }
    .cta { padding: 9px 16px; font-size: 0.95rem; }
    .menu { display: none; margin-left: auto; background: none; border: 0; color: #fff; padding: 6px; cursor: pointer; }
    @media (max-width: 860px) {
      .menu { display: inline-flex; }
      nav { display: none; position: absolute; top: 100%; left: 0; right: 0; flex-direction: column; align-items: stretch; gap: 4px; padding: 12px 20px 18px; background: var(--sr-ink); border-bottom: 1px solid rgb(255 255 255 / 10%); }
      nav.open { display: flex; }
      nav a:not(.cta) { padding: 10px 0; font-size: 1rem; }
      .cta { justify-content: center; margin-top: 6px; }
    }
  `,
})
export class PublicHeaderComponent {
  protected readonly open = signal(false);
}

@Component({
  selector: 'sr-public-footer',
  imports: [RouterLink, LogoComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <footer>
      <div class="hz sr-hazard" aria-hidden="true"></div>
      <div class="inner">
        <div class="col brand-col">
          <sr-logo />
          <p>Keep site work, decisions, and progress in one place.</p>
          <p class="disclaimer">
            <strong>Independent portfolio project.</strong> SiteRoof is not affiliated with, endorsed by, or built for any
            construction company. All projects, people, companies, addresses and records shown are fictional.
          </p>
        </div>
        <div class="col">
          <h2>Explore</h2>
          <ul>
            <li><a routerLink="/demo">Try the live demo</a></li>
            <li><a routerLink="/case-study">Case study</a></li>
            <li><a [href]="github" rel="noopener" target="_blank">Source on GitHub</a></li>
            <li><a href="/openapi/v1.json">OpenAPI document</a></li>
          </ul>
        </div>
        <div class="col">
          <h2>Built with</h2>
          <ul>
            <li>Angular · TypeScript · Angular Material</li>
            <li>ASP.NET Core · C# · EF Core</li>
            <li>SQL Server 2022 · T-SQL views</li>
            <li>xUnit · Testcontainers · Playwright</li>
          </ul>
        </div>
        <div class="col">
          <h2>Attribution</h2>
          <ul>
            <li>Icons: Material Symbols (Apache 2.0)</li>
            <li>Type: Inter and Archivo (SIL OFL 1.1)</li>
            <li>Code: MIT License</li>
          </ul>
        </div>
      </div>
    </footer>
  `,
  styles: `
    footer { background: var(--sr-ink); color: #c9d0d8; }
    .hz { height: 8px; }
    .inner { max-width: 1200px; margin: 0 auto; padding: 40px 20px 48px; display: grid; grid-template-columns: 2fr 1fr 1fr 1fr; gap: 28px; }
    sr-logo { color: #fff; }
    h2 { color: #fff; font-size: 0.8rem; letter-spacing: 0.12em; text-transform: uppercase; margin-bottom: 12px; }
    ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; font-size: 0.9rem; }
    a { color: #e7ebf0; }
    .brand-col p { margin-top: 12px; max-width: 46ch; }
    .disclaimer { font-size: 0.85rem; color: #aeb7c2; }
    @media (max-width: 860px) { .inner { grid-template-columns: 1fr 1fr; } .brand-col { grid-column: 1 / -1; } }
    @media (max-width: 480px) { .inner { grid-template-columns: 1fr; } }
  `,
})
export class PublicFooterComponent {
  protected readonly github = GITHUB_URL;
}
