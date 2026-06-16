/**
 * war-room-load-test.js — War Room E2E Load Testing Simulator
 *
 * Simulates:
 *   - 1,000 to 2,000 parallel riders connected via SignalR
 *   - Rapid fire order injection (500 to 1,000 orders)
 *   - Concurrent delivery lifecycle simulation (500 to 1,000 concurrent riders)
 *   - Visual dashboard stats in real-time
 *
 * Usage:
 *   node war-room-load-test.js [--url http://localhost] [--riders 1500] [--orders 600] [--concurrency 50]
 */
const signalR = require("@microsoft/signalr");
const axios = require("axios");
const http = require("http");
const https = require("https");
const readline = require("readline");
const fs = require("fs");
const path = require("path");

// Robust dotenv file loader to load system variables on local environments
function loadEnv() {
  const possiblePaths = [
    path.join(__dirname, ".env"),
    path.join(__dirname, "..", ".env"),
    path.join(__dirname, "../..", ".env"),
    path.join(__dirname, "../../..", ".env"),
    path.join(__dirname, "../../../..", ".env"),
    path.join(__dirname, "../../../../../.env"),
    "c:\\Users\\ASUS\\Desktop\\Project\\Delivery\\.env"
  ];
  for (const p of possiblePaths) {
    if (fs.existsSync(p)) {
      const content = fs.readFileSync(p, "utf8");
      const lines = content.split("\n");
      for (const line of lines) {
        const match = line.match(/^\s*([\w.-]+)\s*=\s*(.*)\s*$/);
        if (match) {
          const key = match[1];
          let value = match[2].trim();
          if (value.startsWith('"') && value.endsWith('"')) {
            value = value.substring(1, value.length - 1);
          }
          if (value.startsWith("'") && value.endsWith("'")) {
            value = value.substring(1, value.length - 1);
          }
          process.env[key] = value;
        }
      }
      break;
    }
  }
}

// Load env variables prior to bootstrapping the test
loadEnv();

// Optimize Node.js HTTP client sockets for massive scale load-testing
axios.defaults.httpAgent = new http.Agent({ keepAlive: true, maxSockets: 10000 });
axios.defaults.httpsAgent = new https.Agent({ keepAlive: true, maxSockets: 10000 });
axios.defaults.timeout = 15000; // 15 seconds HTTP timeout

const args = process.argv.slice(2);
function getArg(name, defaultValue) {
  const idx = args.indexOf(`--${name}`);
  return idx >= 0 && args[idx + 1] ? args[idx + 1] : defaultValue;
}

const API_URL = getArg("url", process.env.API_URL || "http://localhost");
const NUM_RIDERS = parseInt(getArg("riders", "1500"), 10);
const NUM_ORDERS = parseInt(getArg("orders", "600"), 10);
const REG_CONCURRENCY = parseInt(getArg("concurrency", "50"), 10);

const API = `${API_URL}/api/v1`;
const HUB = `${API_URL}/hubs/tracking`;

// Center of Udon Thani for simulation coordinates
const UDON_CENTER = { lat: 17.4138, lng: 102.7872 };

// Performance stats dashboard structure
const stats = {
  startTime: null,
  ridersRegistered: 0,
  ridersConnected: 0,
  ridersIdle: 0,
  ridersBusy: 0,
  ordersCreated: 0,
  ordersAcceptedByStore: 0,
  offersReceived: 0,
  offersAcceptedSuccess: 0,
  offersAcceptedFail: 0,
  deliveriesCompleted: 0,
  apiErrors: 0,
  signalrErrors: 0,
  latencySumMs: 0,
  latencyCount: 0,
  logMsgs: [],
};

function addLog(msg) {
  const ts = new Date().toLocaleTimeString("th-TH", { hour12: false });
  stats.logMsgs.push(`[${ts}] ${msg}`);
  if (stats.logMsgs.length > 8) {
    stats.logMsgs.shift();
  }
}

// Generate coordinates around a point
function randomPointAround(center, radiusKm) {
  const radiusInDegrees = radiusKm / 111.32;
  const angle = Math.random() * Math.PI * 2;
  const distance = Math.sqrt(Math.random()) * radiusInDegrees;
  return {
    lat: center.lat + Math.cos(angle) * distance,
    lng: center.lng + Math.sin(angle) * distance / Math.cos(center.lat * Math.PI / 180)
  };
}

// Helper to partition an array into chunks
function chunkArray(array, size) {
  const result = [];
  for (let i = 0; i < array.length; i += size) {
    result.push(array.slice(i, i + size));
  }
  return result;
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// Store partner details
const shops = [];
let adminToken = "";

// Initialize Shops and Store Partners
async function seedShops() {
  addLog(`Provisioning admin and 10 store partners/shops...`);
  
  // Login as seeded Admin to bypass public registration restrictions
  const adminEmail = "admin@delivery.com";
  const adminPassword = process.env.SEED_ADMIN_PASSWORD || "Delivery_unique_bootstrap_password_2026";
  try {
    const adminLogin = await axios.post(`${API}/auth/login`, {
      email: adminEmail,
      password: adminPassword
    });
    adminToken = adminLogin.data?.value?.accessToken;
  } catch (err) {
    stats.apiErrors++;
    console.error("Admin login failed:", err.message);
    if (err.response) {
      console.error("Status:", err.response.status);
      console.error("Data:", JSON.stringify(err.response.data));
    }
    process.exit(1);
  }

  // Create 10 Shops
  for (let i = 0; i < 10; i++) {
    const shopEmail = `war_shop_${i}_${Date.now()}@delivery.test`;
    try {
      const regRes = await axios.post(`${API}/auth/register`, {
        email: shopEmail,
        password: "StressTest123!",
        fullName: `War Shop Partner ${i}`,
        role: "StorePartner"
      });

      const token = regRes.data?.value?.accessToken;
      const shopId = regRes.data?.value?.user?.shopId;

      if (!shopId) {
        throw new Error(`Register response did not contain shopId for ${shopEmail}`);
      }

      // Configure Shop location in Udon Thani
      const angle = (i / 10) * Math.PI * 2;
      const lat = UDON_CENTER.lat + Math.sin(angle) * 0.015;
      const lng = UDON_CENTER.lng + Math.cos(angle) * 0.015;

      await axios.put(`${API}/shops/${shopId}`, {
        name: `War Shop ${i}`,
        menuName: `Super Dish ${i}`,
        menuPrice: 80 + i * 5,
        lat: lat,
        lng: lng,
        isOpen: true
      }, {
        headers: { Authorization: `Bearer ${token}` }
      });

      shops.push({
        id: shopId,
        token: token,
        lat: lat,
        lng: lng
      });

    } catch (err) {
      stats.apiErrors++;
      addLog(`Failed to configure shop ${i}: ${err.message}`);
    }
  }

  addLog(`Successfully configured ${shops.length} active shops.`);
}

// Batch Rider Registration
async function registerRiders() {
  addLog(`Registering ${NUM_RIDERS} rider accounts in batches of ${REG_CONCURRENCY}...`);
  const riders = [];

  const rawRiders = Array.from({ length: NUM_RIDERS }).map((_, index) => {
    const shop = shops[index % shops.length];
    const location = randomPointAround({ lat: shop.lat, lng: shop.lng }, 1.5);
    return {
      index,
      email: `war_rider_${index}_${Date.now()}@delivery.test`,
      password: "StressTest123!",
      name: `War Rider ${index}`,
      location,
    };
  });

  const chunks = chunkArray(rawRiders, REG_CONCURRENCY);

  for (const chunk of chunks) {
    const promises = chunk.map(async (r) => {
      try {
        const res = await axios.post(`${API}/auth/register`, {
          email: r.email,
          password: r.password,
          fullName: r.name,
          role: "Rider"
        });
        
        const token = res.data?.value?.accessToken;
        const riderId = res.data?.value?.user?.riderId;

        if (token && riderId) {
          riders.push({
            id: riderId,
            token,
            name: r.name,
            currentLocation: r.location,
            isBusy: false,
          });
          stats.ridersRegistered++;
        }
      } catch (err) {
        stats.apiErrors++;
      }
    });

    await Promise.all(promises);
    await sleep(50); // Small pause between registration batches to prevent DB contention
  }

  addLog(`Registration complete. Registered: ${riders.length}/${NUM_RIDERS}`);
  return riders;
}

// Connect Riders via SignalR
async function connectRiders(riders) {
  addLog(`Connecting ${riders.length} riders to SignalR Hub...`);
  
  // Stagger connections: connect 50 riders every 200ms
  const staggerSize = 50;
  const chunks = chunkArray(riders, staggerSize);

  for (const chunk of chunks) {
    const promises = chunk.map(async (rider) => {
      const connection = new signalR.HubConnectionBuilder()
        .withUrl(HUB, {
          accessTokenFactory: () => rider.token,
          skipNegotiation: true,
          transport: signalR.HttpTransportType.WebSockets
        })
        .withAutomaticReconnect([0, 2000, 5000, 10000])
        .configureLogging(signalR.LogLevel.None)
        .build();

      connection.onclose(() => {
        stats.ridersConnected = Math.max(0, stats.ridersConnected - 1);
        if (!rider.isBusy) {
          stats.ridersIdle = Math.max(0, stats.ridersIdle - 1);
        } else {
          stats.ridersBusy = Math.max(0, stats.ridersBusy - 1);
        }
      });

      // Handle receiving job offers
      connection.on("OfferReceived", async (offer) => {
        stats.offersReceived++;
        
        if (rider.isBusy) return; // Ignore if busy

        // Simulating randomized acceptance response (300ms - 1000ms delay)
        const delay = 300 + Math.random() * 700;
        await sleep(delay);

        try {
          rider.activeOffer = offer;
          await connection.invoke("AcceptOffer", offer.offerId || offer.OfferId, offer.version || offer.Version);
        } catch (err) {
          stats.signalrErrors++;
        }
      });

      // Handle offer acceptance result
      connection.on("OfferAcceptedResult", async (result) => {
        const success = result?.success ?? result?.Success;
        const offerId = rider.activeOffer?.offerId || rider.activeOffer?.OfferId;
        const order = rider.activeOffer?.order;
        const orderId = order?.id || order?.Id;

        if (success) {
          stats.offersAcceptedSuccess++;
          rider.isBusy = true;
          stats.ridersIdle--;
          stats.ridersBusy++;
          
          if (order && order.createdTime) {
            const matchLatency = Date.now() - new Date(order.createdTime).getTime();
            stats.latencySumMs += matchLatency;
            stats.latencyCount++;
          }

          // Execute delivery simulation on a separate timeline
          simulateDeliveryFlow(connection, rider, orderId, order).catch((err) => {
            stats.apiErrors++;
            resetRiderToIdle(rider);
          });
        } else {
          stats.offersAcceptedFail++;
          rider.isBusy = false;
        }
      });

      try {
        await connection.start();
        stats.ridersConnected++;
        stats.ridersIdle++;
        rider.connection = connection;

        // Send initial location
        await connection.invoke("UpdateLocation", rider.currentLocation.lat, rider.currentLocation.lng, 5.0);

        // Periodically update location (presence heartbeat) every 15s to keep active in Redis
        rider.heartbeat = setInterval(async () => {
          if (!rider.isBusy && connection.state === signalR.HubConnectionState.Connected) {
            try {
              // Add minor drift coordinates to simulate wandering
              rider.currentLocation.lat += (Math.random() - 0.5) * 0.0005;
              rider.currentLocation.lng += (Math.random() - 0.5) * 0.0005;
              await connection.invoke("UpdateLocation", rider.currentLocation.lat, rider.currentLocation.lng, 5.0);
            } catch (err) {
              stats.signalrErrors++;
            }
          }
        }, 15000);

      } catch (err) {
        stats.signalrErrors++;
      }
    });

    await Promise.all(promises);
    await sleep(200); // 200ms stagger between chunks
  }

  addLog(`SignalR connections established: ${stats.ridersConnected}/${riders.length}`);
}

function resetRiderToIdle(rider) {
  if (rider.isBusy) {
    rider.isBusy = false;
    stats.ridersBusy = Math.max(0, stats.ridersBusy - 1);
    stats.ridersIdle++;
  }
}

// Simulates driving, picking up, and completing orders
async function simulateDeliveryFlow(connection, rider, orderId, order) {
  const shopLat = order.pickupLat ?? order.PickupLat;
  const shopLng = order.pickupLng ?? order.PickupLng;
  const dropLat = order.dropoffLat ?? order.DropoffLat;
  const dropLng = order.dropoffLng ?? order.DropoffLng;

  // Step 1: Transition status to PICKING_UP
  await axios.patch(`${API}/orders/${orderId}/status`, { status: "PICKING_UP" }, {
    headers: { Authorization: `Bearer ${rider.token}` }
  });

  // Step 2: Stream 3 location ticks moving to pickup shop
  for (let i = 1; i <= 3; i++) {
    const ratio = i / 3;
    const currentLat = rider.currentLocation.lat + (shopLat - rider.currentLocation.lat) * ratio;
    const currentLng = rider.currentLocation.lng + (shopLng - rider.currentLocation.lng) * ratio;
    await connection.invoke("UpdateLocation", currentLat, currentLng, 5.0);
    await sleep(1000);
  }

  // Step 3: Transition status to DELIVERING (picked up)
  await axios.patch(`${API}/orders/${orderId}/status`, { status: "DELIVERING" }, {
    headers: { Authorization: `Bearer ${rider.token}` }
  });

  // Step 4: Stream 5 location ticks moving to dropoff
  for (let i = 1; i <= 5; i++) {
    const ratio = i / 5;
    const currentLat = shopLat + (dropLat - shopLat) * ratio;
    const currentLng = shopLng + (dropLng - shopLng) * ratio;
    await connection.invoke("UpdateLocation", currentLat, currentLng, 5.0);
    await sleep(1000);
  }

  // Step 5: Transition status to COMPLETED
  await axios.patch(`${API}/orders/${orderId}/status`, { status: "COMPLETED" }, {
    headers: { Authorization: `Bearer ${rider.token}` }
  });

  stats.deliveriesCompleted++;
  
  // Set current position to dropoff and reset rider to idle
  rider.currentLocation = { lat: dropLat, lng: dropLng };
  resetRiderToIdle(rider);
}

// Rapid Fire Order Injector
async function fireOrders() {
  addLog(`Rapidly injecting ${NUM_ORDERS} orders and accepting by stores...`);
  
  const rawOrders = Array.from({ length: NUM_ORDERS }).map((_, index) => {
    const shop = shops[index % shops.length];
    const dropoff = randomPointAround({ lat: shop.lat, lng: shop.lng }, 2.5);
    return {
      shop,
      dropoff,
      createdTime: new Date().toISOString()
    };
  });

  // Inject in parallel batches of 30
  const chunks = chunkArray(rawOrders, 30);

  for (const chunk of chunks) {
    const promises = chunk.map(async (o) => {
      try {
        // Create Order via Admin
        const response = await axios.post(`${API}/orders`, {
          shopId: o.shop.id,
          pickupLat: o.shop.lat,
          pickupLng: o.shop.lng,
          dropoffLat: o.dropoff.lat,
          dropoffLng: o.dropoff.lng,
          expectedDeliveryTime: new Date(Date.now() + 45 * 60 * 1000).toISOString()
        }, {
          headers: { Authorization: `Bearer ${adminToken}` }
        });

        const order = response.data?.value;
        const orderId = order?.id;

        if (orderId) {
          stats.ordersCreated++;
          order.createdTime = o.createdTime; // Inject start timestamp for latency calculation
          
          // Accept by Store Partner immediately to trigger matching
          await axios.post(`${API}/orders/${orderId}/accept-by-store`, {}, {
            headers: { Authorization: `Bearer ${o.shop.token}` }
          });
          stats.ordersAcceptedByStore++;
        }
      } catch (err) {
        stats.apiErrors++;
      }
    });

    await Promise.all(promises);
    await sleep(100); // Stagger batches slightly
  }

  addLog(`Finished injecting all orders.`);
}

// CLI Realtime Dashboard Rendering
function startStatsDashboard() {
  stats.startTime = Date.now();
  
  const displayInterval = setInterval(() => {
    const elapsed = Math.round((Date.now() - stats.startTime) / 1000);
    const avgLatencySec = stats.latencyCount > 0 
      ? (stats.latencySumMs / stats.latencyCount / 1000).toFixed(2) 
      : "0.00";

    readline.cursorTo(process.stdout, 0, 0);
    readline.clearScreenDown(process.stdout);

    console.log("════════════════════════════════════════════════════════════════════════");
    console.log("             ⚡  SMART DELIVERY WAR ROOM LOAD TESTER  ⚡");
    console.log("════════════════════════════════════════════════════════════════════════");
    console.log(`  Elapsed Time:         ${elapsed} seconds`);
    console.log(`  Target URL:           ${API_URL}`);
    console.log("────────────────────────────────────────────────────────────────────────");
    console.log("  [RIDER LOGISTICS]");
    console.log(`    Registered Riders:  ${stats.ridersRegistered.toString().padEnd(12)} | Connected (SignalR): ${stats.ridersConnected.toString().padEnd(10)}`);
    console.log(`    Idle Riders:        ${stats.ridersIdle.toString().padEnd(12)} | Active Delivery:    ${stats.ridersBusy.toString().padEnd(10)}`);
    console.log("  [ORDER LIFECYCLE]");
    console.log(`    Orders Created:     ${stats.ordersCreated.toString().padEnd(12)} | Accepted by Store:   ${stats.ordersAcceptedByStore.toString().padEnd(10)}`);
    console.log(`    Deliveries Done:    ${stats.deliveriesCompleted.toString().padEnd(12)} | Avg Match Latency:   ${avgLatencySec}s`);
    console.log("  [SIGNALR EVENT BUS]");
    console.log(`    Offers Broadcasted: ${stats.offersReceived.toString().padEnd(12)} | Successful Matches:  ${stats.offersAcceptedSuccess.toString().padEnd(10)}`);
    console.log(`    Lock Collisions:    ${stats.offersAcceptedFail.toString().padEnd(12)} |`);
    console.log("  [SYSTEM STABILITY]");
    console.log(`    API HTTP Errors:    ${stats.apiErrors.toString().padEnd(12)} | SignalR Errors:      ${stats.signalrErrors.toString().padEnd(10)}`);
    console.log("════════════════════════════════════════════════════════════════════════");
    console.log("  LOG STATUS / CONSOLE LIVE STREAM:");
    stats.logMsgs.forEach((line) => {
      console.log(`    ${line}`);
    });
    console.log("════════════════════════════════════════════════════════════════════════");

    // Exit condition: all orders have been completed or elapsed time is too long
    if (stats.ordersAcceptedByStore > 0 && stats.deliveriesCompleted >= stats.ordersAcceptedByStore && elapsed > 15) {
      clearInterval(displayInterval);
      console.log("\n  🏁  TEST COMPLETED: All active orders processed to completion.");
      teardownAndExit(0);
    } else if (elapsed > 300) { // 5-minute timeout guard
      clearInterval(displayInterval);
      console.log("\n  ⚠️  TEST TERMINATED: Reached maximum timeout limit (5 mins).");
      teardownAndExit(1);
    }
  }, 3000);
}

function teardownAndExit(code) {
  addLog("Cleaning up client connections...");
  process.exit(code);
}

// Master execution thread
async function main() {
  console.log("Preparing database tables and seeds...");
  
  // 1. Initialize Shops and Store Partner connections
  await seedShops();
  
  // 2. Register specified number of riders
  const riders = await registerRiders();

  // 3. Connect riders and begin location heartbeats
  await connectRiders(riders);

  // 4. Fire up the dashboard display
  startStatsDashboard();

  // Wait 3 seconds for presence maps to sync in Redis
  await sleep(3000);

  // 5. Rapid fire order queueing
  await fireOrders();
}

main().catch((err) => {
  console.error("Test execution crashed:", err);
  process.exit(1);
});
