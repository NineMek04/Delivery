/**
 * dispatch-stress.js — ทดสอบ Dispatch Queue Pressure
 *
 * Usage:
 *   node dispatch-stress.js [--orders 50] [--concurrent 5]
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

const NUM_ORDERS = parseInt(getArg("orders", "50"), 10);
const CONCURRENT = parseInt(getArg("concurrent", "5"), 10);

// Udon Thani shop locations
const SHOPS = [
  { lat: 17.4150, lng: 102.7880 },
  { lat: 17.4100, lng: 102.7850 },
  { lat: 17.4200, lng: 102.7900 },
  { lat: 17.4050, lng: 102.7800 },
  { lat: 17.4180, lng: 102.7920 },
];

// Random delivery destinations
const DESTINATIONS = [
  { lat: 17.4000, lng: 102.7750 },
  { lat: 17.4250, lng: 102.7950 },
  { lat: 17.4080, lng: 102.7820 },
  { lat: 17.4300, lng: 102.7700 },
  { lat: 17.4120, lng: 102.7860 },
];

const stats = {
  created: 0,
  failed: 0,
  latencies: [],
  errors: [],
};

async function getAdminToken() {
  const email = `dispatch_stress_${Date.now()}@test.com`;
  try {
    const res = await axios.post(`${API_URL}/api/v1/auth/register`, {
      email,
      password: "StressTest123!",
      fullName: "Dispatch Stress User",
      role: "Admin",
    });
    return res.data?.data?.accessToken;
  } catch (err) {
    console.error("Failed to get admin token:", err.message);
    process.exit(1);
  }
}

async function createOrder(token, index) {
  const shop = SHOPS[index % SHOPS.length];
  const dest = DESTINATIONS[index % DESTINATIONS.length];
  const start = Date.now();

  try {
    const res = await axios.post(
      `${API_URL}/api/v1/orders`,
      {
        pickupLat: shop.lat,
        pickupLng: shop.lng,
        dropoffLat: dest.lat,
        dropoffLng: dest.lng,
        expectedDeliveryTime: new Date(
          Date.now() + 3600000
        ).toISOString(),
      },
      {
        headers: { Authorization: `Bearer ${token}` },
        timeout: 15000,
      }
    );

    const latency = Date.now() - start;
    stats.latencies.push(latency);
    stats.created++;
    return res.data?.data;
  } catch (err) {
    const latency = Date.now() - start;
    stats.latencies.push(latency);
    stats.failed++;
    stats.errors.push(err.response?.status || err.message);
    return null;
  }
}

function percentile(arr, p) {
  const sorted = [...arr].sort((a, b) => a - b);
  const idx = Math.ceil((p / 100) * sorted.length) - 1;
  return sorted[Math.max(0, idx)];
}

async function main() {
  console.log("═══════════════════════════════════════════════");
  console.log("  Dispatch Queue Pressure Test");
  console.log(`  Target: ${API_URL}`);
  console.log(`  Orders: ${NUM_ORDERS}`);
  console.log(`  Concurrent: ${CONCURRENT}`);
  console.log("═══════════════════════════════════════════════\n");

  const token = await getAdminToken();
  console.log("  Admin token acquired.\n");

  const startTime = Date.now();
  let sent = 0;

  while (sent < NUM_ORDERS) {
    const batchSize = Math.min(CONCURRENT, NUM_ORDERS - sent);
    const promises = [];
    for (let i = 0; i < batchSize; i++) {
      promises.push(createOrder(token, sent + i));
    }
    await Promise.allSettled(promises);
    sent += batchSize;
    process.stdout.write(
      `\r  Progress: ${sent}/${NUM_ORDERS} (${stats.created} ok, ${stats.failed} err)`
    );
  }

  const totalTime = ((Date.now() - startTime) / 1000).toFixed(2);

  console.log("\n\n═══════════════════════════════════════════════");
  console.log("  RESULTS");
  console.log("═══════════════════════════════════════════════");
  console.log(`  Total Time:       ${totalTime}s`);
  console.log(`  Orders Created:   ${stats.created}/${NUM_ORDERS}`);
  console.log(`  Failures:         ${stats.failed}`);
  if (stats.latencies.length > 0) {
    console.log(
      `  Avg Latency:      ${(stats.latencies.reduce((a, b) => a + b, 0) / stats.latencies.length).toFixed(0)}ms`
    );
    console.log(`  p50 Latency:      ${percentile(stats.latencies, 50)}ms`);
    console.log(`  p95 Latency:      ${percentile(stats.latencies, 95)}ms`);
    console.log(
      `  Dispatch Rate:    ${(stats.created / totalTime).toFixed(1)} orders/sec`
    );
  }
  if (stats.errors.length > 0) {
    const errorCounts = {};
    stats.errors.forEach((e) => (errorCounts[e] = (errorCounts[e] || 0) + 1));
    console.log(`  Error Breakdown:  ${JSON.stringify(errorCounts)}`);
  }
  console.log("═══════════════════════════════════════════════");
}

main().catch(console.error);
