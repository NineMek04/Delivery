import { Component, inject } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../core/services/auth.service';
import { LucideAngularModule, LayoutDashboard, Warehouse, Truck, ChartLine, Settings, Search, Bell, User, Menu, Users, Store } from 'lucide-angular';

interface NavItem {
  path: string;
  label: string;
  icon: any;
}

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, CommonModule, LucideAngularModule],
  templateUrl: './main-layout.component.html',
  styleUrl: './main-layout.component.scss'
})
export class MainLayoutComponent {
  public authService = inject(AuthService);

  isSidebarOpen = true;
  name = this.authService.getUserData()?.FullName ?? 'Admin'
  // Icons used in header
  icons = { Menu, Search, Bell, User, Settings };

  readonly navItems: NavItem[] = [
    { path: '/dashboard', label: 'Dashboard',  icon: LayoutDashboard },
    { path: '/map',       label: 'Live Map',   icon: Truck },
    { path: '/orders',    label: 'Orders',     icon: Warehouse },
    { path: '/riders',    label: 'Riders',     icon: Users },
    { path: '/shops',     label: 'Shops',      icon: Store },
    { path: '/analytics', label: 'Analytics',  icon: ChartLine },
  ];

  toggleSidebar() {
    this.isSidebarOpen = !this.isSidebarOpen;
  }

  logout() {
    this.authService.logout().subscribe();
  }
}
