/**
 * massive-batch-dispatch.js — Direct RabbitMQ Massive Event Injector
 *
 * Usage:
 *   node massive-batch-dispatch.js [--host localhost] [--port 5672] [--events 100000]
 *
 * Goal:
 *   - Connect to RabbitMQ using AMQP protocol
 *   - Fast publish N (100,000) OrderCreatedIntegrationEvent messages within 5 seconds
 *   - Monitor the processing rate and ensure database deadlock resilience
 */

const amqp = require("amqplib");
const crypto = require("crypto");

const args = process.argv.slice(2);
function getArg(name, defaultValue) {
  const idx = args.indexOf(`--${name}`);
  return idx >= 0 && args[idx + 1] ? args[idx + 1] : defaultValue;
}

const RMQ_HOST = getArg("host", "localhost");
const RMQ_PORT = getArg("port", "5672");
const TOTAL_EVENTS = parseInt(getArg("events", "100000"), 10);

const ExchangeName = "delivery_event_bus";
const RoutingKey = "OrderCreatedIntegrationEvent";

async function main() {
  console.log("===============================================");
  console.log("  Massive Batch Dispatch Event Injector");
  console.log(`  Target RabbitMQ: amqp://${RMQ_HOST}:${RMQ_PORT}`);
  console.log(`  Publishing: ${TOTAL_EVENTS.toLocaleString()} OrderCreatedIntegrationEvents`);
  console.log("===============================================");

  try {
    const conn = await amqp.connect(`amqp://${RMQ_HOST}:${RMQ_PORT}`);
    const channel = await conn.createChannel();

    // Ensure exchange exists
    await channel.assertExchange(ExchangeName, "direct", { durable: true });

    console.log("Connected to RabbitMQ. Commencing rapid publish...");

    const startTime = Date.now();
    let publishedCount = 0;
    
    // High-performance pipelined publish using write buffer draining
    for (let i = 1; i <= TOTAL_EVENTS; i++) {
      const orderId = `stress-order-${i}-${Date.now()}`;
      
      const payload = {
        Id: crypto.randomUUID(),
        CreationDate: new Date().toISOString(),
        CorrelationId: `stress-corr-${i}-${Date.now()}`,
        OrderId: orderId,
        RefNumber: 1000000 + i,
        State: 0, // OrderState.CREATED
        PickupLatitude: 13.7563 + (Math.random() - 0.5) * 0.05,
        PickupLongitude: 100.5018 + (Math.random() - 0.5) * 0.05,
        DropoffLatitude: 13.7663 + (Math.random() - 0.5) * 0.05,
        DropoffLongitude: 100.5118 + (Math.random() - 0.5) * 0.05,
        DistanceKm: 2.5 + Math.random() * 5,
        DeliveryFee: 35.0 + Math.random() * 40
      };

      const messageBuffer = Buffer.from(JSON.stringify(payload));
      
      const properties = {
        persistent: true,
        type: "OrderCreatedIntegrationEvent",
        headers: {
          "X-Correlation-Id": payload.CorrelationId
        }
      };

      const sent = channel.publish(ExchangeName, RoutingKey, messageBuffer, properties);
      publishedCount++;

      // Implement backpressure check if the socket buffer is full
      if (!sent) {
        await new Promise(resolve => channel.once("drain", resolve));
      }

      if (i % 20000 === 0) {
        const rate = publishedCount / ((Date.now() - startTime) / 1000);
        console.log(`  - Sent: ${publishedCount.toLocaleString()} events... (Rate: ${Math.round(rate).toLocaleString()} msg/sec)`);
      }
    }

    const elapsedSec = (Date.now() - startTime) / 1000;
    const finalRate = publishedCount / elapsedSec;

    console.log("\n===============================================");
    console.log("🚀 INJECTION COMPLETED SUCCESSFULLY!");
    console.log(`Total Injected: ${publishedCount.toLocaleString()} events`);
    console.log(`Elapsed Time: ${elapsedSec.toFixed(2)} seconds`);
    console.log(`Overall Ingestion Rate: ${Math.round(finalRate).toLocaleString()} events/second`);
    console.log("===============================================");

    console.log("\nKeeping connection open for 10 seconds to monitor consumer pick-up...");
    await new Promise(resolve => setTimeout(resolve, 10000));

    await channel.close();
    await conn.close();
    console.log("Connection closed gracefully.");
  } catch (err) {
    console.error("An error occurred during stress testing:", err);
  }
}

main().catch(console.error);
