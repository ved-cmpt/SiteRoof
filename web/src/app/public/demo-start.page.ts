import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiError } from '../core/api.service';
import { Persona } from '../core/models';
import { SessionService } from '../core/session.service';
import { IconComponent } from '../shared/ui';
import { LogoComponent } from './public-chrome';

/** Provisions (or resumes) the visitor's private demo workspace, then enters the app. */
@Component({
  selector: 'sr-demo-start',
  imports: [RouterLink, IconComponent, LogoComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main id="main" class="wrap">
      <a routerLink="/" class="home"><sr-logo /></a>
      @if (!error()) {
        <section class="card" role="status" aria-live="polite">
          <span class="spinner" aria-hidden="true"></span>
          <p class="sr-kicker">Provisioning</p>
          <h1>Preparing your private workspace…</h1>
          <p>
            Seeding three fictional projects, nine people, thirty work orders and a pending ERP retry. This usually takes a
            couple of seconds.
          </p>
        </section>
      } @else {
        <section class="card error" role="alert">
          <sr-icon name="error" [size]="22" />
          <h1>We couldn't start the demo</h1>
          <p>{{ error()!.detail }}</p>
          <div class="row">
            <button type="button" class="sr-btn primary" (click)="start()">Try again</button>
            <a routerLink="/">Back to the home page</a>
          </div>
        </section>
      }
    </main>
  `,
  styles: `
    :host { display: block; min-height: 100vh; }
    .wrap { max-width: 560px; margin: 0 auto; padding: 32px 24px; }
    .home { text-decoration: none; }
    .card { margin-top: 96px; background: var(--sr-surface); border: 1px solid var(--sr-line); border-radius: var(--sr-radius-lg); padding: 28px; }
    h1 { font-size: var(--sr-text-xl); letter-spacing: -0.025em; margin: 14px 0 8px; }
    .card p { color: var(--sr-ink-3); margin: 0; }
    .error { border-left: 2px solid var(--sr-critical); }
    .error sr-icon { color: var(--sr-critical); }
    .row { display: flex; gap: 16px; align-items: center; flex-wrap: wrap; margin-top: 20px; }
    .spinner { display: block; width: 18px; height: 18px; border-radius: 50%; border: 2px solid var(--sr-line); border-top-color: var(--sr-accent); animation: spin 0.7s linear infinite; }
    @keyframes spin { to { transform: rotate(360deg); } }
  `,
})
export class DemoStartPage implements OnInit {
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly error = signal<ApiError | null>(null);

  ngOnInit(): void {
    void this.start();
  }

  protected async start(): Promise<void> {
    this.error.set(null);
    try {
      await this.session.ensureLoaded();
      const requested = this.route.snapshot.queryParamMap.get('persona');
      const persona: Persona = requested === 'supervisor' || requested === 'crew' ? requested : 'manager';
      await this.session.startDemo(persona);
      await this.router.navigateByUrl('/app/home?guide=1', { replaceUrl: true });
    } catch (err) {
      this.error.set(ApiError.from(err));
    }
  }
}
