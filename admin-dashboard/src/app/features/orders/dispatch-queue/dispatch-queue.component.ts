import { Component, inject, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideAngularModule, Clock, Truck, CheckCircle, AlertCircle, X, Check } from 'lucide-angular';
import { TrackingSignalRService, DispatchScanStarted, DispatchOffer } from '../../../core/services/tracking-signalr.service';
import { OrderService } from '../../../core/services/order.service';
import { Subscription, interval } from 'rxjs';
import Swal from 'sweetalert2';

interface ActiveOffer extends DispatchOffer {
  timeLeft: number;
}

@Component({
  selector: 'app-dispatch-queue',
  standalone: true,
  imports: [CommonModule, LucideAngularModule],
  templateUrl: './dispatch-queue.component.html',
  styleUrl: './dispatch-queue.component.scss'
})
export class DispatchQueueComponent implements OnInit, OnDestroy {
  ClockIcon = Clock;

  activeScans: DispatchScanStarted[] = [];
  activeOffers: ActiveOffer[] = [];

  private trackingService = inject(TrackingSignalRService);
  private orderService = inject(OrderService);
  private cdr = inject(ChangeDetectorRef);
  private sub = new Subscription();

  ngOnInit() {
    this.sub.add(
      this.trackingService.dispatchScanStarted$.subscribe(scan => {
        this.activeScans.push(scan);
        this.cdr.markForCheck();
      })
    );

    this.sub.add(
      this.trackingService.dispatchCandidatesRanked$.subscribe(rank => {
        // Remove from active scans when ranked
        if (rank.order?.id) {
          this.activeScans = this.activeScans.filter(s => s.order?.id !== rank.order.id);
          this.cdr.markForCheck();
        }
      })
    );

    this.sub.add(
      this.trackingService.offerReceived$.subscribe(offer => {
        // Calculate initial time left based on expiresAt
        const expires = new Date(offer.expiresAt).getTime();
        const now = new Date().getTime();
        let timeLeft = Math.floor((expires - now) / 1000);
        if (timeLeft <= 0) timeLeft = 30; // Fallback 30s

        this.activeOffers.push({ ...offer, timeLeft });
        this.cdr.markForCheck();
      })
    );

    this.sub.add(
      this.trackingService.orderAssigned$.subscribe(data => {
        // Remove from offers when assigned
        this.activeOffers = this.activeOffers.filter(o => o.order?.id !== data.id);
        this.cdr.markForCheck();
      })
    );

    // Countdown Timer
    this.sub.add(
      interval(1000).subscribe(() => {
        let changed = false;
        this.activeOffers.forEach(o => {
          if (o.timeLeft > 0) {
            o.timeLeft--;
            changed = true;
          }
        });

        // Remove expired
        const prevLength = this.activeOffers.length;
        this.activeOffers = this.activeOffers.filter(o => o.timeLeft > 0);
        if (this.activeOffers.length !== prevLength) changed = true;

        if (changed) this.cdr.markForCheck();
      })
    );
  }

  ngOnDestroy() {
    this.sub.unsubscribe();
  }

  forceAssign(offer: ActiveOffer) {
    if (!offer.order?.id || !offer.riderId) return;

    Swal.fire({
      title: 'Force Assignment',
      text: `Force assign Order ${offer.order!.id!.slice(0, 8)} to Rider ${offer.riderId!.slice(0, 8)}?`,
      icon: 'warning',
      showCancelButton: true,
      confirmButtonText: 'Yes, Force Assign',
      background: '#141414',
      color: '#FFFFFF'
    }).then(result => {
      if (result.isConfirmed) {
        // Normally calls an admin override endpoint. Here we'll mock removing it.
        this.activeOffers = this.activeOffers.filter(o => o.offerId !== offer.offerId);
        Swal.fire({ icon: 'success', title: 'Assigned', background: '#141414', color: '#FFFFFF', timer: 1500 });
        this.cdr.markForCheck();
      }
    });
  }
}

