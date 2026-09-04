/**
 * Global E2E Rider Simulator (Dynamic & Reactive Event-Driven Simulator)
 *
 * This script runs in the terminal to simulate rider movements dynamically:
 * 1. Logs in as Admin to monitor all order status updates via SignalR.
 * 2. Listens for OrderStatusChanged events.
 * 3. Identifies the assigned rider for the active order.
 * 4. Automatically queries the rider's email from Postgres (or falls back to static map).
 * 5. Logs in as that rider, establishes a dedicated SignalR connection, and simulates
 *    GPS updates in real-time (reactive to order phase transitions in the Flutter app).
 * 6. Playbacks coordinates along OSRM network routes at 2x speed.
 * 7. Simulates 1.5 km wandering after order completion to set a new starting position.
 */

'use strict';

const axios = require('axios');
const signalR = require('@microsoft/signalr');
const { execSync } = require('child_process');

const args = process.argv.slice(2);

function getArg(name, fallback = undefined) {
  const idx = args.indexOf(`--${name}`);
  return idx >= 0 && args[idx + 1] ? args[idx + 1] : fallback;
}

const API = getArg('api', process.env.DELIVERY_API_URL || 'http://127.0.0.1:5000/api/v1');
const HUB = getArg('hub', process.env.DELIVERY_HUB_URL || 'http://127.0.0.1:5000/hubs/tracking');
const ADMIN_EMAIL = getArg('admin-email', 'admin@delivery.com');
const ADMIN_PASSWORD = getArg('admin-password', '1234567891203');
const DEFAULT_PASSWORD = getArg('default-password', '1234567891203');

// Configs for 2x faster GPS movement (Interval = 2.5s, Step = 80m)
const INTERVAL_MS = Number(getArg('interval', '2500'));
const STEP_METERS = Number(getArg('step-meters', '80'));
const ACCURACY = Number(getArg('accuracy', '10'));

const FROM_LAT = Number(getArg('from-lat', '17.4138'));
const FROM_LNG = Number(getArg('from-lng', '102.7872'));

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

function unwrapValue(response) {
  return response.data?.value || response.data?.Value || response.data;
}

// Global cache to store active riders connections and states
// Key: riderId, Value: { connection, token, email, currentLat, currentLng, runningPlayback: false, completedPhases: Set }
const activeRiders = {};

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
      if (index >= str.length || shift > 30) return [];
      b = str.charCodeAt(index++) - 63;
      result |= (b & 0x1f) << shift;
      shift += 5;
    } while (b >= 0x20);
    lat += (result & 1) ? ~(result >> 1) : (result >> 1);

    shift = 0;
    result = 0;
    do {
      if (index >= str.length || shift > 30) return [];
      b = str.charCodeAt(index++) - 63;
      result |= (b & 0x1f) << shift;
      shift += 5;
    } while (b >= 0x20);
    lng += (result & 1) ? ~(result >> 1) : (result >> 1);

    const point = { lat: lat / 1e5, lng: lng / 1e5 };
    if (point.lat < -90 || point.lat > 90 || point.lng < -180 || point.lng > 180) {
      return [];
    }
    coordinates.push(point);
  }

  return coordinates;
}

function distanceMeters(a, b) {
  const earthRadius = 6371000;
  const lat1 = a.lat * Math.PI / 180;
  const lat2 = b.lat * Math.PI / 180;
  const dLat = (b.lat - a.lat) * Math.PI / 180;
  const dLng = (b.lng - a.lng) * Math.PI / 180;
  const sinLat = Math.sin(dLat / 2);
  const sinLng = Math.sin(dLng / 2);
  const h = sinLat * sinLat + Math.cos(lat1) * Math.cos(lat2) * sinLng * sinLng;
  return 2 * earthRadius * Math.atan2(Math.sqrt(h), Math.sqrt(1 - h));
}

function interpolateRoute(points, stepMeters) {
  if (points.length < 2) return points;
  const result = [points[0]];

  for (let i = 0; i < points.length - 1; i++) {
    const start = points[i];
    const end = points[i + 1];
    const segmentMeters = distanceMeters(start, end);
    const steps = Math.max(1, Math.floor(segmentMeters / stepMeters));

    for (let step = 1; step <= steps; step++) {
      const t = step / steps;
      result.push({
        lat: start.lat + (end.lat - start.lat) * t,
        lng: start.lng + (end.lng - start.lng) * t,
      });
    }
  }

  return result;
}

function calculateWanderingDestination(start, distanceMeters = 1500) {
  const earthRadius = 6371000;
  const bearing = Math.random() * Math.PI * 2;

  const lat1 = start.lat * Math.PI / 180;
  const lng1 = start.lng * Math.PI / 180;

  const lat2 = Math.asin(
    Math.sin(lat1) * Math.cos(distanceMeters / earthRadius) +
    Math.cos(lat1) * Math.sin(distanceMeters / earthRadius) * Math.cos(bearing)
  );

  const lng2 = lng1 + Math.atan2(
    Math.sin(bearing) * Math.sin(distanceMeters / earthRadius) * Math.cos(lat1),
    Math.cos(distanceMeters / earthRadius) - Math.sin(lat1) * Math.sin(lat2)
  );

  return {
    lat: lat2 * 180 / Math.PI,
    lng: lng2 * 180 / Math.PI
  };
}

async function login(email, password) {
  try {
    const response = await axios.post(`${API}/auth/login`, { email, password });
    const value = unwrapValue(response);
    return value?.accessToken || value?.AccessToken;
  } catch (error) {
    throw new Error(`Login failed for ${email}: ${error.message}`);
  }
}

async function getRiderEmail(riderId) {
  const staticMap = {
    "11111111-1111-1111-1111-111111111111": "rider1@delivery.com",
    "22222222-2222-2222-2222-222222222222": "rider2@delivery.com",
    "33333333-3333-3333-3333-333333333333": "rider3@delivery.com"
  };
  if (staticMap[riderId]) return staticMap[riderId];

  // Try querying PostgreSQL Database via Docker or psql
  try {
    const pgPassword = process.env.POSTGRES_PASSWORD || 'Admin@Ts2x04_';
    let email = '';
    
    // ลองชื่อ container ที่กำลังทำงานจริง (ในเครื่องนี้คือ delivery-db) และชื่อ fallback อื่นๆ
    const containers = ['delivery-db', 'postgres', 'db', 'delivery-postgres'];
    for (const container of containers) {
      try {
        email = execSync(`docker exec -i ${container} psql -U postgres -d delivery_db -t -A -c "SELECT \\"Email\\" FROM \\"Users\\" WHERE \\"RiderId\\" = '${riderId}'"`, { stdio: ['pipe', 'pipe', 'ignore'] }).toString().trim();
        if (email && email.includes('@')) {
          console.log(`[Database] Dynamically resolved rider email via container ${container} for ${riderId} -> ${email}`);
          return email;
        }
      } catch (_) {}
    }

    // ลองรัน psql บน localhost ตรงๆ
    try {
      email = execSync(`set PGPASSWORD=${pgPassword}&& psql -h localhost -U postgres -d delivery_db -t -A -c "SELECT \\"Email\\" FROM \\"Users\\" WHERE \\"RiderId\\" = '${riderId}'"`, { stdio: ['pipe', 'pipe', 'ignore'] }).toString().trim();
      if (email && email.includes('@')) {
        console.log(`[Database] Dynamically resolved rider email via localhost psql for ${riderId} -> ${email}`);
        return email;
      }
    } catch (_) {}
  } catch (_) {}

  // Fallback pattern: หากคิวรี่ไม่ได้เลย
  console.log(`[Warning] Could not query email from DB. Trying naming pattern fallback for Rider ID: ${riderId}`);
  return null;
}

async function resolveRoute(token, orderId, phase, startLat, startLng) {
  const response = await axios.post(
    `${API}/rider-routes/resolve`,
    {
      orderId: orderId,
      routePhase: phase,
      currentLat: Number(startLat),
      currentLng: Number(startLng),
    },
    {
      headers: { Authorization: `Bearer ${token}` },
    },
  );

  const value = unwrapValue(response);
  const encodedPolyline = value?.encodedPolyline || value?.EncodedPolyline || '';
  const source = value?.source || value?.Source || 'UNKNOWN';
  if (source !== 'LOCAL_OSRM') {
    throw new Error(`OSRM Route resolution failed. source=${source}`);
  }
  const points = decodePolyline(encodedPolyline);
  if (points.length < 2) {
    throw new Error(`OSRM Route polyline is empty or invalid.`);
  }
  return points;
}

async function getOrderDetails(token, orderId) {
  try {
    const response = await axios.get(`${API}/orders/${orderId}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    return unwrapValue(response);
  } catch (error) {
    console.error(`[Admin API] Failed to fetch order details for ${orderId}:`, error.message);
    return null;
  }
}

function suppressWarnings(connection) {
  const events = [
    'RiderLocationUpdated', 'riderlocationupdated',
    'RiderLocationSnapped', 'riderlocationsnapped',
    'RiderStatusUpdated', 'riderstatusupdated',
    'TelemetryUpdated', 'telemetryupdated',
    'OfferReceived', 'offerreceived',
    'DispatchScanStarted', 'dispatchscanstarted',
    'DispatchCandidatesRanked', 'dispatchcandidatesranked',
    'DispatchOfferSent', 'dispatchoffersent',
    'OrderAssigned', 'orderassigned',
    'OrderStatusChanged', 'orderstatuschanged',
    'OrderCreated', 'ordercreated',
    'OrderAcceptedByStore', 'orderacceptedbystore',
    'ShopStatusChanged', 'shopstatuschanged'
  ];
  for (const event of events) {
    try {
      connection.on(event, () => {});
    } catch (_) {}
  }
}

async function initializeRider(riderId, email) {
  if (activeRiders[riderId]) return activeRiders[riderId];

  console.log(`[Rider Init] Initializing connection for Rider ${riderId} (${email})...`);
  const token = await login(email, DEFAULT_PASSWORD);
  
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(HUB, { accessTokenFactory: () => token })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  // Register handlers to suppress warnings about unhandled client methods
  suppressWarnings(connection);

  await connection.start();
  
  // Set initial status to IDLE and start custom heartbeat loop
  await connection.invoke('UpdateStatus', 'IDLE');
  
  const heartbeatInterval = setInterval(async () => {
    if (connection.state === signalR.HubConnectionState.Connected) {
      try {
        await connection.invoke('UpdateHeartbeat');
      } catch (err) {
        console.error(`[Heartbeat - Rider ${email}] Failed:`, err.message);
      }
    }
  }, 10000);

  activeRiders[riderId] = {
    connection,
    token,
    email,
    currentLat: FROM_LAT,
    currentLng: FROM_LNG,
    runningPlayback: false,
    completedPhases: new Set(),
    heartbeatInterval
  };

  console.log(`[Rider Init] Rider ${email} is online and presence tracking is active.`);
  return activeRiders[riderId];
}

async function playbackGps(rider, points) {
  const playbackPoints = interpolateRoute(points, STEP_METERS);
  console.log(`[GPS Playback - ${rider.email}] Started: ${playbackPoints.length} points at ${INTERVAL_MS}ms interval`);

  rider.runningPlayback = true;
  try {
    for (let i = 0; i < playbackPoints.length; i++) {
      if (rider.connection.state !== signalR.HubConnectionState.Connected) {
        console.warn(`[GPS Playback - ${rider.email}] Connection lost. Aborting.`);
        break;
      }
      const point = playbackPoints[i];
      await rider.connection.invoke('UpdateLocation', point.lat, point.lng, ACCURACY);
      console.log(`[${rider.email}] [${i + 1}/${playbackPoints.length}] ${point.lat.toFixed(6)}, ${point.lng.toFixed(6)}`);
      rider.currentLat = point.lat;
      rider.currentLng = point.lng;
      await sleep(INTERVAL_MS);
    }
  } finally {
    rider.runningPlayback = false;
    console.log(`[GPS Playback - ${rider.email}] Completed.`);
  }
}

async function handleOrderStatusChange(adminToken, orderId, statusRaw) {
  if (!statusRaw || typeof statusRaw !== 'string') return;
  const status = statusRaw.toUpperCase();
  console.log(`[Event Action] Order ${orderId} changed status to ${status}`);

  // Fetch full details of the order to identify the assigned rider
  const order = await getOrderDetails(adminToken, orderId);
  if (!order) return;

  const riderId = order.assignedRiderId || order.AssignedRiderId;
  if (!riderId) {
    console.log(`[Event Action] Order ${orderId} has no assigned rider. Skipping.`);
    return;
  }

  // Find or initialize rider's connection dynamically
  let rider = activeRiders[riderId];
  if (!rider) {
    const email = await getRiderEmail(riderId);
    if (!email) {
      console.error(`[Event Action] Could not resolve email for Rider ${riderId}. Cannot simulate.`);
      return;
    }
    rider = await initializeRider(riderId, email);
  }

  // Mapped statuses for navigation phases
  if (status === 'PICKING_UP' || status === 'ASSIGNED') {
    if (!rider.completedPhases.has('PICKUP') && !rider.runningPlayback) {
      console.log(`[Navigation Phase] Rider ${rider.email} heading to PICKUP (Store)...`);
      try {
        const routePoints = await resolveRoute(rider.token, orderId, 'PICKUP', rider.currentLat, rider.currentLng);
        await playbackGps(rider, routePoints);
        rider.completedPhases.add('PICKUP');
        console.log(`[Navigation Phase] Rider ${rider.email} arrived at Store. Waiting for shop actions.`);
      } catch (err) {
        console.error(`[Navigation Error - PICKUP]`, err.message);
      }
    }
  } else if (status === 'DELIVERING') {
    if (!rider.completedPhases.has('DELIVERY') && !rider.runningPlayback) {
      console.log(`[Navigation Phase] Rider ${rider.email} heading to DELIVERY (Customer)...`);
      try {
        const routePoints = await resolveRoute(rider.token, orderId, 'DELIVERY', rider.currentLat, rider.currentLng);
        await playbackGps(rider, routePoints);
        rider.completedPhases.add('DELIVERY');
        console.log(`[Navigation Phase] Rider ${rider.email} arrived at Customer. Waiting for completion.`);
      } catch (err) {
        console.error(`[Navigation Error - DELIVERY]`, err.message);
      }
    }
  } else if (status === 'COMPLETED') {
    if (!rider.completedPhases.has('WANDER') && !rider.runningPlayback) {
      console.log(`[Navigation Phase] Rider ${rider.email} completed order. Simulating wandering 1.5 km away...`);
      try {
        const startPos = { lat: rider.currentLat, lng: rider.currentLng };
        const destPos = calculateWanderingDestination(startPos, 1500);
        
        await playbackGps(rider, [startPos, destPos]);
        rider.completedPhases.add('WANDER');
        
        console.log(`[Navigation Phase] Rider ${rider.email} arrived at new wandering base: ${destPos.lat.toFixed(6)}, ${destPos.lng.toFixed(6)}`);
        
        // Return rider state back to IDLE
        await rider.connection.invoke('UpdateStatus', 'IDLE');
        rider.completedPhases.clear(); // Clear all stages for subsequent orders
      } catch (err) {
        console.error(`[Navigation Error - WANDER]`, err.message);
      }
    }
  }
}

async function resumeActiveOrders(adminToken) {
  try {
    console.log(`[Sync] Resuming existing active orders and simulating movements if necessary...`);
    const response = await axios.get(`${API}/orders/my`, {
      headers: { Authorization: `Bearer ${adminToken}` },
    });
    const orders = unwrapValue(response);
    const active = orders.filter(o => ['ASSIGNED', 'PICKING_UP', 'DELIVERING'].includes(o.status.toUpperCase()));
    
    for (const order of active) {
      await handleOrderStatusChange(adminToken, order.id, order.status);
    }
  } catch (_) {
    // Ignore if /orders/my fails in admin scope, as it is a rider-specific endpoint in standard layout.
    // Admin can still rely on reactive SignalR triggers.
  }
}

async function main() {
  console.log(`\n======================================================`);
  console.log(`  Dynamic Rider E2E Simulator (Reactive Update Hub)`);
  console.log(`======================================================`);
  
  console.log(`[Admin Login] Logging in as ${ADMIN_EMAIL}...`);
  const adminToken = await login(ADMIN_EMAIL, ADMIN_PASSWORD);
  console.log(`[Admin Login] Logged in successfully. Admin session acquired.`);

  const adminConnection = new signalR.HubConnectionBuilder()
    .withUrl(HUB, { accessTokenFactory: () => adminToken })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  // Monitor OrderStatusChanged events globally
  adminConnection.on('OrderStatusChanged', (arg1, arg2) => {
    let orderId = '';
    let status = '';

    if (arg2 !== undefined) {
      orderId = arg1?.toString() ?? '';
      status = arg2?.toString() ?? '';
    } else if (arg1 && typeof arg1 === 'object') {
      orderId = arg1.orderId || arg1.OrderId || '';
      status = arg1.newStatus || arg1.NewStatus || arg1.status || arg1.Status || '';
    } else {
      orderId = arg1?.toString() ?? '';
    }

    if (!orderId || !status) {
      return;
    }

    handleOrderStatusChange(adminToken, orderId, status).catch(err => {
      console.error(`[Event Error] Error handling status change:`, err.message);
    });
  });

  adminConnection.on('OfferReceived', async (offer) => {
    // Log dispatch offers to help developers check current dispatcher pipelines
    const orderId = offer.orderId || (offer.order?.id);
    const riderId = offer.riderId;
    console.log(`[AI Dispatch] Offer sent to Rider ${riderId} for Order ${orderId}`);
  });

  // Register handlers to suppress warnings about unhandled client methods
  suppressWarnings(adminConnection);

  await adminConnection.start();
  console.log(`[SignalR adminConnection] Main listener is online. Listening to TrackingHub events...`);

  // Start heartbeat for admin connection
  const adminHeartbeat = setInterval(async () => {
    if (adminConnection.state === signalR.HubConnectionState.Connected) {
      try {
        await adminConnection.invoke('UpdateHeartbeat');
      } catch (_) {}
    }
  }, 15000);

  // Resume any active orders immediately
  await resumeActiveOrders(adminToken);

  console.log(`[Simulator] Global Reactive Simulation Loop is active. Waiting for order updates...\n`);

  process.on('SIGINT', async () => {
    console.log('\n[Simulator] Shutting down simulation connections...');
    clearInterval(adminHeartbeat);
    try {
      await adminConnection.stop();
      for (const riderId in activeRiders) {
        const rider = activeRiders[riderId];
        clearInterval(rider.heartbeatInterval);
        await rider.connection.invoke('UpdateStatus', 'OFFLINE');
        await rider.connection.stop();
      }
    } catch (_) {}
    console.log('[Simulator] Disconnected. Goodbye.');
    process.exit(0);
  });
}

main().catch(error => {
  console.error('\n[Fatal Error] Runtime aborted:', error.message || error);
  process.exit(1);
});
