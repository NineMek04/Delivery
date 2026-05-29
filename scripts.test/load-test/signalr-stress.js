/**
 * signalr-stress.js — จำลอง N riders ส่ง GPS พร้อมกันผ่าน SignalR
 *
 * Usage:
 *   node signalr-stress.js [--riders 50] [--duration 60] [--interval 2000]
 *
 * Environment:
 *   API_URL       — Backend URL (default: http://localhost:5000)
 *   ADMIN_EMAIL   — Admin login email
 *   ADMIN_PASSWORD — Admin login password
 */

const signalR = require("@microsoft/signalr");
const axios = require("axios");

const API_URL = process.env.API_URL || "http://localhost:5000";

// Parse CLI arguments
const args = process.argv.slice(2);
function getArg(name, defaultValue) {
  const idx = args.indexOf(`--${name}`);
  return idx >= 0 && args[idx + 1] ? args[idx + 1] : defaultValue;
}

const NUM_RIDERS = parseInt(getArg("riders", "50"), 10);
const DURATION_SEC = parseInt(getArg("duration", "60"), 10);
const INTERVAL_MS = parseInt(getArg("interval", "2000"), 10);

// Udon Thani area coordinates
const CENTER_LAT = 17.4138;
const CENTER_LNG = 102.7872;
const SPREAD = 0.05; // ~5km radius

const stats = {
  connected: 0,
  gpsSent: 0,
  gpsErrors: 0,
  disconnects: 0,
  startTime: null,
};

async function loginAsRider(index) {
  // In a real setup, each rider has own credentials.
  // For stress testing, we register unique users.
  const email = `stress_rider_${index}_${Date.now()}@test.com`;
  const password = "StressTest123!";

  try {
    // Register rider user
    const regRes = await axios.post(`${API_URL}/api/v1/auth/register`, {
      email,
      password,
      fullName: `Stress Rider ${index}`,
      role: "Rider",
    });

    return regRes.data?.value?.accessToken;
  } catch (err) {
    console.error(`[Rider ${index}] Registration failed: ${err.message}`);
    return null;
  }
}

async function simulateRider(index, token) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${API_URL}/hubs/tracking`, {
      accessTokenFactory: () => token,
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  connection.onclose(() => {
    stats.disconnects++;
  });

  try {
    await connection.start();
    stats.connected++;
    console.log(`[Rider ${index}] Connected (total: ${stats.connected})`);
  } catch (err) {
    console.error(`[Rider ${index}] Connection failed: ${err.message}`);
    return;
  }

  // Simulate GPS updates at fixed interval
  let lat = CENTER_LAT + (Math.random() - 0.5) * SPREAD;
  let lng = CENTER_LNG + (Math.random() - 0.5) * SPREAD;

  const gpsInterval = setInterval(async () => {
    // Slight movement (simulate driving)
    lat += (Math.random() - 0.5) * 0.001;
    lng += (Math.random() - 0.5) * 0.001;
    const accuracy = 5 + Math.random() * 15; // 5-20m

    try {
      await connection.invoke("UpdateLocation", lat, lng, accuracy);
      stats.gpsSent++;
    } catch (err) {
      if (stats.gpsErrors === 0) {
        console.error(`[FIRST GPS ERROR]: ${err.message}`);
      }
      stats.gpsErrors++;
    }
  }, INTERVAL_MS);

  // Send heartbeats every 10s
  const heartbeatInterval = setInterval(async () => {
    try {
      await connection.invoke("UpdateHeartbeat");
    } catch {
      /* ignore */
    }
  }, 10000);

  // Stop after duration
  setTimeout(async () => {
    clearInterval(gpsInterval);
    clearInterval(heartbeatInterval);
    try {
      await connection.stop();
    } catch {
      /* ignore */
    }
  }, DURATION_SEC * 1000);
}

async function main() {
  console.log("═══════════════════════════════════════════════");
  console.log("  SignalR GPS Stress Test");
  console.log(`  Target: ${API_URL}`);
  console.log(`  Riders: ${NUM_RIDERS}`);
  console.log(`  Duration: ${DURATION_SEC}s`);
  console.log(`  GPS Interval: ${INTERVAL_MS}ms`);
  console.log("═══════════════════════════════════════════════");

  stats.startTime = Date.now();

  // Register riders and get tokens
  console.log("\n[Phase 1] Registering rider accounts...");
  const tokens = [];
  for (let i = 0; i < NUM_RIDERS; i++) {
    const token = await loginAsRider(i);
    if (token) tokens.push({ index: i, token });
  }
  console.log(`  Registered: ${tokens.length}/${NUM_RIDERS}`);

  // Connect all riders simultaneously
  console.log("\n[Phase 2] Connecting riders via SignalR...");
  const connections = tokens.map(({ index, token }) =>
    simulateRider(index, token)
  );
  await Promise.allSettled(connections);

  // Wait for duration + print stats
  console.log(`\n[Phase 3] Running for ${DURATION_SEC} seconds...\n`);

  const reportInterval = setInterval(() => {
    const elapsed = ((Date.now() - stats.startTime) / 1000).toFixed(0);
    const gpsPerSec = (stats.gpsSent / (elapsed || 1)).toFixed(1);
    console.log(
      `  [${elapsed}s] Connected: ${stats.connected} | GPS sent: ${stats.gpsSent} (${gpsPerSec}/s) | Errors: ${stats.gpsErrors} | Disconnects: ${stats.disconnects}`
    );
  }, 5000);

  setTimeout(() => {
    clearInterval(reportInterval);

    const totalTime = ((Date.now() - stats.startTime) / 1000).toFixed(1);
    const avgGpsPerSec = (stats.gpsSent / totalTime).toFixed(1);

    console.log("\n═══════════════════════════════════════════════");
    console.log("  RESULTS");
    console.log("═══════════════════════════════════════════════");
    console.log(`  Total Time:    ${totalTime}s`);
    console.log(`  Connected:     ${stats.connected}/${NUM_RIDERS}`);
    console.log(`  GPS Sent:      ${stats.gpsSent}`);
    console.log(`  GPS/sec:       ${avgGpsPerSec}`);
    console.log(`  GPS Errors:    ${stats.gpsErrors}`);
    console.log(`  Disconnects:   ${stats.disconnects}`);
    console.log("═══════════════════════════════════════════════");

    process.exit(0);
  }, (DURATION_SEC + 5) * 1000);
}

main().catch(console.error);
