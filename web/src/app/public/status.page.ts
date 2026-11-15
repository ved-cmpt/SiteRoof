import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

const STYLES = `
  :host { display: block; }
  .status { display: grid; gap: var(--sr-s3); justify-items: start; max-width: 640px; margin: var(--sr-s5) auto; padding: var(--sr-s5); background: var(--sr-surface); border: 1px solid var(--sr-line); border-radius: var(--sr-radius); }
  .code { font-family: var(--sr-font-mono); font-size: 0.75rem; color: var(--sr-ink-3); }
  h1 { font-size: 1.25rem; font-weight: 600; margin: 0; }
  p { margin: 0; color: var(--sr-ink-3); }
  .row { display: flex; gap: var(--sr-s4); align-items: center; flex-wrap: wrap; }
  @media (max-width: 767px) { .status { margin: var(--sr-s4); padding: var(--sr-s4); } .row a { width: 100%; } }
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
        <a routerLink="/app/home" class="sr-btn primary">Go to Home</a>
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
        <a routerLink="/app/home" class="sr-btn primary">Back to Home</a>
      </div>
    </section>
  `,
  styles: STYLES,
})
export class ForbiddenPage {}
