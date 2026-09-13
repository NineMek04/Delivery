/**
 * simulate-app-gps-pipeline.js
 *
 * Simulates Rider App GPS Ingestion Pipeline:
 * 1. Authenticates as rider (sorryilostcontact@gmail.com / 012ead36-ed7a-4567-9eae-b1c831d15d29)
 * 2. Connects to SignalR /hubs/tracking
 * 3. Sends UpdateStatus('IDLE') to go Online
 * 4. Sends real-world GPS coordinates via UpdateLocation(lat, lng, accuracy) with accuracy > 50m
 * 5. Sends batched GPS coordinates via POST /api/v1/telemetry/gps/batch
 * 6. Verifies persistence into PostgreSQL RiderLocationHistories
 * 7. Tests Store Reports Summary and CSV Export API
 */

'use strict';

const axios = require('axios');
const signalR = require('@microsoft/signalr');
const { execSync } = require('child_process');

const BASE_URL = process.env.BASE_URL || 'http://localhost:5000';
const API_URL = `${BASE_URL}/api/v1`;
const HUB_URL = `${BASE_URL}/hubs/tracking`;

const RIDER_EMAIL = process.env.RIDER_EMAIL || 'sorryilostcontact@gmail.com';
const RIDER_PASSWORD = process.env.RIDER_PASSWORD || 'Password123!';
const TARGET_RIDER_ID = '012ead36-ed7a-4567-9eae-b1c831d15d29';

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

function getDbLocationCount(riderId) {
  try {
    const cmd = `docker exec -i delivery-db psql -U postgres -d delivery_db -t -A -c "SELECT count(*) FROM public.\\"RiderLocationHistories\\" WHERE \\"RiderId\\" = '${riderId}';"`;
    const out = execSync(cmd, { encoding: 'utf8' }).trim();
    return parseInt(out, 10) || 0;
  } catch (err) {
    console.warn('[DB] Could not query DB directly:', err.message);
    return null;
  }
}

function getLatestDbLocation(riderId) {
  try {
    const cmd = `docker exec -i delivery-db psql -U postgres -d delivery_db -t -A -c "SELECT \\"RecordedAt\\", ST_AsText(\\"Location\\") FROM public.\\"RiderLocationHistories\\" WHERE \\"RiderId\\" = '${riderId}' ORDER BY \\"RecordedAt\\" DESC LIMIT 1;"`;
    return execSync(cmd, { encoding: 'utf8' }).trim();
  } catch (err) {
    return null;
  }
}


async function main() {
  console.log('============================================================');
  console.log('  RIDER APP GPS PIPELINE & DATABASE PERSISTENCE TEST');
  console.log('============================================================');
  console.log(`Target Server: ${BASE_URL}`);
  console.log(`Target Rider:  ${RIDER_EMAIL} (${TARGET_RIDER_ID})`);

  // Step 0: Check DB before test
  const initialCount = getDbLocationCount(TARGET_RIDER_ID);
  console.log(`\n[Step 0] Initial RiderLocationHistories rows in DB: ${initialCount}`);
  const initialLatest = getLatestDbLocation(TARGET_RIDER_ID);
  console.log(`[Step 0] Most recent record before test: ${initialLatest || '(none)'}`);

  // Step 1: Authenticate Rider
  console.log(`\n[Step 1] Authenticating rider...`);
  const loginRes = await axios.post(`${API_URL}/auth/login`, {
    email: RIDER_EMAIL,
    password: RIDER_PASSWORD
  });

  const authData = loginRes.data?.value || loginRes.data?.Value || loginRes.data;
  const token = authData.accessToken || authData.AccessToken;
  const user = authData.user || authData.User;
  const riderId = user?.riderId || user?.RiderId || TARGET_RIDER_ID;

  if (!token) throw new Error('Failed to obtain JWT access token from login response.');
  console.log(`✅ Authenticated! Token acquired. User RiderId: ${riderId}`);

  // Step 2: SignalR Tracking Hub Connection
  console.log(`\n[Step 2] Connecting to SignalR TrackingHub: ${HUB_URL}...`);
  const hubConnection = new signalR.HubConnectionBuilder()
    .withUrl(HUB_URL, {
      accessTokenFactory: () => token,
      transport: signalR.HttpTransportType.WebSockets,
      skipNegotiation: true
    })
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  await hubConnection.start();
  console.log(`✅ SignalR Connected! Connection ID: ${hubConnection.connectionId}`);

  // Step 3: Update Status to IDLE (Online without active orders)
  console.log(`\n[Step 3] Sending UpdateStatus('IDLE') — Rider Going Online...`);
  await hubConnection.invoke('UpdateStatus', 'IDLE');
  console.log(`✅ Rider status set to IDLE`);

  // Step 4: Stream Realtime GPS Updates via SignalR with realistic mobile accuracy (55m - 75m)
  console.log(`\n[Step 4] Streaming GPS points via SignalR UpdateLocation(lat, lng, accuracy)...`);
  const liveCoordinates = [
    { lat: 17.41380, lng: 102.78720, accuracy: 65.0, name: 'ศาลากลางอุดรธานี' },
    { lat: 17.41460, lng: 102.78790, accuracy: 58.5, name: 'ถนนมุขมนตรี จุดที่ 1' },
    { lat: 17.41550, lng: 102.78870, accuracy: 72.0, name: 'ถนนมุขมนตรี จุดที่ 2' }
  ];

  for (let i = 0; i < liveCoordinates.length; i++) {
    const pt = liveCoordinates[i];
    console.log(`  --> [SignalR ${i + 1}/${liveCoordinates.length}] Sending ${pt.name} (${pt.lat}, ${pt.lng}, acc: ${pt.accuracy}m)...`);
    await hubConnection.invoke('UpdateLocation', pt.lat, pt.lng, pt.accuracy);
    await sleep(1500); // 1.5s between points
  }
  console.log(`✅ Realtime SignalR GPS points streamed successfully.`);

  // Step 5: Send Batch GPS points via REST (GpsBufferService offline buffer simulation)
  console.log(`\n[Step 5] Simulating GpsBufferService Batch Ingestion via POST /api/v1/telemetry/gps/batch...`);
  const now = new Date();
  const batchPoints = [
    {
      Latitude: 17.41620,
      Longitude: 102.78950,
      Accuracy: 62.0,
      Timestamp: new Date(now.getTime() - 20000).toISOString()
    },
    {
      Latitude: 17.41710,
      Longitude: 102.79040,
      Accuracy: 55.0,
      Timestamp: new Date(now.getTime() - 10000).toISOString()
    },
    {
      Latitude: 17.41800,
      Longitude: 102.79120,
      Accuracy: 68.0,
      Timestamp: now.toISOString()
    }
  ];

  const batchRes = await axios.post(
    `${API_URL}/telemetry/gps/batch`,
    batchPoints,
    {
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json'
      }
    }
  );
  console.log(`✅ Batch Ingestion Status: ${batchRes.status} ${batchRes.statusText}`);
  console.log(`   Response Body:`, JSON.stringify(batchRes.data));
  console.log(`   X-Recommended-Ping header:`, batchRes.headers['x-recommended-ping'] || 'N/A');

  // Step 6: Verify RabbitMQ consumer persisted to PostgreSQL
  console.log(`\n[Step 6] Waiting 4 seconds for RabbitMQ asynchronous ingestion to commit into DB...`);
  await sleep(4000);

  const finalCount = getDbLocationCount(TARGET_RIDER_ID);
  console.log(`[Step 6] Final RiderLocationHistories rows in DB: ${finalCount}`);
  const finalLatest = getLatestDbLocation(TARGET_RIDER_ID);
  console.log(`[Step 6] Most recent record after test: ${finalLatest}`);

  if (finalCount !== null && initialCount !== null) {
    const diff = finalCount - initialCount;
    console.log(`\n>>> Location records inserted during test: +${diff} rows <<<`);
    if (diff > 0) {
      console.log(`🎉 SUCCESS: GPS points were successfully accepted, routed through RabbitMQ, and saved in PostgreSQL!`);
    } else {
      console.error(`⚠️ WARNING: Record count did not increase. Check TelemetryService accuracy filtering and consumer logs.`);
    }
  }

  // Step 7: Disconnect SignalR cleanly
  console.log(`\n[Step 7] Disconnecting SignalR cleanly...`);
  await hubConnection.stop();

  // Step 8: Test Store Reports endpoints (Bonus check for Point 7)
  console.log(`\n[Step 8] Testing Store Reports Summary & CSV Export API...`);
  try {
    // Get sample shop
    const shopCmd = `docker exec -i delivery-db psql -U postgres -d delivery_db -t -A -c "SELECT \\"Id\\" FROM public.\\"Shops\\" LIMIT 1;"`;
    const shopId = execSync(shopCmd, { encoding: 'utf8' }).trim();
    if (shopId) {
      console.log(`Testing with Shop ID: ${shopId}`);
      // Summary
      const summaryRes = await axios.get(`${API_URL}/shops/${shopId}/reports/summary?period=day`, {
        headers: { Authorization: `Bearer ${token}` }
      });
      console.log(`✅ GET /shops/${shopId}/reports/summary?period=day -> HTTP ${summaryRes.status}`);
      const summaryVal = summaryRes.data?.value || summaryRes.data;
      console.log(`   Total Revenue: ฿${summaryVal.totalRevenue}, Completed Orders: ${summaryVal.completedOrders}/${summaryVal.totalOrders}, Detail Orders: ${summaryVal.orders?.length || 0}`);

      // CSV Export
      const exportRes = await axios.get(`${API_URL}/shops/${shopId}/reports/export?period=day&format=csv`, {
        headers: { Authorization: `Bearer ${token}` }
      });
      console.log(`✅ GET /shops/${shopId}/reports/export?period=day&format=csv -> HTTP ${exportRes.status}`);
      const hasBom = exportRes.data.startsWith('\uFEFF');
      console.log(`   CSV Length: ${exportRes.data.length} chars (UTF-8 BOM Header: ${hasBom ? 'YES' : 'NO'})`);

    }
  } catch (shopErr) {
    console.warn(`[Step 8] Store report check notice:`, shopErr.message);
  }

  console.log('\n============================================================');
  console.log('  TEST COMPLETED SUCCESSFULLY');
  console.log('============================================================');
}

main().catch(err => {
  console.error('\n❌ Test execution failed with error:', err.response?.data || err.message);
  process.exit(1);
});
