/**
 * ai-engine-stress.js — AI Engine Saturation Stress Test
 *
 * Benchmarks FastAPI + Google OR-Tools VRP and Rider Ranking under concurrent CPU load.
 *
 * Usage:
 *   node ai-engine-stress.js [--concurrency 15]
 *
 * Environment:
 *   AI_URL — AI Service URL (default: http://localhost:8000)
 *   AI_KEY — AI API Key (default: DeliverySmartRoutingSystem_AiEngine_ApiKey_2026)
 */

const axios = require("axios");

const AI_URL = process.env.AI_URL || "http://localhost:8000";
const AI_KEY = process.env.AI_KEY || "DeliverySmartRoutingSystem_AiEngine_ApiKey_2026";

const args = process.argv.slice(2);
function getArg(name, defaultValue) {
  const idx = args.indexOf(`--${name}`);
  return idx >= 0 && args[idx + 1] ? args[idx + 1] : defaultValue;
}

const CONCURRENCY = parseInt(getArg("concurrency", "15"), 10); // Concurrent requests per wave

const headers = {
  "X-API-Key": AI_KEY,
  "Content-Type": "application/json",
};

function generateRankRequest(riderCount) {
  const candidates = [];
  for (let i = 0; i < riderCount; i++) {
    candidates.push({
      rider_id: `rider-${i}`,
      lat: 17.41 + (Math.random() - 0.5) * 0.05,
      lng: 102.78 + (Math.random() - 0.5) * 0.05,
      speed_kmh: 20.0,
      current_tasks: [],
    });
  }

  return {
    context: {
      timestamp: new Date().toISOString(),
      city: "udon-thani",
    },
    order: {
      id: `order-stress-${Date.now()}`,
      pickup: [17.41, 102.78],
      dropoff: [17.42, 102.79],
      sla_limit_minutes: 30,
    },
    candidates,
  };
}

function generateOptimizeRequest(orderCount) {
  const locations = [
    { id: "depot", lat: 17.41, lng: 102.78 } // Index 0
  ];
  const pickups_deliveries = [];

  for (let i = 1; i <= orderCount; i++) {
    const pickupIndex = 2 * i - 1;
    const deliveryIndex = 2 * i;
    
    locations.push({
      id: `pickup-${i}`,
      lat: 17.41 + (Math.random() - 0.5) * 0.02,
      lng: 102.78 + (Math.random() - 0.5) * 0.02,
    });
    
    locations.push({
      id: `delivery-${i}`,
      lat: 17.41 + (Math.random() - 0.5) * 0.04,
      lng: 102.78 + (Math.random() - 0.5) * 0.04,
    });

    pickups_deliveries.push([pickupIndex, deliveryIndex]);
  }

  return {
    locations,
    num_vehicles: Math.max(1, Math.min(5, Math.ceil(orderCount / 10))), // scale vehicle count with order size
    depot: 0,
    pickups_deliveries,
  };
}

async function sendRequest(url, payload) {
  const start = Date.now();
  try {
    const res = await axios.post(url, payload, { headers, timeout: 15000 });
    const latency = Date.now() - start;
    return { success: true, latency, status: res.status };
  } catch (err) {
    const latency = Date.now() - start;
    const status = err.response?.status || "TIMEOUT/NETWORK_ERR";
    return { success: false, latency, status, error: err.response?.data || err.message };
  }
}

function percentile(arr, p) {
  if (arr.length === 0) return 0;
  const sorted = [...arr].sort((a, b) => a - b);
  const idx = Math.ceil((p / 100) * sorted.length) - 1;
  return sorted[Math.max(0, idx)];
}

async function runBenchmark(name, rankPayload, optPayload) {
  console.log(`\n-------------------------------------------------`);
  console.log(`  Running Tier: ${name}`);
  console.log(`-------------------------------------------------`);
  console.log(`  - Candidates for Rank:       ${rankPayload.candidates.length}`);
  console.log(`  - Locations for Optimize:    ${optPayload.locations.length} (${(optPayload.locations.length - 1)/2} orders)`);
  console.log(`  - Concurrency level:         ${CONCURRENCY} simultaneous requests`);

  // 1. Rank Endpoint Benchmarking
  console.log(`  [Rank] Firing ${CONCURRENCY} concurrent requests...`);
  const rankPromises = Array.from({ length: CONCURRENCY }).map(() => 
    sendRequest(`${AI_URL}/api/v1/dispatch/rank`, rankPayload)
  );
  const rankResults = await Promise.all(rankPromises);

  // 2. Optimize Endpoint Benchmarking
  console.log(`  [Optimize] Firing ${CONCURRENCY} concurrent requests...`);
  const optPromises = Array.from({ length: CONCURRENCY }).map(() => 
    sendRequest(`${AI_URL}/api/optimize-route`, optPayload)
  );
  const optResults = await Promise.all(optPromises);

  // Summarize
  const summarize = (results) => {
    const successCount = results.filter(r => r.success).length;
    const failCount = results.length - successCount;
    const latencies = results.map(r => r.latency);
    const avg = latencies.reduce((a, b) => a + b, 0) / results.length;
    const statusCodes = {};
    results.forEach(r => {
      statusCodes[r.status] = (statusCodes[r.status] || 0) + 1;
    });

    return {
      success: successCount,
      failed: failCount,
      avg: avg.toFixed(0),
      p50: percentile(latencies, 50),
      p95: percentile(latencies, 95),
      p99: percentile(latencies, 99),
      statusCodes,
    };
  };

  const rankStats = summarize(rankResults);
  const optStats = summarize(optResults);

  console.log(`\n  Results for ${name}:`);
  console.log(`    [Rank]     Success: ${rankStats.success}/${CONCURRENCY} | Avg: ${rankStats.avg}ms | p50: ${rankStats.p50}ms | p95: ${rankStats.p95}ms | Codes: ${JSON.stringify(rankStats.statusCodes)}`);
  console.log(`    [Optimize] Success: ${optStats.success}/${CONCURRENCY} | Avg: ${optStats.avg}ms | p50: ${optStats.p50}ms | p95: ${optStats.p95}ms | Codes: ${JSON.stringify(optStats.statusCodes)}`);
  
  return { tier: name, rank: rankStats, optimize: optStats };
}

async function main() {
  console.log("=================================================");
  console.log("  AI Engine Saturation Stress Test");
  console.log(`  Target AI Service: ${AI_URL}`);
  console.log(`  Concurrency level: ${CONCURRENCY}`);
  console.log("=================================================");

  // 1. Simple Tier
  const simpleRank = generateRankRequest(5);
  const simpleOpt = generateOptimizeRequest(1); // 1 order = 3 locations
  const simpleStats = await runBenchmark("Simple Routing", simpleRank, simpleOpt);

  // 2. Medium Tier
  const medRank = generateRankRequest(50);
  const medOpt = generateOptimizeRequest(20); // 20 orders = 41 locations
  const medStats = await runBenchmark("Medium Routing", medRank, medOpt);

  // 3. Worst-case Tier
  const worstRank = generateRankRequest(200);
  const worstOpt = generateOptimizeRequest(49); // 49 orders = 99 locations (fits max 100 locations)
  const worstStats = await runBenchmark("Worst-case Routing", worstRank, worstOpt);

  console.log("\n=================================================");
  console.log("  BENCHMARK SUMMARY");
  console.log("=================================================");
  const printRow = (title, stats) => {
    console.log(`  ${title}:`);
    console.log(`    Rank      - Avg: ${stats.rank.avg}ms | p50: ${stats.rank.p50}ms | p95: ${stats.rank.p95}ms | Errors: ${stats.rank.failed}`);
    console.log(`    Optimize  - Avg: ${stats.optimize.avg}ms | p50: ${stats.optimize.p50}ms | p95: ${stats.optimize.p95}ms | Errors: ${stats.optimize.failed}`);
  };
  printRow("Simple", simpleStats);
  printRow("Medium", medStats);
  printRow("Worst-case", worstStats);
  console.log("=================================================");
  
  const totalFailed = simpleStats.rank.failed + simpleStats.optimize.failed + medStats.rank.failed + medStats.optimize.failed + worstStats.rank.failed + worstStats.optimize.failed;
  if (totalFailed > 0) {
    console.warn("\n⚠️  [WARNING] Some requests failed or timed out during the AI stress benchmark!");
  } else {
    console.log("\n✅ [SUCCESS] All AI benchmark waves completed successfully with zero failures.");
  }
}

main().catch(console.error);
