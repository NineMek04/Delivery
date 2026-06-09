const axios = require("axios");

const API_URL = process.env.API_URL || "http://localhost:5000";
const url = `${API_URL}/api/telemetry/gps/batch`;

async function getRiderToken() {
  const email = `stress_sustained_${Date.now()}_${Math.random().toString(36).substring(7)}@test.com`;
  try {
    const res = await axios.post(`${API_URL}/api/v1/auth/register`, {
      email,
      password: "StressTest123!",
      fullName: "Sustained Stress Rider",
      role: "Rider",
    });
    return res.data?.value?.accessToken;
  } catch (err) {
    console.error("Failed to register:", err.message);
    process.exit(1);
  }
}

function generatePayload() {
  return Array.from({ length: 5 }).map(() => ({
    Latitude: 13.7 + (Math.random() * 0.01),
    Longitude: 100.5 + (Math.random() * 0.01),
    Accuracy: 10.0,
    Timestamp: new Date().toISOString()
  }));
}

async function main() {
  console.log("===============================================");
  console.log("  Sustained Load Stress Test (15 minutes)");
  console.log("  Target RPS: 1,000");
  console.log(`  Target URL: ${url}`);
  console.log("===============================================");

  const token = await getRiderToken();
  const currentRps = 1000;
  const durationMs = 15 * 60 * 1000; // 15 minutes
  const startTime = Date.now();
  const endTime = startTime + durationMs;

  let totalRequestsSent = 0;
  let totalErrorsCount = 0;
  let totalSuccessCount = 0;

  console.log(`Starting... Will finish at ${new Date(endTime).toISOString()}`);

  while (Date.now() < endTime) {
    const loopStart = Date.now();
    const requestPromises = [];
    
    // Distribute 1000 RPS over 100 ticks per second -> 10 requests per tick (10ms)
    const batchSize = Math.max(1, Math.round(currentRps / 100)); 
    for (let i = 0; i < batchSize; i++) {
      requestPromises.push(
        axios.post(url, generatePayload(), {
          headers: { Authorization: `Bearer ${token}` },
          timeout: 4000
        })
        .then(() => totalSuccessCount++)
        .catch(() => totalErrorsCount++)
      );
      totalRequestsSent++;
    }
    
    const elapsed = Date.now() - loopStart;
    const wait = 10 - elapsed; 
    if (wait > 0) {
      await new Promise(resolve => setTimeout(resolve, wait));
    }

    if (totalRequestsSent % 50000 === 0) {
      console.log(`[${new Date().toISOString()}] Sent: ${totalRequestsSent}, Success: ${totalSuccessCount}, Errors: ${totalErrorsCount}`);
    }
  }

  console.log(`\n===============================================`);
  console.log(`🏆 SUSTAINED TEST COMPLETE!`);
  console.log(`Total Sent: ${totalRequestsSent}`);
  console.log(`Total Success: ${totalSuccessCount}`);
  console.log(`Total Errors: ${totalErrorsCount}`);
  console.log(`===============================================`);
}

main().catch(console.error);
