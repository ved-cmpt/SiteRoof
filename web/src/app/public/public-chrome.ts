import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../shared/ui';

export const GITHUB_URL = 'https://github.com/ved-cmpt/SiteRoof';

/**
 * SiteRoof mark: the plan view of a hip roof (four planes split by the ridge and hip lines) with the
 * east hip end in the accent colour. Built on a 24-unit grid so it stays crisp at 16px; `mono` renders
 * it in the current text colour only.
 */
@Component({
  selector: 'sr-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg viewBox="0 0 24 24" [attr.width]="size()" [attr.height]="size()" aria-hidden="true">
      <path fill="currentColor" d="M1.5 4h21l-5.6 7.3H7.1z" />
      <path fill="currentColor" d="M1.5 20h21l-5.6-7.3H7.1z" />
      <path fill="currentColor" d="M1.5 5.6 5.9 12l-4.4 6.4z" />
      <path [attr.fill]="mono() ? 'currentColor' : '#d9480f'" d="M22.5 5.6 18.1 12l4.4 6.4z" />
    </svg>
    @if (wordmark()) {
      <span>SiteRoof</span>
    }
  `,
  styles: `
    :host { display: inline-flex; align-items: center; gap: 8px; font: 600 1rem/1 var(--sr-font-sans); letter-spacing: -0.02em; color: var(--sr-ink); }
  `,
})
export class LogoComponent {
  readonly size = input(20);
  readonly mono = input(false);
  readonly wordmark = input(true);
}

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

/** Minimal footer: mark, one line, three links. */
@Component({
  selector: 'sr-public-footer',
  imports: [RouterLink, LogoComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <footer>
      <div class="inner">
        <sr-logo [size]="18" />
        <p>A personal project · all data is fictional</p>
        <nav aria-label="Footer">
          <a routerLink="/demo">Try the demo</a>
          <a routerLink="/case-study">Case study</a>
          <a [href]="github" rel="noopener" target="_blank">GitHub</a>
        </nav>
      </div>
    </footer>
  `,
  styles: `
    footer { border-top: 1px solid var(--sr-line); background: var(--sr-bg); }
    .inner { max-width: 1200px; margin: 0 auto; padding: 28px 24px; display: flex; align-items: center; gap: 12px 24px; flex-wrap: wrap; }
    p { margin: 0; font-size: var(--sr-text-sm); color: var(--sr-ink-3); }
    nav { margin-left: auto; display: flex; gap: 20px; font-size: var(--sr-text-sm); }
    nav a { color: var(--sr-ink-2); }
    @media (max-width: 640px) { nav { margin-left: 0; width: 100%; } }
  `,
})
export class PublicFooterComponent {
  protected readonly github = GITHUB_URL;
}
