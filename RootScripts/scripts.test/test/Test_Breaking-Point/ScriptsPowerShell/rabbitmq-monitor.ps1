Write-Host "Fetching RabbitMQ Queues..."

$queues = docker exec delivery-rabbitmq rabbitmqctl list_queues name messages_ready messages_unacknowledged consumers

Write-Host "--------------------------------------------------------"
Write-Host "RabbitMQ Queue Depth & Consumers"
Write-Host "--------------------------------------------------------"
$queues | ForEach-Object { Write-Host $_ }
