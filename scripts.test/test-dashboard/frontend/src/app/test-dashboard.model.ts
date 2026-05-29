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
