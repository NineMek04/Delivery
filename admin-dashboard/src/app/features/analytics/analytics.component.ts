import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-analytics',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './analytics.component.html',
  styleUrl: './analytics.component.scss'
})
export class AnalyticsComponent {
  readonly title = 'FleetControl AI';
  
  readonly hubs = [
    { name: 'North Sector Alpha', fleet: '124 Vehicles', rate: '98.2%', duration: '22m 14s', status: 'OPTIMIZED', tone: 'blue' },
    { name: 'Central Terminal B', fleet: '89 Vehicles', rate: '91.5%', duration: '31m 45s', status: 'HEAVY LOAD', tone: 'amber' },
    { name: 'South Coastal Hub', fleet: '56 Vehicles', rate: '95.0%', duration: '18m 02s', status: 'OPTIMIZED', tone: 'blue' },
    { name: 'West Industrial Wing', fleet: '212 Vehicles', rate: '87.2%', duration: '44m 30s', status: 'DELAYED', tone: 'red' },
  ];
}
