import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule, X } from 'lucide-angular';
import { RiderDto } from '../../../api/generated/model/rider-dto';

@Component({
  selector: 'app-rider-edit-modal',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  templateUrl: './rider-edit-modal.component.html',
  styleUrl: './rider-edit-modal.component.scss'
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
