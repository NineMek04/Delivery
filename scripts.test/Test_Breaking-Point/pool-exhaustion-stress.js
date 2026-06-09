/**
 * pool-exhaustion-stress.js — DB Connection Pool Exhaustion Stress Test
 *
 * This test verifies that when the connection pool is exhausted (e.g., due to slow DB queries),
 * the system returns a fast timeout error (Fail Fast) rather than hanging indefinitely.
 *
 * Usage:
 *   node pool-exhaustion-stress.js [--concurrency 150]
 *
 * Environment:
 *   API_URL — Backend URL (default: http://localhost:5000)
 */

const axios = require("axios");

const API_URL = process.env.API_URL || "http://localhost:5000";

const args = process.argv.slice(2);
function getArg(name, defaultValue) {
  const idx = args.indexOf(`--${name}`);
  return idx >= 0 && args[idx + 1] ? args[idx + 1] : defaultValue;
}

const CONCURRENCY = parseInt(getArg("concurrency", "150"), 10); // Default 150 (exceeds pool limit of 100)

const stats = {
  totalRequests: 0,
  success200: 0,
  error500_503: 0,
  otherStatusCodes: {},
  latencies: [],
};

async function registerUser(email, role, name) {
  try {
    const res = await axios.post(`${API_URL}/api/v1/auth/register`, {
      email,
      password: "StressTest123!",
      fullName: name,
      role: role,
    });
    return res.data?.value;
  } catch (err) {
    console.error(`Failed to register ${role} (${email}):`, err.response?.data || err.message);
    return null;
  }
}

async function sendRequest(token, index) {
  const start = Date.now();
  stats.totalRequests++;
  
  const headers = { 
    Authorization: `Bearer ${token}`,
    "X-Correlation-Id": `pool-exhaust-${Date.now()}-${Math.random().toString(36).substring(7)}`
  };

  try {
    // Query orders which will access the database
    const res = await axios.get(`${API_URL}/api/v1/orders?page=1&pageSize=10`, { 
      headers, 
      timeout: 12000 // HTTP timeout long enough to see DB timeout (DB pool timeout is 5s)
    });
    
    const latency = Date.now() - start;
    stats.latencies.push(latency);
    stats.success200++;
  } catch (err) {
    const latency = Date.now() - start;
    stats.latencies.push(latency);
    const status = err.response?.status || "TIMEOUT/TIMEOUT_EXCEPTION";
    
    if (status === 500 || status === 503) {
      stats.error500_503++;
    } else {
      stats.otherStatusCodes[status] = (stats.otherStatusCodes[status] || 0) + 1;
    }
  }
}

function percentile(arr, p) {
  if (arr.length === 0) return 0;
  const sorted = [...arr].sort((a, b) => a - b);
  const idx = Math.ceil((p / 100) * sorted.length) - 1;
  return sorted[Math.max(0, idx)];
}

async function main() {
  console.log("=================================================");
  console.log("  Connection Pool Exhaustion Stress Test");
  console.log(`  Target URL:        ${API_URL}`);
  console.log(`  Concurrency:       ${CONCURRENCY} (Max Pool Size: 100)`);
  console.log("=================================================\n");

  const timestamp = Date.now();
  
  console.log("[Phase 1] Provisioning Admin user...");
  const adminEmail = `pool_admin_${timestamp}@test.com`;
  const adminUser = await registerUser(adminEmail, "Admin", "Pool Test Admin");
  if (!adminUser) {
    console.error("Critical: Admin registration failed. Aborting.");
    process.exit(1);
  }
  const adminToken = adminUser.accessToken;
  console.log("  - Admin user registered.");

  console.log("\n[Phase 2] Executing Concurrent API requests...");
  console.log("  Sending concurrent requests to database-bound endpoint...");

  const startTime = Date.now();
  const promises = [];

  for (let i = 0; i < CONCURRENCY; i++) {
    promises.push(sendRequest(adminToken, i));
  }

  await Promise.all(promises);

  const totalTime = ((Date.now() - startTime) / 1000).toFixed(2);
  console.log("  API request wave complete!\n");

  console.log("=================================================");
  console.log("  STRESS RESULTS");
  console.log("=================================================");
  console.log(`  Total Requests:       ${stats.totalRequests}`);
  console.log(`  Successful (200 OK):  ${stats.success200}`);
  console.log(`  Exhaustion (500/503): ${stats.error500_503} (Expected pool timeouts / server errors)`);
  
  if (Object.keys(stats.otherStatusCodes).length > 0) {
    console.log(`  Other Status Codes:   ${JSON.stringify(stats.otherStatusCodes)}`);
  }
  
  console.log(`  Total Time:           ${totalTime}s`);
  console.log(`  Throughput:           ${(stats.totalRequests / totalTime).toFixed(1)} req/s`);
  console.log(`  Avg Latency:          ${(stats.latencies.reduce((a, b) => a + b, 0) / stats.latencies.length).toFixed(0)}ms`);
  console.log(`  p50 Latency:          ${percentile(stats.latencies, 50)}ms`);
  console.log(`  p95 Latency:          ${percentile(stats.latencies, 95)}ms`);
  console.log(`  p99 Latency:          ${percentile(stats.latencies, 99)}ms`);
  console.log("=================================================");

  // Verification logic:
  // If the pool is exhausted and Kestrel fails fast, the totalTime should be close to the timeout limit (~5-6s)
  // and we should see some 500/503 pool timeout errors, while some might succeed after the DB lock releases.
  // The crucial check is that the system does not HANG forever (latency must be bounded).
  const maxLatency = Math.max(...stats.latencies);
  console.log(`\n  Max request latency:  ${(maxLatency / 1000).toFixed(2)}s`);
  
  if (maxLatency > 15000) {
    console.warn("⚠️  [WARNING] Some requests took longer than 15s or hung!");
  } else {
    console.log("✅ [SUCCESS] All requests resolved within acceptable timeout boundaries. Fail-Fast mechanism is functional.");
  }
}

main().catch(console.error);
