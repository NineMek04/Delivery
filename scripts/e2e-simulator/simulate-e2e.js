/**
 * Smart Delivery E2E Simulator
 * ============================================================
 * จำลอง Full Flow ของระบบ:
 *   1. Auth  — Admin + Rider login
 *   2. Shop  — สร้างร้านค้าใหม่ผ่าน POST /shops
 *   3. Order — Admin สร้าง Order → AI Dispatch → Rider รับ Offer
 *   4. GPS   — Rider ส่ง GPS ระหว่างเดินทาง (SignalR)
 *   5. Lifecycle — PICKING_UP → DELIVERING → COMPLETED
 *
 * Seed Data (DataSeeder.cs):
 *   admin@delivery.com  / Password123!  (Admin)
 *   ops@delivery.com    / Password123!  (Dispatcher)
 *   rider1@delivery.com / Password123!  (Rider: 11111111-...)
 *   rider2@delivery.com / Password123!  (Rider: 22222222-...)
 *
 * Udon Thani coordinates (SRID 4326)
 */

'use strict';

const axios  = require('axios');
const signalR = require('@microsoft/signalr');

// ─── Config ──────────────────────────────────────────────────
const API  = 'http://localhost:5000/api/v1';
const HUB  = 'http://localhost:5000/hubs/tracking';

const ADMIN_CREDS  = { email: 'admin@delivery.com',  password: 'Password123!' };
const RIDER_CREDS  = { email: 'rider1@delivery.com', password: 'Password123!' };

// Udon Thani — ร้านอาหาร → บ้านลูกค้า
const SHOP_LOCATION  = { lat: 17.4138, lng: 102.7872 }; // Udon Center (pickup)
const DROPOFF        = { lat: 17.4038, lng: 102.8072 }; // UD Town (dropoff)
const RIDER_START    = { lat: 17.4200, lng: 102.7750 }; // จุดเริ่มต้น Rider (ใกล้ pickup ~1.5km)

// ─── State ───────────────────────────────────────────────────
let adminToken   = '';
let riderToken   = '';
let riderId      = '';
let orderId      = '';
let shopId       = '';
let riderPos     = { ...RIDER_START };
let hubConn      = null;
let offerReceived = false;

// ─── Helpers ─────────────────────────────────────────────────
const sleep = ms => new Promise(r => setTimeout(r, ms));

function log(icon, actor, msg) {
  const ts = new Date().toLocaleTimeString('th-TH', { hour12: false });
  console.log(`[${ts}] ${icon} [${actor}] ${msg}`);
}

function section(title) {
  console.log(`\n${'═'.repeat(55)}`);
  console.log(`  ${title}`);
  console.log(`${'═'.repeat(55)}`);
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
    // JWT claim ชื่อ "riderId" หรือ sub
    return payload.riderId || payload.sub || null;
  } catch {
    return null;
  }
}

// ─── Shop ─────────────────────────────────────────────────────
async function createShop() {
  section('STEP 2 — Create Shop');
  const payload = {
    name:      'ร้านส้มตำแม่นิด (Sim)',
    menuName:  'ส้มตำไข่เค็ม',
    menuPrice: 45,
    lat:       SHOP_LOCATION.lat,
    lng:       SHOP_LOCATION.lng
  };
  const res = await axios.post(`${API}/shops`, payload, {
    headers: { Authorization: `Bearer ${adminToken}` }
  });
  shopId = res.data?.value?.id || res.data?.id;
  log('🏪', 'Admin', `Shop created → ID: ${shopId}`);
  log('📍', 'Admin', `Location: ${SHOP_LOCATION.lat}, ${SHOP_LOCATION.lng}`);
}

// ─── Order ────────────────────────────────────────────────────
async function createOrder() {
  section('STEP 3 — Create Order & Trigger Dispatch');
  const payload = {
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
  if (!orderId) throw new Error('Order creation failed — no ID returned');
  log('📦', 'Admin', `Order created → ID: ${orderId}`);
  log('🤖', 'AI',    'Dispatch started — searching for nearest IDLE rider...');
}

// ─── GPS ──────────────────────────────────────────────────────
async function sendGps(lat, lng) {
  if (hubConn?.state !== signalR.HubConnectionState.Connected) return;
  try {
    await hubConn.invoke('UpdateLocation', lat, lng, 5.0); // accuracy 5m
  } catch (err) {
    log('⚠️', 'GPS', `Send failed: ${err.message}`);
  }
}

// ─── Movement ─────────────────────────────────────────────────
async function moveTo(from, to, steps, delayMs, label) {
  log('🛵', 'Rider', `Moving: ${label} (${steps} steps)`);
  const dLat = (to.lat - from.lat) / steps;
  const dLng = (to.lng - from.lng) / steps;
  for (let i = 1; i <= steps; i++) {
    riderPos.lat = from.lat + dLat * i;
    riderPos.lng = from.lng + dLng * i;
    process.stdout.write(`\r  📍 ${riderPos.lat.toFixed(5)}, ${riderPos.lng.toFixed(5)}  (${i}/${steps})`);
    await sendGps(riderPos.lat, riderPos.lng);
    await sleep(delayMs);
  }
  console.log(); // newline after progress
}

// ─── Order Status ─────────────────────────────────────────────
async function updateStatus(status) {
  log('🔄', 'Rider', `Updating order status → ${status}`);
  try {
    await axios.patch(`${API}/orders/${orderId}/status`, { status }, {
      headers: { Authorization: `Bearer ${riderToken}` }
    });
    log('✅', 'Rider', `Status changed to ${status}`);
  } catch (err) {
    log('❌', 'Rider', `Status update failed: ${err.response?.data?.message || err.message}`);
  }
}

// ─── Delivery Flow (called after offer accepted) ──────────────
async function runDelivery() {
  section('STEP 5 — Delivery Simulation');

  // Phase 1: ไปรับของที่ร้าน
  log('🚀', 'Rider', 'Phase 1: Heading to pickup...');
  await moveTo(riderPos, SHOP_LOCATION, 12, 800, 'Rider → Pickup');
  log('📍', 'Rider', 'Arrived at pickup!');
  await updateStatus('PICKING_UP');
  await sleep(1500);

  // Phase 2: ไปส่งของที่บ้านลูกค้า
  log('🚀', 'Rider', 'Phase 2: Heading to dropoff...');
  await moveTo(SHOP_LOCATION, DROPOFF, 15, 800, 'Pickup → Dropoff');
  log('📍', 'Rider', 'Arrived at dropoff!');
  await updateStatus('DELIVERING');
  await sleep(1000);
  await updateStatus('COMPLETED');

  section('✅ E2E SIMULATION COMPLETE');
  log('🎉', 'System', `Order ${orderId} delivered successfully!`);
  log('📊', 'System', 'Check Admin Dashboard → Orders for live status');
  log('🗺️',  'System', 'Check Admin Dashboard → Map for rider trail');

  await sleep(2000);
  process.exit(0);
}

// ─── SignalR ──────────────────────────────────────────────────
async function connectSignalR() {
  section('STEP 4 — Connect Rider to SignalR Hub');

  hubConn = new signalR.HubConnectionBuilder()
    .withUrl(HUB, {
      accessTokenFactory: () => riderToken,
      skipNegotiation:    true,
      transport:          signalR.HttpTransportType.WebSockets
    })
    .withAutomaticReconnect([0, 2000, 5000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  // ── Listeners ──
  hubConn.on('OfferReceived', async (offer) => {
    if (offerReceived) return; // idempotent
    offerReceived = true;

    log('🔔', 'Rider', `Offer received! OfferId=${offer.offerId} v${offer.version}`);
    log('📋', 'Rider', `Order: ${offer.order?.id} | Pickup: ${offer.order?.pickupLat?.toFixed(4)}, ${offer.order?.pickupLng?.toFixed(4)}`);

    // อัปเดต orderId จาก offer (กรณี script สร้าง order ใหม่)
    if (offer.order?.id) orderId = offer.order.id;

    log('⏳', 'Rider', 'Thinking... (2s)');
    await sleep(2000);

    log('✅', 'Rider', 'Accepting offer...');
    try {
      await hubConn.invoke('AcceptOffer', offer.offerId, offer.version);
    } catch (err) {
      log('❌', 'Rider', `AcceptOffer failed: ${err.message}`);
    }
  });

  hubConn.on('OfferAcceptedResult', async (result) => {
    if (result?.success) {
      log('🎉', 'Rider', 'Offer accepted — order locked!');
      await runDelivery();
    } else {
      log('❌', 'Rider', `Offer rejected by server: ${result?.message}`);
    }
  });

  hubConn.on('OrderAssigned', (data) => {
    log('📣', 'Admin', `Order ${data?.id?.slice(0,8)} assigned to Rider ${data?.riderId?.slice(0,8)}`);
  });

  hubConn.onreconnecting(() => log('🔄', 'SignalR', 'Reconnecting...'));
  hubConn.onreconnected(() => log('✅', 'SignalR', 'Reconnected'));
  hubConn.onclose(err => {
    if (err) log('❌', 'SignalR', `Connection closed: ${err.message}`);
  });

  await hubConn.start();
  log('✅', 'Rider', 'Connected to TrackingHub');
}

// ─── Verify Swagger ───────────────────────────────────────────
async function checkHealth() {
  try {
    const res = await axios.get('http://localhost:5000/health');
    log('💚', 'Health', `Backend healthy — ${JSON.stringify(res.data)}`);
  } catch {
    log('❌', 'Health', 'Backend not reachable at localhost:5000 — is Docker running?');
    process.exit(1);
  }
}

// ─── Timeout Guard ────────────────────────────────────────────
function setDispatchTimeout(seconds) {
  return setTimeout(() => {
    if (!offerReceived) {
      log('⏰', 'Sim', `No offer received after ${seconds}s — possible causes:`);
      log('   ', '   ', '1. Rider not in Redis GEORADIUS (no GPS sent before order)');
      log('   ', '   ', '2. AI Engine unreachable (check delivery-ai container)');
      log('   ', '   ', '3. Rider state not IDLE in DB');
      log('   ', '   ', `Tip: Check logs → docker logs delivery-backend --tail 50`);
      process.exit(1);
    }
  }, seconds * 1000);
}

// ─── Main ─────────────────────────────────────────────────────
async function main() {
  console.log('\n╔══════════════════════════════════════════════════════╗');
  console.log('║   🏍️  Smart Delivery E2E Simulator v2.0              ║');
  console.log('║   Udon Thani — Full Dispatch Flow                    ║');
  console.log('╚══════════════════════════════════════════════════════╝\n');

  // ── Step 0: Health check ──
  section('STEP 0 — Health Check');
  await checkHealth();

  // ── Step 1: Auth ──
  section('STEP 1 — Authentication');
  log('🔑', 'Auth', 'Logging in Admin...');
  adminToken = await login(ADMIN_CREDS.email, ADMIN_CREDS.password);
  log('✅', 'Auth', 'Admin token acquired');

  log('🔑', 'Auth', 'Logging in Rider...');
  riderToken = await login(RIDER_CREDS.email, RIDER_CREDS.password);
  riderId = decodeRiderId(riderToken);
  log('✅', 'Auth', `Rider token acquired — RiderId: ${riderId || '(from DB)'}`);

  // ── Step 2: Create Shop ──
  await createShop();

  // ── Step 3: Connect Rider SignalR ──
  await connectSignalR();

  // ── Step 4: Rider broadcasts initial GPS (ต้องทำก่อน create order เพื่อให้ Redis GEORADIUS เจอ) ──
  log('📡', 'Rider', 'Broadcasting initial GPS position to Redis...');
  await sendGps(RIDER_START.lat, RIDER_START.lng);
  await sleep(1500); // รอให้ Redis อัปเดต

  // ── Step 5: Create Order → triggers AI Dispatch ──
  await createOrder();

  // ── Timeout guard: ถ้า 45 วินาทีแล้วยังไม่ได้ offer ให้ exit ──
  setDispatchTimeout(45);

  log('⏳', 'Sim', 'Waiting for AI Dispatch offer... (max 45s)');
}

main().catch(err => {
  console.error('\n❌ Simulation crashed:', err.message);
  if (err.response) {
    console.error('   HTTP Status:', err.response.status);
    console.error('   Response:', JSON.stringify(err.response.data, null, 2));
  }
  process.exit(1);
});
