import { DestroyRef, inject, signal } from '@angular/core';
import { Observable, Subscription } from 'rxjs';
import { ApiError } from './api.service';

/**
 * Minimal async state holder for API-backed views: loading / error / data signals.
 * Keeps the last data while reloading so screens don't flash.
 */
export class Loader<T> {
  readonly data = signal<T | null>(null);
  readonly error = signal<ApiError | null>(null);
  readonly loading = signal(false);
  private sub?: Subscription;

  constructor(destroyRef?: DestroyRef) {
    (destroyRef ?? inject(DestroyRef)).onDestroy(() => this.sub?.unsubscribe());
  }

  load(source: Observable<T>): void {
    this.sub?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.sub = source.subscribe({
      next: (value) => {
        this.data.set(value);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(ApiError.from(err));
        this.loading.set(false);
      },
    });
  }
}
