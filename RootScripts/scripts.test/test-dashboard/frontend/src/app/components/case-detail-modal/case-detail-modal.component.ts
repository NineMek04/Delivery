import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TestCase } from '../../test-dashboard.model';

@Component({
  selector: 'app-case-detail-modal',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './case-detail-modal.component.html',
  styleUrl: './case-detail-modal.component.scss'
})
export class CaseDetailModalComponent {
  @Input() caseItem: TestCase | null = null;
  @Output() closeModal = new EventEmitter<void>();

  onClose() {
    this.closeModal.emit();
  }
}
