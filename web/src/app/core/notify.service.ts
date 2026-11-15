import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ApiError } from './api.service';

@Injectable({ providedIn: 'root' })
export class NotifyService {
  private readonly snack = inject(MatSnackBar);

  success(message: string): void {
    this.snack.open(message, 'Dismiss', { duration: 4000, politeness: 'polite' });
  }

  error(err: unknown, fallback = 'Something went wrong.'): void {
    const e = ApiError.from(err);
    const message = e.firstFieldError ?? (e.detail || fallback);
    this.snack.open(message, 'Dismiss', { duration: 8000, politeness: 'assertive', panelClass: 'sr-snack-error' });
  }
}
