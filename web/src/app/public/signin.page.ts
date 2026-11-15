import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiError } from '../core/api.service';
import { SessionService } from '../core/session.service';
import { IconComponent } from '../shared/ui';
import { LogoComponent } from './public-chrome';

@Component({
  selector: 'sr-signin',
  imports: [FormsModule, RouterLink, MatFormFieldModule, MatInputModule, MatButtonModule, IconComponent, LogoComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main id="main" class="wrap">
      <a routerLink="/" class="home"><sr-logo /></a>
      <div class="grid">
        <section class="card demo">
          @if (expired) {
            <p class="notice" role="status"><sr-icon name="info" [size]="18" /> Your session ended or the demo workspace expired.</p>
          }
          <h1>Explore SiteRoof</h1>
          <p>Start a private demo workspace with fictional projects. No account or password needed.</p>
          <a routerLink="/demo" class="sr-btn primary">Start the demo <sr-icon name="arrow_forward" [size]="18" /></a>
        </section>

        <section class="card">
          <h2>Workspace sign-in</h2>
          <p class="sr-muted">For named accounts created by a workspace admin. Demo personas don't use passwords.</p>
          <form (ngSubmit)="submit()" #f="ngForm" novalidate>
            <mat-form-field appearance="outline">
              <mat-label>Email</mat-label>
              <input matInput type="email" name="email" [(ngModel)]="email" required email autocomplete="username" />
              <mat-error>Enter a valid email address.</mat-error>
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>Password</mat-label>
              <input matInput type="password" name="password" [(ngModel)]="password" required autocomplete="current-password" />
              <mat-error>Enter your password.</mat-error>
            </mat-form-field>
            @if (error()) {
              <p class="err" role="alert">{{ error()!.detail }}</p>
            }
            <button mat-flat-button type="submit" [disabled]="busy() || f.invalid">Sign in</button>
          </form>
        </section>
      </div>
    </main>
  `,
  styles: `
    :host { display: block; min-height: 100vh; }
    .wrap { max-width: 920px; margin: 0 auto; padding: 32px 24px; }
    .home { text-decoration: none; }
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-top: 72px; }
    .card { background: var(--sr-surface); border: 1px solid var(--sr-line); border-radius: var(--sr-radius-lg); padding: 24px; }
    h1 { font-size: var(--sr-text-xl); letter-spacing: -0.025em; }
    h2 { font-size: var(--sr-text-lg); }
    .card > p { color: var(--sr-ink-3); }
    .notice { display: flex; gap: 8px; align-items: center; background: var(--sr-info-soft); color: var(--sr-info); padding: 8px 12px; border-radius: var(--sr-radius); font-size: var(--sr-text-sm); }
    form { display: grid; gap: 4px; margin-top: 12px; }
    .err { color: var(--sr-critical); font-size: var(--sr-text-sm); }
    @media (max-width: 720px) { .grid { grid-template-columns: 1fr; margin-top: 40px; } }
  `,
})
export class SignInPage {
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly expired = this.route.snapshot.queryParamMap.has('expired');
  protected email = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<ApiError | null>(null);

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.session.ensureLoaded();
      await this.session.login(this.email, this.password);
      const next = this.route.snapshot.queryParamMap.get('next');
      await this.router.navigateByUrl(next?.startsWith('/app') ? next : '/app/home');
    } catch (err) {
      this.error.set(ApiError.from(err));
    } finally {
      this.busy.set(false);
    }
  }
}
