import { Component, OnInit, OnDestroy, ViewChild, ElementRef, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { io, Socket } from 'socket.io-client';
import { Chart, registerables, TooltipItem } from 'chart.js';
import { LiveTerminalComponent } from './components/live-terminal/live-terminal.component';
import { SimulatorHostComponent } from './components/simulator-host/simulator-host.component';

Chart.register(...registerables);

export interface TestSession {
  sessionId: string;
  testSuite: string;
  status: string;
  createdAt: string;
  completedAt?: string;
  durationMs?: number;
  triggerType: 'docker' | 'host';
  logFile: string;
  reportFile?: string;
  summary?: TestSummary;
  error?: string;
}

export interface TestCase {
  name: string;
  location: string;
  inputs: string;
  status: 'PASS' | 'FAIL' | 'SKIPPED';
  durationMs: number;
  error?: string;
}

export interface TestSummary {
  suiteType: string;
  status: string;
  total: number;
  passed: number;
  failed: number;
  skipped: number;
  successRate: number;
  durationMs: number;
  generatedAt: string;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule, LiveTerminalComponent, SimulatorHostComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent implements OnInit, OnDestroy {
  title = 'Testing Dashboard';
  apiUrl = 'http://localhost:3001';

  // Config State
  activeSuite: 'overall' | 'csharp' | 'python' | 'load' | 'simulator' = 'overall';
  triggerType: 'docker' | 'host' = 'docker'; // Rule 8: toggleable

  // Active Session State
  activeSessionId: string | null = null;
  activeLogs = '';
  activeStatus = 'IDLE'; // 'IDLE', 'QUEUED', 'RUNNING', 'COMPLETED', 'FAILED', 'CANCELLED'
  activeDurationMs: number | null = null;
  activeError = '';
  activeSummary: TestSummary | null = null;

  // History State
  sessions: TestSession[] = [];
  
  private socket: Socket | null = null;
  private heartbeatInterval: ReturnType<typeof setInterval> | null = null;

  private chartInstance: Chart | null = null;
  private overallChartInstance: Chart | null = null;

  @ViewChild('unitChart') unitChartCanvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('overallChart') overallChartCanvas!: ElementRef<HTMLCanvasElement>;

  ngOnInit() {
    this.initSocket();
    this.loadSessions();
  }

  private initSocket() {
    this.socket = io(this.apiUrl);

    this.socket.on('connect', () => {
      console.log('[Socket] Connected to backend service');
      if (this.activeSessionId) {
        this.joinSessionRoom(this.activeSessionId);
      }
    });

    this.socket.on('log-history', (data: string) => {
      this.activeLogs = data;
    });

    this.socket.on('log', (chunk: string) => {
      this.activeLogs += chunk;
    });

    this.socket.on('status', (data: any) => {
      this.activeStatus = data.status;
      if (data.durationMs) this.activeDurationMs = data.durationMs;
      if (data.error) this.activeError = data.error;
      if (data.summary) this.activeSummary = data.summary;
      this.loadSessions();
    });

    // WebSockets heartbeats to prevent silent disconnections
    this.heartbeatInterval = setInterval(() => {
      if (this.socket && this.socket.connected) {
        this.socket.emit('ping');
      }
    }, 25000);
  }

  private joinSessionRoom(sessionId: string) {
    if (this.socket && this.socket.connected) {
      this.socket.emit('join-session', sessionId);
    }
  }

  async loadSessions() {
    try {
      const res = await fetch(`${this.apiUrl}/api/test/sessions`);
      if (res.ok) {
        this.sessions = await res.json();
        
        // Ensure chart updates if viewing overall or active session
        if (this.activeSuite === 'overall') {
          setTimeout(() => this.renderOverallChart(), 100);
        } else if (this.activeSessionId && (this.activeStatus === 'COMPLETED' || this.activeStatus === 'FAILED')) {
          this.loadReportData(this.activeSessionId);
        }
      }
    } catch (err) {
      console.error('[API] Failed to fetch session history:', err);
    }
  }

  async loadReportData(sessionId: string) {
    try {
      const res = await fetch(`${this.apiUrl}/api/test/sessions/${sessionId}/report-data`);
      if (res.ok) {
        const data = await res.json();
        setTimeout(() => this.renderUnitChart(data.testCases || []), 100);
      }
    } catch (err) {
      console.error('Failed to load report data', err);
    }
  }

  async triggerTestRun() {
    this.activeLogs = '';
    this.activeError = '';
    this.activeDurationMs = null;
    this.activeSummary = null;
    this.activeStatus = 'QUEUED';

    try {
      const res = await fetch(`${this.apiUrl}/api/test/run`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          suiteType: this.activeSuite,
          triggerType: this.triggerType
        })
      });

      if (!res.ok) {
        throw new Error('Failed to schedule job');
      }

      const data = await res.json();
      this.activeSessionId = data.sessionId;
      this.activeStatus = data.status;

      console.log('[API] Session started:', this.activeSessionId);
      this.joinSessionRoom(this.activeSessionId!);
      this.loadSessions();

    } catch (err: any) {
      this.activeStatus = 'FAILED';
      this.activeError = err.message;
      this.activeLogs = `[API Error] Trigger failed: ${err.message}`;
    }
  }

  async cancelActiveRun() {
    if (!this.activeSessionId) return;

    try {
      const res = await fetch(`${this.apiUrl}/api/test/cancel`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sessionId: this.activeSessionId })
      });

      if (res.ok) {
        console.log('[API] Cancel requested successfully');
        this.activeStatus = 'CANCELLED';
        this.loadSessions();
      }
    } catch (err) {
      console.error('[API] Failed to cancel test:', err);
    }
  }

  selectSuite(suite: 'overall' | 'csharp' | 'python' | 'load' | 'simulator') {
    this.activeSuite = suite;
    
    // Clear terminal log display if switching to a fresh suite
    if (suite === 'overall') {
      setTimeout(() => this.renderOverallChart(), 100);
    } else {
      if (this.activeStatus === 'IDLE' || ['COMPLETED', 'FAILED', 'CANCELLED'].includes(this.activeStatus)) {
        this.activeLogs = '';
        this.activeStatus = 'IDLE';
        this.activeSessionId = null;
        this.activeDurationMs = null;
        this.activeError = '';
        this.activeSummary = null;
      }
    }
  }
  viewHistoricalSession(session: TestSession) {
    if (this.activeSessionId) {
      // Leave previous socket room
      this.socket?.emit('leave-session', this.activeSessionId);
    }

    this.activeSuite = session.testSuite as any;
    this.triggerType = session.triggerType;
    this.activeSessionId = session.sessionId;
    this.activeStatus = session.status;
    this.activeDurationMs = session.durationMs || null;
    this.activeError = session.error || '';
    this.activeSummary = session.summary || null;
    this.activeLogs = '';

    this.joinSessionRoom(session.sessionId);

    if (this.activeStatus === 'COMPLETED' || this.activeStatus === 'FAILED') {
      this.loadReportData(session.sessionId);
    }
  }

  downloadLogs(sessionId: string) {
    window.open(`${this.apiUrl}/api/test/sessions/${sessionId}/logs`);
  }

  downloadReport(sessionId: string) {
    window.open(`${this.apiUrl}/api/test/sessions/${sessionId}/report`);
  }

  getSuiteName(suiteKey: string): string {
    switch (suiteKey) {
      case 'csharp': return 'Backend Integration (C#)';
      case 'python': return 'AI Engine Validation (Python)';
      case 'load': return 'Load & Stress Testing (Node.js)';
      case 'simulator': return 'E2E Simulator (Node.js)';
      default: return suiteKey;
    }
  }

  get dashboardStats() {
    const completed = this.sessions.filter(session => session.summary);
    const totals = completed.reduce(
      (acc, session) => {
        acc.total += session.summary?.total || 0;
        acc.passed += session.summary?.passed || 0;
        acc.failed += session.summary?.failed || 0;
        acc.durationMs += session.durationMs || session.summary?.durationMs || 0;
        return acc;
      },
      { total: 0, passed: 0, failed: 0, durationMs: 0 }
    );

    return {
      runs: completed.length,
      total: totals.total,
      passed: totals.passed,
      failed: totals.failed,
      successRate: totals.total ? Math.round((totals.passed / totals.total) * 100) : 0,
      avgDurationMs: completed.length ? Math.round(totals.durationMs / completed.length) : 0,
    };
  }

  get chartSessions() {
    return this.sessions
      .filter(session => session.summary)
      .slice(0, 8)
      .reverse();
  }

  getBarWidth(value: number, total: number): number {
    if (!total) return 0;
    return Math.max(4, Math.round((value / total) * 100));
  }

  renderUnitChart(testCases: TestCase[]) {
    if (!this.unitChartCanvas?.nativeElement) return;
    
    if (this.chartInstance) {
      this.chartInstance.destroy();
    }

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

  renderOverallChart() {
    if (!this.overallChartCanvas?.nativeElement) return;
    
    if (this.overallChartInstance) {
      this.overallChartInstance.destroy();
    }

    // Aggregate success rate by suite
    const suites = ['csharp', 'python', 'load', 'simulator'];
    const suiteNames = ['Backend C#', 'AI Python', 'Load Test', 'Simulator'];
    const rates = suites.map(suite => {
      const completed = this.sessions.filter(s => s.testSuite === suite && s.summary);
      if (completed.length === 0) return 0;
      const latest = completed[completed.length - 1]; // get most recent
      return latest.summary?.successRate || 0;
    });

    this.overallChartInstance = new Chart(this.overallChartCanvas.nativeElement, {
      type: 'polarArea',
      data: {
        labels: suiteNames,
        datasets: [{
          data: rates,
          backgroundColor: [
            'rgba(99, 102, 241, 0.7)',
            'rgba(234, 179, 8, 0.7)',
            'rgba(236, 72, 153, 0.7)',
            'rgba(16, 185, 129, 0.7)'
          ]
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { position: 'right', labels: { color: '#fff' } }
        },
        scales: {
          r: {
            ticks: { backdropColor: 'transparent', color: '#ccc' },
            grid: { color: 'rgba(255,255,255,0.1)' }
          }
        }
      }
    });
  }

  ngOnDestroy() {
    if (this.heartbeatInterval) {
      clearInterval(this.heartbeatInterval);
      this.heartbeatInterval = null;
    }
    if (this.socket) {
      this.socket.disconnect();
    }
  }
}
