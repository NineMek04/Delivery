import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule, RefreshCcw, Search, MapPin } from 'lucide-angular';
import { RiderService } from '../../core/services/rider.service';
import { RiderDto } from '../../api/generated/model/rider-dto';

@Component({
  selector: 'app-riders',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  templateUrl: './riders.component.html',
  styleUrl: './riders.component.scss'
})
export class RidersComponent implements OnInit {
  readonly title = 'Rider_Fleet';
  readonly icons = { RefreshCcw, Search, MapPin };

  private readonly riderService = inject(RiderService);

  riders: RiderDto[] = [];
  isLoading = false;
  query = '';

  ngOnInit(): void {
    this.loadRiders();
  }

  loadRiders(): void {
    this.isLoading = true;
    this.riderService.getAll().subscribe({
      next: (riders) => {
        this.riders = riders;
        this.isLoading = false;
      },
      error: () => {
        this.isLoading = false;
      }
    });
  }

  get filteredRiders(): RiderDto[] {
    const q = this.query.trim().toLowerCase();
    if (!q) return this.riders;
    return this.riders.filter(r =>
      (r.id || '').toLowerCase().includes(q) ||
      (r.name || '').toLowerCase().includes(q) ||
      (r.status || '').toLowerCase().includes(q)
    );
  }

  get idleCount(): number {
    return this.riders.filter(r => r.status === 'IDLE').length;
  }

  get busyCount(): number {
    return this.riders.filter(r => ['BUSY', 'DELIVERING', 'PICKING_UP'].includes(r.status || '')).length;
  }

  get offlineCount(): number {
    return this.riders.filter(r => r.status === 'OFFLINE').length;
  }

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

  formatCoord(val?: number | null): string {
    return val != null ? val.toFixed(5) : '—';
  }

  formatTime(val?: string | null): string {
    if (!val) return '—';
    return new Date(val).toLocaleTimeString('th-TH', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  }
}
