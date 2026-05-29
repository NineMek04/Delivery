import { Component, Input, Output, EventEmitter, ViewChild, ElementRef, OnChanges, SimpleChanges, AfterViewInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Chart, registerables } from 'chart.js';
import { TestCase } from '../../test-dashboard.model';

Chart.register(...registerables);

@Component({
  selector: 'app-suite-details',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './suite-details.component.html',
  styleUrl: './suite-details.component.scss'
})
export class SuiteDetailsComponent implements OnChanges, AfterViewInit, OnDestroy {
  @Input() activeSuite: string = '';
  @Input() filteredCases: TestCase[] = [];
  @Input() searchQuery: string = '';
  @Input() activeStatusFilter: string = 'all';
  @Input() chartType: 'doughnut' | 'bar' = 'doughnut';

  @Input() totalCount: number = 0;
  @Input() passedCount: number = 0;
  @Input() failedCount: number = 0;

  @Output() searchChanged = new EventEmitter<string>();
  @Output() statusFilterChanged = new EventEmitter<string>();
  @Output() chartTypeChanged = new EventEmitter<'doughnut' | 'bar'>();
  @Output() caseClicked = new EventEmitter<TestCase>();

  @ViewChild('unitChart') unitChartCanvas!: ElementRef<HTMLCanvasElement>;
  private chartInstance: Chart | null = null;

  ngOnChanges(changes: SimpleChanges) {
    if (changes['filteredCases'] || changes['chartType'] || changes['activeSuite']) {
      setTimeout(() => this.renderChart(), 50);
    }
  }

  ngAfterViewInit() {
    setTimeout(() => this.renderChart(), 50);
  }

  onSearchChange(val: string) {
    this.searchChanged.emit(val);
  }

  onStatusFilterChanged(status: string) {
    this.statusFilterChanged.emit(status);
  }

  onToggleChartType(type: 'doughnut' | 'bar') {
    this.chartTypeChanged.emit(type);
  }

  onCaseClicked(row: TestCase) {
    this.caseClicked.emit(row);
  }

  private renderChart() {
    if (!this.unitChartCanvas?.nativeElement) return;
    
    if (this.chartInstance) {
      this.chartInstance.destroy();
      this.chartInstance = null;
    }

    const testCases = this.filteredCases;

    if (this.chartType === 'doughnut') {
      const passed = testCases.filter(t => t.status === 'PASS').length;
      const failed = testCases.filter(t => t.status === 'FAIL').length;
      const skipped = testCases.filter(t => t.status === 'SKIPPED').length;

      this.chartInstance = new Chart(this.unitChartCanvas.nativeElement, {
        type: 'doughnut',
        data: {
          labels: ['Pass', 'Fail', 'Skipped'],
          datasets: [{
            data: [passed, failed, skipped],
            backgroundColor: ['rgba(63, 185, 80, 0.7)', 'rgba(248, 113, 113, 0.7)', 'rgba(156, 163, 175, 0.7)'],
            borderColor: ['#3fb950', '#f85149', '#8b949e'],
            borderWidth: 1
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            legend: {
              position: 'right',
              labels: {
                color: '#8b949e',
                font: { size: 11 }
              }
            }
          },
          cutout: '70%'
        }
      });
    } else {
      const labels = testCases.map(t => t.name.substring(0, 15) + (t.name.length > 15 ? '...' : ''));
      const dataPoints = testCases.map(t => t.durationMs);
      const bgColors = testCases.map(t => t.status === 'PASS' ? 'rgba(74, 222, 128, 0.7)' : (t.status === 'FAIL' ? 'rgba(248, 113, 113, 0.7)' : 'rgba(156, 163, 175, 0.7)'));
      const borderColors = testCases.map(t => t.status === 'PASS' ? 'rgba(74, 222, 128, 1)' : (t.status === 'FAIL' ? 'rgba(248, 113, 113, 1)' : 'rgba(156, 163, 175, 1)'));

      this.chartInstance = new Chart(this.unitChartCanvas.nativeElement, {
        type: 'bar',
        data: {
          labels,
          datasets: [{
            label: 'Test Duration (ms)',
            data: dataPoints,
            backgroundColor: bgColors,
            borderColor: borderColors,
            borderWidth: 1,
            borderRadius: 4
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: {
            legend: { display: false },
            tooltip: {
              callbacks: {
                title: (context) => {
                  return testCases[context[0].dataIndex].name;
                },
                afterTitle: (context) => {
                  const t = testCases[context[0].dataIndex];
                  return `Location: ${t.location}\nInputs: ${t.inputs}`;
                },
                label: (context) => {
                  const t = testCases[context.dataIndex];
                  let label = `Result: ${t.status} (${t.durationMs}ms)`;
                  if (t.error) {
                    label += `\nError: ${t.error}`;
                  }
                  return label;
                }
              }
            }
          },
          scales: {
            y: { beginAtZero: true, grid: { color: 'rgba(255, 255, 255, 0.1)' } },
            x: { grid: { display: false } }
          }
        }
      });
    }
  }

  ngOnDestroy() {
    if (this.chartInstance) {
      this.chartInstance.destroy();
    }
  }
}
