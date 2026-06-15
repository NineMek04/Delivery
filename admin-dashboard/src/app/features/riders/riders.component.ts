import { Component, OnInit, inject, DestroyRef, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule, RefreshCcw, Search, MapPin, Pencil, Trash2, Check, X } from 'lucide-angular';
import { RiderService } from '../../core/services/rider.service';
import { AnalyticsService, RiderPerformanceDto } from '../../core/services/analytics.service';
import { RiderDto } from '../../api/generated/model/rider-dto';
import { DataTableComponent, TableColumn } from '../../component/data-table/data-table.component';
import { RiderEditModalComponent } from './rider-edit-modal/rider-edit-modal.component';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TrackingSignalRService } from '../../core/services/tracking-signalr.service';
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
  private readonly trackingService = inject(TrackingSignalRService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly cdr = inject(ChangeDetectorRef);

  riders: RiderDto[] = [];
  topRiders: RiderPerformanceDto[] = [];
  isLoading = false;
  hasError = false;
  query = '';
  
  // Stats & Connection status
  idleCount = 0;
  busyCount = 0;
  offlineCount = 0;
  connectionStatus: 'CONNECTED' | 'DISCONNECTED' | 'RECONNECTING' = 'CONNECTED';

  // Pagination
  currentPage = 1;
  pageSize = 10;
  totalCount = 0;

  columns: TableColumn[] = [
    { field: 'id', header: 'RIDER_ID', isSortable: true },
    { field: 'name', header: 'NAME', isSortable: true },
    { field: 'phone', header: 'PHONE' },
    { field: 'rating', header: 'RATING', isSortable: true },
    { field: 'status', header: 'STATUS', isSortable: true },
    { field: 'lat', header: 'LATITUDE' },
    { field: 'lng', header: 'LONGITUDE' },
    { field: 'lastUpdated', header: 'LAST_UPDATE', isSortable: true }
  ];

  // modal edit state
  isEditModalOpen = false;
  selectedRider: RiderDto | null = null;

  recalculateStats(): void {
    this.idleCount = this.riders.filter(r => r.status === 'IDLE').length;
    this.busyCount = this.riders.filter(r => r.status === 'BUSY').length;
    this.offlineCount = this.riders.filter(r => ['OFFLINE', 'STALE'].includes(r.status || '')).length;
  }

  ngOnInit(): void {
    this.loadRiders();
    this.loadTopRiders();
    this.startRiderRealtimeUpdates();
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
        this.recalculateStats();
        this.isLoading = false;
      },
      error: () => {
        this.isLoading = false;
        this.hasError = true;
      }
    });
  }

  private startRiderRealtimeUpdates(): void {
    this.trackingService.startConnection();
    
    // ตรวจจับสถานะการเชื่อมต่อ SignalR
    this.trackingService.connectionStatus$.pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(status => {
      this.connectionStatus = status;
      this.cdr.markForCheck();
    });

    // อัปเดตพิกัดไรเดอร์แบบ In-place Mutation
    this.trackingService.riderLocations$.pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(locations => {
      let hasChanged = false;
      this.riders.forEach(rider => {
        if (rider.id && locations.has(rider.id)) {
          const update = locations.get(rider.id)!;
          if (rider.status !== update.status || rider.lat !== update.latitude || rider.lng !== update.longitude) {
            rider.status = update.status;
            rider.lat = update.latitude;
            rider.lng = update.longitude;
            rider.lastUpdated = update.timestamp;
            hasChanged = true;
          }
        }
      });
      if (hasChanged) {
        this.recalculateStats(); // คำนวณสถิติใหม่
        this.cdr.markForCheck(); // บังคับอัปเดตวิวเฉพาะส่วนโดยไม่ Clone Array ป้องกัน GC Spikes
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

  onSortChange(event: {field: string | null, dir: 'asc'|'desc'|null}) {
    if (!event.dir || !event.field) {
      this.loadRiders(); // reset to default server order
      return;
    }
    
    this.riders.sort((a, b) => {
      let valA: any = a[event.field as keyof RiderDto];
      let valB: any = b[event.field as keyof RiderDto];
      
      if (valA == null) valA = '';
      if (valB == null) valB = '';
      
      if (typeof valA === 'string') valA = valA.toLowerCase();
      if (typeof valB === 'string') valB = valB.toLowerCase();
      
      if (valA < valB) return event.dir === 'asc' ? -1 : 1;
      if (valA > valB) return event.dir === 'asc' ? 1 : -1;
      return 0;
    });
  }

  // ── Quick Toggle ──────────────────────────────────────────────────
  toggleStatus(rider: RiderDto) {
    if (!rider.id) return;
    const newStatus = rider.status === 'IDLE' ? 'OFFLINE' : 'IDLE';
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
