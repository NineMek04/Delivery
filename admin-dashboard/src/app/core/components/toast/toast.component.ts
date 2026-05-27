import { Component, inject } from '@angular/core';
import { ToastService } from '../../services/toast.service';
import { CommonModule } from '@angular/common';
import { LucideAngularModule, Info, CheckCircle, AlertTriangle, XCircle, X } from 'lucide-angular';

@Component({
  selector: 'app-toast',
  standalone: true,
  imports: [CommonModule, LucideAngularModule],
  template: `
    <div class="toast-container">
      @for (toast of toastService.toasts(); track toast.id) {
        <div class="toast" [ngClass]="toast.type">
          <div class="toast-icon">
            @if (toast.type === 'success') {
              <lucide-icon [img]="CheckCircleIcon" size="20"></lucide-icon>
            } @else if (toast.type === 'error') {
              <lucide-icon [img]="XCircleIcon" size="20"></lucide-icon>
            } @else if (toast.type === 'warning') {
              <lucide-icon [img]="AlertTriangleIcon" size="20"></lucide-icon>
            } @else {
              <lucide-icon [img]="InfoIcon" size="20"></lucide-icon>
            }
          </div>
          <div class="toast-content">
            <div class="toast-title">{{ toast.title }}</div>
            <div class="toast-message">{{ toast.message }}</div>
          </div>
          <button class="toast-close" (click)="toastService.remove(toast.id)">
            <lucide-icon [img]="XIcon" size="16"></lucide-icon>
          </button>
        </div>
      }
    </div>
  `,
  styles: [`
    .toast-container {
      position: fixed;
      bottom: 24px;
      right: 24px;
      z-index: 9999;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .toast {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      background: var(--surface);
      border: 1px solid var(--border-color);
      border-radius: 8px;
      padding: 16px;
      min-width: 300px;
      max-width: 400px;
      box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1), 0 4px 6px -2px rgba(0, 0, 0, 0.05);
      animation: slideIn 0.3s ease-out forwards;
      color: var(--text-primary);
    }

    .toast.success .toast-icon { color: var(--success); }
    .toast.error .toast-icon { color: var(--danger); }
    .toast.warning .toast-icon { color: var(--warning); }
    .toast.info .toast-icon { color: var(--info); }

    .toast-content {
      flex: 1;
    }

    .toast-title {
      font-weight: 600;
      font-size: 0.95rem;
      margin-bottom: 4px;
    }

    .toast-message {
      font-size: 0.85rem;
      color: var(--text-secondary);
      line-height: 1.4;
    }

    .toast-close {
      background: none;
      border: none;
      color: var(--text-muted);
      cursor: pointer;
      padding: 2px;
      border-radius: 4px;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: color 0.2s, background-color 0.2s;
    }

    .toast-close:hover {
      color: var(--text-primary);
      background-color: var(--bg-hover);
    }

    @keyframes slideIn {
      from {
        opacity: 0;
        transform: translateX(100%);
      }
      to {
        opacity: 1;
        transform: translateX(0);
      }
    }
  `]
})
export class ToastComponent {
  toastService = inject(ToastService);
  
  InfoIcon = Info;
  CheckCircleIcon = CheckCircle;
  AlertTriangleIcon = AlertTriangle;
  XCircleIcon = XCircle;
  XIcon = X;
}
