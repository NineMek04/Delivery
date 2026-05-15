import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideAngularModule, Plus, Truck, AlertTriangle, Warehouse, User, ChevronRight, ArrowUpRight, ArrowDownRight } from 'lucide-angular';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration } from 'chart.js';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, LucideAngularModule, BaseChartDirective],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent {
  icons = { Plus, Truck, AlertTriangle, Warehouse, User, ChevronRight, ArrowUpRight, ArrowDownRight };
  readonly title = 'Operational_Status';

  public chartData: ChartConfiguration<'line'>['data'] = {
    labels: ['00:00', '04:00', '08:00', '12:00', '16:00', '20:00', '23:59'],
    datasets: [
      {
        data: [30, 25, 45, 65, 55, 80, 40],
        label: 'Flux_Value',
        fill: true,
        tension: 0.4,
        borderColor: '#00FF66',
        backgroundColor: 'rgba(0, 255, 102, 0.1)',
        pointBackgroundColor: '#00FF66',
        pointBorderColor: '#000',
        pointHoverBackgroundColor: '#fff',
        pointHoverBorderColor: '#00FF66',
      }
    ]
  };

  public chartOptions: ChartConfiguration<'line'>['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    scales: {
      y: {
        beginAtZero: true,
        grid: { color: '#262626', drawTicks: false },
        border: { display: false },
        ticks: { color: '#888888', font: { family: 'JetBrains Mono', size: 10 } }
      },
      x: {
        grid: { display: false },
        border: { display: false },
        ticks: { color: '#888888', font: { family: 'JetBrains Mono', size: 10 } }
      }
    },
    plugins: {
      legend: { display: false },
      tooltip: {
        backgroundColor: '#141414',
        titleColor: '#00FF66',
        bodyColor: '#fff',
        borderColor: '#262626',
        borderWidth: 1,
        titleFont: { family: 'JetBrains Mono' },
        bodyFont: { family: 'JetBrains Mono' }
      }
    }
  };
  
  readonly pendingOrders = [
    { id: '#ORD-8821', name: 'Northside Medical Supplies', distance: '2.4 km from hub', tag: 'STANDARD', note: 'Placed: 4m ago', dimmed: false },
    { id: '#ORD-8825', name: 'Downtown Gourmet Cafe', distance: '0.8 km from hub', tag: 'EXPRESS', note: 'Priority Handling', dimmed: false },
    { id: '#ORD-8829', name: 'Central Tech Park B4', distance: '5.2 km from hub', tag: 'STANDARD', note: 'Placed: 12m ago', dimmed: false },
    { id: '#ORD-8830', name: 'Warehouse 77 Logistics', distance: 'Scheduled for 14:00', tag: 'BULK', note: '', dimmed: true },
  ];

  readonly routeCards = [
    {
      featured: true,
      id: 'SHP-928',
      destination: 'Distribution Center West',
      vehicle: 'Volvo FH16',
      driver: 'Somsak R.',
      status: 'in-transit',
      eta: '14:30'
    },
    {
      featured: false,
      id: 'SHP-929',
      destination: 'City Port Terminal',
      vehicle: 'Scania R500',
      driver: 'Vichai P.',
      status: 'pending',
      eta: '16:45'
    },
    {
      featured: false,
      id: 'SHP-930',
      destination: 'Northern Hub',
      vehicle: 'Isuzu Giga',
      driver: 'Anan K.',
      status: 'delivered',
      eta: '11:15'
    }
  ];
}
