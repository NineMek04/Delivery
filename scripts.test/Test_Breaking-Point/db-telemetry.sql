-- 1. Database Connections & Deadlocks
SELECT datname, numbackends as active_connections, deadlocks 
FROM pg_stat_database 
WHERE datname = 'delivery_db';

-- 2. Top 10 Queries by Total Time
SELECT query, calls, total_exec_time as total_time, mean_exec_time as mean_time, rows
FROM pg_stat_statements
ORDER BY total_exec_time DESC
LIMIT 10;

-- 3. Top 10 Queries by Mean Time
SELECT query, calls, total_exec_time as total_time, mean_exec_time as mean_time, rows
FROM pg_stat_statements
ORDER BY mean_exec_time DESC
LIMIT 10;

-- 4. Active Wait Events (CPU, Lock, IO, Connection Pool)
SELECT wait_event_type, wait_event, COUNT(*) as count
FROM pg_stat_activity
WHERE state = 'active'
GROUP BY wait_event_type, wait_event
ORDER BY count DESC;

-- 5. Active Locks
SELECT relation::regclass, mode, granted, COUNT(*)
FROM pg_locks
GROUP BY relation, mode, granted
ORDER BY count DESC;
