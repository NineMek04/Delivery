import { Component, Injector, OnInit, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthService } from './core/services/auth.service';

type PageId = 'dashboard' | 'map' | 'orders' | 'analytics';

interface NavItem {
  id: PageId;
  label: string;
  icon: string;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, CommonModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css',
})
export class AppComponent implements OnInit {
  static InjectorInstance: Injector;
  private authService = inject(AuthService);
  
  isAuthenticated = false;

  title = 'FleetControl AI';
  activePage: PageId = 'dashboard';

  constructor(private injector: Injector) {
    AppComponent.InjectorInstance = this.injector;
  }

  ngOnInit() {
    this.authService.isAuthenticated$.subscribe(isAuth => {
      this.isAuthenticated = isAuth;
    });
  }

  logout() {
    this.authService.logout();
  }

  readonly navItems: NavItem[] = [
    { id: 'dashboard', label: 'Dashboard', icon: '▦' },
    { id: 'map', label: 'Live Map', icon: '◇' },
    { id: 'orders', label: 'Orders', icon: '▱' },
    { id: 'analytics', label: 'Analytics', icon: '▣' },
  ];

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

  readonly alerts = [
    { title: 'Delayed Delivery', text: 'Order #4412 is stuck in heavy traffic at Midtown Tunnel.', time: '2m ago', tone: 'danger' },
    { title: 'Low Battery', text: 'Rider "Marco S." is at 12% battery. Recommend swap.', time: '8m ago', tone: 'warning' },
    { title: 'Signal Warning', text: 'Weak GPS signal detected for Unit 9918 in Brooklyn area.', time: '15m ago', tone: 'info' },
  ];

  readonly riders = [
    { name: 'Elena Vance', battery: '84%', signal: 'Strong', status: 'Online', avatar: 'EV', tone: 'online' },
    { name: 'Marco Rossi', battery: '12%', signal: 'Strong', status: 'Delivering', avatar: 'MR', tone: 'low' },
    { name: 'Sarah Chen', battery: '98%', signal: 'Weak', status: 'Delivering', avatar: 'SC', tone: 'weak' },
  ];

  readonly orders = [
    { id: '#ORD-5502', time: '10:24 AM', pickup: 'Hub Central B', dropoff: '82nd Ave, North Park', status: 'ASSIGNED', rider: 'Marco P.', statusTone: 'blue' },
    { id: '#ORD-5501', time: '10:22 AM', pickup: 'Main Distribution Center', dropoff: 'Queens Square 12', status: 'PICKED UP', rider: 'Sarah J.', statusTone: 'amber' },
    { id: '#ORD-5499', time: '10:15 AM', pickup: 'West End Hub', dropoff: 'Riverdale Dr 404', status: 'DELIVERED', rider: 'Derek M.', statusTone: 'green' },
    { id: '#ORD-5498', time: '10:10 AM', pickup: 'South Side Port', dropoff: 'Industrial Way B2', status: 'PENDING', rider: 'Not Assigned', statusTone: 'gray' },
    { id: '#ORD-5497', time: '09:55 AM', pickup: 'Main Distribution Center', dropoff: 'Sunset Blvd 99', status: 'DELIVERED', rider: 'Lina O.', statusTone: 'green' },
  ];

  readonly hubs = [
    { name: 'North Sector Alpha', fleet: '124 Vehicles', rate: '98.2%', duration: '22m 14s', status: 'OPTIMIZED', tone: 'blue' },
    { name: 'Central Terminal B', fleet: '89 Vehicles', rate: '91.5%', duration: '31m 45s', status: 'HEAVY LOAD', tone: 'amber' },
    { name: 'South Coastal Hub', fleet: '56 Vehicles', rate: '95.0%', duration: '18m 02s', status: 'OPTIMIZED', tone: 'blue' },
    { name: 'West Industrial Wing', fleet: '212 Vehicles', rate: '87.2%', duration: '44m 30s', status: 'DELAYED', tone: 'red' },
  ];

  setPage(page: PageId): void {
    this.activePage = page;
  }
}
