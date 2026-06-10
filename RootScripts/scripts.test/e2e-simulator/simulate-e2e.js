/**
 * Smart Delivery Realtime Simulator
 * Runs the dispatch flow before the Flutter rider app is ready:
 * - creates one randomized shop/order in Udon Thani
 * - creates 5-10 simulated riders around the shop
 * - broadcasts realtime GPS through SignalR
 * - waits for the backend AI dispatch offer
 * - accepts the offer as the selected rider
 * - moves rider to pickup and dropoff along real OSRM road polylines when available
 */

'use strict';

const axios = require('axios');
const signalR = require('@microsoft/signalr');

const API = process.env.DELIVERY_API_URL || 'http://localhost:5000/api/v1';
const HUB = process.env.DELIVERY_HUB_URL || 'http://localhost:5000/hubs/tracking';
const HEALTH_URL = process.env.DELIVERY_HEALTH_URL || 'http://localhost:5000/health';
const OSRM_URL = process.env.DELIVERY_OSRM_URL || 'http://localhost:5001';

const ADMIN_CREDS = {
  email: process.env.DELIVERY_ADMIN_EMAIL || 'admin@delivery.com',
  password: process.env.DELIVERY_ADMIN_PASSWORD || 'Password123!'
};

const PASSWORD = process.env.DELIVERY_SIM_PASSWORD || 'Password123!';
const RIDER_COUNT = Number(process.env.DELIVERY_SIM_RIDERS) || randomInt(12, 18);
const RUN_ID = new Date().toISOString().replace(/[-:.TZ]/g, '').slice(0, 14);
const UDON_CENTER = { lat: 17.4138, lng: 102.7872 };

let adminToken = '';
let orderId = '';
let activeOrder = null;
let offerAccepted = false;
let deliveryStarted = false;
let riderConnections = [];

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
    location: "e2e-simulator/simulate-e2e.js",
    inputs,
    status,
    durationMs: 0,
    error: status === "FAIL" ? details : null
  });

  console.log(`\n>> TEST_CASE_UPDATE | ${name} | ${status} | ${details} | ${inputs}\n`);
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

function randomInt(min, max) {
  return Math.floor(min + Math.random() * (max - min + 1));
}

function randomFloat(min, max) {
  return min + Math.random() * (max - min);
}

function randomPointAround(center, radiusKm) {
  const radiusInDegrees = radiusKm / 111.32;
  const angle = Math.random() * Math.PI * 2;
  const distance = Math.sqrt(Math.random()) * radiusInDegrees;
  return {
    lat: center.lat + Math.cos(angle) * distance,
    lng: center.lng + Math.sin(angle) * distance / Math.cos(center.lat * Math.PI / 180)
  };
}

function log(actor, message) {
  const ts = new Date().toLocaleTimeString('th-TH', { hour12: false });
  console.log(`[${ts}] [${actor}] ${message}`);
}

function section(title) {
  console.log(`\n${'='.repeat(72)}\n${title}\n${'='.repeat(72)}`);
}

function unwrapValue(response) {
  return response.data?.value || response.data?.Value || response.data;
}

function decodePolyline(str) {
  let index = 0;
  let lat = 0;
  let lng = 0;
  const coordinates = [];

  while (index < str.length) {
    let b;
    let shift = 0;
    let result = 0;
    do {
      b = str.charCodeAt(index++) - 63;
      result |= (b & 0x1f) << shift;
      shift += 5;
    } while (b >= 0x20);
    lat += (result & 1) ? ~(result >> 1) : (result >> 1);

    shift = 0;
    result = 0;
    do {
      b = str.charCodeAt(index++) - 63;
      result |= (b & 0x1f) << shift;
      shift += 5;
    } while (b >= 0x20);
    lng += (result & 1) ? ~(result >> 1) : (result >> 1);

    coordinates.push({ lat: lat / 1e5, lng: lng / 1e5 });
  }

  return coordinates;
}

function straightLine(start, end, steps = 18) {
  return Array.from({ length: steps + 1 }, (_, i) => {
    const t = i / steps;
    return {
      lat: start.lat + (end.lat - start.lat) * t,
      lng: start.lng + (end.lng - start.lng) * t
    };
  });
}

async function routeFromOsrm(start, end) {
  const url = `${OSRM_URL}/route/v1/driving/${start.lng},${start.lat};${end.lng},${end.lat}?overview=full&geometries=geojson`;
  const response = await axios.get(url, { timeout: 2500 });
  const coords = response.data?.routes?.[0]?.geometry?.coordinates || [];
  if (!coords.length) throw new Error('OSRM returned no route coordinates');
  return coords.map(([lng, lat]) => ({ lat, lng }));
}

async function bestRoute(start, end, encodedPolyline, label) {
  let routeCoords = [];
  if (encodedPolyline) {
    try {
      const decoded = decodePolyline(encodedPolyline);
      if (decoded.length > 1) {
        log('Route', `${label}: using backend encoded road polyline (${decoded.length} points)`);
        routeCoords = decoded;
      }
    } catch (error) {
      log('Route', `${label}: backend polyline decode failed (${error.message})`);
    }
  }

  if (!routeCoords.length) {
    try {
      const osrm = await routeFromOsrm(start, end);
      log('Route', `${label}: using local OSRM route (${osrm.length} points)`);
      routeCoords = osrm;
    } catch (error) {
      log('Route', `${label}: OSRM unavailable, using straight-line fallback (${error.message})`);
      routeCoords = straightLine(start, end);
    }
  }

  console.log(`\n>> ROUTE_COORDINATES | ${label} | ${JSON.stringify(routeCoords)}\n`);
  return routeCoords;
}

async function login(email, password) {
  const response = await axios.post(`${API}/auth/login`, { email, password });
  const value = unwrapValue(response);
  const token = value?.accessToken || value?.AccessToken;
  if (!token) throw new Error(`Login failed for ${email}: missing access token`);
  return { token, user: value.user || value.User };
}

async function registerOrLoginRider(rider) {
  try {
    const response = await axios.post(`${API}/auth/register`, {
      email: rider.email,
      password: PASSWORD,
      fullName: rider.name,
      role: 'Rider'
    });
    const value = unwrapValue(response);
    return {
      token: value.accessToken || value.AccessToken,
      user: value.user || value.User
    };
  } catch (error) {
    if (error.response?.status !== 409) throw error;
    return login(rider.email, PASSWORD);
  }
}

async function createShop() {
  const menus = [
    ['Udon Basil Bowl Sim', 'Crispy pork basil rice', 69],
    ['Nong Prajak Noodle Sim', 'Beef noodle special', 85],
    ['UD Town Coffee Sim', 'Iced latte and sandwich', 95],
    ['Kai Yang Route Sim', 'Grilled chicken set', 120]
  ];
  const [name, menuName, menuPrice] = menus[randomInt(0, menus.length - 1)];
  const location = randomPointAround(UDON_CENTER, 2.2);

  const response = await axios.post(`${API}/shops`, {
    name: `${name} ${RUN_ID}`,
    menuName,
    menuPrice,
    lat: location.lat,
    lng: location.lng
  }, {
    headers: { Authorization: `Bearer ${adminToken}` }
  });

  const shop = unwrapValue(response);
  const resultShop = {
    id: shop.id || shop.Id,
    name: shop.name || shop.Name || name,
    menuName: shop.menuName || shop.MenuName || menuName,
    menuPrice: shop.menuPrice || shop.MenuPrice || menuPrice,
    lat: shop.lat ?? shop.Lat ?? location.lat,
    lng: shop.lng ?? shop.Lng ?? location.lng
  };
  console.log(`\n>> SHOP_CREATED | ${resultShop.name} | ${resultShop.lat} | ${resultShop.lng}\n`);
  return resultShop;
}

function buildRiders(shop) {
  const riders = [];
  for (let i = 1; i <= RIDER_COUNT; i++) {
    const radius = i <= 3 ? randomFloat(0.25, 1.2) : randomFloat(1.0, 5.8);
    const start = randomPointAround({ lat: shop.lat, lng: shop.lng }, radius);
    riders.push({
      email: `sim-rider-${RUN_ID}-${i}@delivery.test`,
      name: `Sim Rider ${i}`,
      start,
      current: { ...start },
      radiusKm: radius
    });
  }
  return riders.sort((a, b) => a.radiusKm - b.radiusKm);
}

async function createOrder(shop, dropoff) {
  const response = await axios.post(`${API}/orders`, {
    shopId: shop.id,
    pickupLat: shop.lat,
    pickupLng: shop.lng,
    dropoffLat: dropoff.lat,
    dropoffLng: dropoff.lng,
    expectedDeliveryTime: new Date(Date.now() + 45 * 60 * 1000).toISOString()
  }, {
    headers: { Authorization: `Bearer ${adminToken}` }
  });

  const order = unwrapValue(response);
  orderId = order.id || order.Id;
  activeOrder = order;
  console.log(`\n>> ORDER_CREATED | ${orderId} | _ | _ | ${dropoff.lat} | ${dropoff.lng}\n`);
  return order;
}

async function sendGps(conn, lat, lng) {
  if (conn.state !== signalR.HubConnectionState.Connected) return;
  await conn.invoke('UpdateLocation', lat, lng, 5.0);
}

function interpolateCoordinates(coords, stepDistanceMeters = 12) {
  const interpolated = [];
  if (!coords.length) return interpolated;
  interpolated.push(coords[0]);
  
  for (let i = 0; i < coords.length - 1; i++) {
    const start = coords[i];
    const end = coords[i + 1];
    const latDiff = end.lat - start.lat;
    const lngDiff = end.lng - start.lng;
    const dist = Math.sqrt(latDiff * latDiff + lngDiff * lngDiff) * 111320;
    
    const numSteps = Math.max(1, Math.floor(dist / stepDistanceMeters));
    for (let j = 1; j <= numSteps; j++) {
      const t = j / numSteps;
      interpolated.push({
        lat: start.lat + latDiff * t,
        lng: start.lng + lngDiff * t
      });
    }
  }
  return interpolated;
}

async function startWandering(conn, rider) {
  log('Rider Wandering', `${rider.name} started wandering loop`);
  rider.isDelivering = false;
  const startLoc = { ...rider.start };
  
  while (!rider.isDelivering) {
    const latDiff = rider.current.lat - startLoc.lat;
    const lngDiff = rider.current.lng - startLoc.lng;
    const distFromStart = Math.sqrt(latDiff * latDiff + lngDiff * lngDiff) * 111320;
    
    let target;
    if (distFromStart > 4000) {
      target = randomPointAround(startLoc, randomFloat(0.5, 1.5));
    } else {
      target = randomPointAround(rider.current, randomFloat(0.08, 0.16));
    }
    
    const steps = straightLine(rider.current, target, randomInt(12, 18));
    for (const step of steps) {
      if (rider.isDelivering) break;
      rider.current = step;
      await sendGps(conn, step.lat, step.lng);
      console.log(`\n>> RIDER_GPS | ${rider.id} | ${rider.name} | ${step.lat} | ${step.lng} | IDLE\n`);
      await sleep(300); // 300ms intervals matching frontend exactly
    }
    
    if (rider.isDelivering) break;
    await sleep(randomInt(800, 2000));
  }
  log('Rider Wandering', `${rider.name} wandering loop terminated`);
}

async function moveAlong(conn, rider, coords, label) {
  const detailed = interpolateCoordinates(coords, 12);
  log(rider.name, `${label}: ${detailed.length} smooth realtime GPS ticks`);

  for (let i = 0; i < detailed.length; i++) {
    const node = detailed[i];
    const jitter = {
      lat: node.lat + randomFloat(-0.00001, 0.00001),
      lng: node.lng + randomFloat(-0.00001, 0.00001)
    };

    rider.current = jitter;
    process.stdout.write(`\r  ${rider.name} ${label} ${i + 1}/${detailed.length}: ${jitter.lat.toFixed(5)}, ${jitter.lng.toFixed(5)}`);
    await sendGps(conn, jitter.lat, jitter.lng);
    console.log(`\n>> RIDER_GPS | ${rider.id} | ${rider.name} | ${jitter.lat} | ${jitter.lng} | DELIVERING\n`);

    // Scale progress between 15% and 98%
    let percent = 15;
    if (label.includes('pickup') || label.includes('to store')) {
      percent = Math.round(15 + (i / detailed.length) * 35);
    } else {
      percent = Math.round(50 + (i / detailed.length) * 48);
    }
    console.log(`\n>> SIMULATION_PROGRESS | ${percent}\n`);

    await sleep(300); // 300ms intervals for ultra-smooth transitions!
  }

  process.stdout.write('\n');
}

async function updateStatus(token, status) {
  await axios.patch(`${API}/orders/${orderId}/status`, { status }, {
    headers: { Authorization: `Bearer ${token}` }
  });
  log('Order', `Status -> ${status}`);
}

async function runDelivery(conn, rider, offer) {
  if (deliveryStarted) return;
  deliveryStarted = true;
  rider.isDelivering = true;

  section(`DELIVERY STARTED BY ${rider.name}`);
  console.log(`\n>> ACTIVE_RIDER | ${rider.name}\n`);

  const order = offer.order || activeOrder;
  const pickup = {
    lat: order.pickupLat ?? order.PickupLat,
    lng: order.pickupLng ?? order.PickupLng
  };
  const dropoff = {
    lat: order.dropoffLat ?? order.DropoffLat,
    lng: order.dropoffLng ?? order.DropoffLng
  };

  const pickupPolyline = offer.pickupRoute?.encodedPolyline || offer.pickupRoute?.EncodedPolyline;
  const pickupRoute = await bestRoute(rider.current, pickup, pickupPolyline, 'Rider -> Store');
  await updateStatus(rider.token, 'PICKING_UP');
  await moveAlong(conn, rider, pickupRoute, 'to pickup');

  log(rider.name, `Picked up menu/order at store (${pickup.lat.toFixed(5)}, ${pickup.lng.toFixed(5)})`);
  await sleep(1500);

  const deliveryPolyline = order.encodedPolyline || order.EncodedPolyline;
  const deliveryRoute = await bestRoute(pickup, dropoff, deliveryPolyline, 'Store -> Dropoff');
  await updateStatus(rider.token, 'DELIVERING');
  await moveAlong(conn, rider, deliveryRoute, 'to dropoff');

  await updateStatus(rider.token, 'COMPLETED');
  console.log(`\n>> RIDER_GPS | ${rider.id} | ${rider.name} | ${dropoff.lat} | ${dropoff.lng} | COMPLETED\n`);
  console.log('\n>> SIMULATION_PROGRESS | 100\n');
  log('Simulator', `Completed Order ${orderId} with ${rider.name}`);

  await sleep(3000);
  await Promise.allSettled(riderConnections.map(item => item.conn.stop()));
  logTest("E2E Delivery Lifecycle", "PASS", "Delivery completed successfully", `Rider=${rider.name}, Order=${orderId}`);
  finishProcess(0);
}

async function connectRider(rider) {
  const auth = await registerOrLoginRider(rider);
  rider.token = auth.token;
  rider.id = auth.user?.riderId || auth.user?.RiderId;

  if (!rider.id) throw new Error(`Rider ${rider.email} has no riderId in auth response`);

  console.log(`\n>> RIDER_MAPPING | ${rider.name} | ${rider.id}\n`);

  const conn = new signalR.HubConnectionBuilder()
    .withUrl(HUB, {
      accessTokenFactory: () => rider.token,
      skipNegotiation: true,
      transport: signalR.HttpTransportType.WebSockets
    })
    .withAutomaticReconnect([0, 2000, 5000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  conn.on('OfferReceived', async offer => {
    if (offerAccepted) return;
    offerAccepted = true;
    rider.isDelivering = true; // Stop wandering immediately
    activeOrder = offer.order || activeOrder;

    log('AI Offer', `${rider.name} received offer ${offer.offerId || offer.OfferId} for Order ${(activeOrder?.id || activeOrder?.Id || '').slice(0, 8)}`);
    await sleep(randomInt(1200, 2600));

    try {
      rider.acceptedOffer = offer;
      await conn.invoke('AcceptOffer', offer.offerId || offer.OfferId, offer.version || offer.Version);
    } catch (error) {
      log(rider.name, `AcceptOffer failed: ${error.message}`);
    }
  });

  conn.on('OfferAcceptedResult', async result => {
    const success = result?.success ?? result?.Success;
    if (success) {
      await runDelivery(conn, rider, rider.acceptedOffer || {});
    } else {
      rider.isDelivering = false; // Resume wandering if somehow rejected
      log(rider.name, `Offer rejected by backend: ${result?.message || result?.Message || 'unknown reason'}`);
    }
  });

  await conn.start();
  await sendGps(conn, rider.start.lat, rider.start.lng);
  log('Rider GPS', `${rider.name} online at ${rider.start.lat.toFixed(5)}, ${rider.start.lng.toFixed(5)} (${rider.radiusKm.toFixed(2)} km from shop)`);
  
  // Start autonomous background wandering in non-blocking loop
  startWandering(conn, rider).catch(err => log(rider.name, `Wandering error: ${err.message}`));

  return conn;
}

async function checkHealth() {
  try {
    await axios.get(HEALTH_URL, { timeout: 2000 });
    logTest("Backend Health", "PASS", "Service is up", HEALTH_URL);
  } catch (error) {
    logTest("Backend Health", "FAIL", error.message, HEALTH_URL);
    throw new Error(`Backend health check failed at ${HEALTH_URL}: ${error.message}`);
  }
}

function setTimeoutGuard(seconds) {
  setTimeout(() => {
    if (!offerAccepted) {
      logTest("AI Dispatch Timer", "FAIL", `No dispatch offer after ${seconds}s.`, `seconds=${seconds}`);
      log('Timeout', `No dispatch offer after ${seconds}s. Check backend, Redis presence, AI service, and rider states.`);
      finishProcess(1);
    }
  }, seconds * 1000);
}

async function main() {
  section('SMART DELIVERY REALTIME SIMULATOR');
  log('Config', `API=${API}`);
  log('Config', `Hub=${HUB}`);
  log('Config', `Riders=${RIDER_COUNT}`);

  await checkHealth();

  const admin = await login(ADMIN_CREDS.email, ADMIN_CREDS.password);
  adminToken = admin.token;
  logTest("Admin Login", "PASS", "Admin authenticated", `Email=${ADMIN_CREDS.email}`);
  log('Auth', 'Admin authenticated');

  const shop = await createShop();
  const dropoff = randomPointAround({ lat: shop.lat, lng: shop.lng }, randomFloat(1.3, 4.0));
  logTest("Create Shop", "PASS", "Shop created successfully", `Shop=${shop.name}`);
  log('Shop', `${shop.name} | ${shop.menuName} (${shop.menuPrice} THB) at ${shop.lat.toFixed(5)}, ${shop.lng.toFixed(5)}`);
  log('Dropoff', `${dropoff.lat.toFixed(5)}, ${dropoff.lng.toFixed(5)}`);

  const riders = buildRiders(shop);
  for (const rider of riders) {
    const conn = await connectRider(rider);
    riderConnections.push({ conn, rider });
  }

  await sleep(2000);

  const order = await createOrder(shop, dropoff);
  logTest("Create Order", "PASS", "Order created", `OrderId=${order.id || order.Id}`);
  log('Order', `Created ${(order.id || order.Id || '').slice(0, 8)}. AI dispatch scan should now appear on the map.`);
  
  if (process.env.DELIVERY_SIM_SCENARIO === 'BATCH') {
    const dropoff2 = randomPointAround({ lat: shop.lat, lng: shop.lng }, randomFloat(1.3, 4.0));
    const order2 = await createOrder(shop, dropoff2);
    log('Order', `Created BATCH sibling ${(order2.id || order2.Id || '').slice(0, 8)}.`);
  }
  
  log('Order', `Menu ready for simulated pickup: ${shop.menuName}`);

  setTimeoutGuard(60);
}

main().catch(error => {
  console.error('\nSimulation crashed:', error.message);
  logTest("E2E Simulation Flow", "FAIL", error.message, "main()");
  if (error.response) {
    console.error('HTTP Status:', error.response.status);
    console.error('Response:', JSON.stringify(error.response.data, null, 2));
  }
  finishProcess(1);
});
