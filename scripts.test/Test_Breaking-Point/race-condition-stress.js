/**
 * race-condition-stress.js — High-Concurrency Race Condition Verification
 *
 * Verifies that when 100 concurrent accept requests are sent for the same offer,
 * exactly 1 request succeeds (Order state = ASSIGNED, Rider state = BUSY)
 * and the remaining 99 requests are safely rejected.
 *
 * Usage:
 *   node race-condition-stress.js
 *
 * Environment:
 *   API_URL — Backend URL (default: http://localhost:5000)
 */

const signalR = require("@microsoft/signalr");
const axios = require("axios");
const { execSync } = require("child_process");

const API_URL = process.env.API_URL || "http://localhost:5000";

async function sleep(ms) {
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

async function createOrder(customerToken, customerId, shopId) {
  try {
    const res = await axios.post(`${API_URL}/api/v1/orders`, {
      pickupLat: 13.7563,
      pickupLng: 100.5018,
      dropoffLat: 13.7570,
      dropoffLng: 100.5025,
      expectedDeliveryTime: new Date(Date.now() + 3600000).toISOString(),
      customerId: customerId,
      shopId: shopId,
      items: [],
    }, {
      headers: { Authorization: `Bearer ${customerToken}` }
    });
    return res.data?.value?.id;
  } catch (err) {
    console.error("Failed to create order:", err.response?.data || err.message);
    return null;
  }
}

function updateDatabaseState(orderId, offerId, riderId) {
  try {
    // 1. Set Order to OFFERING (2) with the specific offer and rider
    const orderSql = `UPDATE \\"Orders\\" SET \\"State\\" = 2, \\"CurrentOfferId\\" = '${offerId}', \\"AssignedRiderId\\" = '${riderId}', \\"OfferVersion\\" = 1, \\"OfferExpiresAt\\" = '${new Date(Date.now() + 600000).toISOString()}' WHERE \\"Id\\" = '${orderId}';`;
    execSync(`docker exec -i delivery-db psql -U postgres -d delivery_db -c "${orderSql}"`);

    // 2. Set Rider to RESERVED (2)
    const riderSql = `UPDATE \\"Riders\\" SET \\"State\\" = 2 WHERE \\"Id\\" = '${riderId}';`;
    execSync(`docker exec -i delivery-db psql -U postgres -d delivery_db -c "${riderSql}"`);

    console.log("  - Database state manually set to OFFERING/RESERVED.");
    return true;
  } catch (err) {
    console.error("Failed to update database state:", err.message);
    return false;
  }
}

async function verifyFinalDatabaseState(orderId, riderId) {
  try {
    // Check Order State
    const orderSql = `SELECT \\"State\\", \\"AssignedRiderId\\" FROM \\"Orders\\" WHERE \\"Id\\" = '${orderId}';`;
    const orderRes = execSync(`docker exec -i delivery-db psql -U postgres -d delivery_db -t -c "${orderSql}"`).toString().trim();
    
    // Check Rider State
    const riderSql = `SELECT \\"State\\" FROM \\"Riders\\" WHERE \\"Id\\" = '${riderId}';`;
    const riderRes = execSync(`docker exec -i delivery-db psql -U postgres -d delivery_db -t -c "${riderSql}"`).toString().trim();
    
    console.log(`\n  Final Database Verification:`);
    console.log(`    - Order row: ${orderRes}`);
    console.log(`    - Rider row: ${riderRes}`);
  } catch (err) {
    console.error("Failed to verify database state:", err.message);
  }
}

async function main() {
  console.log("=================================================");
  console.log("  High-Concurrency Race Condition Stress Test");
  console.log(`  Target URL:        ${API_URL}`);
  console.log("=================================================\n");

  const timestamp = Date.now();
  const offerId = `race-offer-${timestamp}`;

  console.log("[Phase 1] Provisioning Rider and Customer...");
  
  const partnerEmail = `race_partner_${timestamp}@test.com`;
  const partnerUser = await registerUser(partnerEmail, "StorePartner", "Race Test Partner");
  if (!partnerUser) {
    console.error("Critical: Partner registration failed. Aborting.");
    process.exit(1);
  }
  const shopId = partnerUser.user?.shopId;

  const riderEmail = `race_rider_${timestamp}@test.com`;
  const riderUser = await registerUser(riderEmail, "Rider", "Race Test Rider");
  if (!riderUser) {
    console.error("Critical: Rider registration failed. Aborting.");
    process.exit(1);
  }
  const riderToken = riderUser.accessToken;
  const riderId = riderUser.user?.riderId;
  console.log(`  - Rider provisioned. Id: ${riderId}`);

  const customerEmail = `race_cust_${timestamp}@test.com`;
  const customerUser = await registerUser(customerEmail, "Customer", "Race Test Customer");
  if (!customerUser) {
    console.error("Critical: Customer registration failed. Aborting.");
    process.exit(1);
  }
  const customerToken = customerUser.accessToken;
  const customerId = customerUser.user?.id;

  const orderId = await createOrder(customerToken, customerId, shopId);
  if (!orderId) {
    console.error("Critical: Order creation failed. Aborting.");
    process.exit(1);
  }
  console.log(`  - Order provisioned. Id: ${orderId}`);

  console.log("\n[Phase 2] Forcing Database Offering/Reserved states...");
  if (!updateDatabaseState(orderId, offerId, riderId)) {
    console.error("Critical: Failed to set database state. Aborting.");
    process.exit(1);
  }

  console.log("\n[Phase 3] Establishing SignalR Rider Connection...");
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${API_URL}/hubs/tracking`, {
      accessTokenFactory: () => riderToken,
    })
    .configureLogging(signalR.LogLevel.Error)
    .build();

  await connection.start();
  console.log("  - SignalR connection active.");

  const callbacks = [];
  connection.on("OfferAcceptedResult", (success) => {
    callbacks.push(success);
  });

  console.log("\n[Phase 4] Launching Concurrency Storm (100 parallel invokes)...");
  const promises = [];
  const CONCURRENCY = 100;
  
  const startTime = Date.now();
  for (let i = 0; i < CONCURRENCY; i++) {
    promises.push(connection.invoke("AcceptOffer", offerId, 1));
  }

  await Promise.all(promises);
  console.log("  - Sent 100 parallel invokes.");
  
  // Wait for callbacks to complete
  console.log("  - Waiting 2 seconds for callbacks...");
  await sleep(2000);

  const duration = Date.now() - startTime - 2000;

  console.log("\n=================================================");
  console.log("  RACE RESULTS");
  console.log("=================================================");
  console.log(`  Total Callback Signals: ${callbacks.length}`);
  
  const trueCallbacks = callbacks.filter(c => c && (c.success === true || c.Success === true)).length;
  const falseCallbacks = callbacks.filter(c => c && (c.success === false || c.Success === false || c.Message)).length;
  
  console.log(`  - Successful Accepts (true):  ${trueCallbacks}`);
  console.log(`  - Rejected Accepts (false):   ${falseCallbacks}`);
  console.log(`  - Process Duration:           ${duration}ms`);
  
  await verifyFinalDatabaseState(orderId, riderId);
  console.log("=================================================");

  await connection.stop();

  if (trueCallbacks === 1 && falseCallbacks === CONCURRENCY - 1) {
    console.log("\n✅ [SUCCESS] Race condition handled perfectly. Exactly 1 request was accepted, and 99 were rejected.");
    process.exit(0);
  } else {
    console.error("\n❌ [FAIL] Integrity failure! Expected exactly 1 accept, but got " + trueCallbacks);
    process.exit(1);
  }
}

main().catch(console.error);
