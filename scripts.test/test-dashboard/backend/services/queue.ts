import { Queue, QueueEvents } from 'bullmq';
import Redis from 'ioredis';
import dotenv from 'dotenv';

dotenv.config();

const REDIS_HOST = process.env.REDIS_HOST || 'localhost';
const REDIS_PORT = parseInt(process.env.REDIS_PORT || '6379', 10);

export const connection = new Redis({
  host: REDIS_HOST,
  port: REDIS_PORT,
  maxRetriesPerRequest: null, // Required by BullMQ
});

export const QUEUE_NAME = 'test-runs';

export const testQueue = new Queue(QUEUE_NAME, {
  connection: connection as any,
  defaultJobOptions: {
    attempts: 1,
    removeOnComplete: true,
    removeOnFail: true,
  },
});

export const queueEvents = new QueueEvents(QUEUE_NAME, { connection: connection as any });

console.log(`[Queue] Queue initialized on Redis ${REDIS_HOST}:${REDIS_PORT}`);
