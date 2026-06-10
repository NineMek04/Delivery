/**
 * breaking-point-stress.js — Breaking Point Stress Test (Ramp-up to Failure)
 *
 * Usage:
 *   node breaking-point-stress.js [--endpoint telemetry/gps/batch]
 *
 * Pattern:
 *   - Start at 1,000 RPS
 *   - Increase by 2,000 RPS every 10 seconds
 *   - Stop when error rate (502/503/Socket Hang Up/timeouts) > 10% of requests in a step
 *   - Ceiling: 20,000 RPS (or VUs)
 */

const axios = require("axios");

const API_URL = process.env.API_URL || "http://localhost:5000";

const args = process.argv.slice(2);
function getArg(name, defaultValue) {
  const idx = args.indexOf(`--${name}`);
  return idx >= 0 && args[idx + 1] ? args[idx + 1] : defaultValue;
}

const ENDPOINT = getArg("endpoint", "telemetry/gps/batch");
const url = `${API_URL}/api/${ENDPOINT}`;

async function getRiderToken() {
  const email = `stress_breaking_${Date.now()}_${Math.random().toString(36).substring(7)}@test.com`;
  try {
    const res = await axios.post(`${API_URL}/api/v1/auth/register`, {
      email,
      password: "StressTest123!",
      fullName: "Breaking Stress Rider",
      role: "Rider",
    });
    return res.data?.value?.accessToken;
  } catch (err) {
    console.error("Failed to register and get rider token:", err.message);
    process.exit(1);
  }
}

// Generate a random batch payload
function generatePayload() {
  return Array.from({ length: 5 }).map(() => ({
    Latitude: 13.7 + (Math.random() * 0.01),
    Longitude: 100.5 + (Math.random() * 0.01),
    Accuracy: 10.0,
    Timestamp: new Date().toISOString()
  }));
}

async function main() {
  console.log("===============================================");
  console.log("  Ramp-up to Failure: Breaking Point Stress Test");
  console.log(`  Target URL: ${url}`);
  console.log("===============================================");

  console.log("Authenticating test rider...");
  const token = await getRiderToken();
  console.log("Authentication successful.");

  let currentRps = 1000;
  const rampUpIncrement = 2000;
  const stepDurationMs = 10000; // 10 seconds per step
  const ceilingRps = 20000;

  let totalRequestsSent = 0;
  let totalErrorsCount = 0;

  console.log(`\nStarting step-load stress test...`);
  
  while (currentRps <= ceilingRps) {
    console.log(`\n>>> [STEP] Running at ${currentRps.toLocaleString()} RPS for 10s...`);

    let stepRequests = 0;
    let stepErrors = 0;
    let stepSuccess = 0;
    const latencies = [];

    const intervalMs = 1000 / currentRps;
    const startTime = Date.now();
    const endTime = startTime + stepDurationMs;

    const requestPromises = [];

    // Helper to send request with tracking
    const fireRequest = async () => {
      const start = Date.now();
      stepRequests++;
      totalRequestsSent++;
      try {
        const res = await axios.post(url, generatePayload(), {
          headers: { Authorization: `Bearer ${token}` },
          timeout: 4000
        });
        latencies.push(Date.now() - start);
        stepSuccess++;
      } catch (err) {
        latencies.push(Date.now() - start);
        stepErrors++;
        totalErrorsCount++;
      }
    };

    // Maintain target RPS using a strict tick loop
    while (Date.now() < endTime) {
      const loopStart = Date.now();
      // Fire requests for this millisecond interval
      const batchSize = Math.max(1, Math.round(currentRps / 100)); // distribute in chunks
      for (let i = 0; i < batchSize; i++) {
        requestPromises.push(fireRequest());
      }
      
      const elapsed = Date.now() - loopStart;
      const wait = 10 - elapsed; // target 100 ticks per second (every 10ms)
      if (wait > 0) {
        await new Promise(resolve => setTimeout(resolve, wait));
      }
    }

    // Wait for all requests in this step to finish or timeout
    await Promise.allSettled(requestPromises);

    const stepElapsed = Date.now() - startTime;
    const errorRate = (stepErrors / stepRequests) * 100;
    const avgLatency = latencies.reduce((a, b) => a + b, 0) / (latencies.length || 1);
    
    // Sort for percentiles
    latencies.sort((a, b) => a - b);
    const p95 = latencies[Math.ceil(latencies.length * 0.95) - 1] || 0;

    console.log(`[RESULTS] Step Finished:`);
    console.log(`  - RPS achieved: ~${Math.round(stepRequests / (stepElapsed / 1000)).toLocaleString()}`);
    console.log(`  - Requests: ${stepRequests.toLocaleString()}`);
    console.log(`  - Success: ${stepSuccess.toLocaleString()}`);
    console.log(`  - Errors: ${stepErrors.toLocaleString()} (${errorRate.toFixed(2)}% Error Rate)`);
    console.log(`  - Avg Latency: ${avgLatency.toFixed(1)}ms (p95: ${p95}ms)`);

    // Check breaking point condition: > 10% error rate
    if (errorRate > 10.0) {
      console.log(`\n===============================================`);
      console.log(`🔥 BREAKING POINT HIT AT ${currentRps.toLocaleString()} RPS!`);
      console.log(`Reason: Error rate (${errorRate.toFixed(2)}%) exceeded 10.0% ceiling.`);
      console.log(`Total Requests Sent: ${totalRequestsSent.toLocaleString()}`);
      console.log(`Total Errors Recorded: ${totalErrorsCount.toLocaleString()}`);
      console.log(`System Maximum Tolerable Load: ${(currentRps - rampUpIncrement).toLocaleString()} RPS`);
      console.log(`===============================================`);
      return;
    }

    // Step up
    currentRps += rampUpIncrement;
  }

  console.log(`\n===============================================`);
  console.log(`🏆 TEST COMPLETE: SYSTEM SURVIVED MAX CEILING OF ${ceilingRps.toLocaleString()} RPS!`);
  console.log(`Total Requests Sent: ${totalRequestsSent.toLocaleString()}`);
  console.log(`Total Errors: ${totalErrorsCount.toLocaleString()}`);
  console.log(`===============================================`);
}

main().catch(console.error);
