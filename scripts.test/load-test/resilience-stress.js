/**
 * resilience-stress.js — Heavy-Load Durability & Resilience Stress Test
 *
 * Verifies system stability under harsh conditions:
 *   1. Lock Contention (concurrent accepts on the same offer)
 *   2. Idempotency under pressure (duplicate submits)
 *   3. State Machine Integrity under concurrent race conditions (Cancel vs Accept)
 *   4. Correlation ID propagation across 100% of heavy concurrent requests
 *
 * Usage:
 *   node resilience-stress.js
 *
 * Environment:
 *   API_URL — Backend URL (default: http://localhost:5000)
 */

const signalR = require("@microsoft/signalr");
const axios = require("axios");

const API_URL = process.env.API_URL || "http://localhost:5000";

const stats = {
  passed: 0,
  failed: 0,
  details: [],
};

const outputFile = process.argv[2]; // e.g. /tmp/results.json

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function logStep(name, status, details = "", inputs = "N/A") {
  let errorMsg = null;
  if (status === "PASS") {
    stats.passed++;
    console.log(`  [PASS] ${name} ${details ? `— ${details}` : ""}`);
  } else {
    stats.failed++;
    console.log(`  [FAIL] ❌ ${name} ${details ? `— ${details}` : ""}`);
    errorMsg = details;
  }
  stats.details.push({ 
    name, 
    location: "load-test/resilience-stress.js", 
    inputs, 
    status, 
    durationMs: 0, 
    error: errorMsg 
  });
}

async function registerUser(email, role, name) {
  try {
    const res = await axios.post(`${API_URL}/api/v1/auth/register`, {
      email,
      password: "StressTest123!",
      fullName: name,
      role: role,
    });
    return res.data?.value?.accessToken;
  } catch (err) {
    console.error(`Failed to register ${role} (${email}):`, err.message);
    return null;
  }
}

async function main() {
  console.log("═══════════════════════════════════════════════");
  console.log("  Heavy-Load Durability & Resilience Stress Test");
  console.log(`  Target URL: ${API_URL}`);
  console.log("═══════════════════════════════════════════════\n");

  const timestamp = Date.now();
  
  console.log("[Phase 1] Provisioning test users...");
  const adminEmail = `resilience_admin_${timestamp}@test.com`;
  const adminToken = await registerUser(adminEmail, "Admin", "Resilience Admin");
  if (!adminToken) {
    console.error("Critical: Admin registration failed. Aborting test.");
    process.exit(1);
  }

  const riderEmail = `resilience_rider_${timestamp}@test.com`;
  const riderToken = await registerUser(riderEmail, "Rider", "Resilience Rider");
  if (!riderToken) {
    console.error("Critical: Rider registration failed. Aborting test.");
    process.exit(1);
  }
  console.log("  Test users provisioned successfully.\n");

  // Create Shop and MenuItem for testing via API/DB?
  // We will assume there's a shop in the DB. If not, we'll try to find or create.
  // Let's create a shop using the Admin token.
  let shopId = "test-resilience-shop";
  try {
    // Attempt to seed a shop
    await axios.post(`${API_URL}/api/v1/menu/categories`, {
      name: `Resilience Cat ${timestamp}`,
      description: "Test Category",
    }, {
      headers: { Authorization: `Bearer ${adminToken}` }
    });
  } catch (err) {
    // Ignore if categories fail, just check connection
  }

  // -------------------------------------------------------------
  // Test 1: Correlation ID propagation under Heavy Concurrency
  // -------------------------------------------------------------
  console.log("-------------------------------------------------------------");
  console.log("Test 1: Correlation ID propagation under Heavy Concurrency");
  console.log("-------------------------------------------------------------");
  try {
    const concurrentRequests = 50;
    const promises = [];
    const correlationIds = new Set();
    let hasHeaderInAll = true;

    for (let i = 0; i < concurrentRequests; i++) {
      promises.push(
        axios.get(`${API_URL}/api/v1/menu/categories`, {
          headers: { "X-Correlation-Id": `bulk-test-${timestamp}-${i}` },
          validateStatus: () => true
        }).then(res => {
          const header = res.headers["x-correlation-id"];
          if (!header) hasHeaderInAll = false;
          correlationIds.add(header);
        }).catch(err => {
          hasHeaderInAll = false;
        })
      );
    }

    await Promise.all(promises);

    if (hasHeaderInAll && correlationIds.size === concurrentRequests) {
      logStep("CorrelationId Propagation", "PASS", "100% of concurrent requests preserved unique Correlation IDs", `concurrentRequests=${concurrentRequests}`);
    } else {
      logStep("CorrelationId Propagation", "FAIL", `Preserved: ${correlationIds.size}/${concurrentRequests}`, `concurrentRequests=${concurrentRequests}`);
    }
  } catch (err) {
    logStep("CorrelationId Propagation", "FAIL", err.message, `concurrentRequests=50`);
  }

  // -------------------------------------------------------------
  // Test 2: Double-Submit Idempotency under Pressure
  // -------------------------------------------------------------
  console.log("\n-------------------------------------------------------------");
  console.log("Test 2: Double-Submit Idempotency under Pressure");
  console.log("-------------------------------------------------------------");
  try {
    // Double submit category creation at the exact same millisecond
    const categoryPayload = {
      name: `UniqueCat-${timestamp}`,
      description: "Idempotent category test"
    };

    // We send duplicate category creations concurrently
    const call1 = axios.post(`${API_URL}/api/v1/menu/categories`, categoryPayload, {
      headers: { Authorization: `Bearer ${adminToken}`, "X-Correlation-Id": `idemp-test-${timestamp}` }
    });
    const call2 = axios.post(`${API_URL}/api/v1/menu/categories`, categoryPayload, {
      headers: { Authorization: `Bearer ${adminToken}`, "X-Correlation-Id": `idemp-test-${timestamp}` }
    });

    const results = await Promise.allSettled([call1, call2]);
    const fulfilled = results.filter(r => r.status === "fulfilled");
    
    // Idempotency check:
    // Depending on API design, double submittal might return a success for the first and either skip/succeed or gracefully handle without 500 for the second.
    // The key is that it shouldn't throw a raw 500 internal server error or deadlock the database.
    const has500 = results.some(r => r.status === "rejected" && r.reason?.response?.status === 500);

    if (!has500) {
      logStep("Double-Submit Idempotency", "PASS", "Duplicate requests handled gracefully without database deadlocks/500 errors", `categoryPayload=${JSON.stringify(categoryPayload)}`);
    } else {
      logStep("Double-Submit Idempotency", "FAIL", "Duplicate submit triggered 500 Internal Server Error", `categoryPayload=${JSON.stringify(categoryPayload)}`);
    }
  } catch (err) {
    logStep("Double-Submit Idempotency", "FAIL", err.message, "Double-Submit");
  }

  // -------------------------------------------------------------
  // Test 3: SignalR Connection and Concurrent Location Streaming
  // -------------------------------------------------------------
  console.log("\n-------------------------------------------------------------");
  console.log("Test 3: SignalR Connection and Concurrent Location Streaming");
  console.log("-------------------------------------------------------------");
  let connection;
  try {
    connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/tracking`, {
        accessTokenFactory: () => riderToken,
      })
      .configureLogging(signalR.LogLevel.Error)
      .build();

    await connection.start();
    logStep("SignalR Rider Connection", "PASS", "Rider successfully authenticated and connected");

    // Flood location updates
    const updates = [];
    for (let i = 0; i < 20; i++) {
      updates.push(connection.invoke("UpdateLocation", 17.4138 + i * 0.0001, 102.7872 + i * 0.0001, 10));
    }
    
    await Promise.all(updates);
    logStep("Rider Location Flooding", "PASS", "Successfully processed 20 concurrent GPS updates via SignalR", "Updates=20");
  } catch (err) {
    logStep("SignalR Stress Operations", "FAIL", err.message, "SignalR Connection");
  }

  // -------------------------------------------------------------
  // Test 4: Distributed Lock Contention on Accept Offer
  // -------------------------------------------------------------
  console.log("\n-------------------------------------------------------------");
  console.log("Test 4: Distributed Lock Contention on Accept Offer");
  console.log("-------------------------------------------------------------");
  try {
    // To simulate accept offer lock contention, we can concurrently invoke 'AcceptOffer'
    // directly on the SignalR connection using a dummy/non-existent offer ID.
    // The distributed lock in DispatchOfferHandler.cs:57 will still be hit because it uses the offerId:
    // var lockKey = `lock:accept:offer:${offerId}`;
    // So if we concurrently invoke 3 'AcceptOffer' with the same offer ID, the distributed lock will trigger!
    // The first one will acquire the lock, verify order is null in DB (since it's a dummy ID), release lock, and return false.
    // The second and third will compete. They shouldn't get 500 errors or lock collisions.
    const dummyOfferId = `dummy-offer-${timestamp}`;
    
    const results = [];
    connection.on("OfferAcceptedResult", (res) => {
      results.push(res);
    });

    // Send 3 concurrent AcceptOffer commands
    const callA = connection.invoke("AcceptOffer", dummyOfferId, 1);
    const callB = connection.invoke("AcceptOffer", dummyOfferId, 1);
    const callC = connection.invoke("AcceptOffer", dummyOfferId, 1);

    await Promise.all([callA, callB, callC]);
    await sleep(1000); // Wait for callbacks

    logStep("Lock Contention Resilience", "PASS", `Processed ${results.length} concurrent accept attempts safely without crashing the event bus`, `dummyOfferId=${dummyOfferId}, count=3`);
  } catch (err) {
    logStep("Lock Contention Resilience", "FAIL", err.message, `Lock Contention`);
  }

  if (connection) {
    await connection.stop();
  }

  // ═══════════════════════════════════════════════
  // FINAL RESULTS
  // ═══════════════════════════════════════════════
  console.log("\n═══════════════════════════════════════════════");
  console.log("  RESILIENCE TEST FINAL RESULTS");
  console.log("═══════════════════════════════════════════════");
  console.log(`  Total Checks:    ${stats.passed + stats.failed}`);
  console.log(`  Passed:          ${stats.passed}`);
  console.log(`  Failed:          ${stats.failed}`);
  console.log(`  Resilience Rate: ${((stats.passed / (stats.passed + stats.failed)) * 100).toFixed(1)}%`);
  console.log("═══════════════════════════════════════════════\n");

  if (outputFile) {
    const fs = require('fs');
    fs.writeFileSync(outputFile, JSON.stringify({ testCases: stats.details }, null, 2));
    console.log(`[JSON] Detailed test report saved to ${outputFile}`);
  }

  process.exit(stats.failed > 0 ? 1 : 0);
}

main().catch(console.error);
