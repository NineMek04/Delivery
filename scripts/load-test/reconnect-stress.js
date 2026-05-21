/**
 * reconnect-stress.js — ทดสอบ SignalR Reconnect Stability
 *
 * Usage:
 *   node reconnect-stress.js [--riders 20] [--cycles 10] [--delay 3000]
 *
 * Environment:
 *   API_URL — Backend URL (default: http://localhost:5000)
 */

const signalR = require("@microsoft/signalr");
const axios = require("axios");

const API_URL = process.env.API_URL || "http://localhost:5000";

const args = process.argv.slice(2);
function getArg(name, defaultValue) {
  const idx = args.indexOf(`--${name}`);
  return idx >= 0 && args[idx + 1] ? args[idx + 1] : defaultValue;
}

const NUM_RIDERS = parseInt(getArg("riders", "20"), 10);
const CYCLES = parseInt(getArg("cycles", "10"), 10);
const DELAY_MS = parseInt(getArg("delay", "3000"), 10);

const stats = {
  totalConnects: 0,
  totalDisconnects: 0,
  reconnectFailures: 0,
  cleanReconnects: 0,
  stateRecoveries: 0,
};

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function registerRider(index) {
  const email = `reconnect_rider_${index}_${Date.now()}@test.com`;
  try {
    const res = await axios.post(`${API_URL}/api/v1/auth/register`, {
      email,
      password: "StressTest123!",
      fullName: `Reconnect Rider ${index}`,
      role: "Rider",
    });
    return res.data?.data?.accessToken;
  } catch (err) {
    console.error(`[Rider ${index}] Registration failed: ${err.message}`);
    return null;
  }
}

async function runReconnectCycle(index, token) {
  for (let cycle = 0; cycle < CYCLES; cycle++) {
    // Connect
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/tracking`, {
        accessTokenFactory: () => token,
      })
      .configureLogging(signalR.LogLevel.Error)
      .build();

    try {
      await connection.start();
      stats.totalConnects++;

      // Send a heartbeat to confirm connection
      await connection.invoke("UpdateHeartbeat");

      // Send one GPS update
      const lat = 17.4138 + (Math.random() - 0.5) * 0.01;
      const lng = 102.7872 + (Math.random() - 0.5) * 0.01;
      await connection.invoke("UpdateLocation", lat, lng, 10);

      // Hold connection briefly
      await sleep(500 + Math.random() * 1000);

      // Disconnect
      await connection.stop();
      stats.totalDisconnects++;
      stats.cleanReconnects++;
    } catch (err) {
      stats.reconnectFailures++;
    }

    // Wait before next cycle
    await sleep(DELAY_MS);
  }
}

async function main() {
  console.log("═══════════════════════════════════════════════");
  console.log("  SignalR Reconnect Stability Test");
  console.log(`  Target: ${API_URL}`);
  console.log(`  Riders: ${NUM_RIDERS}`);
  console.log(`  Cycles per rider: ${CYCLES}`);
  console.log(`  Delay between cycles: ${DELAY_MS}ms`);
  console.log("═══════════════════════════════════════════════\n");

  const startTime = Date.now();

  // Register riders
  console.log("[Phase 1] Registering riders...");
  const tokens = [];
  for (let i = 0; i < NUM_RIDERS; i++) {
    const token = await registerRider(i);
    if (token) tokens.push({ index: i, token });
  }
  console.log(`  Registered: ${tokens.length}/${NUM_RIDERS}\n`);

  // Run reconnect cycles in parallel
  console.log("[Phase 2] Running reconnect cycles...\n");
  const promises = tokens.map(({ index, token }) =>
    runReconnectCycle(index, token)
  );

  // Progress reporter
  const progressInterval = setInterval(() => {
    const elapsed = ((Date.now() - startTime) / 1000).toFixed(0);
    console.log(
      `  [${elapsed}s] Connects: ${stats.totalConnects} | Disconnects: ${stats.totalDisconnects} | Failures: ${stats.reconnectFailures}`
    );
  }, 5000);

  await Promise.allSettled(promises);
  clearInterval(progressInterval);

  const totalTime = ((Date.now() - startTime) / 1000).toFixed(1);
  const expectedTotal = tokens.length * CYCLES;
  const successRate = ((stats.cleanReconnects / expectedTotal) * 100).toFixed(
    1
  );

  console.log("\n═══════════════════════════════════════════════");
  console.log("  RESULTS");
  console.log("═══════════════════════════════════════════════");
  console.log(`  Total Time:          ${totalTime}s`);
  console.log(`  Expected Cycles:     ${expectedTotal}`);
  console.log(`  Clean Reconnects:    ${stats.cleanReconnects}`);
  console.log(`  Failures:            ${stats.reconnectFailures}`);
  console.log(`  Success Rate:        ${successRate}%`);
  console.log(`  Total Connects:      ${stats.totalConnects}`);
  console.log(`  Total Disconnects:   ${stats.totalDisconnects}`);
  console.log("═══════════════════════════════════════════════");

  process.exit(stats.reconnectFailures > 0 ? 1 : 0);
}

main().catch(console.error);
