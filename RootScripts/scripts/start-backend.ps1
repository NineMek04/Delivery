$workspaceRoot = $PSScriptRoot
$envFile = Join-Path $workspaceRoot ".env"

if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -and !$line.StartsWith("#")) {
            $key, $value = $line -split "=", 2
            if ($key -and $value) {
                $value = $value.Trim().Trim('"').Trim("'")
                [Environment]::SetEnvironmentVariable(
                    $key.Trim(),
                    $value,
                    [EnvironmentVariableTarget]::Process)
            }
        }
    }
}

$requiredVariables = @(
    "POSTGRES_PASSWORD",
    "REDIS_PASSWORD",
    "JWT_SECRET",
    "AI_SERVICE_API_KEY",
    "RABBITMQ_USER",
    "RABBITMQ_PASSWORD"
)

foreach ($name in $requiredVariables) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Required environment variable '$name' is missing. Configure it in .env or the current process."
    }
}

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://0.0.0.0:5000"
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Database=delivery_db;Username=postgres;Password=$env:POSTGRES_PASSWORD;Maximum Pool Size=1024;"
$env:ConnectionStrings__Redis = "localhost:6379,password=$env:REDIS_PASSWORD"
$env:AI_SERVICE_URL = "http://localhost:8000"
$env:Routing__LocalOsrmUrl = "http://localhost:5001"
$env:MessageBroker__Host = "localhost"
$env:MessageBroker__Port = "5672"
$env:MessageBroker__Username = $env:RABBITMQ_USER
$env:MessageBroker__Password = $env:RABBITMQ_PASSWORD
$env:Jwt__Key = $env:JWT_SECRET
$env:Jwt__Issuer = "DeliveryBackendApi"
$env:Jwt__Audience = "DeliveryClients"
$env:Authentication__RequireSecureCookie = "false"

Set-Location (Join-Path $workspaceRoot "BackendApi")
dotnet run
