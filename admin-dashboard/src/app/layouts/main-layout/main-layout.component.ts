import { Component, inject } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../core/services/auth.service';

interface NavItem {
  path: string;
  label: string;
  icon: string;
}

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, CommonModule],
  templateUrl: './main-layout.component.html',
  styleUrl: './main-layout.component.scss'
})
export class MainLayoutComponent {
  private authService = inject(AuthService);

  readonly navItems: NavItem[] = [
    { path: '/dashboard', label: 'Dashboard', icon: '▦' },
    { path: '/map', label: 'Live Map', icon: '◇' },
    { path: '/orders', label: 'Orders', icon: '▱' },
    { path: '/analytics', label: 'Analytics', icon: '▣' },
  ];

  logout() {
    this.authService.logout();
  }
}
