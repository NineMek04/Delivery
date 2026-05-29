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
  metrics?: any;
  error?: string;
}

export interface TestCase {
  name: string;
  location: string;
  inputs: string;
  status: 'PASS' | 'FAIL' | 'SKIPPED';
  durationMs: number;
  error?: string;
  expanded?: boolean;
  requestPayload?: string;
  responseTrace?: string;
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

export interface LoadTestMetrics {
  rps: number;
  p95LatencyMs: number;
  errorRate: number;
  totalRequests: number;
  durationMs: number;
  concurrentUsers: number;
  timestamp: string;
}

export interface AiBenchmarkMetrics {
  requestsPerSecond: number;
  averageLatencyMs: number;
  queueDepth: number;
  cacheHitRate: number;
  timeoutRate: number;
  timestamp: string;
}

export interface LogEntry {
  timestamp: string;
  level: 'info' | 'warn' | 'error' | 'debug';
  message: string;
  source: string;
  isHighlighted?: boolean;
}

export interface SystemCapacityThreshold {
  maxRps: number;
  maxDbConnections: number;
  maxMemoryMb: number;
}
