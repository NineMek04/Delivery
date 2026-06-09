/**
 * lock-contention-stress.js — Database Row Lock & Transaction Contention Stress Test
 *
 * This test simulates high concurrency by firing multiple conflicting state changes 
 * on the SAME order rows at the exact same millisecond. This stresses EF Core's
 * row concurrency mechanisms (xmin/RowVersion) and PostgreSQL's lock/deadlock handling.
 *
 * Usage:
 *   node lock-contention-stress.js [--orders 10] [--concurrency 50]
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

const ORDER_COUNT = parseInt(getArg("orders", "15"), 10);
const CONCURRENCY_PER_ORDER = parseInt(getArg("concurrency", "40"), 10); // 40 requests per order (20 accepts, 20 cancels)

const stats = {
  totalRequests: 0,
  success200: 0,
  conflict400: 0,
  forbidden403: 0,
  unhandled500: 0,
  otherStatusCodes: {},
  latencies: [],
};

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
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

async function createOrder(customerToken, customerId, shopId, index) {
  try {
    const res = await axios.post(`${API_URL}/api/v1/orders`, {
      pickupLat: 13.7563 + index * 0.0001,
      pickupLng: 100.5018 + index * 0.0001,
      dropoffLat: 13.7570 + index * 0.0001,
      dropoffLng: 100.5025 + index * 0.0001,
      expectedDeliveryTime: new Date(Date.now() + 3600000).toISOString(),
      customerId: customerId,
      shopId: shopId,
      items: [],
    }, {
      headers: { Authorization: `Bearer ${customerToken}` }
    });
    return res.data?.value?.id;
  } catch (err) {
    console.error(`Failed to create order #${index}:`, err.response?.data || err.message);
    return null;
  }
}

async function sendRequest(url, method, token, payload = null) {
  const start = Date.now();
  stats.totalRequests++;
  const headers = { 
    Authorization: `Bearer ${token}`,
    "X-Correlation-Id": `lock-stress-${Date.now()}-${Math.random().toString(36).substring(7)}`
  };

  try {
    const res = method === "POST" 
      ? await axios.post(url, payload, { headers, timeout: 5000 })
      : await axios.patch(url, payload, { headers, timeout: 5000 });
      
    const latency = Date.now() - start;
    stats.latencies.push(latency);
    stats.success200++;
  } catch (err) {
    const latency = Date.now() - start;
    stats.latencies.push(latency);
    const status = err.response?.status || "TIMEOUT";
    
    if (status === 400) {
      stats.conflict400++;
    } else if (status === 403) {
      stats.forbidden403++;
    } else if (status === 500) {
      stats.unhandled500++;
      console.log(`\n    [500 Server Error] Details:`, err.response?.data?.message || err.response?.data || err.message);
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
  console.log("  Database Lock Contention & Transaction Stress Test");
  console.log(`  Target URL:              ${API_URL}`);
  console.log(`  Order count to test:     ${ORDER_COUNT}`);
  console.log(`  Concurrency per order:   ${CONCURRENCY_PER_ORDER} (${CONCURRENCY_PER_ORDER / 2} accepts, ${CONCURRENCY_PER_ORDER / 2} cancels)`);
  console.log("=================================================\n");

  const timestamp = Date.now();
  
  console.log("[Phase 1] Provisioning test users...");
  
  const partnerEmail = `lock_partner_${timestamp}@test.com`;
  const partnerUser = await registerUser(partnerEmail, "StorePartner", "Lock Test Partner");
  if (!partnerUser) {
    console.error("Critical: Partner registration failed. Aborting.");
    process.exit(1);
  }
  const partnerToken = partnerUser.accessToken;
  const shopId = partnerUser.user?.shopId;
  console.log(`  - Store Partner registered. ShopId: ${shopId}`);

  const customerEmail = `lock_cust_${timestamp}@test.com`;
  const customerUser = await registerUser(customerEmail, "Customer", "Lock Test Customer");
  if (!customerUser) {
    console.error("Critical: Customer registration failed. Aborting.");
    process.exit(1);
  }
  const customerToken = customerUser.accessToken;
  const customerId = customerUser.user?.id;
  console.log(`  - Customer registered. Id: ${customerId}`);

  const adminEmail = `lock_admin_${timestamp}@test.com`;
  const adminUser = await registerUser(adminEmail, "Admin", "Lock Test Admin");
  if (!adminUser) {
    console.error("Critical: Admin registration failed. Aborting.");
    process.exit(1);
  }
  const adminToken = adminUser.accessToken;
  console.log(`  - Admin registered.`);

  console.log("\n[Phase 2] Seeding orders...");
  const orderIds = [];
  for (let i = 0; i < ORDER_COUNT; i++) {
    const id = await createOrder(customerToken, customerId, shopId, i);
    if (id) {
      orderIds.push(id);
    }
  }
  console.log(`  - Successfully seeded ${orderIds.length} orders in CREATED state.`);
  
  if (orderIds.length === 0) {
    console.error("No orders seeded. Aborting.");
    process.exit(1);
  }

  console.log("\n[Phase 3] Starting Concurrent Updates (Lock Contention Storm)...");
  console.log("  For each order, firing concurrent Store Accepts & Admin Cancels...");
  
  const startTime = Date.now();

  for (let idx = 0; idx < orderIds.length; idx++) {
    const orderId = orderIds[idx];
    const promises = [];

    // Half accept-by-store, half cancel
    const halfCount = Math.floor(CONCURRENCY_PER_ORDER / 2);

    // Accept by store endpoint
    const acceptUrl = `${API_URL}/api/v1/orders/${orderId}/accept-by-store`;
    for (let i = 0; i < halfCount; i++) {
      promises.push(sendRequest(acceptUrl, "POST", partnerToken));
    }

    // Cancel order endpoint
    const cancelUrl = `${API_URL}/api/v1/orders/${orderId}/cancel`;
    for (let i = 0; i < halfCount; i++) {
      promises.push(sendRequest(cancelUrl, "POST", adminToken));
    }

    process.stdout.write(`\r  Storming order ${idx + 1}/${orderIds.length}... `);
    
    // Execute all concurrently for this order row
    await Promise.all(promises);
  }

  const totalTime = ((Date.now() - startTime) / 1000).toFixed(2);
  console.log("\n  Contention Storm complete!\n");

  console.log("=================================================");
  console.log("  STRESS RESULTS");
  console.log("=================================================");
  console.log(`  Total Requests:       ${stats.totalRequests}`);
  console.log(`  Successful (200 OK):  ${stats.success200} (State transitions completed)`);
  console.log(`  Rejected (400 Bad):   ${stats.conflict400} (Expected concurrency/business rule failures)`);
  console.log(`  Forbidden (403):      ${stats.forbidden403}`);
  console.log(`  Unhandled (500 Err):  ${stats.unhandled500} (Database deadlock/unhandled collisions)`);
  
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

  // Output warning if any 500 error occurred
  if (stats.unhandled500 > 0) {
    console.warn("\n⚠️  [WARNING] Unhandled 500 errors were detected during stress testing. This indicates possible database deadlocks or server-side exception leak!");
  } else {
    console.log("\n✅ [SUCCESS] Zero 500 errors. All updates handled gracefully by EF Core concurrency check or business rules.");
  }
}

main().catch(console.error);
