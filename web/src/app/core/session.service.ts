import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { Persona, Session, SessionUser } from './models';

/** Signal-backed session state. The server is the authority; this only mirrors /api/auth/me. */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  private readonly state = signal<Session | null>(null);
  /** Bumped whenever the signed-in identity changes, so pages can reload their data. */
  readonly identityVersion = signal(0);

  readonly loaded = computed(() => this.state() !== null);
  readonly user = computed<SessionUser | null>(() => this.state()?.user ?? null);
  readonly isAuthenticated = computed(() => this.state()?.authenticated === true);
  readonly isDemo = computed(() => this.user()?.tenant.isDemo === true);
  readonly isAdmin = computed(() => this.user()?.roles.includes('Admin') === true);
  readonly persona = computed(() => this.user()?.persona ?? null);

  async refresh(): Promise<Session> {
    try {
      const session = await firstValueFrom(this.api.me());
      this.set(session);
      return session;
    } catch {
      const anonymous: Session = { authenticated: false, user: null };
      this.state.set(anonymous);
      return anonymous;
    }
  }

  async ensureLoaded(): Promise<Session> {
    return this.state() ?? this.refresh();
  }

  async startDemo(persona: Persona = 'manager'): Promise<void> {
    this.set(await firstValueFrom(this.api.startDemo(persona)));
  }

  async switchPersona(persona: Persona): Promise<void> {
    this.set(await firstValueFrom(this.api.switchPersona(persona)));
  }

  async resetDemo(): Promise<void> {
    this.set(await firstValueFrom(this.api.resetDemo()));
  }

  async login(email: string, password: string): Promise<void> {
    this.set(await firstValueFrom(this.api.login(email, password)));
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.api.logout());
    } finally {
      this.set({ authenticated: false, user: null });
      await this.router.navigateByUrl('/');
    }
  }

  /** Called when the API answers 401 (session expired or demo workspace cleaned up). */
  markSignedOut(): void {
    if (this.state()?.authenticated) this.set({ authenticated: false, user: null });
  }

  private set(session: Session): void {
    const before = this.state()?.user?.id;
    this.state.set(session);
    if (before !== session.user?.id) this.identityVersion.update((v) => v + 1);
  }
}
