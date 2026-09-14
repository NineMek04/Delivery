import {
  Component, EventEmitter, Input, Output, OnChanges, SimpleChanges, inject, DestroyRef
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LucideAngularModule, X, Clock, MapPin, Star, ChevronRight, RefreshCcw } from 'lucide-angular';
import { RiderDto } from '../../../api/generated/model/rider-dto';
import { RiderHistoryService, RiderCompletedOrder } from '../../../core/services/rider-history.service';

type Preset = 'TODAY' | '7D' | '14D' | '30D';

@Component({
  selector: 'app-rider-history-modal',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  templateUrl: './rider-history-modal.component.html',
  styleUrl: './rider-history-modal.component.scss'
})
export class RiderHistoryModalComponent implements OnChanges {
  readonly icons = { X, Clock, MapPin, Star, ChevronRight, RefreshCcw };
  readonly presets: Preset[] = ['TODAY', '7D', '14D', '30D'];

  @Input() isOpen = false;
  @Input() rider: RiderDto | null = null;

  @Output() closed          = new EventEmitter<void>();
  @Output() orderSelected   = new EventEmitter<{ order: RiderCompletedOrder; rider: RiderDto }>();

  private readonly historyService = inject(RiderHistoryService);
  private readonly destroyRef     = inject(DestroyRef);

  // ── Filter state ──────────────────────────────────────────────────
  activePreset: Preset = 'TODAY';
  customFrom = '';
  customTo   = '';

  // ── Data state ────────────────────────────────────────────────────
  orders: RiderCompletedOrder[] = [];
  isLoading = false;
  hasError  = false;
  errorMsg  = '';

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['isOpen']?.currentValue === true && this.rider) {
      this.setPreset('TODAY');
    }
  }

  // ── Filter actions ────────────────────────────────────────────────

  setPreset(preset: Preset): void {
    this.activePreset = preset;
    const range = RiderHistoryService.buildDateRange(preset);
    // แสดงค่าใน date inputs (local time yyyy-mm-dd format)
    this.customFrom = this.toDateInput(range.from);
    this.customTo   = this.toDateInput(range.to);
    this.load(range.from, range.to);
  }

  onCustomDateChange(): void {
    if (!this.customFrom || !this.customTo) return;
    this.activePreset = 'TODAY'; // ล้าง preset highlight เมื่อ custom
    const from = new Date(this.customFrom);
    const to   = new Date(this.customTo);
    to.setHours(23, 59, 59, 999); // ถึงสิ้นวัน
    if (from < to) this.load(from, to);
  }

  refresh(): void {
    this.setPreset(this.activePreset);
  }

  // ── Data loading ──────────────────────────────────────────────────

  private load(from: Date, to: Date): void {
    if (!this.rider?.id) return;
    this.isLoading = true;
    this.hasError  = false;
    this.orders    = [];

    this.historyService
      .getCompletedOrders(this.rider.id, from, to)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.orders    = data;
          this.isLoading = false;
        },
        error: (err) => {
          this.isLoading = false;
          this.hasError  = true;
          this.errorMsg  = err?.error?.message ?? 'ไม่สามารถดึงข้อมูลได้';
        }
      });
  }

  // ── Events ────────────────────────────────────────────────────────

  selectOrder(order: RiderCompletedOrder): void {
    if (!this.rider) return;
    this.orderSelected.emit({ order, rider: this.rider });
  }

  close(): void {
    this.closed.emit();
  }

  // ── Helpers ───────────────────────────────────────────────────────

  private toDateInput(d: Date): string {
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
  }

  formatDate(iso: string | null): string {
    if (!iso) return '—';
    return new Date(iso).toLocaleString('th-TH', {
      day: '2-digit', month: 'short', year: '2-digit',
      hour: '2-digit', minute: '2-digit'
    });
  }

  formatFee(fee: number): string {
    return `฿${fee.toFixed(0)}`;
  }

  formatKm(km: number): string {
    return `${km.toFixed(1)} km`;
  }

  starArray(rating: number | null): number[] {
    if (!rating) return [];
    return Array.from({ length: rating }, (_, i) => i);
  }

  riderLabel(): string {
    return this.rider?.name ?? this.rider?.id?.slice(0, 8).toUpperCase() ?? '—';
  }
}
