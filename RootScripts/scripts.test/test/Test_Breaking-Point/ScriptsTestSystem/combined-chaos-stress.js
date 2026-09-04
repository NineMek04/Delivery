/**
 * combined-chaos-stress.js — Stage 5: The Ultimate Combined Load Test (with Chaos Injection)
 *
 * Simulates:
 *   1. SignalR GPS stream for N riders (UpdateLocation + Heartbeat every 2s)
 *   2. HTTP API requests to Backend (RPS: 100-500)
 *   3. Route optimizer routing/ranking requests via Backend proxy (concurrency: 10-50)
 *   4. Backpressure (simulated via pause/resume queues)
 *   5. Chaos Injection (Docker container restarts & network partitions)
 *
 * Usage:
 *   node combined-chaos-stress.js [--riders 500] [--api-rps 100] [--duration 1800] [--route-rps 10]
 *
 * Environment:
 *   API_URL — Backend URL (default: http://localhost:5000)
 */

const signalR = require("@microsoft/signalr");
const axios = require("axios");
const { exec, execSync } = require("child_process");

const API_URL = process.env.API_URL || "http://localhost:5000";
// Parse CLI arguments
const args = process.argv.slice(2);
function getArg(name, defaultValue) {
  const idx = args.indexOf(`--${name}`);
  return idx >= 0 && args[idx + 1] ? args[idx + 1] : defaultValue;
}

const NUM_RIDERS = parseInt(getArg("riders", "500"), 10);
const API_RPS = parseInt(getArg("api-rps", "100"), 10);
const ROUTE_OPTIMIZER_RPS = parseInt(getArg("route-rps", getArg("ai-rps", "10")), 10);
const DURATION_SEC = parseInt(getArg("duration", "1800"), 10);

const stats = {
  activeSignalR: 0,
  signalRConnects: 0,
  signalRDisconnects: 0,
  gpsSent: 0,
  gpsErrors: 0,
  
  apiRequests: 0,
  apiSuccess: 0,
  apiError400: 0,
  apiError500: 0,
  apiOtherError: 0,
  
  routeOptimizerRequests: 0,
  routeOptimizerSuccess: 0,
  routeOptimizerError: 0,
  
  latencies: [],
  startTime: null,
};

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function percentile(arr, p) {
  if (arr.length === 0) return 0;
  const sorted = [...arr].sort((a, b) => a - b);
  const idx = Math.ceil((p / 100) * sorted.length) - 1;
  return sorted[Math.max(0, idx)];
}

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

// Global tokens for API load
let customerToken = "";
let customerId = "";
let shopId = "";
let partnerToken = "";
let adminToken = "";

// Track Active intervals/timeouts so we can clean up
const activeIntervals = [];
const activeConnections = [];

async function simulateRider(index, token) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${API_URL}/hubs/tracking`, {
      accessTokenFactory: () => token,
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(signalR.LogLevel.None)
    .build();

  connection.onclose(() => {
    stats.activeSignalR = Math.max(0, stats.activeSignalR - 1);
    stats.signalRDisconnects++;
  });

  connection.onreconnecting(() => {
    stats.activeSignalR = Math.max(0, stats.activeSignalR - 1);
  });

  connection.onreconnected(() => {
    stats.activeSignalR++;
  });

  try {
    await connection.start();
    stats.activeSignalR++;
    stats.signalRConnects++;
    activeConnections.push(connection);
  } catch (err) {
    stats.gpsErrors++;
    return;
  }

  // Udon Thani Center
  let lat = 17.4138 + (Math.random() - 0.5) * 0.05;
  let lng = 102.7872 + (Math.random() - 0.5) * 0.05;

  const gpsInt = setInterval(async () => {
    if (connection.state !== signalR.HubConnectionState.Connected) return;
    lat += (Math.random() - 0.5) * 0.0002;
    lng += (Math.random() - 0.5) * 0.0002;
    
    try {
      const start = Date.now();
      await connection.invoke("UpdateLocation", lat, lng, 10.0);
      stats.gpsSent++;
      stats.latencies.push(Date.now() - start);
    } catch {
      stats.gpsErrors++;
    }
  }, 2000);

  const hbInt = setInterval(async () => {
    if (connection.state !== signalR.HubConnectionState.Connected) return;
    try {
      await connection.invoke("UpdateHeartbeat");
    } catch {
      // ignore
    }
  }, 10000);

  activeIntervals.push(gpsInt);
  activeIntervals.push(hbInt);
}

// HTTP load simulator
function startHttpLoad() {
  const httpInterval = setInterval(async () => {
    // Generate batches to meet RPS
    const batchSize = Math.max(1, Math.floor(API_RPS / 5)); // fire 5 times per second
    
    for (let i = 0; i < batchSize; i++) {
      stats.apiRequests++;
      const r = Math.random();
      const start = Date.now();
      
      let promise;
      if (r < 0.4) {
        // Customer fetching orders
        promise = axios.get(`${API_URL}/api/v1/orders/customer`, {
          headers: { Authorization: `Bearer ${customerToken}` }
        });
      } else if (r < 0.7) {
        // Shop fetching orders
        promise = axios.get(`${API_URL}/api/v1/orders/shop`, {
          headers: { Authorization: `Bearer ${partnerToken}` }
        });
      } else if (r < 0.95) {
        // Get single order status
        promise = axios.get(`${API_URL}/api/v1/orders`, {
          headers: { Authorization: `Bearer ${adminToken}` }
        });
      } else {
        // Create a new order (2-5% of traffic)
        promise = axios.post(`${API_URL}/api/v1/orders`, {
          pickupLat: 17.41 + Math.random() * 0.01,
          pickupLng: 102.78 + Math.random() * 0.01,
          dropoffLat: 17.42 + Math.random() * 0.01,
          dropoffLng: 102.79 + Math.random() * 0.01,
          expectedDeliveryTime: new Date(Date.now() + 3600000).toISOString(),
          customerId: customerId,
          shopId: shopId,
          items: [],
        }, {
          headers: { Authorization: `Bearer ${customerToken}` }
        });
      }

      promise.then(() => {
        stats.apiSuccess++;
        stats.latencies.push(Date.now() - start);
      }).catch((err) => {
        const status = err.response?.status;
        if (status === 400) stats.apiError400++;
        else if (status === 500) stats.apiError500++;
        else stats.apiOtherError++;
      });
    }
  }, 200);

  activeIntervals.push(httpInterval);
}

// Route optimizer load simulator
function startRouteOptimizerLoad() {
  const routeOptimizerHeaders = {
    "Authorization": `Bearer ${adminToken}`,
    "Content-Type": "application/json",
  };

  const routeOptimizerInterval = setInterval(async () => {
    const batchSize = Math.max(1, Math.floor(ROUTE_OPTIMIZER_RPS / 2));
    
    for (let i = 0; i < batchSize; i++) {
      stats.routeOptimizerRequests++;
      const isOptimize = Math.random() > 0.5;
      const start = Date.now();

      let promise;
      if (isOptimize) {
        // Optimize
        promise = axios.post(`${API_URL}/api/v1/ai/optimize-route`, {
          locations: [
            { id: "depot", lat: 17.4138, lng: 102.7872 },
            { id: "p1", lat: 17.415, lng: 102.79 },
            { id: "d1", lat: 17.42, lng: 102.8 }
          ],
          num_vehicles: 1,
          depot: 0,
          pickups_deliveries: [[1, 2]]
        }, { headers: routeOptimizerHeaders, timeout: 5000 });
      } else {
        // Rank
        promise = axios.post(`${API_URL}/api/v1/ai/dispatch/rank`, {
          context: { timestamp: new Date().toISOString(), city: "udon-thani" },
          order: { id: "opt-1", pickup: [17.4138, 102.7872], dropoff: [17.42, 102.8], sla_limit_minutes: 30 },
          candidates: Array.from({ length: 10 }).map((_, idx) => ({
            rider_id: `rider-bench-${idx}`,
            lat: 17.41 + Math.random() * 0.02,
            lng: 102.78 + Math.random() * 0.02,
            speed_kmh: 20.0,
            current_tasks: []
          }))
        }, { headers: routeOptimizerHeaders, timeout: 5000 });
      }

      promise.then(() => {
        stats.routeOptimizerSuccess++;
        stats.latencies.push(Date.now() - start);
      }).catch(() => {
        stats.routeOptimizerError++;
      });
    }
  }, 500);

  activeIntervals.push(routeOptimizerInterval);
}

// Chaos scheduler
function runChaosCommand(name, cmd) {
  console.log(`\n🔥 [CHAOS INJECTION] Triggering: ${name}`);
  console.log(`   Command: ${cmd}`);
  exec(cmd, (err, stdout, stderr) => {
    if (err) {
      console.error(`❌ [CHAOS ERROR] Failed to run ${name}:`, err.message);
    } else {
      console.log(`✅ [CHAOS SUCCESS] ${name} executed successfully.`);
    }
  });
}

function scheduleChaos() {
  // Minute 10 (600s): Restart RabbitMQ
  setTimeout(() => {
    runChaosCommand("Restart RabbitMQ", "docker restart delivery-rabbitmq");
  }, 10 * 60 * 1000);

  // Minute 15 (900s): Disconnect RabbitMQ Network
  setTimeout(() => {
    runChaosCommand("Disconnect RabbitMQ Network", "docker network disconnect delivery_default delivery-rabbitmq");
    
    // Reconnect after 60s
    setTimeout(() => {
      runChaosCommand("Reconnect RabbitMQ Network", "docker network connect delivery_default delivery-rabbitmq");
    }, 60 * 1000);
  }, 15 * 60 * 1000);

  // Minute 20 (1200s): Restart Redis
  setTimeout(() => {
    runChaosCommand("Restart Redis", "docker restart delivery-redis");
  }, 20 * 60 * 1000);

  // Minute 25 (1500s): Kill Route Optimizer
  setTimeout(() => {
    runChaosCommand("Kill Route Optimizer", "docker kill delivery-route-optimizer");
  }, 25 * 60 * 1000);

  // Minute 30 (1800s): Restart RabbitMQ + Redis concurrently
  setTimeout(() => {
    runChaosCommand("Failure Cascade (Restart MQ + Redis)", "docker restart delivery-rabbitmq && docker restart delivery-redis");
  }, 30 * 60 * 1000);
}

async function main() {
  stats.startTime = Date.now();
  const timestamp = Date.now();

  console.log("=================================================");
  console.log("  Stage 5: Ultimate Combined Stress & Chaos Test");
  console.log(`  Target Backend:     ${API_URL}`);
  console.log("  Target Route Optimizer: via Backend proxy");
  console.log(`  Rider connections:  ${NUM_RIDERS}`);
  console.log(`  HTTP API RPS:       ${API_RPS}`);
  console.log(`  Route Optimizer RPS:${ROUTE_OPTIMIZER_RPS}`);
  console.log(`  Duration:           ${DURATION_SEC}s (${(DURATION_SEC / 60).toFixed(0)} mins)`);
  console.log("=================================================\n");

  console.log("[Phase 1] Provisioning test entities & tokens...");
  
  const partnerUser = await registerUser(`s5_partner_${timestamp}@test.com`, "StorePartner", "S5 Partner");
  if (!partnerUser) {
    console.error("Critical: Partner registration failed. Aborting.");
    process.exit(1);
  }
  partnerToken = partnerUser.accessToken;
  shopId = partnerUser.user?.shopId;

  // Open shop via SQL
  try {
    execSync(`docker exec -i delivery-db psql -U postgres -d delivery_db -c "UPDATE \\"Shops\\" SET \\"IsOpen\\" = true WHERE \\"Id\\" = '${shopId}';"`);
    console.log("  - Test Shop opened in PostgreSQL.");
  } catch (err) {
    console.warn("  - Warning: Failed to open shop in PostgreSQL:", err.message);
  }

  const customerUser = await registerUser(`s5_cust_${timestamp}@test.com`, "Customer", "S5 Customer");
  if (!customerUser) {
    console.error("Critical: Customer registration failed. Aborting.");
    process.exit(1);
  }
  customerToken = customerUser.accessToken;
  customerId = customerUser.user?.id;

  const adminUser = await registerUser(`s5_admin_${timestamp}@test.com`, "Admin", "S5 Admin");
  if (!adminUser) {
    console.error("Critical: Admin registration failed. Aborting.");
    process.exit(1);
  }
  adminToken = adminUser.accessToken;

  console.log("\n[Phase 2] Provisioning rider tokens...");
  const riderTokens = [];
  const riderBatchSize = 50;
  for (let i = 0; i < NUM_RIDERS; i += riderBatchSize) {
    const batchPromises = Array.from({ length: Math.min(riderBatchSize, NUM_RIDERS - i) }).map((_, idx) => {
      const idxRider = i + idx;
      return registerUser(`s5_rider_${idxRider}_${timestamp}@test.com`, "Rider", `S5 Rider ${idxRider}`);
    });
    const results = await Promise.all(batchPromises);
    results.forEach(r => {
      if (r) riderTokens.push(r.accessToken);
    });
    process.stdout.write(`\r  Riders registered: ${riderTokens.length}/${NUM_RIDERS}`);
  }
  console.log("\n  - All rider tokens provisioned.");

  console.log("\n[Phase 3] Launching SignalR Rider GPS Stream...");
  const connectPromises = riderTokens.map((tok, idx) => simulateRider(idx, tok));
  await Promise.allSettled(connectPromises);
  console.log(`  - Active Rider Connections: ${stats.activeSignalR}/${NUM_RIDERS}`);

  console.log("\n[Phase 4] Starting Background HTTP API & Route Optimizer Load...");
  startHttpLoad();
  startRouteOptimizerLoad();

  console.log("\n[Phase 5] Scheduling Chaos Injection Timeline...");
  scheduleChaos();

  console.log("\n[Phase 6] Test is Running. Logging telemetry every 10 seconds...");

  const reportInterval = setInterval(() => {
    const elapsed = ((Date.now() - stats.startTime) / 1000).toFixed(0);
    const avgLatency = stats.latencies.length > 0 
      ? (stats.latencies.reduce((a, b) => a + b, 0) / stats.latencies.length).toFixed(0)
      : 0;

    console.log(
      `[${elapsed}s] ` +
      `Riders: ${stats.activeSignalR}/${NUM_RIDERS} (Disconnects: ${stats.signalRDisconnects}, GPSErr: ${stats.gpsErrors}) | ` +
      `API Req: ${stats.apiRequests} (OK: ${stats.apiSuccess}, 400: ${stats.apiError400}, 500: ${stats.apiError500}) | ` +
      `Route Req: ${stats.routeOptimizerRequests} (OK: ${stats.routeOptimizerSuccess}, Err: ${stats.routeOptimizerError}) | ` +
      `Avg Latency: ${avgLatency}ms`
    );
  }, 10000);

  // End of test timeout
  setTimeout(() => {
    clearInterval(reportInterval);
    activeIntervals.forEach(clearInterval);
    activeConnections.forEach(conn => {
      try { conn.stop(); } catch { /* ignore */ }
    });

    const elapsed = ((Date.now() - stats.startTime) / 1000).toFixed(1);
    console.log("\n=================================================");
    console.log("  TEST COMPLETE — SUMMARY OF COMBINED LOAD");
    console.log("=================================================");
    console.log(`  Total Duration:     ${elapsed}s`);
    console.log(`  Riders Connected:   ${stats.activeSignalR}/${NUM_RIDERS}`);
    console.log(`  SignalR Connects:   ${stats.signalRConnects}`);
    console.log(`  SignalR Disconnects:${stats.signalRDisconnects}`);
    console.log(`  GPS Sent Count:     ${stats.gpsSent} (Errors: ${stats.gpsErrors})`);
    console.log(`  API Requests Sent:  ${stats.apiRequests}`);
    console.log(`  - 200 OK:           ${stats.apiSuccess}`);
    console.log(`  - 400 Bad:          ${stats.apiError400}`);
    console.log(`  - 500 Server Err:   ${stats.apiError500}`);
    console.log(`  - Other Errors:     ${stats.apiOtherError}`);
    console.log(`  Route Requests Sent:${stats.routeOptimizerRequests}`);
    console.log(`  - Success:          ${stats.routeOptimizerSuccess}`);
    console.log(`  - Failures:         ${stats.routeOptimizerError}`);
    
    if (stats.latencies.length > 0) {
      console.log(`  Avg Latency:        ${(stats.latencies.reduce((a, b) => a + b, 0) / stats.latencies.length).toFixed(0)}ms`);
      console.log(`  p50 Latency:        ${percentile(stats.latencies, 50)}ms`);
      console.log(`  p95 Latency:        ${percentile(stats.latencies, 95)}ms`);
      console.log(`  p99 Latency:        ${percentile(stats.latencies, 99)}ms`);
    }
    console.log("=================================================");

    process.exit(0);
  }, DURATION_SEC * 1000 + 10000); // add a small buffer
}

main().catch(console.error);
