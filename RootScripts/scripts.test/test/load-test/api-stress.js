/**
 * api-stress.js — HTTP API load testing
 *
 * Usage:
 *   node api-stress.js [--concurrent 10] [--requests 200] [--endpoint telemetry/gps/batch]
 *
 * Environment:
 *   API_URL — Backend URL (default: http://localhost:5000)
 *
 * Note: Simulates batch HTTP GPS telemetry for Rider Apps.
 */

const axios = require("axios");

const API_URL = process.env.API_URL || "http://localhost:5000";

const args = process.argv.slice(2);
function getArg(name, defaultValue) {
  const idx = args.indexOf(`--${name}`);
  return idx >= 0 && args[idx + 1] ? args[idx + 1] : defaultValue;
}

const CONCURRENT = parseInt(getArg("concurrent", "200"), 10);
const TOTAL_REQUESTS = parseInt(getArg("requests", "5000"), 10);
const ENDPOINT = getArg("endpoint", "telemetry/gps/batch");

const stats = {
  success: 0,
  errors: 0,
  latencies: [],
  statusCodes: {},
};

async function getRiderToken() {
  const email = `api_stress_${Date.now()}_${Math.random().toString(36).substring(7)}@test.com`;
  try {
    const res = await axios.post(`${API_URL}/api/v1/auth/register`, {
      email,
      password: "StressTest123!",
      fullName: "API Stress Rider",
      role: "Rider",
    });
    return res.data?.value?.accessToken;
  } catch (err) {
    console.error("Failed to get rider token:", err.message);
    process.exit(1);
  }
}

async function makeRequest(token, index) {
  const start = Date.now();
  // Ensure the URL matches the .NET route exactly (api/telemetry vs api/v1/telemetry)
  const isTelemetry = ENDPOINT.includes("telemetry");
  const url = isTelemetry ? `${API_URL}/api/${ENDPOINT}` : `${API_URL}/api/v1/${ENDPOINT}?page=1&pageSize=10`;

  // Create a batch payload of 5 points
  const payload = isTelemetry ? Array.from({ length: 5 }).map((_, i) => ({
    Latitude: 13.7 + (Math.random() * 0.01),
    Longitude: 100.5 + (Math.random() * 0.01),
    Accuracy: 10.0,
    Timestamp: new Date().toISOString()
  })) : null;

  try {
    const reqPromise = isTelemetry
      ? axios.post(url, payload, { headers: { Authorization: `Bearer ${token}` }, timeout: 10000 })
      : axios.get(url, { headers: { Authorization: `Bearer ${token}` }, timeout: 10000 });
      
    const res = await reqPromise;
    const latency = Date.now() - start;
    stats.latencies.push(latency);
    stats.success++;
    stats.statusCodes[res.status] = (stats.statusCodes[res.status] || 0) + 1;
  } catch (err) {
    const latency = Date.now() - start;
    stats.latencies.push(latency);
    stats.errors++;
    const code = err.response?.status || "TIMEOUT";
    stats.statusCodes[code] = (stats.statusCodes[code] || 0) + 1;
  }
}

function percentile(arr, p) {
  const sorted = [...arr].sort((a, b) => a - b);
  const idx = Math.ceil((p / 100) * sorted.length) - 1;
  return sorted[Math.max(0, idx)];
}

async function runBatch(token, batchSize) {
  const promises = [];
  for (let i = 0; i < batchSize; i++) {
    promises.push(makeRequest(token, i));
  }
  await Promise.allSettled(promises);
}

async function main() {
  console.log("═══════════════════════════════════════════════");
  console.log("  HTTP API Stress Test");
  console.log(`  Target: ${ENDPOINT.includes("telemetry") ? `${API_URL}/api/${ENDPOINT}` : `${API_URL}/api/v1/${ENDPOINT}`}`);
  console.log(`  Concurrent: ${CONCURRENT}`);
  console.log(`  Total Requests: ${TOTAL_REQUESTS}`);
  console.log("═══════════════════════════════════════════════\n");

  const token = await getRiderToken();
  console.log("  Rider token acquired.\n");

  const startTime = Date.now();
  let sent = 0;

  while (sent < TOTAL_REQUESTS) {
    const batchSize = Math.min(CONCURRENT, TOTAL_REQUESTS - sent);
    await runBatch(token, batchSize);
    sent += batchSize;
    process.stdout.write(
      `\r  Progress: ${sent}/${TOTAL_REQUESTS} (${stats.success} ok, ${stats.errors} err)`
    );
  }

  const totalTime = ((Date.now() - startTime) / 1000).toFixed(2);
  const rps = (TOTAL_REQUESTS / totalTime).toFixed(1);

  console.log("\n\n═══════════════════════════════════════════════");
  console.log("  RESULTS");
  console.log("═══════════════════════════════════════════════");
  console.log(`  Total Time:    ${totalTime}s`);
  console.log(`  Requests:      ${TOTAL_REQUESTS}`);
  console.log(`  RPS:           ${rps}`);
  console.log(`  Success:       ${stats.success}`);
  console.log(`  Errors:        ${stats.errors}`);
  console.log(`  Avg Latency:   ${(stats.latencies.reduce((a, b) => a + b, 0) / stats.latencies.length).toFixed(0)}ms`);
  console.log(`  p50 Latency:   ${percentile(stats.latencies, 50)}ms`);
  console.log(`  p95 Latency:   ${percentile(stats.latencies, 95)}ms`);
  console.log(`  p99 Latency:   ${percentile(stats.latencies, 99)}ms`);
  console.log(`  Status Codes:  ${JSON.stringify(stats.statusCodes)}`);
  console.log("═══════════════════════════════════════════════");
}

main().catch(console.error);
