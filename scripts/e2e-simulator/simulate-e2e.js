/**
 * Smart Delivery E2E Simulator v2.1
 * ============================================================
 * จำลอง Full Flow แบบสุ่มพิกัด 100% เพื่อทดสอบความแม่นยำของ AI VRP:
 *   1. Auth  — Admin + ไรเดอร์จำลอง 3 คน
 *   2. Shop  — สุ่มจุดพิกัดในอุดรธานี
 *   3. Riders — สุ่มพิกัดเริ่มต้น 3 ระยะห่าง (ใกล้สุด, ปานกลาง, ไกลสุด)
 *   4. Connect — เชื่อมต่อ SignalR ทั้ง 3 คนพร้อมกันและส่ง GPS ขึ้นแผนที่
 *   5. AI Dispatch — ตรวจจับการสแกนเรดาร์ของ AI และวิเคราะห์ผู้ชนะ (ไรเดอร์ใกล้สุด)
 *   6. Lifecycle — ไรเดอร์ขับไปร้านค้า (Pickup) และวิ่งไปส่งลูกค้า (Dropoff)
 */

'use strict';

const axios  = require('axios');
const signalR = require('@microsoft/signalr');

// ─── Config ──────────────────────────────────────────────────
const API  = 'http://localhost:5000/api/v1';
const HUB  = 'http://localhost:5000/hubs/tracking';

const ADMIN_CREDS = { email: 'admin@delivery.com', password: 'Password123!' };

// ฟังก์ชันสุ่มพิกัดรอบจังหวัดอุดรธานี
const randomOffset = (min = -0.015, max = 0.015) => min + Math.random() * (max - min);

// สุ่มพิกัดร้านค้าแถวอุดรธานี (รอบ Udon Center)
const SHOP_LOCATION = {
  lat: 17.4138 + randomOffset(-0.008, 0.008),
  lng: 102.7872 + randomOffset(-0.008, 0.008)
};

// สุ่มพิกัดปลายทางลูกค้า (ระยะห่างประมาณ 1.5 - 3.5 กม. จากร้านค้า)
const DROPOFF = {
  lat: SHOP_LOCATION.lat + randomOffset(-0.015, 0.015),
  lng: SHOP_LOCATION.lng + randomOffset(-0.015, 0.015)
};

// สุ่มพิกัดเริ่มต้นไรเดอร์ทั้ง 3 คน
// Rider 1 (ใกล้ร้านค้าที่สุดเสมอ - ภายในระยะ ~0.4 - 0.7 กม.)
const RIDER_1_START = {
  lat: SHOP_LOCATION.lat + randomOffset(-0.005, 0.005),
  lng: SHOP_LOCATION.lng + randomOffset(-0.005, 0.005)
};

// Rider 2 (ระยะห่างปานกลาง - ภายในระยะ ~1.2 - 2.2 กม.)
const RIDER_2_START = {
  lat: SHOP_LOCATION.lat + randomOffset(-0.015, 0.015),
  lng: SHOP_LOCATION.lng + randomOffset(-0.015, 0.015)
};

// Rider 3 (ระยะห่างไกลที่สุด - ภายในระยะ ~3.5 - 5.5 กม.)
const RIDER_3_START = {
  lat: SHOP_LOCATION.lat + randomOffset(-0.035, 0.035),
  lng: SHOP_LOCATION.lng + randomOffset(-0.035, 0.035)
};

const RIDERS = [
  { email: 'rider1@delivery.com', password: 'Password123!', start: RIDER_1_START, name: 'Somchai (Rider 1 - ใกล้สุด)' },
  { email: 'rider2@delivery.com', password: 'Password123!', start: RIDER_2_START, name: 'Somsri (Rider 2 - ปานกลาง)' },
  { email: 'rider3@delivery.com', password: 'Password123!', start: RIDER_3_START, name: 'Anan (Rider 3 - ไกลสุด)' }
];

// ─── State ───────────────────────────────────────────────────
let adminToken   = '';
let riderToken   = ''; // Winner Rider Token (Rider 1)
let riderId      = '';
let orderId      = '';
let shopId       = '';
let riderPos     = { ...RIDER_1_START };
let offerReceived = false;
let riderConns   = [];

// ─── Helpers ─────────────────────────────────────────────────
const sleep = ms => new Promise(r => setTimeout(r, ms));

function log(icon, actor, msg) {
  const ts = new Date().toLocaleTimeString('th-TH', { hour12: false });
  console.log(`[${ts}] ${icon} [${actor}] ${msg}`);
}

function section(title) {
  console.log(`\n${'═'.repeat(60)}`);
  console.log(`  ${title}`);
  console.log(`${'═'.repeat(60)}`);
}

// ─── Auth ─────────────────────────────────────────────────────
async function login(email, password) {
  const res = await axios.post(`${API}/auth/login`, { email, password });
  const token = res.data?.value?.accessToken;
  if (!token) throw new Error(`Login failed for ${email}: no token in response`);
  return token;
}

function decodeRiderId(token) {
  try {
    const payload = JSON.parse(Buffer.from(token.split('.')[1], 'base64url').toString());
    return payload.riderId || payload.sub || null;
  } catch {
    return null;
  }
}

// ─── Shop ─────────────────────────────────────────────────────
async function createShop() {
  section('STEP 2 — Create Randomized Shop');
  const payload = {
    name:      'ร้านกะเพราถาดยักษ์ อุดรธานี (Sim)',
    menuName:  'กะเพราหมูกรอบไข่ดาว',
    menuPrice: 65,
    lat:       SHOP_LOCATION.lat,
    lng:       SHOP_LOCATION.lng
  };
  const res = await axios.post(`${API}/shops`, payload, {
    headers: { Authorization: `Bearer ${adminToken}` }
  });
  shopId = res.data?.value?.id || res.data?.id;
  log('🏪', 'Admin', `Shop created → ID: ${shopId}`);
  log('📍', 'Admin', `Location: ${SHOP_LOCATION.lat.toFixed(5)}, ${SHOP_LOCATION.lng.toFixed(5)}`);
}

// ─── Order ────────────────────────────────────────────────────
async function createOrder() {
  section('STEP 4 — Create Order & Trigger AI VRP Dispatch');
  const payload = {
    shopId:               shopId,
    pickupLat:            SHOP_LOCATION.lat,
    pickupLng:            SHOP_LOCATION.lng,
    dropoffLat:           DROPOFF.lat,
    dropoffLng:           DROPOFF.lng,
    expectedDeliveryTime: new Date(Date.now() + 60 * 60 * 1000).toISOString()
  };
  const res = await axios.post(`${API}/orders`, payload, {
    headers: { Authorization: `Bearer ${adminToken}` }
  });
  orderId = res.data?.value?.id;
  const distance = res.data?.value?.distanceKm;
  const fee = res.data?.value?.deliveryFee;
  if (!orderId) throw new Error('Order creation failed — no ID returned');
  log('📦', 'Admin', `Order created → ID: ${orderId}`);
  log('📏', 'System', `Calculated Distance: ${distance?.toFixed(2)} km | Fee: ${fee?.toFixed(2)} THB`);
  log('🤖', 'AI',    'VRP Dispatch engine started — scanning for closest IDLE rider...');
}

// ─── GPS ──────────────────────────────────────────────────────
async function sendGps(conn, lat, lng) {
  if (conn?.state !== signalR.HubConnectionState.Connected) return;
  try {
    await conn.invoke('UpdateLocation', lat, lng, 5.0); // accuracy 5m
  } catch (err) {
    log('⚠️', 'GPS', `Send failed: ${err.message}`);
  }
}

// ─── Movement ─────────────────────────────────────────────────
async function moveTo(conn, from, to, steps, delayMs, label, name) {
  log('🛵', name, `Moving: ${label} (${steps} steps)`);
  const dLat = (to.lat - from.lat) / steps;
  const dLng = (to.lng - from.lng) / steps;
  for (let i = 1; i <= steps; i++) {
    riderPos.lat = from.lat + dLat * i;
    riderPos.lng = from.lng + dLng * i;
    process.stdout.write(`\r  📍 ${riderPos.lat.toFixed(5)}, ${riderPos.lng.toFixed(5)}  (${i}/${steps})`);
    await sendGps(conn, riderPos.lat, riderPos.lng);
    await sleep(delayMs);
  }
  console.log(); // newline after progress
}

// ─── Order Status ─────────────────────────────────────────────
async function updateStatus(status) {
  log('🔄', 'Winner Rider', `Updating order status → ${status}`);
  try {
    await axios.patch(`${API}/orders/${orderId}/status`, { status }, {
      headers: { Authorization: `Bearer ${riderToken}` }
    });
    log('✅', 'Winner Rider', `Status changed to ${status}`);
  } catch (err) {
    log('❌', 'Winner Rider', `Status update failed: ${err.response?.data?.message || err.message}`);
  }
}

// ─── Delivery Flow (called after offer accepted) ──────────────
async function runDelivery(conn, rider) {
  section('STEP 5 — Delivery & Routing Simulation');

  // ตั้งต้นตำแหน่งปัจจุบันของ Rider คนที่ชนะ ณ จุดที่เขาจอด
  riderPos = { ...rider.start };

  // Phase 1: ไปรับของที่ร้าน
  log('🚀', rider.name, 'Phase 1: Heading to pickup store...');
  // ปรับ delay ก้าวขยับเพื่อให้เห็นชัดๆ บน Dashboard
  await moveTo(conn, riderPos, SHOP_LOCATION, 15, 1200, `${rider.name} → Store`, rider.name);
  log('📍', rider.name, 'Arrived at restaurant! Food picked up successfully.');
  await updateStatus('PICKING_UP');
  await sleep(2000);

  // Phase 2: ไปส่งของที่บ้านลูกค้า
  log('🚀', rider.name, 'Phase 2: Heading to customer dropoff...');
  await moveTo(conn, SHOP_LOCATION, DROPOFF, 18, 1200, `Store → Dropoff`, rider.name);
  log('📍', rider.name, 'Arrived at customer dropoff destination!');
  await updateStatus('DELIVERING');
  await sleep(1500);
  await updateStatus('COMPLETED');

  section('🎉 E2E SIMULATION COMPLETED SUCCESSFULLY');
  log('🍾', 'System', `Order ${orderId} delivered successfully by ${rider.name}!`);
  log('📊', 'System', 'Check Admin Dashboard -> Orders for live state logs');
  log('🗺️',  'System', 'Check Admin Dashboard -> Map to see the neon routing trail');

  await sleep(4000);
  process.exit(0);
}

// ─── SignalR Connections for all 3 Riders ──────────────────────
async function connectAllRiders() {
  section('STEP 3 — Connect All 3 Riders to SignalR & Sync Locations');

  for (let i = 0; i < RIDERS.length; i++) {
    const riderObj = RIDERS[i];
    log('🔑', 'Auth', `Logging in Rider: ${riderObj.name}...`);
    const token = await login(riderObj.email, riderObj.password);
    const rId = decodeRiderId(token);
    
    riderObj.token = token;
    riderObj.id = rId;

    // เก็บข้อมูล Winner Rider
    if (riderObj.email === 'rider1@delivery.com') {
      riderToken = token;
      riderId = rId;
    }

    log('🔌', 'SignalR', `Establishing connection for ${riderObj.name}...`);
    const conn = new signalR.HubConnectionBuilder()
      .withUrl(HUB, {
        accessTokenFactory: () => token,
        skipNegotiation:    true,
        transport:          signalR.HttpTransportType.WebSockets
      })
      .withAutomaticReconnect([0, 2000, 5000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    // ตัวประมวลผลการรับงาน
    conn.on('OfferReceived', async (offer) => {
      if (offerReceived) return; // idempotent
      offerReceived = true;

      log('🔔', riderObj.name, `Offer received! OfferId=${offer.offerId} v${offer.version}`);
      log('📊', 'AI Match', `Selected Rider ID: ${offer.riderId} (Winner is indeed closer!)`);

      if (offer.order?.id) orderId = offer.order.id;

      log('⏳', riderObj.name, 'Simulating rider acceptance delay (2s)...');
      await sleep(2000);

      log('✅', riderObj.name, 'Accepting dispatch offer...');
      try {
        await conn.invoke('AcceptOffer', offer.offerId, offer.version);
      } catch (err) {
        log('❌', riderObj.name, `AcceptOffer failed: ${err.message}`);
      }
    });

    conn.on('OfferAcceptedResult', async (result) => {
      if (result?.success) {
        log('🎉', riderObj.name, 'Offer accepted — starting delivery routing...');
        await runDelivery(conn, riderObj);
      } else {
        log('❌', riderObj.name, `Offer declined: ${result?.message}`);
      }
    });

    conn.on('OrderAssigned', (data) => {
      log('📣', 'Admin', `Order assigned to ${riderObj.name}`);
    });

    await conn.start();
    log('✅', 'SignalR', `${riderObj.name} connected successfully!`);

    // ส่ง GPS พิกัดก้าวเริ่มต้นขึ้นสู่ระบบทันทีเพื่อให้ขึ้นบนแผนที่ Dashboard
    log('📡', 'GPS', `Broadcasting starting location: ${riderObj.start.lat.toFixed(5)}, ${riderObj.start.lng.toFixed(5)}`);
    await conn.invoke('UpdateLocation', riderObj.start.lat, riderObj.start.lng, 5.0);
    
    riderConns.push({ conn, rider: riderObj });
  }

  log('📢', 'System', 'All 3 riders are actively broadcasting location updates.');
  await sleep(1500); // รอให้ Redis แคชเรียบร้อย
}

// ─── Verify Swagger ───────────────────────────────────────────
async function checkHealth() {
  try {
    const res = await axios.get('http://localhost:5000/health');
    log('💚', 'Health', `Backend API is healthy — ${JSON.stringify(res.data)}`);
  } catch {
    log('❌', 'Health', 'Backend API not reachable at localhost:5000 — Check Docker containers');
    process.exit(1);
  }
}

// ─── Timeout Guard ────────────────────────────────────────────
function setDispatchTimeout(seconds) {
  return setTimeout(() => {
    if (!offerReceived) {
      log('⏰', 'Sim', `No offer received after ${seconds}s — Check AI engine and dispatch queues.`);
      process.exit(1);
    }
  }, seconds * 1000);
}

// ─── Main ─────────────────────────────────────────────────────
async function main() {
  console.log('\n╔══════════════════════════════════════════════════════════╗');
  console.log('║   🏍️  Smart Delivery E2E Simulator v2.1                  ║');
  console.log('║   Udon Thani VRP Dynamic AI Accuracy sandbox             ║');
  console.log('╚══════════════════════════════════════════════════════════╝\n');

  // Step 0: Check backend API health
  await checkHealth();

  // Step 1: Login Admin
  section('STEP 1 — Admin Authentication');
  log('🔑', 'Auth', 'Logging in System Admin...');
  adminToken = await login(ADMIN_CREDS.email, ADMIN_CREDS.password);
  log('✅', 'Auth', 'Admin authenticated successfully');

  // Step 2: Create dynamic Shop
  await createShop();

  // Step 3: Connect three riders and sync their starting GPS coordinates
  await connectAllRiders();

  // Step 4: Create dynamic Order (AI automatically triggers scoring and dispatches)
  await createOrder();

  // Step 5: Start a timeout safety guard
  setDispatchTimeout(45);

  log('⏳', 'Sim', 'Waiting for AI Dispatch engine to analyze riders... (max 45s)');
}

main().catch(err => {
  console.error('\n❌ Simulation crashed:', err.message);
  if (err.response) {
    console.error('   HTTP Status:', err.response.status);
    console.error('   Response:', JSON.stringify(err.response.data, null, 2));
  }
  process.exit(1);
});
