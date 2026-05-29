/**
 * chaos-reconnect.js — Chaos Reconnect & Connection Flood Simulator
 *
 * Usage:
 *   node chaos-reconnect.js [--riders 5000] [--duration 60]
 *
 * Goal:
 *   - Fast register and launch N (5,000) riders
 *   - Cycle each rider between connected and disconnected states every 1s
 *   - Verify .NET 8 ThreadPool and TrackingHub connection handling under extreme surge
 */

const signalR = require("@microsoft/signalr");
const axios = require("axios");

const API_URL = process.env.API_URL || "http://localhost:5000";

const args = process.argv.slice(2);
function getArg(name, defaultValue) {
  const idx = args.indexOf(`--${name}`);
  return idx >= 0 && args[idx + 1] ? args[idx + 1] : defaultValue;
}

const NUM_RIDERS = parseInt(getArg("riders", "5000"), 10);
const DURATION_SEC = parseInt(getArg("duration", "60"), 10);

const stats = {
  activeConnections: 0,
  successfulConnects: 0,
  successfulDisconnects: 0,
  connectionFailures: 0,
  heartbeatsSent: 0,
};

function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

async function registerAndGetToken(index) {
  const email = `chaos_rider_${index}_${Date.now()}_${Math.random().toString(36).substring(7)}@test.com`;
  try {
    const res = await axios.post(`${API_URL}/api/v1/auth/register`, {
      email,
      password: "StressTest123!",
      fullName: `Chaos Rider ${index}`,
      role: "Rider",
    });
    return res.data?.value?.accessToken;
  } catch (err) {
    // Graceful error logging
    return null;
  }
}

async function runRiderChaos(index, token) {
  if (!token) return;

  const endTime = Date.now() + DURATION_SEC * 1000;

  while (Date.now() < endTime) {
    // 1. Connect
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/tracking`, {
        accessTokenFactory: () => token,
      })
      .configureLogging(signalR.LogLevel.None)
      .build();

    try {
      await connection.start();
      stats.activeConnections++;
      stats.successfulConnects++;

      // Trigger location/heartbeat to check link responsiveness
      await connection.invoke("UpdateHeartbeat");
      stats.heartbeatsSent++;

      // Keep connected briefly (random between 500ms and 1500ms)
      await sleep(500 + Math.random() * 1000);

      // 2. Disconnect
      await connection.stop();
      stats.activeConnections--;
      stats.successfulDisconnects++;
    } catch (err) {
      stats.connectionFailures++;
    }

    // Delay before next connection cycle
    await sleep(500 + Math.random() * 1000);
  }
}

async function main() {
  console.log("===============================================");
  console.log("  SignalR Chaos Reconnect & Surge Simulator");
  console.log(`  Target: ${API_URL}`);
  console.log(`  Simulating: ${NUM_RIDERS.toLocaleString()} Riders`);
  console.log(`  Duration: ${DURATION_SEC} seconds`);
  console.log("===============================================");

  console.log("\nRegistering and authenticating riders in batches...");
  const tokens = [];
  const batchSize = 100;
  
  for (let i = 0; i < NUM_RIDERS; i += batchSize) {
    const promises = [];
    const currentBatchEnd = Math.min(i + batchSize, NUM_RIDERS);
    for (let j = i; j < currentBatchEnd; j++) {
      promises.push(registerAndGetToken(j));
    }
    const results = await Promise.all(promises);
    tokens.push(...results.filter(t => t !== null));
    console.log(`  - Authenticated: ${tokens.length}/${NUM_RIDERS}`);
    await sleep(100); // Prevent local socket starvation during registration
  }

  console.log(`\nStarting chaos connection storm with ${tokens.length} active riders...`);
  
  const startTime = Date.now();
  const riderPromises = tokens.map((token, index) => runRiderChaos(index, token));

  // Print telemetry updates every 3 seconds
  const reportingInterval = setInterval(() => {
    const elapsed = Math.round((Date.now() - startTime) / 1000);
    console.log(`[REPORT] ${elapsed}s elapsed:`);
    console.log(`  - Active Connections: ${stats.activeConnections.toLocaleString()}`);
    console.log(`  - Successful Connects: ${stats.successfulConnects.toLocaleString()}`);
    console.log(`  - Successful Disconnects: ${stats.successfulDisconnects.toLocaleString()}`);
    console.log(`  - Connection Failures: ${stats.connectionFailures.toLocaleString()}`);
    console.log(`  - Heartbeats Sent: ${stats.heartbeatsSent.toLocaleString()}`);
  }, 3000);

  await Promise.all(riderPromises);

  clearInterval(reportingInterval);

  console.log("\n===============================================");
  console.log("🏆 CHAOS TEST RUN FINISHED!");
  console.log(`Total Connect-Disconnect Cycles attempted: ${(stats.successfulConnects + stats.connectionFailures).toLocaleString()}`);
  console.log(`Total Successful Connects: ${stats.successfulConnects.toLocaleString()}`);
  console.log(`Total Successful Disconnects: ${stats.successfulDisconnects.toLocaleString()}`);
  console.log(`Total Connection Failures: ${stats.connectionFailures.toLocaleString()}`);
  console.log(`Total Heartbeats Sent: ${stats.heartbeatsSent.toLocaleString()}`);
  console.log("===============================================");
}

main().catch(console.error);
