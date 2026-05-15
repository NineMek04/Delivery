import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent {
  readonly title = 'FleetControl AI';
  
  readonly pendingOrders = [
    { id: '#ORD-8821', name: 'Northside Medical Supplies', distance: '2.4 km from hub', tag: 'STANDARD', note: 'Placed: 4m ago', dimmed: false },
    { id: '#ORD-8825', name: 'Downtown Gourmet Cafe', distance: '0.8 km from hub', tag: 'EXPRESS', note: 'Priority Handling', dimmed: false },
    { id: '#ORD-8829', name: 'Central Tech Park B4', distance: '5.2 km from hub', tag: 'STANDARD', note: 'Placed: 12m ago', dimmed: false },
    { id: '#ORD-8830', name: 'Warehouse 77 Logistics', distance: 'Scheduled for 14:00', tag: 'BULK', note: '', dimmed: true },
  ];

  readonly routeCards = [
    {
      featured: true,
      title: 'Optimized Loop Alpha',
      rider: 'RIDER ID: RD-092',
      state: 'ACTIVE',
      match: '98% Match',
      savings: '$12.50',
      savingsSub: '-14% vs Manual',
      time: '15m',
      timeSub: 'Est. 22m total',
      orders: ['#ORD-8821  •  Northside', '#ORD-8825  •  Downtown', '#ORD-8842  •  East Bank'],
      action: 'Approve Dispatch',
    },
    {
      featured: false,
      title: 'Suburban Direct Gamma',
      rider: 'RIDER ID: RD-115',
      state: 'AVAILABLE',
      match: '82% Match',
      savings: '$5.20',
      savingsSub: '-6% vs Manual',
      time: '8m',
      timeSub: 'Est. 34m total',
      orders: ['#ORD-8829  •  Tech Park', '#ORD-8833  •  West Suburbs'],
      action: 'Approve Dispatch',
    },
  ];
}
