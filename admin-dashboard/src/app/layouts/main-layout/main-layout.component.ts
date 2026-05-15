import { Component, inject } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../core/services/auth.service';
import { LucideAngularModule, LayoutDashboard, Warehouse, Truck, BarChart3, Settings, Search, Bell, User, Menu, ChevronRight } from 'lucide-angular';

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
  private authService = inject(AuthService);

  isSidebarOpen = true;

  // Icons used in header
  icons = { Menu, Search, Bell, User, Settings };

  readonly navItems: NavItem[] = [
    { path: '/dashboard', label: 'Dashboard', icon: LayoutDashboard },
    { path: '/map', label: 'Live Map', icon: Truck }, // Using Truck for Fleet/Map
    { path: '/orders', label: 'Orders', icon: Warehouse }, // Using Warehouse for Orders
    { path: '/analytics', label: 'Analytics', icon: BarChart3 },
  ];

  toggleSidebar() {
    this.isSidebarOpen = !this.isSidebarOpen;
  }

  logout() {
    this.authService.logout();
  }
}
