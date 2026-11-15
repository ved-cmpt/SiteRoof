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
          <h1>Preparing your private workspace…</h1>
          <p>
            Seeding three fictional projects, nine people, thirty work orders and a pending ERP retry. This usually takes a
            couple of seconds.
          </p>
        </section>
      } @else {
        <section class="card error" role="alert">
          <sr-icon name="error" [size]="32" />
          <h1>We couldn't start the demo</h1>
          <p>{{ error()!.detail }}</p>
          <div class="row">
            <button type="button" class="sr-btn-safety" (click)="start()">Try again</button>
            <a routerLink="/">Back to the home page</a>
          </div>
        </section>
      }
    </main>
  `,
  styles: `
    :host { display: block; min-height: 100vh; background: var(--sr-steel); color: #fff; }
    .wrap { max-width: 640px; margin: 0 auto; padding: 48px 20px; }
    .home { color: #fff; text-decoration: none; }
    .card { margin-top: 64px; background: var(--sr-paper); color: var(--sr-ink); border-radius: 12px; padding: 32px; border-top: 6px solid var(--sr-safety); }
    .card p { color: var(--sr-ink-2); }
    .error sr-icon { color: var(--sr-critical); }
    .row { display: flex; gap: 16px; align-items: center; flex-wrap: wrap; margin-top: 16px; }
    .spinner { display: block; width: 32px; height: 32px; border-radius: 50%; border: 4px solid var(--sr-line); border-top-color: var(--sr-safety); animation: spin 0.8s linear infinite; margin-bottom: 16px; }
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
      await this.router.navigateByUrl('/app/overview?guide=1', { replaceUrl: true });
    } catch (err) {
      this.error.set(ApiError.from(err));
    }
  }
}
