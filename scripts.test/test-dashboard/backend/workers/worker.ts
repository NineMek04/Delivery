import { Worker, Job } from 'bullmq';
import { connection, QUEUE_NAME } from '../services/queue';
import { ArtifactService } from '../services/artifact-service';
import { runTestInDocker, cancelDockerTest } from '../services/docker-execution';
import Redis from 'ioredis';
import dotenv from 'dotenv';
import { parseStringPromise } from 'xml2js';

dotenv.config();

const REDIS_HOST = process.env.REDIS_HOST || 'localhost';
const REDIS_PORT = parseInt(process.env.REDIS_PORT || '6379', 10);

// Redis client for publishing real-time events to the API Server
const pubClient = new Redis({
  host: REDIS_HOST,
  port: REDIS_PORT,
});

function parseTestSummary(logText: string, suiteType: string, status: string, durationMs: number) {
  let passed = 0;
  let failed = 0;
  let skipped = 0;
  let total = 0;

  const dotnetMatch = logText.match(/Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)/i);
  if (dotnetMatch) {
    failed = Number(dotnetMatch[1]);
    passed = Number(dotnetMatch[2]);
    skipped = Number(dotnetMatch[3]);
    total = Number(dotnetMatch[4]);
  } else {
    const pytestSummary = [...logText.matchAll(/(\d+)\s+(passed|failed|skipped|error|errors)/gi)];
    for (const match of pytestSummary) {
      const count = Number(match[1]);
      const kind = match[2].toLowerCase();
      if (kind === 'passed') passed += count;
      if (kind === 'failed' || kind === 'error' || kind === 'errors') failed += count;
      if (kind === 'skipped') skipped += count;
    }
    total = passed + failed + skipped;
  }

  if (total === 0) {
    total = 1;
    passed = status === 'COMPLETED' ? 1 : 0;
    failed = status === 'COMPLETED' ? 0 : 1;
  }

  return {
    suiteType,
    status,
    total,
    passed,
    failed,
    skipped,
    successRate: total > 0 ? Math.round((passed / total) * 100) : 0,
    durationMs,
    generatedAt: new Date().toISOString(),
  };
}

export interface TestCase {
  name: string;
  location: string;
  inputs: string;
  status: 'PASS' | 'FAIL' | 'SKIPPED';
  durationMs: number;
  error?: string;
}

async function parseDetailedReport(reportData: string | null, suiteType: string): Promise<TestCase[]> {
  const testCases: TestCase[] = [];
  if (!reportData) return testCases;

  try {
    if (suiteType === 'csharp' && reportData.includes('<TestRun')) {
      const result = await parseStringPromise(reportData);
      const results = result.TestRun?.Results?.[0]?.UnitTestResult || [];
      for (const res of results) {
        const name = res.$.testName || 'Unknown Test';
        const outcome = res.$.outcome === 'Passed' ? 'PASS' : res.$.outcome === 'Failed' ? 'FAIL' : 'SKIPPED';
        const durationStr = res.$.duration || '00:00:00.000'; // HH:MM:SS.fff
        let durationMs = 0;
        const timeParts = durationStr.split(':');
        if (timeParts.length === 3) {
          durationMs = (Number(timeParts[0]) * 3600 + Number(timeParts[1]) * 60 + parseFloat(timeParts[2])) * 1000;
        }
        
        let errorMsg = undefined;
        if (outcome === 'FAIL') {
          errorMsg = res.Output?.[0]?.ErrorInfo?.[0]?.Message?.[0] || 'Unknown Error';
        }

        testCases.push({
          name,
          location: 'BackendApi.IntegrationTests',
          inputs: 'N/A', // MSTest/XUnit TRX doesn't natively serialize complex input args easily in summary
          status: outcome,
          durationMs: Math.round(durationMs),
          error: errorMsg
        });
      }
    } else {
      // Parse JSON for Python, Load, Simulator
      const parsed = JSON.parse(reportData);
      
      if (suiteType === 'python') {
        // pytest-json-report format
        const tests = parsed.tests || [];
        for (const t of tests) {
          const outcome = t.outcome === 'passed' ? 'PASS' : t.outcome === 'failed' ? 'FAIL' : 'SKIPPED';
          let errorMsg = undefined;
          if (outcome === 'FAIL') {
            errorMsg = t.call?.crash?.message || 'Error';
          }
          const setupDur = t.setup?.duration || 0;
          const callDur = t.call?.duration || 0;
          testCases.push({
            name: t.nodeid.split('::').pop() || 'Unknown',
            location: t.nodeid.split('::')[0] || 'Unknown File',
            inputs: 'N/A',
            status: outcome,
            durationMs: Math.round((setupDur + callDur) * 1000),
            error: errorMsg
          });
        }
      } else {
        // Custom format for Load and Simulator
        if (parsed.testCases && Array.isArray(parsed.testCases)) {
          testCases.push(...parsed.testCases);
        }
      }
    }
  } catch (err) {
    console.error('[Worker] Failed to parse detailed report data', err);
  }
  
  return testCases;
}

const worker = new Worker(
  QUEUE_NAME,
  async (job: Job) => {
    const { sessionId, suiteType } = job.data;
    console.log(`[Worker] Starting job ${job.id} for session ${sessionId} (${suiteType})`);

    const startTime = Date.now();
    let collectedLogs = '';
    ArtifactService.updateSession(sessionId, { status: 'RUNNING' });
    
    // Publish initial status update
    await pubClient.publish(`session:${sessionId}:status`, JSON.stringify({ status: 'RUNNING' }));

    const onLog = async (chunk: string) => {
      collectedLogs += chunk;
      ArtifactService.appendLog(sessionId, chunk);
      await pubClient.publish(`session:${sessionId}:logs`, chunk);
    };

    try {
      const reportData = await runTestInDocker(sessionId, suiteType, onLog);
      
      const durationMs = Date.now() - startTime;
      let summary = parseTestSummary(collectedLogs, suiteType, 'COMPLETED', durationMs);
      const testCases = await parseDetailedReport(reportData, suiteType);
      
      // Override summary if we successfully extracted structured data
      if (testCases.length > 0) {
         const passed = testCases.filter(t => t.status === 'PASS').length;
         const failed = testCases.filter(t => t.status === 'FAIL').length;
         const skipped = testCases.filter(t => t.status === 'SKIPPED').length;
         const total = testCases.length;
         summary = {
            suiteType,
            status: 'COMPLETED',
            total,
            passed,
            failed,
            skipped,
            successRate: total > 0 ? Math.round((passed / total) * 100) : 0,
            durationMs,
            generatedAt: new Date().toISOString()
         };
      }

      ArtifactService.saveReport(sessionId, {
        sessionId,
        suiteType,
        status: 'COMPLETED',
        summary,
        testCases,
        completedAt: new Date().toISOString(),
      });

      ArtifactService.updateSession(sessionId, {
        status: 'COMPLETED',
        completedAt: new Date().toISOString(),
        durationMs,
      });

      await pubClient.publish(
        `session:${sessionId}:status`,
        JSON.stringify({ status: 'COMPLETED', durationMs, summary })
      );
      console.log(`[Worker] Job ${job.id} for session ${sessionId} completed successfully in ${durationMs}ms`);

    } catch (error: any) {
      const durationMs = Date.now() - startTime;
      
      // Determine if the session was cancelled
      const session = ArtifactService.getSession(sessionId);
      const isCancelled = session?.status === 'CANCELLED';
      const finalStatus = isCancelled ? 'CANCELLED' : 'FAILED';
      const summary = parseTestSummary(collectedLogs, suiteType, finalStatus, durationMs);
      
      ArtifactService.saveReport(sessionId, {
        sessionId,
        suiteType,
        status: finalStatus,
        summary,
        testCases: [], // Failed or cancelled tests might not yield report files easily
        error: error.message,
        completedAt: new Date().toISOString(),
      });

      ArtifactService.updateSession(sessionId, {
        status: finalStatus,
        completedAt: new Date().toISOString(),
        durationMs,
        error: error.message,
      });

      await pubClient.publish(
        `session:${sessionId}:status`,
        JSON.stringify({ status: finalStatus, durationMs, error: error.message, summary })
      );
      console.error(`[Worker] Job ${job.id} for session ${sessionId} failed/cancelled: ${error.message}`);
      throw error; // Re-throw to let BullMQ mark job as failed
    }
  },
  {
    connection: connection as any,
    concurrency: 1, // Run 1 heavy container execution at a time to prevent resource exhaustion
  }
);

worker.on('active', (job) => {
  console.log(`[Worker] Job ${job.id} is now active`);
});

worker.on('completed', (job) => {
  console.log(`[Worker] Job ${job.id} has completed`);
});

worker.on('failed', (job, err) => {
  console.error(`[Worker] Job ${job?.id} failed: ${err.message}`);
});

console.log('[Worker] Worker node successfully listening to BullMQ queue...');
