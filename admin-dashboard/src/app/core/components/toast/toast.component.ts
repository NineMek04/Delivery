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
      background: var(--color-surface-card);
      border: 1px solid var(--color-surface-line);
      border-left: 4px solid var(--color-surface-line);
      border-radius: 8px;
      padding: 16px;
      min-width: 300px;
      max-width: 400px;
      box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.5), 0 8px 10px -6px rgba(0, 0, 0, 0.5);
      animation: slideIn 0.3s cubic-bezier(0.16, 1, 0.3, 1) forwards;
      color: var(--color-text-primary);
      backdrop-filter: blur(10px);
    }

    .toast.success {
      background: rgba(0, 255, 102, 0.08);
      border-color: rgba(0, 255, 102, 0.2);
      border-left-color: var(--color-status-success);
    }
    .toast.success .toast-icon { color: var(--color-status-success); }

    .toast.error {
      background: rgba(255, 51, 51, 0.08);
      border-color: rgba(255, 51, 51, 0.2);
      border-left-color: var(--color-status-error);
    }
    .toast.error .toast-icon { color: var(--color-status-error); }

    .toast.warning {
      background: rgba(245, 158, 11, 0.08);
      border-color: rgba(245, 158, 11, 0.2);
      border-left-color: var(--color-status-warning);
    }
    .toast.warning .toast-icon { color: var(--color-status-warning); }

    .toast.info {
      background: rgba(0, 153, 255, 0.08);
      border-color: rgba(0, 153, 255, 0.2);
      border-left-color: #0099ff;
    }
    .toast.info .toast-icon { color: #0099ff; }

    .toast-content {
      flex: 1;
    }

    .toast-title {
      font-weight: 700;
      font-size: 0.95rem;
      margin-bottom: 4px;
      letter-spacing: 0.02em;
    }

    .toast-message {
      font-size: 0.85rem;
      color: var(--color-text-muted);
      line-height: 1.5;
    }

    .toast-close {
      background: none;
      border: none;
      color: var(--color-text-muted);
      cursor: pointer;
      padding: 4px;
      border-radius: 4px;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s ease;
    }

    .toast-close:hover {
      color: var(--color-text-primary);
      background-color: rgba(255, 255, 255, 0.1);
    }

    @keyframes slideIn {
      from {
        opacity: 0;
        transform: translateX(120%) scale(0.9);
      }
      to {
        opacity: 1;
        transform: translateX(0) scale(1);
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
