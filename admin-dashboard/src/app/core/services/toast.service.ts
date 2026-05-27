import { Injectable, signal } from '@angular/core';

export interface Toast {
  id: string;
  title: string;
  message: string;
  type: 'success' | 'error' | 'warning' | 'info';
  timestamp: number;
}

@Injectable({
  providedIn: 'root'
})
export class ToastService {
  // Active floating toasts
  private _toasts = signal<Toast[]>([]);
  public readonly toasts = this._toasts.asReadonly();

  // Notification history inbox (last 20)
  private _history = signal<Toast[]>([]);
  public readonly history = this._history.asReadonly();

  private maxHistory = 100;
  private autoDismissTime = 3000;

  show(title: string, message: string, type: 'success' | 'error' | 'warning' | 'info' = 'info') {
    const newToast: Toast = {
      id: crypto.randomUUID(),
      title,
      message,
      type,
      timestamp: Date.now()
    };

    // Add to active toasts
    this._toasts.update(current => [...current, newToast]);

    // Add to history (keeping max 20)
    this._history.update(current => {
      const updated = [newToast, ...current];
      return updated.slice(0, this.maxHistory);
    });

    // Auto dismiss
    setTimeout(() => {
      this.remove(newToast.id);
    }, this.autoDismissTime);
  }

  remove(id: string) {
    this._toasts.update(current => current.filter(t => t.id !== id));
  }

  clearHistory() {
    this._history.set([]);
  }
}
