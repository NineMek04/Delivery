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

// Listen for Redis pub/sub messages and broadcast them over Socket.io
subClient.on('message', (channel, message) => {
  const parts = channel.split(':');
  if (parts.length >= 3) {
    const sessionId = parts[1];
    const eventType = parts[2]; // 'logs' or 'status'

    if (eventType === 'logs') {
      io.to(`session:${sessionId}`).emit('log', message);
    } else if (eventType === 'status') {
      io.to(`session:${sessionId}`).emit('status', JSON.parse(message));
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
