export interface LoadTestMetrics {
  rps: number;
  p95LatencyMs: number;
  errorRate: number;
  totalRequests: number;
  durationMs: number;
  concurrentUsers: number;
  timestamp: string;
}

export class LogParserService {
  static parseLoadTestMetrics(logs: string): LoadTestMetrics | null {
    // 1. Regex for breaking-point-stress.js
    const rpsMatch = logs.match(/RPS achieved:\s*~?([\d,]+)/);
    
    // 2. Regex for massive-batch-dispatch.js (e.g. Achieved 45,000 msg/sec)
    const mbRpsMatch = logs.match(/(?:Achieved|throughput):\s*~?([\d,]+)\s*(?:msg\/sec|RPS)/i);
    
    let rps = 0;
    if (rpsMatch) rps = Number(rpsMatch[1].replace(/,/g, ''));
    else if (mbRpsMatch) rps = Number(mbRpsMatch[1].replace(/,/g, ''));

    const requestsMatch = logs.match(/(?:Requests|Total dispatched):\s*([\d,]+)/);
    const totalRequests = requestsMatch ? Number(requestsMatch[1].replace(/,/g, '')) : 0;

    const errorRateMatch = logs.match(/\((\d+\.\d+)%\s*Error Rate\)/) || logs.match(/Error Rate:\s*(\d+\.\d+)%/);
    const errorRate = errorRateMatch ? parseFloat(errorRateMatch[1]) : 0;

    const latencyMatch = logs.match(/p95:\s*([\d\.]+)ms/) || logs.match(/p95\s*Latency:\s*([\d\.]+)ms/i);
    const p95LatencyMs = latencyMatch ? parseFloat(latencyMatch[1]) : 0;

    if (rps > 0 || totalRequests > 0) {
      return {
        rps,
        p95LatencyMs,
        errorRate,
        totalRequests,
        durationMs: 0, // Will be enriched by worker
        concurrentUsers: 0,
        timestamp: new Date().toISOString()
      };
    }
    return null;
  }
}
