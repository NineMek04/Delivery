import express from 'express';
import http from 'http';
import { Server } from 'socket.io';
import cors from 'cors';
import Redis from 'ioredis';
import dotenv from 'dotenv';
import path from 'path';
import fs from 'fs';
import { testQueue } from '../services/queue';
import { ArtifactService } from '../services/artifact-service';
import { cancelDockerTest } from '../services/docker-execution';

dotenv.config();

const PORT = process.env.PORT || 3001;
const REDIS_HOST = process.env.REDIS_HOST || 'localhost';
const REDIS_PORT = parseInt(process.env.REDIS_PORT || '6379', 10);

const app = express();
app.use(cors());
app.use(express.json());

const server = http.createServer(app);
const io = new Server(server, {
  cors: {
    origin: '*',
    methods: ['GET', 'POST'],
  },
});

// Dedicated Redis connection for subscribing to pub/sub channels
const subClient = new Redis({
  host: REDIS_HOST,
  port: REDIS_PORT,
});

subClient.on('connect', () => {
  console.log(`[API Server] Redis subscriber connected to ${REDIS_HOST}:${REDIS_PORT}`);
});

// State for log batching / throttling
const logBuffers: Record<string, string> = {};
const BATCH_INTERVAL_MS = 500;

// Batch emit logs every 500ms to prevent UI freezing
setInterval(() => {
  for (const sessionId of Object.keys(logBuffers)) {
    if (logBuffers[sessionId]) {
      io.to(`session:${sessionId}`).emit('log', logBuffers[sessionId]);
      logBuffers[sessionId] = ''; // clear buffer after emitting
    }
  }
}, BATCH_INTERVAL_MS);

// Listen for Redis pub/sub messages and broadcast them over Socket.io
subClient.on('message', (channel, message) => {
  const parts = channel.split(':');
  if (parts.length >= 3) {
    const sessionId = parts[1];
    const eventType = parts[2]; // 'logs' or 'status'

    if (eventType === 'logs') {
      // Buffer the log chunk instead of immediate emit
      if (!logBuffers[sessionId]) {
        logBuffers[sessionId] = '';
      }
      logBuffers[sessionId] += message;
    } else if (eventType === 'status') {
      io.to(`session:${sessionId}`).emit('status', JSON.parse(message));
      // Cleanup buffer on end statuses
      try {
        const parsed = JSON.parse(message);
        if (['COMPLETED', 'FAILED', 'CANCELLED', 'TIMEOUT'].includes(parsed.status)) {
           // Flush remaining logs immediately
           if (logBuffers[sessionId]) {
             io.to(`session:${sessionId}`).emit('log', logBuffers[sessionId]);
             delete logBuffers[sessionId];
           }
        }
      } catch (e) {}
    }
  }
});

// REST API Endpoints

// 1. Trigger Test Suite
app.post('/api/test/run', async (req, res) => {
  try {
    const { suiteType, triggerType } = req.body; // suiteType: 'csharp', 'python', 'load', 'simulator'
    if (!suiteType) {
      return res.status(400).json({ error: 'suiteType is required' });
    }

    const triggerMode = triggerType || 'docker'; // 'docker' or 'host'
    const session = ArtifactService.createSession(suiteType, triggerMode);

    console.log(`[API Server] Creating job for suite ${suiteType} (Session ID: ${session.sessionId})`);

    // Add to BullMQ
    const job = await testQueue.add(
      'run-test',
      { sessionId: session.sessionId, suiteType },
      { jobId: session.sessionId }
    );

    ArtifactService.updateSession(session.sessionId, { status: 'QUEUED' });

    res.json({
      message: 'Test run scheduled',
      sessionId: session.sessionId,
      jobId: job.id,
      status: 'QUEUED',
    });
  } catch (error: any) {
    console.error('[API Server] Failed to run test:', error);
    res.status(500).json({ error: error.message });
  }
});

// 2. Cancel Active Test Run
app.post('/api/test/cancel', async (req, res) => {
  try {
    const { sessionId } = req.body;
    if (!sessionId) {
      return res.status(400).json({ error: 'sessionId is required' });
    }

    const session = ArtifactService.getSession(sessionId);
    if (!session) {
      return res.status(404).json({ error: 'Session not found' });
    }

    if (['COMPLETED', 'FAILED', 'CANCELLED', 'TIMEOUT'].includes(session.status)) {
      return res.status(400).json({ error: 'Session is already finished' });
    }

    console.log(`[API Server] Cancelling test session ${sessionId}`);

    // Update state to CANCELLED
    ArtifactService.updateSession(sessionId, { status: 'CANCELLED' });

    // Cancel in Docker Execution Service (if running)
    const cancelledDocker = await cancelDockerTest(sessionId);

    // Cancel in BullMQ queue (if still waiting)
    const job = await testQueue.getJob(sessionId);
    if (job) {
      await job.remove().catch(() => {});
    }

    // Publish cancel status update
    const pubClient = new Redis({ host: REDIS_HOST, port: REDIS_PORT });
    await pubClient.publish(
      `session:${sessionId}:status`,
      JSON.stringify({ status: 'CANCELLED', error: 'Cancelled by user request' })
    );
    pubClient.disconnect();

    res.json({ message: 'Cancellation signal sent successfully', sessionId });
  } catch (error: any) {
    console.error('[API Server] Failed to cancel test:', error);
    res.status(500).json({ error: error.message });
  }
});

// 3. List All Session Histories
app.get('/api/test/sessions', (req, res) => {
  const sessions = ArtifactService.getAllSessions();
  res.json(sessions);
});

// 4. Get Single Session Details
app.get('/api/test/sessions/:id', (req, res) => {
  const session = ArtifactService.getSession(req.params.id);
  if (!session) {
    return res.status(404).json({ error: 'Session not found' });
  }
  res.json(session);
});

// 5. Download Session Execution Log File
app.get('/api/test/sessions/:id/logs', (req, res) => {
  const sessionId = req.params.id;
  const session = ArtifactService.getSession(sessionId);
  if (!session) {
    return res.status(404).json({ error: 'Session not found' });
  }

  const logPath = ArtifactService.getLogPath(sessionId);
  if (!fs.existsSync(logPath)) {
    fs.writeFileSync(logPath, '', 'utf-8');
  }
  res.download(logPath, `execution-${sessionId}.log`);
});

// 6. Download Structured JSON Report File
app.get('/api/test/sessions/:id/report', (req, res) => {
  const sessionId = req.params.id;
  const reportPath = ArtifactService.getReportPath(sessionId);
  if (!reportPath || !fs.existsSync(reportPath)) {
    return res.status(404).json({ error: 'Report file not found' });
  }
  res.download(reportPath, `report-${sessionId}.json`);
});

// 7. Read Structured JSON Report Data for Dashboard Charts
app.get('/api/test/sessions/:id/report-data', (req, res) => {
  const sessionId = req.params.id;
  const reportPath = ArtifactService.getReportPath(sessionId);
  if (!reportPath || !fs.existsSync(reportPath)) {
    return res.status(404).json({ error: 'Report file not found' });
  }

  try {
    res.json(JSON.parse(fs.readFileSync(reportPath, 'utf-8')));
  } catch (error: any) {
    res.status(500).json({ error: error.message });
  }
});

// --- Interactive Map Simulator State & Endpoints ---

interface SimRider {
  id: string;
  refNumber: string;
  name: string;
  email: string;
  status: 'IDLE' | 'PICKING_UP' | 'DELIVERING';
  lat: number;
  lng: number;
  color: string;
  activeOrders: string[];
  targetPath: { lat: number; lng: number }[];
  pathIndex: number;
  routeSegments: { label: string; coords: { lat: number; lng: number }[] }[];
}

interface SimOrder {
  id: string;
  type: 'SINGLE' | 'BATCH';
  pickup: { lat: number; lng: number };
  dropoff: { lat: number; lng: number };
  pickups?: { lat: number; lng: number }[];
  dropoffs?: { lat: number; lng: number }[];
  status: 'PENDING' | 'PICKING_UP' | 'DELIVERING' | 'COMPLETED';
  riderId?: string;
  createdAt: string;
}

let simRiders: SimRider[] = [];
let simOrders: SimOrder[] = [];
let simIntervalHandle: ReturnType<typeof setInterval> | null = null;
let currentSimSessionId: string | null = null;
let simTickCount = 0;

async function logSim(message: string) {
  const ts = new Date().toLocaleTimeString('th-TH', { hour12: false });
  const logLine = `[${ts}] ${message}\n`;
  console.log(`[Simulator Log] ${logLine.trim()}`);
  
  if (currentSimSessionId) {
    ArtifactService.appendLog(currentSimSessionId, logLine);
    const pubClient = new Redis({ host: REDIS_HOST, port: REDIS_PORT });
    await pubClient.publish(`session:${currentSimSessionId}:logs`, logLine);
    pubClient.disconnect();
  }
}

const OSRM_URL = process.env.OSRM_URL || 'http://localhost:5001';

async function snapToRoad(lat: number, lng: number): Promise<{ lat: number; lng: number }> {
  try {
    const url = `${OSRM_URL}/nearest/v1/driving/${lng},${lat}`;
    const nodeFetch = (global as any).fetch || fetch;
    const res = await nodeFetch(url);
    if (!res.ok) throw new Error(`OSRM nearest status: ${res.status}`);
    const data: any = await res.json();
    if (data.code === 'Ok' && data.waypoints?.length > 0) {
      const loc = data.waypoints[0].location;
      return { lat: loc[1], lng: loc[0] };
    }
  } catch (err) {
    console.error(`[OSRM Nearest] Failed to snap coordinate (${lat}, ${lng}):`, err);
  }
  return { lat, lng };
}

async function getOsrmRoute(start: { lat: number; lng: number }, end: { lat: number; lng: number }): Promise<{ lat: number; lng: number }[]> {
  try {
    const url = `${OSRM_URL}/route/v1/driving/${start.lng},${start.lat};${end.lng},${end.lat}?overview=full&geometries=geojson`;
    const nodeFetch = (global as any).fetch || fetch;
    const res = await nodeFetch(url);
    if (!res.ok) throw new Error(`OSRM route status: ${res.status}`);
    const data: any = await res.json();
    const coords = data.routes?.[0]?.geometry?.coordinates;
    if (coords && coords.length > 0) {
      return coords.map(([lng, lat]: [number, number]) => ({ lat, lng }));
    }
  } catch (err) {
    console.error('[OSRM Route] Failed to get route from OSRM:', err);
  }
  
  const path = [];
  const steps = 10;
  for (let i = 0; i <= steps; i++) {
    const t = i / steps;
    path.push({
      lat: start.lat + (end.lat - start.lat) * t,
      lng: start.lng + (end.lng - start.lng) * t
    });
  }
  return path;
}

async function fetchRidersFromBackend(): Promise<any[]> {
  try {
    const nodeFetch = (global as any).fetch || fetch;
    const loginRes = await nodeFetch('http://localhost:5000/api/v1/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: 'admin@delivery.com', password: 'Password123!' })
    });
    if (!loginRes.ok) throw new Error(`Login failed: ${loginRes.status}`);
    const loginData: any = await loginRes.json();
    
    // Support either wrapped data (C# value/Value) or direct payload
    const token = loginData.accessToken || 
                  loginData.AccessToken || 
                  loginData.value?.accessToken || 
                  loginData.value?.AccessToken || 
                  loginData.Value?.AccessToken || 
                  loginData.data?.accessToken || 
                  loginData.data?.AccessToken;
    if (!token) throw new Error('Token not found in login response');

    const ridersRes = await nodeFetch('http://localhost:5000/api/v1/riders?pageSize=100', {
      headers: { 'Authorization': `Bearer ${token}` }
    });
    if (!ridersRes.ok) throw new Error(`Fetch riders failed: ${ridersRes.status}`);
    const ridersData: any = await ridersRes.json();
    const val = ridersData.value || ridersData.Value || ridersData.data || ridersData;
    const items = val.items || val.Items || [];
    return items;
  } catch (err: any) {
    console.error('[Simulator Sourcing] Error sourcing riders from .NET backend:', err.message);
    return [];
  }
}

function randomCoordInUdon(): { lat: number; lng: number } {
  const lat = 17.398 + Math.random() * 0.03;
  const lng = 102.768 + Math.random() * 0.04;
  return { lat, lng };
}

function startSimLoop() {
  if (simIntervalHandle) return;
  simTickCount = 0;
  
  simIntervalHandle = setInterval(async () => {
    simTickCount++;
    let stateChanged = false;
    
    for (const r of simRiders) {
      if (r.status === 'IDLE' || r.targetPath.length === 0) continue;
      
      const speed = 2; 
      r.pathIndex += speed;
      
      if (r.pathIndex >= r.targetPath.length) {
        r.lat = r.targetPath[r.targetPath.length - 1].lat;
        r.lng = r.targetPath[r.targetPath.length - 1].lng;
        
        for (const orderId of r.activeOrders) {
          const order = simOrders.find(o => o.id === orderId);
          if (order) {
            order.status = 'COMPLETED';
            await logSim(`🎉 Rider [${r.name}] นำส่งสินค้าออร์เดอร์ ${orderId} สำเร็จเรียบร้อย! (สถานะออร์เดอร์ -> COMPLETED)`);
          }
        }
        
        await logSim(`🏁 Rider [${r.name}] ปฏิบัติภารกิจเสร็จสิ้นทั้งหมด กลับสู่สถานะ IDLE`);
        
        r.status = 'IDLE';
        r.activeOrders = [];
        r.targetPath = [];
        r.pathIndex = 0;
        r.routeSegments = [];
        stateChanged = true;
      } else {
        const currentPt = r.targetPath[r.pathIndex];
        r.lat = currentPt.lat;
        r.lng = currentPt.lng;
        
        let accumulatedPoints = 0;
        for (let i = 0; i < r.routeSegments.length; i++) {
          const segment = r.routeSegments[i];
          accumulatedPoints += segment.coords.length;
          
          if (r.pathIndex < accumulatedPoints) {
            const label = segment.label.toLowerCase();
            const orderMatch = label.match(/order (ord-\d+)/i);
            if (orderMatch) {
              const activeId = orderMatch[1].toUpperCase();
              const order = simOrders.find(o => o.id === activeId);
              if (order) {
                if (label.includes('store')) {
                  if (order.status !== 'PICKING_UP') {
                    order.status = 'PICKING_UP';
                    stateChanged = true;
                    await logSim(`🛵 Rider [${r.name}] กำลังเดินทางไปรับสินค้าที่ร้านสำหรับออร์เดอร์ ${activeId}`);
                  }
                  if (r.status !== 'PICKING_UP') {
                    r.status = 'PICKING_UP';
                    stateChanged = true;
                  }
                } else if (label.includes('dropoff')) {
                  if (order.status !== 'DELIVERING') {
                    order.status = 'DELIVERING';
                    stateChanged = true;
                    await logSim(`📦 Rider [${r.name}] รับสินค้าสำเร็จ กำลังออกเดินทางนำส่งออร์เดอร์ ${activeId}`);
                  }
                  if (r.status !== 'DELIVERING') {
                    r.status = 'DELIVERING';
                    stateChanged = true;
                  }
                }
              }
            }
            break;
          }
        }
      }
    }
    
    if (simTickCount % 4 === 0) {
      const activeRiders = simRiders.filter(r => r.status !== 'IDLE');
      const activeOrders = simOrders.filter(o => o.status !== 'COMPLETED');
      if (activeRiders.length > 0 || activeOrders.length > 0) {
        console.log(`\n==================================================`);
        console.log(`[TICK ${simTickCount}] SIMULATION ACTIVE SUMMARY`);
        console.log(`--------------------------------------------------`);
        console.log(`Active Riders (${activeRiders.length}):`);
        for (const r of activeRiders) {
          console.log(` - RID-${r.refNumber} [${r.name}] State: ${r.status} | Location: [${r.lat.toFixed(5)}, ${r.lng.toFixed(5)}] | Progress: ${r.pathIndex}/${r.targetPath.length} | Orders: ${r.activeOrders.join(', ')}`);
        }
        console.log(`Active Orders (${activeOrders.length}):`);
        for (const o of activeOrders) {
          console.log(` - ${o.id} (${o.type}) State: ${o.status} | Rider: ${o.riderId || 'None'}`);
        }
        console.log(`==================================================\n`);
      }
    }
    
    io.emit('simulator-tick', { riders: simRiders, orders: simOrders });
  }, 500);
}
app.get('/api/simulator/status', (req, res) => {
  res.json({
    running: simIntervalHandle !== null,
    sessionId: currentSimSessionId,
    ridersCount: simRiders.length
  });
});

app.post('/api/simulator/start', async (req, res) => {
  try {
    const session = ArtifactService.createSession('interactive', 'host');
    currentSimSessionId = session.sessionId;

    await logSim('========================================================================');
    await logSim(`เริ่มการทดสอบจำลองแผนที่แบบโต้ตอบ (Interactive Map Simulator)`);
    await logSim(`Session ID: ${currentSimSessionId}`);
    await logSim('========================================================================');
    
    if (simIntervalHandle) {
      clearInterval(simIntervalHandle);
      simIntervalHandle = null;
    }
    simRiders = [];
    simOrders = [];
    
    await logSim('กำลังดึงข้อมูล Rider จริงจากฐานข้อมูล .NET API...');
    const dbRiders = await fetchRidersFromBackend();
    
    if (dbRiders.length > 0) {
      await logSim(`ค้นพบไรเดอร์จำนวน ${dbRiders.length} คนในระบบ ดำเนินการสุ่มพิกัดเริ่มต้น...`);
      for (const r of dbRiders) {
        const rawLoc = randomCoordInUdon();
        const snapped = await snapToRoad(rawLoc.lat, rawLoc.lng);
        
        simRiders.push({
          id: r.id || r.Id || String(r.refNumber),
          refNumber: String(r.refNumber),
          name: r.name || r.Name,
          email: r.email || r.Email || '',
          status: 'IDLE',
          lat: snapped.lat,
          lng: snapped.lng,
          color: `hsl(${Math.floor(Math.random() * 360)}, 85%, 60%)`,
          activeOrders: [],
          targetPath: [],
          pathIndex: 0,
          routeSegments: []
        });
        await logSim(`Rider [${r.name}] (RID-${r.refNumber}) วางตำแหน่งเริ่มต้นที่พิกัดถนนจริง: [${snapped.lat.toFixed(5)}, ${snapped.lng.toFixed(5)}]`);
      }
    } else {
      await logSim('ไม่สามารถเชื่อมต่อฐานข้อมูลได้ หรือไม่มีข้อมูลในระบบ ดำเนินการสร้าง 10 Mock Riders...');
      for (let i = 1; i <= 10; i++) {
        const rawLoc = randomCoordInUdon();
        const snapped = await snapToRoad(rawLoc.lat, rawLoc.lng);
        simRiders.push({
          id: `mock-rider-${i}`,
          refNumber: `RID-00000${i}`,
          name: `Sim Rider ${i}`,
          email: `sim-rider-${i}@delivery.test`,
          status: 'IDLE',
          lat: snapped.lat,
          lng: snapped.lng,
          color: `hsl(${Math.floor(Math.random() * 360)}, 85%, 60%)`,
          activeOrders: [],
          targetPath: [],
          pathIndex: 0,
          routeSegments: []
        });
        await logSim(`Rider [Sim Rider ${i}] (RID-00000${i}) วางตำแหน่งเริ่มต้นที่พิกัดถนนจริง: [${snapped.lat.toFixed(5)}, ${snapped.lng.toFixed(5)}]`);
      }
    }
    
    await logSim('สตาร์ทลูปจำลองระบบขนส่ง (Simulation Tick Loop 500ms) เรียบร้อย พร้อมสำหรับสร้างออร์เดอร์');
    startSimLoop();
    io.emit('simulator-tick', { riders: simRiders, orders: simOrders });
    
    res.json({
      success: true,
      sessionId: currentSimSessionId,
      message: 'Simulation started successfully',
      ridersCount: simRiders.length
    });
  } catch (error: any) {
    console.error('[Simulator Start Error]:', error);
    res.status(500).json({ error: error.message });
  }
});

app.post('/api/simulator/create-order', async (req, res) => {
  try {
    const { type, pickup, dropoff, pickups, dropoffs } = req.body;
    
    let pickupsList: { lat: number; lng: number }[] = [];
    let dropoffsList: { lat: number; lng: number }[] = [];
    
    if (type === 'BATCH') {
      pickupsList = pickups || (pickup ? [pickup] : []);
      dropoffsList = dropoffs || (dropoff ? [dropoff] : []);
    } else {
      pickupsList = pickup ? [pickup] : [];
      dropoffsList = dropoff ? [dropoff] : [];
    }

    if (pickupsList.length === 0 || dropoffsList.length === 0) {
      return res.status(400).json({ error: 'pickup and dropoff coordinates are required' });
    }

    const orderId = `ORD-${Date.now().toString().slice(-6)}`;
    await logSim(`------------------------------------------------------------------------`);
    await logSim(`[สร้างออร์เดอร์ใหม่] หมายเลข: ${orderId} (ประเภท: ${type || 'SINGLE'})`);
    
    const snappedPickups = [];
    for (const p of pickupsList) {
      const snapped = await snapToRoad(p.lat, p.lng);
      snappedPickups.push(snapped);
      await logSim(`- จุดรับสินค้า (Pickup): [${snapped.lat.toFixed(5)}, ${snapped.lng.toFixed(5)}] (Snapped)`);
    }
    
    const snappedDropoffs = [];
    for (const d of dropoffsList) {
      const snapped = await snapToRoad(d.lat, d.lng);
      snappedDropoffs.push(snapped);
      await logSim(`- จุดส่งสินค้า (Dropoff): [${snapped.lat.toFixed(5)}, ${snapped.lng.toFixed(5)}] (Snapped)`);
    }
    
    const newOrder: SimOrder = {
      id: orderId,
      type: type || 'SINGLE',
      pickup: snappedPickups[0],
      dropoff: snappedDropoffs[0],
      pickups: snappedPickups,
      dropoffs: snappedDropoffs,
      status: 'PENDING',
      createdAt: new Date().toISOString()
    };
    
    await logSim('กำลังสแกนหา Rider ที่อยู่ใกล้ที่สุดและพร้อมปฏิบัติงาน...');
    let closestRider: SimRider | null = null;
    let minDistance = Infinity;
    
    const firstPickup = snappedPickups[0];
    console.log(`\n[ORDER MATCHING MATH] Matching rider for order ${orderId} (first pickup: [${firstPickup.lat.toFixed(5)}, ${firstPickup.lng.toFixed(5)}])`);
    console.log(`Candidates Euclidean distances list:`);
    
    for (const r of simRiders) {
      if (r.status === 'IDLE' || r.status === 'PICKING_UP') {
        const dist = Math.sqrt(Math.pow(r.lat - firstPickup.lat, 2) + Math.pow(r.lng - firstPickup.lng, 2));
        console.log(` - RID-${r.refNumber} [${r.name}] | state: ${r.status} | distance: ${dist.toFixed(6)} degrees`);
        if (dist < minDistance) {
          minDistance = dist;
          closestRider = r;
        }
      }
    }
    
    if (!closestRider) {
      console.log(` -> Match failed: No eligible riders available.\n`);
      await logSim('❌ ไม่พบไรเดอร์ที่พร้อมให้บริการในขณะนี้! (ทุกคนกำลังยุ่งหรือปิดแอป)');
      return res.status(400).json({ error: 'No eligible riders available at the moment' });
    }
    
    console.log(` -> Selected closest rider: RID-${closestRider.refNumber} [${closestRider.name}] with distance ${minDistance.toFixed(6)} degrees\n`);
    
    newOrder.riderId = closestRider.id;
    newOrder.status = 'PICKING_UP';
    simOrders.push(newOrder);
    
    closestRider.activeOrders.push(orderId);
    
    if (closestRider.status === 'IDLE') {
      closestRider.status = 'PICKING_UP';
      await logSim(`✅ เลือกไรเดอร์ [${closestRider.name}] (RID-${closestRider.refNumber}) (สถานะเดิม: IDLE)`);
      await logSim(`- กำลังดึงเส้นทางถนนจริง (OSRM Route) จากไรเดอร์ -> จุดรับ -> จุดส่ง...`);
      
      const routeSegments = [];
      let currentStart = { lat: closestRider.lat, lng: closestRider.lng };
      
      for (let i = 0; i < snappedPickups.length; i++) {
        const pPt = snappedPickups[i];
        const segRoute = await getOsrmRoute(currentStart, pPt);
        routeSegments.push({
          label: `to store (Order ${orderId})`,
          coords: segRoute
        });
        currentStart = pPt;
      }
      
      for (let i = 0; i < snappedDropoffs.length; i++) {
        const dPt = snappedDropoffs[i];
        const segRoute = await getOsrmRoute(currentStart, dPt);
        routeSegments.push({
          label: `to dropoff (Order ${orderId})`,
          coords: segRoute
        });
        currentStart = dPt;
      }
      
      closestRider.targetPath = routeSegments.flatMap(s => s.coords);
      closestRider.pathIndex = 0;
      closestRider.routeSegments = routeSegments;
      await logSim(`- โหลดแผนการเดินทางสำเร็จ: รวม ${closestRider.targetPath.length} พิกัดพาสถนน (${routeSegments.length} segments)`);
    } else {
      // Task Merging!
      await logSim(`🔄 เลือกไรเดอร์ [${closestRider.name}] (RID-${closestRider.refNumber})`);
      await logSim(`⚠️ แจ้งเตือน: ไรเดอร์กำลังมุ่งหน้าไปรับของออร์เดอร์เดิมอยู่! ทำการผสานภารกิจ (Task Merging)...`);
      await logSim(`- ลำดับจุดจอดใหม่: ไรเดอร์ปัจจุบัน -> ร้านค้าออร์เดอร์เดิม -> ร้านค้าออร์เดอร์ใหม่ -> จุดส่งออร์เดอร์เดิม -> จุดส่งออร์เดอร์ใหม่`);
      
      const currentLoc = { lat: closestRider.lat, lng: closestRider.lng };
      const firstOrderId = closestRider.activeOrders[0];
      const firstOrder = simOrders.find(o => o.id === firstOrderId)!;
      
      const firstOrderPickups = firstOrder.pickups || [firstOrder.pickup];
      const firstOrderDropoffs = firstOrder.dropoffs || [firstOrder.dropoff];
      
      const mergedSegments = [];
      let currentStart = currentLoc;
      
      // 1. Visit remaining pickups of Order A
      for (const p of firstOrderPickups) {
        const segRoute = await getOsrmRoute(currentStart, p);
        mergedSegments.push({
          label: `to store (Order ${firstOrderId})`,
          coords: segRoute
        });
        currentStart = p;
      }
      
      // 2. Visit pickups of Order B
      for (const p of snappedPickups) {
        const segRoute = await getOsrmRoute(currentStart, p);
        mergedSegments.push({
          label: `to store (Order ${orderId})`,
          coords: segRoute
        });
        currentStart = p;
      }
      
      // 3. Visit dropoffs of Order A
      for (const d of firstOrderDropoffs) {
        const segRoute = await getOsrmRoute(currentStart, d);
        mergedSegments.push({
          label: `to dropoff (Order ${firstOrderId})`,
          coords: segRoute
        });
        currentStart = d;
      }
      
      // 4. Visit dropoffs of Order B
      for (const d of snappedDropoffs) {
        const segRoute = await getOsrmRoute(currentStart, d);
        mergedSegments.push({
          label: `to dropoff (Order ${orderId})`,
          coords: segRoute
        });
        currentStart = d;
      }
      
      closestRider.targetPath = mergedSegments.flatMap(s => s.coords);
      closestRider.pathIndex = 0;
      closestRider.routeSegments = mergedSegments;
      await logSim(`- คำนวณเส้นทางผสานงาน OSRM สำเร็จ: รวม ${closestRider.targetPath.length} พิกัดพาสถนน (${mergedSegments.length} segments)`);
    }
    
    io.emit('simulator-tick', { riders: simRiders, orders: simOrders });
    
    res.json({
      success: true,
      order: newOrder,
      assignedRider: closestRider.name
    });
  } catch (error: any) {
    console.error('[Simulator Create Order Error]:', error);
    res.status(500).json({ error: error.message });
  }
});

// Socket.io Connection & Streaming Orchestration
io.on('connection', (socket) => {
  console.log(`[Socket] Client connected: ${socket.id}`);

  // Register room subscription
  socket.on('join-session', async (sessionId: string) => {
    socket.join(`session:${sessionId}`);
    console.log(`[Socket] Client ${socket.id} joined room session:${sessionId}`);

    // Subscribe subscriber client to channel
    await subClient.subscribe(`session:${sessionId}:logs`);
    await subClient.subscribe(`session:${sessionId}:status`);

    // Stream historical log file content so terminal starts populated
    const logPath = ArtifactService.getLogPath(sessionId);
    if (fs.existsSync(logPath)) {
      const historyLogs = fs.readFileSync(logPath, 'utf-8');
      socket.emit('log-history', historyLogs);
    }
  });

  // Client leave room
  socket.on('leave-session', async (sessionId: string) => {
    socket.leave(`session:${sessionId}`);
    console.log(`[Socket] Client ${socket.id} left room session:${sessionId}`);
    
    // Check if any other clients are in the room, if not unsubscribe
    const clientsInRoom = io.sockets.adapter.rooms.get(`session:${sessionId}`);
    if (!clientsInRoom || clientsInRoom.size === 0) {
      await subClient.unsubscribe(`session:${sessionId}:logs`);
      await subClient.unsubscribe(`session:${sessionId}:status`);
    }
  });

  // Keep-alive heartbeat connection checks
  socket.on('ping', () => {
    socket.emit('pong');
  });

  socket.on('disconnect', () => {
    console.log(`[Socket] Client disconnected: ${socket.id}`);
  });
});

server.listen(PORT, () => {
  console.log(`[API Server] Server is running on port ${PORT}`);
});
