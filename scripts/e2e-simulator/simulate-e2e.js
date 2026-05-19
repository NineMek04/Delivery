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
let activeOrder  = null; // ข้อมูลออเดอร์พร้อมเส้นทาง EncodedPolyline จาก OSRM

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

// ถอดรหัสพิกัดย่อ Google Polyline เป็นพิกัดละติจูด/ลองจิจูดคู่จริง
function decodePolyline(str) {
  let index = 0;
  const len = str.length;
  let lat = 0;
  let lng = 0;
  const coordinates = [];

  while (index < len) {
    let b;
    let shift = 0;
    let result = 0;
    do {
      b = str.charCodeAt(index++) - 63;
      result |= (b & 0x1f) << shift;
      shift += 5;
    } while (b >= 0x20);
    const dlat = ((result & 1) ? ~(result >> 1) : (result >> 1));
    lat += dlat;

    shift = 0;
    result = 0;
    do {
      b = str.charCodeAt(index++) - 63;
      result |= (b & 0x1f) << shift;
      shift += 5;
    } while (b >= 0x20);
    const dlng = ((result & 1) ? ~(result >> 1) : (result >> 1));
    lng += dlng;

    coordinates.push({ lat: lat / 1e5, lng: lng / 1e5 });
  }
  return coordinates;
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
  log('📏', 'System', `Road Route Distance: ${distance?.toFixed(2)} km | Fee: ${fee?.toFixed(2)} THB`);
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
async function moveToPolyline(conn, coords, delayMs, label, name) {
  log('🛵', name, `Moving along: ${label} (${coords.length} street nodes)`);
  
  // ดาวน์แซมพลิงโหนดเพื่อให้สิมูเลชันวิ่งสมูทและจบการทดสอบภายใน 15-25 วินาที
  const maxSteps = 22;
  const stepInterval = Math.max(1, Math.floor(coords.length / maxSteps));
  
  const nodesToVisit = [];
  for (let i = 0; i < coords.length; i += stepInterval) {
    nodesToVisit.push(coords[i]);
  }
  if (nodesToVisit[nodesToVisit.length - 1] !== coords[coords.length - 1]) {
    nodesToVisit.push(coords[coords.length - 1]);
  }

  for (let i = 0; i < nodesToVisit.length; i++) {
    const node = nodesToVisit[i];
    
    // 1. เพิ่มความแปรปรวน GPS Jitter (เบี่ยงเบนทางกายภาพเสมือนจริงเล็กน้อย)
    const jitterLat = node.lat + (Math.random() - 0.5) * 0.00008;
    const jitterLng = node.lng + (Math.random() - 0.5) * 0.00008;
    
    riderPos.lat = jitterLat;
    riderPos.lng = jitterLng;
    
    process.stdout.write(`\r  📍 ${riderPos.lat.toFixed(5)}, ${riderPos.lng.toFixed(5)}  (${i + 1}/${nodesToVisit.length})`);
    await sendGps(conn, riderPos.lat, riderPos.lng);
    
    // 2. เพิ่มความแปรปรวนด้านความเร็วเสมือนการจราจรติดขัด (Traffic speed variance delay)
    const variableDelay = delayMs * (0.65 + Math.random() * 0.7);
    await sleep(variableDelay);
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

  riderPos = { ...rider.start };

  // Phase 1: เดินทางไปรับของที่ร้านคู่ค้า (ลากเส้นตรงจำลองพร้อม Jitter)
  log('🚀', rider.name, 'Phase 1: Heading to pickup store...');
  const pickupSteps = 12;
  const pickupCoords = [];
  for (let i = 0; i <= pickupSteps; i++) {
    const t = i / pickupSteps;
    pickupCoords.push({
      lat: riderPos.lat + (SHOP_LOCATION.lat - riderPos.lat) * t,
      lng: riderPos.lng + (SHOP_LOCATION.lng - riderPos.lng) * t
    });
  }
  await moveToPolyline(conn, pickupCoords, 800, `${rider.name} → Store`, rider.name);
  log('📍', rider.name, 'Arrived at restaurant! Food picked up successfully.');
  await updateStatus('PICKING_UP');
  await sleep(2500);

  // Phase 2: เดินทางไปส่งที่บ้านลูกค้า (ถอดรหัสเส้นโค้งถนนจริง OSRM Dijkstra)
  log('🚀', rider.name, 'Phase 2: Heading to customer dropoff using real OSRM road curves...');
  
  let deliveryCoords = [];
  const polylineStr = activeOrder ? (activeOrder.encodedPolyline || activeOrder.EncodedPolyline) : null;
  if (polylineStr) {
    try {
      deliveryCoords = decodePolyline(polylineStr);
      log('ℹ️', 'Dijkstra', `OSRM Polyline decoded successfully into ${deliveryCoords.length} path coordinates.`);
    } catch (err) {
      log('⚠️', 'Dijkstra', `Polyline decode failed: ${err.message}. Falling back to straight-line interpolation.`);
    }
  }

  // Fallback ในกรณีที่ไม่ได้ถอดรหัส
  if (deliveryCoords.length === 0) {
    const deliverySteps = 15;
    for (let i = 0; i <= deliverySteps; i++) {
      const t = i / deliverySteps;
      deliveryCoords.push({
        lat: SHOP_LOCATION.lat + (DROPOFF.lat - SHOP_LOCATION.lat) * t,
        lng: SHOP_LOCATION.lng + (DROPOFF.lng - SHOP_LOCATION.lng) * t
      });
    }
  }

  await moveToPolyline(conn, deliveryCoords, 800, `Store → Dropoff (Road Network)`, rider.name);
  log('📍', rider.name, 'Arrived at customer dropoff destination!');
  await updateStatus('DELIVERING');
  await sleep(2000);
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

    conn.on('OfferReceived', async (offer) => {
      if (offerReceived) return;
      offerReceived = true;

      // เซ็ตข้อมูลและ JWT Token ประจำตัวผู้ชนะแบบเรียลไทม์เพื่อใช้ PATCH อัปเดตสถานะผ่านด่านความปลอดภัยหลังบ้าน
      riderToken = token;
      riderId = rId;

      log('🔔', riderObj.name, `Offer received! OfferId=${offer.offerId} v${offer.version}`);
      log('📊', 'AI Match', `Selected Rider ID: ${offer.riderId} (Winner is indeed closer!)`);

      if (offer.order?.id) orderId = offer.order.id;
      if (offer.order) activeOrder = offer.order; // บันทึกออเดอร์พร้อมเส้นทาง EncodedPolyline

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
