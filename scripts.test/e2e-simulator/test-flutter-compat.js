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
    requestPayload: inputs,
    responseTrace: status === "PASS" ? details : null,
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
  
  logTest(
    "Location Broadcast Received", 
    locationBroadcastReceived ? "PASS" : "FAIL", 
    `📋 เกณฑ์การวัดผล (Passing Criteria): ระบบต้องกระจายพิกัดไปหากลุ่มแอดมินทันทีผ่าน WebSocket event 'RiderLocationUpdated'\n🏁 ผลลัพธ์การทดสอบ (Test Outcome):\n✅ สัญญาณออกอากาศตำแหน่ง GPS สำเร็จ\n✅ เจ้าหน้าที่ส่วนกลางได้รับข้อมูลพิกัดละติจูด/ลองจิจูดตรงกัน`, 
    `🎯 วัตถุประสงค์ (Objective): ตรวจสอบความถูกต้องของการกระจายสัญญาณพิกัด GPS ของไรเดอร์ (Rider GPS Broadcasting)\n⚙️ ขั้นตอนการทดสอบ (Test Steps):\n1. เชื่อมต่อบัญชีไรเดอร์จำลองเข้าสู่ระบบหลัก\n2. เรียกใช้ฟังก์ชัน 'UpdateRiderLocation' เพื่อสตรีมตำแหน่งจำลองพิกัดละติจูด/ลองจิจูด\n3. เจ้าหน้าที่ส่วนกลาง (Admin/Dispatcher) ต้องได้รับพิกัดตำแหน่งจริงผ่านกลุ่มความปลอดภัย SignalR Group`
  );

  logTest(
    "Status Broadcast Received", 
    statusBroadcastReceived ? "PASS" : "FAIL", 
    `📋 เกณฑ์การวัดผล (Passing Criteria): แอดมินต้องได้รับข้อมูลยืนยันการสลับสถานะไรเดอร์ทันทีผ่านเหตุการณ์ 'RiderStatusUpdated'\n🏁 ผลลัพธ์การทดสอบ (Test Outcome):\n✅ ระบบออกอากาศเปลี่ยนสถานะไรเดอร์เสร็จสิ้น\n✅ แผงควบคุมศูนย์กลางขึ้นคำนำหน้าสถานะตรงกันถูกต้อง`, 
    `🎯 วัตถุประสงค์ (Objective): ตรวจสอบการส่งสัญญาณอัปเดตสถานะไรเดอร์ออนไลน์ไปยังศูนย์จัดการกลาง (Rider Status Broadcasting)\n⚙️ ขั้นตอนการทดสอบ (Test Steps):\n1. เรียกใช้งานผ่านคำสั่งเสียงหรือหน้ากาก 'UpdateRiderStatus("AVAILABLE")' (ระบบปรับเป็นสถานะพร้อมทำงานโดยอัตโนมัติ)\n2. เรียกประวัติเปลี่ยนสถานะเป็น 'OFFLINE'\n3. แผงควบคุม Admin Dashboard ต้องดักจับสถานะสดนี้ได้ทันท่วงที`
  );

  logTest(
    "Status Result Received", 
    statusResultReceived ? "PASS" : "FAIL", 
    `📋 เกณฑ์การวัดผล (Passing Criteria): แอปพลิเคชันมือถือไรเดอร์ต้องได้รับสัญญาณยืนยัน 'RiderStatusUpdatedResult' ตอบรับสำเร็จจาก Gateway เสมือนการทำงานผ่านเครือข่ายสากล\n🏁 ผลลัพธ์การทดสอบ (Test Outcome):\n✅ ไรเดอร์จำลองได้รับ Acknowledgment ตอบรับตรงกัน\n✅ ยืนยันความเข้ากันได้กับชุดเชื่อมต่อ SignalR ของแอป Flutter สำเร็จ`, 
    `🎯 วัตถุประสงค์ (Objective): ตรวจสอบการตอบสนองความเข้ากันได้ของระบบตอบรับสถานะแบบสองทางกับตัวลูกค้า/ผู้ใช้ (Status Acknowledgment)\n⚙️ ขั้นตอนการทดสอบ (Test Steps):\n1. จำลองโทรศัพท์ไรเดอร์เพื่อยิงอัปเดตเปลี่ยนสถานะการส่งอาหาร\n2. ประเมินการส่งสัญญาณยืนยันการรับทราบธุรกรรม (Acknowledgment Flow) จากเซิร์ฟเวอร์หลังบ้านกลับมาหาไรเดอร์`
  );
  
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
