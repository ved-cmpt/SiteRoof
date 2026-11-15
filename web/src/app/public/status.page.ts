import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../shared/ui';

const STYLES = `
  .status { max-width: 640px; margin: 64px auto; padding: 32px; background: var(--sr-paper); border-radius: 12px; border-top: 6px solid var(--sr-safety); box-shadow: var(--sr-shadow); }
  .code { font: 850 3rem/1 var(--sr-font-display); color: var(--sr-line-strong); }
  .row { display: flex; gap: 16px; align-items: center; flex-wrap: wrap; margin-top: 16px; }
  :host { display: block; padding: 0 16px; }
`;

@Component({
  selector: 'sr-not-found',
  imports: [RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="status" data-testid="not-found-page">
      <span class="code" aria-hidden="true">404</span>
      <h1>We couldn't find that page</h1>
      <p>The link may be out of date, or the record may belong to another workspace.</p>
      <div class="row">
        <a routerLink="/app/overview" class="sr-btn-safety"><sr-icon name="dashboard" [size]="18" /> Go to the overview</a>
        <a routerLink="/">Home page</a>
      </div>
    </section>
  `,
  styles: STYLES,
})
export class NotFoundPage {}

@Component({
  selector: 'sr-forbidden',
  imports: [RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="status" data-testid="forbidden-page">
      <span class="code" aria-hidden="true">403</span>
      <h1>You don't have access to this</h1>
      <p>Your role or project membership doesn't include this page. Access is checked by the server on every request.</p>
      <div class="row">
        <a routerLink="/app/overview" class="sr-btn-safety"><sr-icon name="dashboard" [size]="18" /> Back to the overview</a>
      </div>
    </section>
  `,
  styles: STYLES,
})
export class ForbiddenPage {}
