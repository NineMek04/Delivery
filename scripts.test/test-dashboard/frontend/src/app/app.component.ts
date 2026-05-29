import { Component, OnInit, OnDestroy, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { io, Socket } from 'socket.io-client';
import { LiveTerminalComponent } from './components/live-terminal/live-terminal.component';
import { SimulatorHostComponent } from './components/simulator-host/simulator-host.component';
import { CaseDetailModalComponent } from './components/case-detail-modal/case-detail-modal.component';
import { SuiteDetailsComponent } from './components/suite-details/suite-details.component';
import { OverallOverviewComponent } from './components/overall-overview/overall-overview.component';
import { TestCase, TestSession, TestSummary } from './test-dashboard.model';
import { INITIAL_CSHARP_CASES, INITIAL_PYTHON_CASES, INITIAL_LOAD_CASES, INITIAL_SIMULATOR_CASES } from './test-dashboard.config';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule, 
    FormsModule, 
    LiveTerminalComponent, 
    SimulatorHostComponent,
    CaseDetailModalComponent,
    SuiteDetailsComponent,
    OverallOverviewComponent
  ],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent implements OnInit, OnDestroy {
  title = 'Testing Dashboard';
  apiUrl = 'http://localhost:3001';

  // Config State
  activeSuite: 'overall' | 'csharp' | 'python' | 'load' | 'simulator' = 'overall';
  triggerType: 'docker' | 'host' = 'docker';

  // Search & Filter State
  searchQuery = '';
  activeStatusFilter: 'all' | 'PASS' | 'FAIL' | 'SKIPPED' = 'all';
  filteredCases: TestCase[] = [];
  chartType: 'doughnut' | 'bar' = 'doughnut';

  // Active Session State
  activeSessionId: string | null = null;
  activeLogs = '';
  activeStatus = 'IDLE'; // 'IDLE', 'RUNNING', 'COMPLETED', 'FAILED', 'CANCELLED'
  activeDurationMs: number | null = null;
  activeError = '';
  activeSummary: TestSummary | null = null;

  // Real Data Binding State per Suite
  latestCsharpSession: TestSession | null = null;
  latestPythonSession: TestSession | null = null;
  latestLoadSession: TestSession | null = null;
  latestSimulatorSession: TestSession | null = null;

  csharpCases: TestCase[] = [];
  pythonCases: TestCase[] = [];
  loadCases: TestCase[] = [];
  simulatorCases: TestCase[] = [];

  // History State
  sessions: TestSession[] = [];
  
  private socket: Socket | null = null;
  private heartbeatInterval: ReturnType<typeof setInterval> | null = null;

  @ViewChild(SimulatorHostComponent) simulatorHost: SimulatorHostComponent | null = null;

  parsedLogLines = new Set<string>();
  selectedDetailCase: TestCase | null = null;

  parseLiveLogs(chunk: string) {
    if (!chunk) return;
    
    const lines = chunk.split('\n');
    for (const line of lines) {
      const trimmed = line.trim();
      if (!trimmed || this.parsedLogLines.has(trimmed)) continue;

      if (trimmed.includes('>> TEST_CASE_UPDATE')) {
        this.parsedLogLines.add(trimmed);
        const parts = trimmed.split('|');
        if (parts.length >= 5) {
          const name = parts[1].trim();
          const statusStr = parts[2].trim();
          const details = parts[3].trim();
          const inputs = parts[4].trim();

          const tc: TestCase = {
            name,
            location: 'e2e-simulator/simulate-e2e.js',
            inputs,
            status: statusStr === 'PASS' ? 'PASS' : statusStr === 'FAIL' ? 'FAIL' : 'SKIPPED',
            durationMs: 0,
            error: statusStr === 'FAIL' ? details : undefined,
            responseTrace: statusStr === 'PASS' ? details : undefined,
            requestPayload: inputs
          };

          this.updateOrCreateCase(tc);
        }
      } else if (trimmed.includes('>> SHOP_CREATED')) {
        this.parsedLogLines.add(trimmed);
        const parts = trimmed.split('|');
        if (parts.length >= 4) {
          const name = parts[1].trim();
          const lat = parseFloat(parts[2].trim());
          const lng = parseFloat(parts[3].trim());
          if (this.simulatorHost) {
            this.simulatorHost.updateTestTelemetry({ shop: { name, lat, lng } });
          }
        }
      } else if (trimmed.includes('>> ORDER_CREATED')) {
        this.parsedLogLines.add(trimmed);
        const parts = trimmed.split('|');
        if (parts.length >= 6) {
          const orderId = parts[1].trim();
          const dropoffLat = parseFloat(parts[4].trim());
          const dropoffLng = parseFloat(parts[5].trim());
          if (this.simulatorHost) {
            this.simulatorHost.updateTestTelemetry({ dropoff: { lat: dropoffLat, lng: dropoffLng } });
          }
        }
      } else if (trimmed.includes('>> ROUTE_COORDINATES')) {
        this.parsedLogLines.add(trimmed);
        const parts = trimmed.split('|');
        if (parts.length >= 3) {
          const label = parts[1].trim();
          try {
            const coords = JSON.parse(parts[2].trim());
            if (this.simulatorHost) {
              this.simulatorHost.updateTestTelemetry({ route: { label, coords } });
            }
          } catch (e) {
            console.error('Failed to parse route coordinates JSON', e);
          }
        }
      } else if (trimmed.includes('>> ACTIVE_RIDER')) {
        this.parsedLogLines.add(trimmed);
        const parts = trimmed.split('|');
        if (parts.length >= 2) {
          const riderName = parts[1].trim();
          if (this.simulatorHost) {
            this.simulatorHost.updateTestTelemetry({ activeRider: riderName });
          }
        }
      } else if (trimmed.includes('>> RIDER_MAPPING')) {
        this.parsedLogLines.add(trimmed);
        const parts = trimmed.split('|');
        if (parts.length >= 3) {
          const name = parts[1].trim();
          const id = parts[2].trim();
          if (this.simulatorHost) {
            this.simulatorHost.updateTestTelemetry({ riderMapping: { name, id } });
          }
        }
      } else if (trimmed.includes('>> RIDER_GPS')) {
        this.parsedLogLines.add(trimmed);
        const parts = trimmed.split('|');
        if (parts.length >= 6) {
          const id = parts[1].trim();
          const name = parts[2].trim();
          const lat = parseFloat(parts[3].trim());
          const lng = parseFloat(parts[4].trim());
          const status = parts[5].trim() as any;
          if (this.simulatorHost) {
            this.simulatorHost.updateTestTelemetry({
              riderGps: { id, name, lat, lng, status }
            });
          }
        }
      } else if (trimmed.includes('>> SIMULATION_PROGRESS')) {
        this.parsedLogLines.add(trimmed);
        const parts = trimmed.split('|');
        if (parts.length >= 2) {
          const progressVal = parseInt(parts[1].trim(), 10);
          if (!isNaN(progressVal) && this.simulatorHost) {
            this.simulatorHost.updateTestTelemetry({ progress: progressVal });
          }
        }
      }
    }
  }

  private updateOrCreateCase(tc: TestCase) {
    let casesList: TestCase[] = [];
    if (this.activeSuite === 'simulator') {
      casesList = this.simulatorCases;
    } else if (this.activeSuite === 'csharp') {
      casesList = this.csharpCases;
    } else if (this.activeSuite === 'python') {
      casesList = this.pythonCases;
    } else if (this.activeSuite === 'load') {
      casesList = this.loadCases;
    } else {
      return;
    }

    const idx = casesList.findIndex(c => c.name === tc.name);
    if (idx !== -1) {
      casesList[idx] = { ...casesList[idx], ...tc };
    } else {
      casesList.push(tc);
    }

    if (this.activeSuite === 'simulator') {
      this.simulatorCases = [...casesList];
    } else if (this.activeSuite === 'csharp') {
      this.csharpCases = [...casesList];
    } else if (this.activeSuite === 'python') {
      this.pythonCases = [...casesList];
    } else if (this.activeSuite === 'load') {
      this.loadCases = [...casesList];
    }
    this.filterCases();
  }

  prepopulateDetailedCases() {
    this.csharpCases = [...INITIAL_CSHARP_CASES];
    this.pythonCases = [...INITIAL_PYTHON_CASES];
    this.loadCases = [...INITIAL_LOAD_CASES];
    this.simulatorCases = [...INITIAL_SIMULATOR_CASES];
    this.filterCases();
  }

  filterCases() {
    let cases = this.getActiveSuiteCases();
    
    cases = [...cases].sort((a, b) => {
      if (a.status === 'FAIL' && b.status !== 'FAIL') return -1;
      if (a.status !== 'FAIL' && b.status === 'FAIL') return 1;
      return 0;
    });

    if (this.searchQuery) {
      const q = this.searchQuery.toLowerCase();
      cases = cases.filter(c => 
        c.name.toLowerCase().includes(q) || 
        c.location.toLowerCase().includes(q)
      );
    }
    
    if (this.activeStatusFilter !== 'all') {
      cases = cases.filter(c => c.status === this.activeStatusFilter);
    }
    
    this.filteredCases = cases;
  }

  getCasesCount(status: 'all' | 'PASS' | 'FAIL' | 'SKIPPED'): number {
    const cases = this.getActiveSuiteCases();
    if (status === 'all') return cases.length;
    return cases.filter(c => c.status === status).length;
  }

  setStatusFilter(status: 'all' | 'PASS' | 'FAIL' | 'SKIPPED' | string) {
    this.activeStatusFilter = status as any;
    this.filterCases();
  }

  toggleChartType(type: 'doughnut' | 'bar') {
    this.chartType = type;
  }

  ngOnInit() {
    this.prepopulateDetailedCases();
    this.initSocket();
    this.loadSessions();
    this.calculateDashboardStats();
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
      this.parseLiveLogs(data);
    });

    this.socket.on('log', (chunk: string) => {
      this.activeLogs += chunk;
      this.parseLiveLogs(chunk);
    });

    this.socket.on('status', (data: any) => {
      this.activeStatus = data.status;
      if (data.durationMs) this.activeDurationMs = data.durationMs;
      if (data.error) this.activeError = data.error;
      if (data.summary) this.activeSummary = data.summary;
      this.loadSessions();
    });

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

  async fetchReportData(sessionId: string): Promise<TestCase[]> {
    try {
      const res = await fetch(`${this.apiUrl}/api/test/sessions/${sessionId}/report-data`);
      if (res.ok) {
        const data = await res.json();
        return data.testCases || [];
      }
    } catch (err) {
      console.error('[API] Failed to fetch report data for session ' + sessionId, err);
    }
    return [];
  }

  async loadSessions() {
    try {
      const res = await fetch(`${this.apiUrl}/api/test/sessions`);
      if (res.ok) {
        this.sessions = await res.json();
        
        const completed = this.sessions.filter(s => s.status === 'COMPLETED');
        
        const latestCs = completed.find(s => s.testSuite === 'csharp');
        if (latestCs && (!this.latestCsharpSession || this.latestCsharpSession.sessionId !== latestCs.sessionId)) {
          this.latestCsharpSession = latestCs;
          if (this.activeSuite !== 'csharp' || (this.activeStatus !== 'RUNNING' && this.activeStatus !== 'QUEUED')) {
            this.fetchReportData(latestCs.sessionId).then(cases => {
              this.csharpCases = cases;
              this.filterCases();
            });
          }
        } else if (!latestCs) {
          this.latestCsharpSession = null;
          this.csharpCases = [];
        }
        
        const latestPy = completed.find(s => s.testSuite === 'python');
        if (latestPy && (!this.latestPythonSession || this.latestPythonSession.sessionId !== latestPy.sessionId)) {
          this.latestPythonSession = latestPy;
          if (this.activeSuite !== 'python' || (this.activeStatus !== 'RUNNING' && this.activeStatus !== 'QUEUED')) {
            this.fetchReportData(latestPy.sessionId).then(cases => {
              this.pythonCases = cases;
              this.filterCases();
            });
          }
        } else if (!latestPy) {
          this.latestPythonSession = null;
          this.pythonCases = [];
        }
        
        const latestLoad = completed.find(s => s.testSuite === 'load');
        if (latestLoad && (!this.latestLoadSession || this.latestLoadSession.sessionId !== latestLoad.sessionId)) {
          this.latestLoadSession = latestLoad;
          if (this.activeSuite !== 'load' || (this.activeStatus !== 'RUNNING' && this.activeStatus !== 'QUEUED')) {
            this.fetchReportData(latestLoad.sessionId).then(cases => {
              this.loadCases = cases;
              this.filterCases();
            });
          }
        } else if (!latestLoad) {
          this.latestLoadSession = null;
          this.loadCases = [];
        }
        
        const latestSim = completed.find(s => s.testSuite === 'simulator');
        if (latestSim && (!this.latestSimulatorSession || this.latestSimulatorSession.sessionId !== latestSim.sessionId)) {
          this.latestSimulatorSession = latestSim;
          if (this.activeSuite !== 'simulator' || (this.activeStatus !== 'RUNNING' && this.activeStatus !== 'QUEUED')) {
            this.fetchReportData(latestSim.sessionId).then(cases => {
              this.simulatorCases = cases;
              this.filterCases();
            });
          }
        } else if (!latestSim) {
          this.latestSimulatorSession = null;
          this.simulatorCases = [];
        }

        if (this.activeSessionId && (this.activeStatus === 'COMPLETED' || this.activeStatus === 'FAILED')) {
          this.loadReportData(this.activeSessionId);
        }

        this.calculateDashboardStats();
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
        const cases = data.testCases || [];
        
        if (this.activeSuite === 'csharp') {
          this.csharpCases = cases;
        } else if (this.activeSuite === 'python') {
          this.pythonCases = cases;
        } else if (this.activeSuite === 'load') {
          this.loadCases = cases;
        } else if (this.activeSuite === 'simulator') {
          this.simulatorCases = cases;
        }
        
        this.filterCases();
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

    this.parsedLogLines.clear();
    if (this.activeSuite === 'simulator') {
      this.simulatorCases = [];
      if (this.simulatorHost) {
        this.simulatorHost.resetTestTelemetry();
      }
    } else if (this.activeSuite === 'csharp') {
      this.csharpCases = [];
    } else if (this.activeSuite === 'python') {
      this.pythonCases = [];
    } else if (this.activeSuite === 'load') {
      this.loadCases = [];
    }

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

  exportLogs() {
    if (!this.activeLogs) {
      alert('No logs available to export.');
      return;
    }
    const blob = new Blob([this.activeLogs], { type: 'text/plain' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `test-execution-${this.activeSuite}-${Date.now()}.log`;
    a.click();
    window.URL.revokeObjectURL(url);
  }

  toggleRow(row: TestCase) {
    this.selectedDetailCase = row;
  }

  closeDetailModal() {
    this.selectedDetailCase = null;
  }

  selectSuite(suite: 'overall' | 'csharp' | 'python' | 'load' | 'simulator') {
    this.activeSuite = suite;
    this.searchQuery = '';
    this.activeStatusFilter = 'all';
    this.filterCases();
    
    if (suite !== 'overall') {
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

  getSuiteDescription(suiteKey: string): { files: string, check: string } {
    switch (suiteKey) {
      case 'csharp': 
        return {
          files: 'scripts.test/BackendApi.IntegrationTests/',
          check: 'การทำงานของ Business Logic, การยิง Database (ผ่าน Testcontainers), การยิง API Endpoint, และ Message Queues (RabbitMQ/MediatR) ทุกจุดว่าทำงานถูกต้องตามกติกาของ Bounded Context ไหม'
        };
      case 'python': 
        return {
          files: 'scripts.test/ai-engine.tests/',
          check: 'โมเดลคำนวณระยะทางของ Google OR-Tools, การจัดกลุ่มเส้นทาง (VRP), และการจำกัดน้ำหนักและเงื่อนไขของออเดอร์ก่อนจะเสนอให้ Rider'
        };
      case 'load': 
        return {
          files: 'scripts.test/load-test/resilience-stress.js',
          check: 'การทนทานต่อการรุมยิง (Concurrency), การทำ Idempotency ป้องกันกดออเดอร์ซ้ำซ้อน, Lock Contention, ความเสถียรตอนยิง GPS ถล่มเข้า SignalR และการส่งต่อ CorrelationId ใน Logs แบบ 100%'
        };
      case 'simulator': 
        return {
          files: 'scripts.test/e2e-simulator/simulate-e2e.js และ test-flutter-compat.js',
          check: 'ครอบคลุมวงจรชีวิต 1 ออเดอร์เต็มรูปแบบ ตั้งแต่ Admin สร้างออเดอร์ > AI ค้นหา Rider ที่ใกล้ที่สุด > Rider กดรับงาน > Rider เดินทางไปรับ-ส่งของตาม OSRM > รวมถึงมิติ Flutter Compatibility ที่เช็คว่าแอปมือถือสามารถยิงอัปเดตสถานะเข้า SignalR Hub ของเราและตอบกลับได้ถูกต้องหรือไม่'
        };
      case 'overall':
      default:
        return {
          files: 'ทุกโฟลเดอร์ใน scripts.test/',
          check: 'สรุปภาพรวมและอัตราความสำเร็จ (Success Rate) จากทุกส่วนของระบบ (Backend, AI, Load, E2E Simulator)'
        };
    }
  }

  dashboardStats: any = { runs: 0, total: 0, passed: 0, failed: 0, successRate: 0, avgDurationMs: 0 };

  private calculateDashboardStats() {
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

    this.dashboardStats = {
      runs: completed.length,
      total: totals.total,
      passed: totals.passed,
      failed: totals.failed,
      successRate: totals.total ? Math.round((totals.passed / totals.total) * 100) : 0,
      avgDurationMs: completed.length ? Math.round(totals.durationMs / completed.length) : 0,
    };
  }

  getActiveSuiteCases(): TestCase[] {
    switch (this.activeSuite) {
      case 'csharp': return this.csharpCases;
      case 'python': return this.pythonCases;
      case 'load': return this.loadCases;
      case 'simulator': return this.simulatorCases;
      default: return [];
    }
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
