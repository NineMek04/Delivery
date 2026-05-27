import fs from 'fs';
import path from 'path';
import { v4 as uuidv4 } from 'uuid';

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

const ARTIFACTS_DIR = process.env.ARTIFACTS_DIR || path.join(process.cwd(), 'artifacts');

if (!fs.existsSync(ARTIFACTS_DIR)) {
  fs.mkdirSync(ARTIFACTS_DIR, { recursive: true });
}

const SESSIONS_DB_FILE = path.join(ARTIFACTS_DIR, 'sessions.json');

function readSessions(): Record<string, TestSession> {
  if (!fs.existsSync(SESSIONS_DB_FILE)) {
    return {};
  }
  try {
    const data = fs.readFileSync(SESSIONS_DB_FILE, 'utf-8');
    return JSON.parse(data);
  } catch (error) {
    console.error('[ArtifactService] Failed to read sessions JSON:', error);
    return {};
  }
}

function writeSessions(sessions: Record<string, TestSession>) {
  try {
    fs.writeFileSync(SESSIONS_DB_FILE, JSON.stringify(sessions, null, 2), 'utf-8');
  } catch (error) {
    console.error('[ArtifactService] Failed to write sessions JSON:', error);
  }
}

export const ArtifactService = {
  createSession(testSuite: string, triggerType: 'docker' | 'host'): TestSession {
    const sessionId = uuidv4();
    const sessionDir = path.join(ARTIFACTS_DIR, sessionId);
    fs.mkdirSync(sessionDir, { recursive: true });
    const logPath = path.join(sessionDir, 'execution.log');
    fs.writeFileSync(logPath, '', 'utf-8');

    const session: TestSession = {
      sessionId,
      testSuite,
      status: 'CREATED',
      createdAt: new Date().toISOString(),
      triggerType,
      logFile: path.join(sessionId, 'execution.log'),
    };

    const sessions = readSessions();
    sessions[sessionId] = session;
    writeSessions(sessions);

    return session;
  },

  updateSession(sessionId: string, updates: Partial<TestSession>): TestSession | null {
    const sessions = readSessions();
    if (!sessions[sessionId]) return null;

    sessions[sessionId] = { ...sessions[sessionId], ...updates };
    writeSessions(sessions);
    return sessions[sessionId];
  },

  getSession(sessionId: string): TestSession | null {
    const sessions = readSessions();
    return sessions[sessionId] || null;
  },

  getAllSessions(): TestSession[] {
    const sessions = readSessions();
    return Object.values(sessions).sort(
      (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
    );
  },

  appendLog(sessionId: string, data: string) {
    const logPath = path.join(ARTIFACTS_DIR, sessionId, 'execution.log');
    fs.appendFileSync(logPath, data, 'utf-8');
  },

  getLogPath(sessionId: string): string {
    return path.join(ARTIFACTS_DIR, sessionId, 'execution.log');
  },

  saveReport(sessionId: string, reportData: any) {
    const reportPath = path.join(ARTIFACTS_DIR, sessionId, 'report.json');
    fs.writeFileSync(reportPath, JSON.stringify(reportData, null, 2), 'utf-8');
    this.updateSession(sessionId, {
      reportFile: path.join(sessionId, 'report.json'),
      summary: reportData.summary,
    });
  },

  getReportPath(sessionId: string): string | null {
    const reportPath = path.join(ARTIFACTS_DIR, sessionId, 'report.json');
    if (fs.existsSync(reportPath)) {
      return reportPath;
    }
    return null;
  }
};
