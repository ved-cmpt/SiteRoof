import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

const STYLES = `
  .status { max-width: 600px; margin: 72px auto; padding: 28px; background: var(--sr-surface); border: 1px solid var(--sr-line); border-radius: var(--sr-radius-lg); }
  .code { display: inline-block; font-family: var(--sr-font-mono); font-size: 0.75rem; letter-spacing: 0.12em; color: var(--sr-ink-3); border: 1px solid var(--sr-line-strong); padding: 2px 8px; border-radius: var(--sr-radius-sm); }
  h1 { font-size: var(--sr-text-xl); letter-spacing: -0.025em; margin: 14px 0 8px; }
  p { color: var(--sr-ink-3); }
  .row { display: flex; gap: 16px; align-items: center; flex-wrap: wrap; margin-top: 16px; }
  :host { display: block; padding: 0 16px; }
`;

@Component({
  selector: 'sr-not-found',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="status" data-testid="not-found-page">
      <span class="code" aria-hidden="true">ERROR 404</span>
      <h1>We couldn't find that page</h1>
      <p>The link may be out of date, or the record may belong to another workspace.</p>
      <div class="row">
        <a routerLink="/app/overview" class="sr-btn primary">Go to the overview</a>
        <a routerLink="/">Home page</a>
      </div>
    </section>
  `,
  styles: STYLES,
})
export class NotFoundPage {}

@Component({
  selector: 'sr-forbidden',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="status" data-testid="forbidden-page">
      <span class="code" aria-hidden="true">ERROR 403</span>
      <h1>You don't have access to this</h1>
      <p>Your role or project membership doesn't include this page. Access is checked by the server on every request.</p>
      <div class="row">
        <a routerLink="/app/overview" class="sr-btn primary">Back to the overview</a>
      </div>
    </section>
  `,
  styles: STYLES,
})
export class ForbiddenPage {}
