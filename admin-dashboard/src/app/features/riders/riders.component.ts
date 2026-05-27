import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule, RefreshCcw, Search, MapPin, Pencil, Trash2, Check, X } from 'lucide-angular';
import { RiderService } from '../../core/services/rider.service';
import { AnalyticsService, RiderPerformanceDto } from '../../core/services/analytics.service';
import { RiderDto } from '../../api/generated/model/rider-dto';
import { DataTableComponent, TableColumn } from '../../component/data-table/data-table.component';
import { RiderEditModalComponent } from './rider-edit-modal/rider-edit-modal.component';
import Swal from 'sweetalert2';

@Component({
  selector: 'app-riders',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule, DataTableComponent, RiderEditModalComponent],
  templateUrl: './riders.component.html',
  styleUrl: './riders.component.scss'
})
export class RidersComponent implements OnInit {
  readonly title = 'Rider_Fleet';
  readonly icons = { RefreshCcw, Search, MapPin, Pencil, Trash2, Check, X };

  private readonly riderService = inject(RiderService);
  private readonly analyticsService = inject(AnalyticsService);

  riders: RiderDto[] = [];
  topRiders: RiderPerformanceDto[] = [];
  isLoading = false;
  hasError = false;
  query = '';
  
  // Pagination
  currentPage = 1;
  pageSize = 10;
  totalCount = 0;

  columns: TableColumn[] = [
    { field: 'id', header: 'RIDER_ID' },
    { field: 'name', header: 'NAME' },
    { field: 'phone', header: 'PHONE' },
    { field: 'rating', header: 'RATING' },
    { field: 'status', header: 'STATUS' },
    { field: 'lat', header: 'LATITUDE' },
    { field: 'lng', header: 'LONGITUDE' },
    { field: 'lastUpdated', header: 'LAST_UPDATE' }
  ];

  // modal edit state
  isEditModalOpen = false;
  selectedRider: RiderDto | null = null;

  // For stats, we store counts based on current page data. 
  // Ideally, total stats should come from backend, but we'll compute from current page for simplicity
  get idleCount(): number {
    return this.riders.filter(r => r.status === 'IDLE').length;
  }
  get busyCount(): number {
    return this.riders.filter(r => ['BUSY', 'DELIVERING', 'PICKING_UP'].includes(r.status || '')).length;
  }
  get offlineCount(): number {
    return this.riders.filter(r => r.status === 'OFFLINE').length;
  }

  ngOnInit(): void {
    this.loadRiders();
    this.loadTopRiders();
  }

  loadTopRiders(): void {
    this.analyticsService.getTopRiders(3).subscribe({
      next: (data) => this.topRiders = data
    });
  }

  loadRiders(): void {
    this.isLoading = true;
    this.hasError = false;
    this.riderService.getAllPaginated(this.currentPage, this.pageSize, this.query).subscribe({
      next: (res) => {
        this.riders = res.items;
        this.totalCount = res.totalCount;
        this.isLoading = false;
      },
      error: () => {
        this.isLoading = false;
        this.hasError = true;
      }
    });
  }

  onPageChange(page: number) {
    this.currentPage = page;
    this.loadRiders();
  }

  onSearch(query: string) {
    this.query = query;
    this.currentPage = 1;
    this.loadRiders();
  }

  // ── Quick Toggle ──────────────────────────────────────────────────
  toggleStatus(rider: RiderDto) {
    if (!rider.id) return;
    const newStatus = rider.status === 'IDLE' ? 'UNAVAILABLE' : 'IDLE';
    const payload: Partial<RiderDto> = { status: newStatus };
    this.riderService.update(rider.id, payload).subscribe({
      next: () => { rider.status = newStatus; },
      error: () => { Swal.fire('Error', 'Failed to update status', 'error'); }
    });
  }

  // ── Modal Edit ──────────────────────────────────────────────────

  startEdit(rider: RiderDto): void {
    this.selectedRider = rider;
    this.isEditModalOpen = true;
  }

  closeEditModal(): void {
    this.isEditModalOpen = false;
    this.selectedRider = null;
  }

  saveModalEdit(updatedData: RiderDto): void {
    if (!updatedData.id) return;
    
    Swal.fire({
      title: 'กำลังบันทึก...',
      allowOutsideClick: false,
      background: '#141414',
      color: '#FFFFFF',
      didOpen: () => Swal.showLoading()
    });

    const payload: Partial<RiderDto> = {
      name: updatedData.name,
      status: updatedData.status,
      phone: updatedData.phone
    };

    this.riderService.update(updatedData.id, payload).subscribe({
      next: () => {
        // update local list
        const idx = this.riders.findIndex(r => r.id === updatedData.id);
        if (idx !== -1) {
          this.riders[idx] = { ...this.riders[idx], ...updatedData };
        }
        this.closeEditModal();
        Swal.fire({ icon: 'success', title: 'บันทึกสำเร็จ', timer: 1500, showConfirmButton: false, background: '#141414', color: '#FFFFFF' });
      },
      error: (err) => {
        const serverMessage = err?.error?.message ?? err?.error?.Message ?? err?.message ?? 'กรุณาลองใหม่อีกครั้ง';
        Swal.fire({ icon: 'error', title: 'บันทึกไม่สำเร็จ', text: serverMessage, background: '#141414', color: '#FFFFFF' });
      }
    });
  }

  // ── Delete ───────────────────────────────────────────────────────

  deleteRider(rider: RiderDto): void {
    if (!rider.id) return;
    Swal.fire({
      title: 'ลบไรเดอร์?',
      text: `"${rider.name || rider.trackingCode}" จะถูกลบออกจากระบบ`,
      icon: 'warning',
      showCancelButton: true,
      confirmButtonColor: '#d33',
      cancelButtonColor: '#3085d6',
      confirmButtonText: 'ใช่, ลบเลย',
      cancelButtonText: 'ยกเลิก',
      background: '#141414',
      color: '#FFFFFF'
    }).then(result => {
      if (!result.isConfirmed || !rider.id) return;

      Swal.fire({
        title: 'กำลังลบ...',
        allowOutsideClick: false,
        background: '#141414',
        color: '#FFFFFF',
        didOpen: () => {
          Swal.showLoading();
        }
      });

      this.riderService.delete(rider.id).subscribe({
        next: () => {
          this.loadRiders();
          Swal.fire({ 
            icon: 'success', 
            title: 'ลบสำเร็จ', 
            timer: 1500, 
            showConfirmButton: false,
            background: '#141414',
            color: '#FFFFFF'
          });
        },
        error: (err) => {
          const serverMessage = err?.error?.message ?? err?.error?.Message ?? err?.message ?? 'กรุณาลองใหม่อีกครั้ง';
          Swal.fire({ 
            icon: 'error', 
            title: 'ลบไม่สำเร็จ', 
            text: serverMessage,
            background: '#141414',
            color: '#FFFFFF'
          });
        }
      });
    });
  }

  // ── Helpers ──────────────────────────────────────────────────────

  getStatusTone(status?: string | null): string {
    switch (status) {
      case 'IDLE':       return 'green';
      case 'RESERVED':   return 'amber';
      case 'BUSY':       return 'blue';
      case 'STALE':      return 'amber';
      case 'OFFLINE':    return 'gray';
      default:           return 'gray';
    }
  }

  shortId(id?: string | null): string {
    return id ? id.slice(0, 8).toUpperCase() : '—';
  }

  getRiderTrackingCode(rider?: RiderDto | null): string {
    if (!rider) return '—';
    return rider.trackingCode ? rider.trackingCode : this.shortId(rider.id);
  }

  formatCoord(val?: number | null): string {
    return val != null ? val.toFixed(5) : '—';
  }

  formatTime(val?: string | null): string {
    if (!val) return '—';
    return new Date(val).toLocaleTimeString('th-TH', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  }
}
