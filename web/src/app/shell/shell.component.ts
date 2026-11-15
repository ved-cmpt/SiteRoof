import { BreakpointObserver } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { ApiService } from '../core/api.service';
import { Loader } from '../core/loader';
import { Persona, ProjectSummary, Scenario } from '../core/models';
import { NotifyService } from '../core/notify.service';
import { SessionService } from '../core/session.service';
import { ConfirmDialogComponent } from '../features/dialogs';
import { LogoComponent } from '../public/public-chrome';
import { IconName } from '../shared/icons.generated';
import { AgoPipe, IconComponent } from '../shared/ui';

interface NavItem {
  path: string;
  label: string;
  icon: IconName;
  admin?: boolean;
}

@Component({
  selector: 'sr-shell',
  host: { class: 'sr-app' },
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatSidenavModule,
    MatMenuModule,
    MatButtonModule,
    MatTooltipModule,
    IconComponent,
    LogoComponent,
    AgoPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
})
export class ShellComponent implements OnInit {
  protected readonly session = inject(SessionService);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly isHandset = toSignal(
    inject(BreakpointObserver)
      .observe('(max-width: 960px)')
      .pipe(map((r) => r.matches)),
    { initialValue: false },
  );
  protected readonly navOpen = signal(false);
  protected readonly guideOpen = signal(false);
  protected readonly switching = signal(false);
  protected readonly scenario = new Loader<Scenario>();
  /** Projects listed in the sidebar (only those the current persona can access). */
  protected readonly projects = new Loader<ProjectSummary[]>();
  protected readonly currentProjectId = signal('');

  protected readonly nav: NavItem[] = [
    { path: '/app/home', label: 'Home', icon: 'dashboard' },
    { path: '/app/projects', label: 'Projects', icon: 'apartment' },
    { path: '/app/approvals', label: 'Approvals', icon: 'fact_check' },
    { path: '/app/activity', label: 'Activity', icon: 'history' },
    { path: '/app/admin', label: 'Admin', icon: 'admin_panel_settings', admin: true },
  ];
  protected readonly visibleNav = computed(() => this.nav.filter((n) => !n.admin || this.session.isAdmin()));
  protected readonly initials = computed(() =>
    (this.session.user()?.displayName ?? '?')
      .split(' ')
      .map((p) => p[0])
      .join('')
      .slice(0, 2),
  );
  protected readonly stepsDone = computed(() => this.scenario.data()?.steps.filter((s) => s.done === true).length ?? 0);

  ngOnInit(): void {
    if (this.session.isDemo()) this.scenario.load(this.api.scenario());
    this.projects.load(this.api.projects());
    this.trackProject(this.router.url);
    // The guide opens on first entry beside the content; on phones it would cover the app, so it waits to be opened.
    const wantsGuide = this.route.snapshot.queryParamMap.has('guide') || this.router.url.includes('guide=1');
    if (wantsGuide && !this.isHandset()) this.guideOpen.set(true);
    let identity = this.session.identityVersion();
    this.router.events
      .pipe(
        filter((e): e is NavigationEnd => e instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((e) => {
        this.navOpen.set(false);
        this.trackProject(e.urlAfterRedirects);
        if (this.session.isDemo()) this.scenario.load(this.api.scenario());
        if (identity !== this.session.identityVersion()) {
          identity = this.session.identityVersion();
          this.projects.load(this.api.projects());
        }
      });
  }

  private trackProject(url: string): void {
    const match = /\/app\/projects\/([0-9a-f-]{36})/i.exec(url);
    this.currentProjectId.set(match?.[1] ?? '');
  }

  /** First name of a persona for the guide's buttons ("Switch to Marcus and go"). */
  protected personaName(key: Persona): string {
    const name = this.scenario.data()?.personas.find((p) => p.key === key)?.displayName;
    return name?.split(' ')[0] ?? key;
  }

  protected async switchTo(persona: Persona, target?: string): Promise<void> {
    if (this.switching()) return;
    const sameUser = this.session.persona() === persona;
    this.switching.set(true);
    try {
      if (!sameUser) await this.session.switchPersona(persona);
      const name = this.session.user()?.displayName;
      const url = target ?? this.router.url.split('?')[0];
      // Re-create the routed component so every view reloads with the new identity's permissions.
      await this.router.navigateByUrl('/app/home', { skipLocationChange: true });
      await this.router.navigateByUrl(url);
      if (!sameUser) this.notify.success(`You are now ${name}.`);
      this.scenario.load(this.api.scenario());
    } catch (err) {
      this.notify.error(err, 'Could not switch persona.');
    } finally {
      this.switching.set(false);
    }
  }

  protected resetDemo(): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          title: 'Reset your demo workspace?',
          body: 'Everything you changed is discarded and a freshly seeded workspace replaces it. Other visitors are not affected.',
          confirm: 'Reset demo',
        },
      })
      .afterClosed()
      .subscribe(async (ok) => {
        if (!ok) return;
        try {
          await this.session.resetDemo();
          await this.router.navigateByUrl('/app', { skipLocationChange: true });
          await this.router.navigateByUrl('/app/home');
          this.notify.success('Your demo workspace was reset.');
          this.scenario.load(this.api.scenario());
        } catch (err) {
          this.notify.error(err, 'Could not reset the demo.');
        }
      });
  }

  protected logout(): void {
    void this.session.logout();
  }
}
