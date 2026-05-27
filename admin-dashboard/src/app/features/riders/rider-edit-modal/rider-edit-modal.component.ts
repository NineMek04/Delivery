import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule, X } from 'lucide-angular';
import { RiderDto } from '../../../api/generated/model/rider-dto';

@Component({
  selector: 'app-rider-edit-modal',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  template: `
    <div class="modal-overlay" *ngIf="isOpen" (click)="close()">
      <div class="modal-content" (click)="$event.stopPropagation()">
        <div class="modal-header">
          <h3>Edit Rider</h3>
          <button class="close-btn" (click)="close()">
            <lucide-icon [img]="XIcon" size="20"></lucide-icon>
          </button>
        </div>
        
        <div class="modal-body" *ngIf="editModel">
          <div class="form-group">
            <label>Name</label>
            <input type="text" [(ngModel)]="editModel.name" class="form-control" />
          </div>
          <div class="form-group">
            <label>Phone</label>
            <input type="text" [(ngModel)]="editModel.phone" class="form-control" />
          </div>
          <div class="form-group">
            <label>Status</label>
            <select [(ngModel)]="editModel.status" class="form-control">
              <option value="IDLE">IDLE</option>
              <option value="UNAVAILABLE">UNAVAILABLE</option>
              <option value="OFFLINE">OFFLINE</option>
              <option value="BUSY">BUSY</option>
            </select>
          </div>
        </div>

        <div class="modal-footer">
          <button class="btn btn-secondary" (click)="close()">Cancel</button>
          <button class="btn btn-primary" (click)="save()">Save Changes</button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .modal-overlay {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.6);
      backdrop-filter: blur(4px);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
    }
    .modal-content {
      background: var(--surface);
      border: 1px solid var(--border-color);
      border-radius: 8px;
      width: 400px;
      max-width: 90vw;
      box-shadow: 0 10px 25px rgba(0,0,0,0.5);
    }
    .modal-header {
      padding: 16px 20px;
      border-bottom: 1px solid var(--border-color);
      display: flex;
      justify-content: space-between;
      align-items: center;
      h3 { margin: 0; font-size: 1.1rem; }
      .close-btn { background: none; border: none; color: var(--text-muted); cursor: pointer; }
    }
    .modal-body {
      padding: 20px;
      display: flex;
      flex-direction: column;
      gap: 16px;
    }
    .form-group {
      display: flex;
      flex-direction: column;
      gap: 8px;
      label { font-size: 0.85rem; color: var(--text-secondary); }
      .form-control {
        background: var(--bg-hover);
        border: 1px solid var(--border-color);
        color: var(--text-primary);
        padding: 8px 12px;
        border-radius: 4px;
        &:focus { border-color: var(--primary); outline: none; }
      }
    }
    .modal-footer {
      padding: 16px 20px;
      border-top: 1px solid var(--border-color);
      display: flex;
      justify-content: flex-end;
      gap: 12px;
    }
    .btn { padding: 8px 16px; border-radius: 4px; border: none; cursor: pointer; font-weight: 500; }
    .btn-secondary { background: var(--bg-hover); color: var(--text-primary); }
    .btn-primary { background: var(--primary); color: #000; }
  `]
})
export class RiderEditModalComponent {
  XIcon = X;

  @Input() isOpen = false;
  @Input() rider: RiderDto | null = null;
  
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<RiderDto>();

  editModel: Partial<RiderDto> | null = null;

  ngOnChanges() {
    if (this.rider && this.isOpen) {
      this.editModel = { ...this.rider };
    }
  }

  close() {
    this.isOpen = false;
    this.closed.emit();
  }

  save() {
    if (this.editModel) {
      this.saved.emit(this.editModel as RiderDto);
    }
  }
}
