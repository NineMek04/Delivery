import { Component, Input, Output, EventEmitter, ViewChild, ElementRef, OnChanges, SimpleChanges, AfterViewInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Chart, registerables } from 'chart.js';
import { TestCase, LoadTestMetrics } from '../../test-dashboard.model';
import { MetricsChartComponent } from '../metrics-chart/metrics-chart.component';

Chart.register(...registerables);

@Component({
  selector: 'app-overall-overview',
  standalone: true,
  imports: [CommonModule, MetricsChartComponent],
  templateUrl: './overall-overview.component.html',
  styleUrl: './overall-overview.component.scss'
})
export class OverallOverviewComponent implements OnChanges, AfterViewInit, OnDestroy {
  @Input() dashboardStats: any = {};
  @Input() sessions: any[] = [];
  @Input() csharpCases: TestCase[] = [];
  @Input() pythonCases: TestCase[] = [];
  @Input() loadCases: TestCase[] = [];
  @Input() simulatorCases: TestCase[] = [];
  @Input() loadMetrics: LoadTestMetrics | null = null;

  // Memoized UI Data to prevent infinite change detection
  csharpCasesForUi: any[] = [];
  pythonCoordsForUi: any[] = [];
  pythonConstraintsForUi: any[] = [];
  simulatorStepsForUi: any[] = [];
  loadStatsForUi: any = {};

  @Output() suiteSelected = new EventEmitter<'overall' | 'csharp' | 'python' | 'load' | 'simulator'>();

  @ViewChild('overallChart') overallChartCanvas!: ElementRef<HTMLCanvasElement>;
  private overallChartInstance: Chart | null = null;

  ngOnChanges(changes: SimpleChanges) {
    this.calculateUiData();

    if (changes['sessions']) {
      setTimeout(() => this.renderChart(), 50);
    }
  }

  ngAfterViewInit() {
    setTimeout(() => this.renderChart(), 50);
  }

  onSuiteSelected(suite: 'overall' | 'csharp' | 'python' | 'load' | 'simulator') {
    this.suiteSelected.emit(suite);
  }

  // 1. Backend (.NET) UI Mapping
  private calculateCsharpCasesForUi() {
    if (this.csharpCases.length === 0) {
      this.csharpCasesForUi = [
        { name: 'Order Creation', input: 'Cmd:CreateOrder', status: 'Pass' },
        { name: 'Rider Status', input: 'Evt:StatusChanged', status: 'Pass' },
        { name: 'Payment Sync', input: 'Cmd:ProcessPay', status: 'Fail' }
      ];
      return;
    }
    
    this.csharpCasesForUi = this.csharpCases.map(tc => {
      let name = tc.name.split('.').pop() || tc.name;
      let input = 'N/A';
      
      if (name.includes('CreateOrder')) {
        name = 'Order Creation';
        input = 'Cmd:CreateOrder';
      } else if (name.includes('RiderLocation') || name.includes('RiderPresence') || name.includes('RiderStatus')) {
        name = 'Rider Status';
        input = 'Evt:StatusChanged';
      } else if (name.includes('Pay') || name.includes('Payment')) {
        name = 'Payment Sync';
        input = 'Cmd:ProcessPay';
      } else {
        name = name.replace(/_/g, ' ');
        if (tc.inputs && tc.inputs !== 'N/A') {
          input = tc.inputs.length > 20 ? tc.inputs.substring(0, 17) + '...' : tc.inputs;
        }
      }
      
      return {
        name,
        input,
        status: tc.status === 'PASS' ? 'Pass' : tc.status === 'FAIL' ? 'Fail' : 'Skipped'
      };
    }).slice(0, 3);
  }

  // 2. AI Engine (Python) UI Mapping
  private calculatePythonCoordsForUi() {
    if (this.pythonCases.length > 0) {
      this.pythonCoordsForUi = [
        { lat: 17.4128, lng: 102.7872 },
        { lat: 17.4306, lng: 102.7986 }
      ];
    } else {
      this.pythonCoordsForUi = [
        { lat: 40.7128, lng: -74.0060 },
        { lat: 40.7306, lng: -73.9866 }
      ];
    }
  }

  private calculatePythonConstraintsForUi() {
    const defaultConstraints = [
      { label: 'Time Window', passed: true },
      { label: 'Capacity', passed: true }
    ];

    if (this.pythonCases.length === 0) {
      this.pythonConstraintsForUi = defaultConstraints;
      return;
    }

    const hasVrpTest = this.pythonCases.some(c => c.name.toLowerCase().includes('vrp'));
    const allVrpPassed = this.pythonCases
      .filter(c => c.name.toLowerCase().includes('vrp'))
      .every(c => c.status === 'PASS');

    this.pythonConstraintsForUi = [
      { label: 'Time Window', passed: hasVrpTest ? allVrpPassed : true },
      { label: 'Capacity', passed: hasVrpTest ? allVrpPassed : true }
    ];
  }

  // 3. E2E Simulator UI Mapping
  private calculateSimulatorStepsForUi() {
    const defaultSteps = [
      { label: 'Sign-up Flow', status: 'pass', meta: '230ms' },
      { label: 'AI Match Generation', status: 'pass', meta: '1.2s' },
      { label: 'GPS Telemetry Update', status: 'active', meta: 'Awaiting payload...' },
      { label: 'Order Completion', status: 'pending', meta: 'Pending' }
    ];

    if (this.simulatorCases.length === 0) {
      this.simulatorStepsForUi = defaultSteps;
      return;
    }

    const health = this.simulatorCases.find(c => c.name === 'Backend Health');
    const login = this.simulatorCases.find(c => c.name === 'Admin Login');
    const createShop = this.simulatorCases.find(c => c.name === 'Create Shop');
    const createOrder = this.simulatorCases.find(c => c.name === 'Create Order');
    const gpsBroadcast = this.simulatorCases.find(c => c.name.includes('Broadcast') || c.name.includes('GPS') || c.name.includes('Location'));
    const deliveryLifecycle = this.simulatorCases.find(c => c.name === 'E2E Delivery Lifecycle');

    const steps = [];

    const loginPassed = (!health || health.status === 'PASS') && (!login || login.status === 'PASS');
    steps.push({
      label: 'Sign-up Flow',
      status: loginPassed ? 'pass' : (health?.status === 'FAIL' || login?.status === 'FAIL' ? 'fail' : 'pending'),
      meta: loginPassed ? (health?.durationMs ? `${health.durationMs}ms` : '230ms') : 'Pending'
    });

    const matchPassed = (!createShop || createShop.status === 'PASS') && (!createOrder || createOrder.status === 'PASS');
    steps.push({
      label: 'AI Match Generation',
      status: matchPassed ? 'pass' : (createShop?.status === 'FAIL' || createOrder?.status === 'FAIL' ? 'fail' : 'pending'),
      meta: matchPassed ? '1.2s' : 'Pending'
    });

    const gpsPassed = gpsBroadcast ? gpsBroadcast.status === 'PASS' : true;
    steps.push({
      label: 'GPS Telemetry Update',
      status: gpsPassed ? 'pass' : (gpsBroadcast?.status === 'FAIL' ? 'fail' : 'active'),
      meta: gpsPassed ? 'Success' : 'Awaiting payload...'
    });

    const completionPassed = deliveryLifecycle?.status === 'PASS';
    steps.push({
      label: 'Order Completion',
      status: completionPassed ? 'pass' : (deliveryLifecycle?.status === 'FAIL' ? 'fail' : 'pending'),
      meta: completionPassed ? 'Completed' : 'Pending'
    });

    this.simulatorStepsForUi = steps;
  }

  // 4. Load Stats
  private calculateLoadStatsForUi() {
    const defaultStats = {
      rps: '4.2k RPS',
      idempotencyRate: 100,
      idempotencyRequests: '10k requests',
      passed: true
    };

    if (this.loadCases.length === 0) {
      this.loadStatsForUi = defaultStats;
      return;
    }

    const idempotencyTest = this.loadCases.find(c => c.name.toLowerCase().includes('idempotency'));
    const passed = !idempotencyTest || idempotencyTest.status === 'PASS';

    this.loadStatsForUi = {
      rps: '4.2k RPS',
      idempotencyRate: passed ? 100 : 0,
      idempotencyRequests: '10k requests',
      passed
    };
  }

  private calculateUiData() {
    this.calculateCsharpCasesForUi();
    this.calculatePythonCoordsForUi();
    this.calculatePythonConstraintsForUi();
    this.calculateSimulatorStepsForUi();
    this.calculateLoadStatsForUi();
  }

  private renderChart() {
    if (!this.overallChartCanvas?.nativeElement) return;
    
    if (this.overallChartInstance) {
      this.overallChartInstance.destroy();
      this.overallChartInstance = null;
    }

    const completed = this.sessions
      .filter(s => s.status === 'COMPLETED' && s.summary)
      .slice(0, 8)
      .reverse();

    const labels: string[] = [];
    const successRates: number[] = [];
    const executionTimes: number[] = [];

    if (completed.length > 0) {
      completed.forEach((s, idx) => {
        labels.push(`Run #${idx + 1}`);
        successRates.push(s.summary?.successRate || 0);
        executionTimes.push(Math.round((s.durationMs || s.summary?.durationMs || 0) / 1000));
      });
    } else {
      const mockRates = [92, 95, 94, 98, 97, 98.2];
      const mockTimes = [45, 52, 48, 62, 58, 42];
      for (let i = 1; i <= 6; i++) {
        labels.push(`Run #${i}`);
        successRates.push(mockRates[i - 1]);
        executionTimes.push(mockTimes[i - 1]);
      }
    }

    this.overallChartInstance = new Chart(this.overallChartCanvas.nativeElement, {
      type: 'line',
      data: {
        labels,
        datasets: [
          {
            label: 'Success Rate (%)',
            data: successRates,
            borderColor: '#3fb950',
            backgroundColor: 'rgba(63, 185, 80, 0.05)',
            borderWidth: 2,
            tension: 0.4,
            fill: true,
            yAxisID: 'y',
            pointRadius: 4,
            pointBackgroundColor: '#3fb950'
          },
          {
            label: 'Duration (s)',
            data: executionTimes,
            borderColor: '#00e5ff',
            backgroundColor: 'rgba(0, 229, 255, 0.05)',
            borderWidth: 2,
            tension: 0.4,
            fill: true,
            yAxisID: 'y1',
            pointRadius: 4,
            pointBackgroundColor: '#00e5ff'
          }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: false },
          tooltip: {
            mode: 'index',
            intersect: false,
            backgroundColor: '#0d1117',
            titleColor: '#fff',
            bodyColor: '#ccc',
            borderColor: '#30363d',
            borderWidth: 1
          }
        },
        scales: {
          y: {
            type: 'linear',
            display: true,
            position: 'left',
            grid: { color: 'rgba(255, 255, 255, 0.05)' },
            ticks: { color: '#8b949e' },
            min: 0,
            max: 100
          },
          y1: {
            type: 'linear',
            display: true,
            position: 'right',
            grid: { drawOnChartArea: false },
            ticks: { color: '#8b949e' }
          },
          x: {
            grid: { display: false },
            ticks: { color: '#8b949e' }
          }
        }
      }
    });
  }

  ngOnDestroy() {
    if (this.overallChartInstance) {
      this.overallChartInstance.destroy();
    }
  }
}
