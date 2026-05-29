import { Component, Input, OnChanges, SimpleChanges, ViewChild, ElementRef, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Chart, registerables } from 'chart.js';
import { LoadTestMetrics } from '../../test-dashboard.model';

Chart.register(...registerables);

@Component({
  selector: 'app-metrics-chart',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="charts-container">
      <div class="chart-box">
        <div class="chart-header">
          <h3>System Capacity (RPS)</h3>
          <span class="badge" [class.danger]="metrics?.rps! >= 5000">
            {{ metrics?.rps || 0 | number }} / 5000
          </span>
        </div>
        <canvas #gaugeCanvas></canvas>
      </div>
      <div class="chart-box">
        <h3>RPS vs Latency Trend</h3>
        <canvas #lineCanvas></canvas>
      </div>
    </div>
  `,
  styles: [`
    .charts-container {
      display: flex;
      gap: 20px;
      margin-bottom: 20px;
    }
    .chart-box {
      flex: 1;
      background: #161b22;
      border: 1px solid #30363d;
      border-radius: 8px;
      padding: 16px;
      height: 250px;
      display: flex;
      flex-direction: column;
    }
    .chart-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 10px;
    }
    h3 {
      margin: 0;
      color: #8b949e;
      font-size: 14px;
      text-transform: uppercase;
      letter-spacing: 1px;
    }
    .badge {
      background: rgba(0, 229, 255, 0.15);
      color: #00e5ff;
      padding: 4px 8px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 600;
    }
    .badge.danger {
      background: rgba(255, 123, 114, 0.15);
      color: #ff7b72;
    }
    canvas {
      flex: 1;
      width: 100% !important;
      height: 100% !important;
      min-height: 150px;
    }
  `]
})
export class MetricsChartComponent implements AfterViewInit, OnChanges {
  @Input() metrics: LoadTestMetrics | null = null;
  
  @ViewChild('gaugeCanvas') gaugeCanvas!: ElementRef;
  @ViewChild('lineCanvas') lineCanvas!: ElementRef;
  
  private gaugeChart: Chart | null = null;
  private lineChart: Chart | null = null;

  ngAfterViewInit() {
    this.initCharts();
    if (this.metrics) {
      this.updateCharts();
    }
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['metrics'] && this.metrics) {
      this.updateCharts();
    }
  }

  private initCharts() {
    this.gaugeChart = new Chart(this.gaugeCanvas.nativeElement, {
      type: 'doughnut',
      data: {
        labels: ['Current RPS', 'Remaining Capacity'],
        datasets: [{
          data: [0, 5000],
          backgroundColor: ['#00e5ff', '#21262d'],
          borderWidth: 0,
          circumference: 180,
          rotation: 270
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: false },
          tooltip: { enabled: true }
        },
        cutout: '80%'
      }
    });

    this.lineChart = new Chart(this.lineCanvas.nativeElement, {
      type: 'line',
      data: {
        labels: ['Step 1', 'Step 2', 'Step 3', 'Step 4', 'Step 5'],
        datasets: [
          {
            label: 'RPS',
            data: [0, 0, 0, 0, 0],
            borderColor: '#00e5ff',
            backgroundColor: 'rgba(0, 229, 255, 0.1)',
            fill: true,
            tension: 0.4,
            yAxisID: 'y'
          },
          {
            label: 'Latency (ms)',
            data: [0, 0, 0, 0, 0],
            borderColor: '#ff7b72',
            backgroundColor: 'transparent',
            borderDash: [5, 5],
            tension: 0.4,
            yAxisID: 'y1'
          }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        scales: {
          y: {
            type: 'linear',
            display: true,
            position: 'left',
            grid: { color: '#30363d' },
            ticks: { color: '#8b949e' }
          },
          y1: {
            type: 'linear',
            display: true,
            position: 'right',
            grid: { drawOnChartArea: false },
            ticks: { color: '#ff7b72' }
          },
          x: {
            grid: { display: false },
            ticks: { color: '#8b949e' }
          }
        },
        plugins: {
          legend: {
            labels: { color: '#c9d1d9' }
          }
        }
      }
    });
  }

  private updateCharts() {
    if (!this.gaugeChart || !this.lineChart || !this.metrics) return;

    // Update Gauge
    const maxCapacity = 5000;
    const current = Math.min(this.metrics.rps, maxCapacity);
    const remaining = maxCapacity - current;
    this.gaugeChart.data.datasets[0].data = [current, remaining];
    
    // Color red if maxed (Breaking Point)
    const isBreaking = this.metrics.rps >= 5000 || this.metrics.errorRate > 10;
    this.gaugeChart.data.datasets[0].backgroundColor = [isBreaking ? '#ff7b72' : '#00e5ff', '#21262d'];
    this.gaugeChart.update();

    // Update Line Chart (simulate trend for now based on current result)
    // Normally, this data comes from backend historical lists, but we interpolate for demo
    const rpsData = [0, this.metrics.rps * 0.25, this.metrics.rps * 0.5, this.metrics.rps * 0.75, this.metrics.rps];
    const latData = [2, this.metrics.p95LatencyMs * 0.3, this.metrics.p95LatencyMs * 0.5, this.metrics.p95LatencyMs * 0.8, this.metrics.p95LatencyMs];
    
    this.lineChart.data.datasets[0].data = rpsData;
    this.lineChart.data.datasets[1].data = latData;
    this.lineChart.update();
  }
}
