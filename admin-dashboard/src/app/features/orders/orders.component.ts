import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-orders',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './orders.component.html',
  styleUrl: './orders.component.scss'
})
export class OrdersComponent {
  readonly title = 'FleetControl AI';
  
  readonly orders = [
    { id: '#ORD-5502', time: '10:24 AM', pickup: 'Hub Central B', dropoff: '82nd Ave, North Park', status: 'ASSIGNED', rider: 'Marco P.', statusTone: 'blue' },
    { id: '#ORD-5501', time: '10:22 AM', pickup: 'Main Distribution Center', dropoff: 'Queens Square 12', status: 'PICKED UP', rider: 'Sarah J.', statusTone: 'amber' },
    { id: '#ORD-5499', time: '10:15 AM', pickup: 'West End Hub', dropoff: 'Riverdale Dr 404', status: 'DELIVERED', rider: 'Derek M.', statusTone: 'green' },
    { id: '#ORD-5498', time: '10:10 AM', pickup: 'South Side Port', dropoff: 'Industrial Way B2', status: 'PENDING', rider: 'Not Assigned', statusTone: 'gray' },
    { id: '#ORD-5497', time: '09:55 AM', pickup: 'Main Distribution Center', dropoff: 'Sunset Blvd 99', status: 'DELIVERED', rider: 'Lina O.', statusTone: 'green' },
  ];
}
