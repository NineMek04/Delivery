import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-map',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './map.component.html',
  styleUrl: './map.component.scss'
})
export class MapComponent {
  readonly title = 'Live Fleet Map';
  
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
}
