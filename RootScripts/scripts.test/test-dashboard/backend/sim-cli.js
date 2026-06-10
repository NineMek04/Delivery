const http = require('http');

const LANDMARKS = [
  { name: 'Central Plaza', lat: 17.4082, lng: 102.7984 },
  { name: 'UD Town', lat: 17.4038, lng: 102.8072 },
  { name: 'Train Market', lat: 17.4042, lng: 102.8021 },
  { name: 'Nong Prajak', lat: 17.4215, lng: 102.7830 },
  { name: 'Tung Sri Muang', lat: 17.4111, lng: 102.7885 }
];

function printUsage() {
  console.log(`
Interactive Simulator CLI Tool
==============================
Usage:
  node sim-cli.js start
  node sim-cli.js status
  node sim-cli.js order --type SINGLE --pickup <landmarkIndex|lat,lng> --dropoff <landmarkIndex|lat,lng>
  node sim-cli.js order --type BATCH --pickups <landmarkIndicesOrCoordsSeparatedBySemicolon> --dropoffs <landmarkIndicesOrCoordsSeparatedBySemicolon>

Landmarks:
${LANDMARKS.map((l, i) => `  [${i}] ${l.name} (${l.lat}, ${l.lng})`).join('\n')}

Examples:
  node sim-cli.js start
  node sim-cli.js status
  node sim-cli.js order --type SINGLE --pickup 0 --dropoff 3
  node sim-cli.js order --type BATCH --pickups "0;1" --dropoffs "3;4"
  node sim-cli.js order --type BATCH --pickups "17.4082,102.7984" --dropoffs "17.4215,102.7830"
`);
}

function parseCoords(input) {
  if (!input) throw new Error('Missing input');
  const idx = parseInt(input, 10);
  if (!isNaN(idx) && idx >= 0 && idx < LANDMARKS.length && !input.includes(',')) {
    return { lat: LANDMARKS[idx].lat, lng: LANDMARKS[idx].lng };
  }
  const parts = input.split(',');
  if (parts.length === 2) {
    const lat = parseFloat(parts[0].trim());
    const lng = parseFloat(parts[1].trim());
    if (!isNaN(lat) && !isNaN(lng)) {
      return { lat, lng };
    }
  }
  throw new Error(`Invalid coordinate/landmark input: ${input}`);
}

function makeRequest(path, method, body = null) {
  return new Promise((resolve, reject) => {
    const dataString = body ? JSON.stringify(body) : '';
    const options = {
      hostname: 'localhost',
      port: 3001,
      path: path,
      method: method,
      headers: {
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(dataString)
      }
    };

    const req = http.request(options, (res) => {
      let responseBody = '';
      res.on('data', (chunk) => { responseBody += chunk; });
      res.on('end', () => {
        try {
          const parsed = JSON.parse(responseBody);
          if (res.statusCode >= 400) {
            reject(new Error(parsed.error || `HTTP ${res.statusCode}`));
          } else {
            resolve(parsed);
          }
        } catch (e) {
          reject(new Error(`Non-JSON response (HTTP ${res.statusCode}): ${responseBody}`));
        }
      });
    });

    req.on('error', (err) => {
      reject(new Error(`Failed to connect to backend server: ${err.message}`));
    });

    if (dataString) {
      req.write(dataString);
    }
    req.end();
  });
}

async function main() {
  const args = process.argv.slice(2);
  const cmd = args[0];

  if (!cmd || cmd === 'help' || cmd === '--help' || cmd === '-h') {
    printUsage();
    return;
  }

  try {
    if (cmd === 'start') {
      console.log('Sending start simulation signal...');
      const res = await makeRequest('/api/simulator/start', 'POST');
      console.log('Success:', res.message);
      console.log('Session ID:', res.sessionId);
      console.log('Riders initialized:', res.ridersCount);
    } else if (cmd === 'status') {
      console.log('Fetching status...');
      const res = await makeRequest('/api/simulator/status', 'GET');
      console.log('Status:');
      console.log('  Running:', res.running ? '✅ Yes' : '❌ No');
      console.log('  Session ID:', res.sessionId || 'None');
      console.log('  Riders count:', res.ridersCount);
    } else if (cmd === 'order') {
      // Parse arguments
      let type = 'SINGLE';
      let pickupInput = '';
      let dropoffInput = '';
      let pickupsInput = '';
      let dropoffsInput = '';

      for (let i = 1; i < args.length; i++) {
        if (args[i] === '--type') type = args[++i].toUpperCase();
        else if (args[i] === '--pickup') pickupInput = args[++i];
        else if (args[i] === '--dropoff') dropoffInput = args[++i];
        else if (args[i] === '--pickups') pickupsInput = args[++i];
        else if (args[i] === '--dropoffs') dropoffsInput = args[++i];
      }

      if (type === 'BATCH') {
        const pInput = pickupsInput || pickupInput;
        const dInput = dropoffsInput || dropoffInput;
        if (!pInput || !dInput) {
          throw new Error('Batch order requires --pickups and --dropoffs stops.');
        }
        const pickups = pInput.split(';').map(parseCoords);
        const dropoffs = dInput.split(';').map(parseCoords);

        console.log(`Creating BATCH order with ${pickups.length} pickups and ${dropoffs.length} dropoffs...`);
        const res = await makeRequest('/api/simulator/create-order', 'POST', {
          type,
          pickups,
          dropoffs
        });
        console.log('Success: Order created!');
        console.log('Order ID:', res.order.id);
        console.log('Assigned Rider:', res.assignedRider);
      } else {
        if (!pickupInput || !dropoffInput) {
          throw new Error('Single order requires --pickup and --dropoff stops.');
        }
        const pickup = parseCoords(pickupInput);
        const dropoff = parseCoords(dropoffInput);

        console.log('Creating SINGLE order...');
        const res = await makeRequest('/api/simulator/create-order', 'POST', {
          type,
          pickup,
          dropoff
        });
        console.log('Success: Order created!');
        console.log('Order ID:', res.order.id);
        console.log('Assigned Rider:', res.assignedRider);
      }
    } else {
      console.error(`Unknown command: ${cmd}`);
      printUsage();
    }
  } catch (err) {
    console.error('Error:', err.message);
    process.exit(1);
  }
}

main();
