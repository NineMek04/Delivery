/**
 * Flutter SignalR Compatibility Test Script
 * Simulates mobile client calls to TrackingHub partial methods:
 * - UpdateRiderLocation(lat, lng)
 * - UpdateRiderStatus(status) / UpdateStatus(status)
 */

'use strict';

const axios = require('axios');
const signalR = require('@microsoft/signalr');

const API = process.env.DELIVERY_API_URL || 'http://localhost:5000/api/v1';
const HUB = process.env.DELIVERY_HUB_URL || 'http://localhost:5000/hubs/tracking';
const ADMIN_CREDS = {
  email: process.env.DELIVERY_ADMIN_EMAIL || 'admin@delivery.com',
  password: process.env.DELIVERY_ADMIN_PASSWORD || 'Password123!'
};
const RIDER_CREDS = {
  email: 'flutter-rider-test@delivery.test',
  password: 'Password123!',
  fullName: 'Flutter Compat Tester',
  role: 'Rider'
};

const outputFile = process.argv[2]; // e.g. /tmp/results.json
const stats = {
  passed: 0,
  failed: 0,
  details: []
};

function logTest(name, status, details = "", inputs = "N/A") {
  if (status === "PASS") stats.passed++;
  else stats.failed++;
  
  stats.details.push({
    name,
    location: "e2e-simulator/test-flutter-compat.js",
    inputs,
    status,
    durationMs: 0,
    error: status === "FAIL" ? details : null
  });
}

function finishProcess(code) {
  if (outputFile) {
    const fs = require('fs');
    fs.writeFileSync(outputFile, JSON.stringify({ testCases: stats.details }, null, 2));
    console.log(`[JSON] Detailed test report saved to ${outputFile}`);
  }
  process.exit(code);
}

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

async function loginOrRegisterRider() {
  try {
    console.log(`[Auth] Registering rider: ${RIDER_CREDS.email}`);
    const response = await axios.post(`${API}/auth/register`, RIDER_CREDS);
    return response.data?.value || response.data?.Value || response.data;
  } catch (error) {
    if (error.response?.status === 409) {
      console.log(`[Auth] Rider already exists, logging in instead.`);
      const response = await axios.post(`${API}/auth/login`, {
        email: RIDER_CREDS.email,
        password: RIDER_CREDS.password
      });
      return response.data?.value || response.data?.Value || response.data;
    }
    throw error;
  }
}

async function loginAdmin() {
  console.log(`[Auth] Logging in as admin...`);
  const response = await axios.post(`${API}/auth/login`, ADMIN_CREDS);
  return response.data?.value || response.data?.Value || response.data;
}

async function runTest() {
  console.log('==================================================');
  console.log('STARTING FLUTTER SIGNALR COMPATIBILITY TEST');
  console.log('==================================================');

  // 1. Authenticate Admin and Rider
  const adminAuth = await loginAdmin();
  const adminToken = adminAuth.accessToken || adminAuth.AccessToken;

  const riderAuth = await loginOrRegisterRider();
  const riderToken = riderAuth.accessToken || riderAuth.AccessToken;
  const riderId = riderAuth.user?.riderId || riderAuth.user?.RiderId;

  console.log(`[Auth] Admin Token acquired.`);
  console.log(`[Auth] Rider Token acquired. Rider ID: ${riderId}`);

  // 2. Connect Admin to TrackingHub (to listen for broadcasts)
  console.log(`[Hub] Connecting Admin client...`);
  const adminConn = new signalR.HubConnectionBuilder()
    .withUrl(HUB, {
      accessTokenFactory: () => adminToken,
      skipNegotiation: true,
      transport: signalR.HttpTransportType.WebSockets
    })
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  let locationBroadcastReceived = false;
  let statusBroadcastReceived = false;

  adminConn.on('RiderLocationUpdated', (data) => {
    console.log(`[Admin HUB] Received RiderLocationUpdated event:`, JSON.stringify(data, null, 2));
    if (data.riderId === riderId || data.RiderId === riderId) {
      locationBroadcastReceived = true;
    }
  });

  adminConn.on('RiderStatusUpdated', (data) => {
    console.log(`[Admin HUB] Received RiderStatusUpdated event:`, JSON.stringify(data, null, 2));
    if (data.riderId === riderId || data.RiderId === riderId) {
      statusBroadcastReceived = true;
    }
  });

  await adminConn.start();
  console.log(`[Hub] Admin connected successfully.`);

  // 3. Connect Rider to TrackingHub
  console.log(`[Hub] Connecting Rider client...`);
  const riderConn = new signalR.HubConnectionBuilder()
    .withUrl(HUB, {
      accessTokenFactory: () => riderToken,
      skipNegotiation: true,
      transport: signalR.HttpTransportType.WebSockets
    })
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  let statusResultReceived = false;
  riderConn.on('RiderStatusUpdatedResult', (result) => {
    console.log(`[Rider HUB] Received RiderStatusUpdatedResult:`, JSON.stringify(result, null, 2));
    statusResultReceived = true;
  });

  await riderConn.start();
  console.log(`[Hub] Rider connected successfully.`);

  // Give a small pause for hub registration to settle
  await sleep(1000);

  // 4. Test GPS update: UpdateRiderLocation(lat, lng)
  console.log(`\n[Test 1] Invoking UpdateRiderLocation...`);
  const testLat = 17.4138 + (Math.random() - 0.5) * 0.01;
  const testLng = 102.7872 + (Math.random() - 0.5) * 0.01;
  
  await riderConn.invoke('UpdateRiderLocation', testLat, testLng);
  console.log(`[Test 1] UpdateRiderLocation invoked successfully for lat: ${testLat}, lng: ${testLng}`);

  // 5. Test Status update: UpdateRiderStatus(status)
  console.log(`\n[Test 2] Invoking UpdateRiderStatus("AVAILABLE") (case-insensitive conversion to IDLE)...`);
  const success1 = await riderConn.invoke('UpdateRiderStatus', 'AVAILABLE');
  console.log(`[Test 2] UpdateRiderStatus result returned directly: ${success1}`);

  await sleep(1000);

  // 6. Test Status update: UpdateStatus(status)
  console.log(`\n[Test 3] Invoking UpdateStatus("OFFLINE")...`);
  const success2 = await riderConn.invoke('UpdateStatus', 'OFFLINE');
  console.log(`[Test 3] UpdateStatus result returned directly: ${success2}`);

  await sleep(1500);

  // 7. Verify all assertions
  console.log('\n==================================================');
  console.log('VERIFYING TEST RESULTS');
  console.log('==================================================');
  
  logTest("Location Broadcast Received", locationBroadcastReceived ? "PASS" : "FAIL", "Admin received location update", "SignalR: RiderLocationUpdated");
  logTest("Status Broadcast Received", statusBroadcastReceived ? "PASS" : "FAIL", "Admin received status update", "SignalR: RiderStatusUpdated");
  logTest("Status Result Received", statusResultReceived ? "PASS" : "FAIL", "Rider received status acknowledgment", "SignalR: RiderStatusUpdatedResult");
  
  console.log(`[Assert] Location Broadcast Received by Admin: ${locationBroadcastReceived ? '✅ PASSED' : '❌ FAILED'}`);
  console.log(`[Assert] Status Broadcast Received by Admin: ${statusBroadcastReceived ? '✅ PASSED' : '❌ FAILED'}`);
  console.log(`[Assert] RiderStatusUpdatedResult Received by Caller: ${statusResultReceived ? '✅ PASSED' : '❌ FAILED'}`);
  
  const allPassed = locationBroadcastReceived && statusBroadcastReceived && statusResultReceived;
  console.log('==================================================');
  if (allPassed) {
    console.log('ALL TESTS PASSED SUCCESSFULLY! 🎉');
  } else {
    console.error('SOME COMPATIBILITY TESTS FAILED. ❌');
  }
  console.log('==================================================');

  // Cleanup
  await riderConn.stop();
  await adminConn.stop();
  finishProcess(allPassed ? 0 : 1);
}

runTest().catch(error => {
  console.error('\nTest crashed:', error.message);
  logTest("Flutter Compat Flow", "FAIL", error.message, "runTest()");
  if (error.response) {
    console.error('HTTP Status:', error.response.status);
    console.error('Response:', JSON.stringify(error.response.data, null, 2));
  }
  finishProcess(1);
});
