from fastapi import FastAPI, Depends
from app.api.v1.api import v1_router
from app.api.v1.endpoints import optimize
from app.core.security import verify_api_key
import os
import sys

def load_vault_config():
    vault_addr = os.getenv("VAULT_ADDR")
    role_id_file = os.getenv("VAULT_ROLE_ID_FILE")
    secret_id_file = os.getenv("VAULT_SECRET_ID_FILE")
    vault_required = os.getenv("VAULT_REQUIRED", "false").lower() == "true"

    if not vault_addr or not role_id_file or not secret_id_file:
        if vault_required:
            print("ERROR: VAULT_REQUIRED is true but VAULT config vars are missing.", file=sys.stderr)
            sys.exit(1)
        return

    try:
        import hvac
        with open(role_id_file, 'r') as f:
            role_id = f.read().strip()
        with open(secret_id_file, 'r') as f:
            secret_id = f.read().strip()

        client = hvac.Client(url=vault_addr)
        client.auth.approle.login(role_id=role_id, secret_id=secret_id)
        
        try:
            secret_version_response = client.secrets.kv.v2.read_secret_version(
                mount_point='secret',
                path='delivery/route-optimizer'
            )
        except Exception:
            secret_version_response = client.secrets.kv.v2.read_secret_version(
                mount_point='secret',
                path='delivery/ai'
            )
        
        secrets = secret_version_response['data']['data']
        
        route_optimizer_key = secrets.get("RouteOptimizerApiKey") or secrets.get("AiServiceApiKey")
        if route_optimizer_key:
            os.environ["ROUTE_OPTIMIZER_API_KEY"] = route_optimizer_key
            os.environ["AI_SERVICE_API_KEY"] = route_optimizer_key
            
        if "PostgresPassword" in secrets:
            db_url = os.getenv("DATABASE_URL", "")
            if "postgresql://postgres@" in db_url:
                os.environ["DATABASE_URL"] = db_url.replace(
                    "postgresql://postgres@",
                    f"postgresql://postgres:{secrets['PostgresPassword']}@"
                )

        print("[Vault] Successfully loaded secrets via AppRole")
        
    except Exception as e:
        if vault_required:
            print(f"ERROR: Failed to load configuration from Vault and VAULT_REQUIRED is true. {e}", file=sys.stderr)
            sys.exit(1)
        print(f"[Vault] Warning: Failed to load secrets from Vault. Fallback to Env. Error: {e}")

# Run config load at startup before FastAPI initializes
load_vault_config()

# Initialize FastAPI App
app = FastAPI(
    title="Delivery Routing Optimization API",
    description="Deterministic route optimization, weighted heuristic rider ranking, and ETA estimation service.",
    version="0.2.1",
)

# Register API Routers
# /api/v1/dispatch/rank (protected by API key)
app.include_router(v1_router, prefix="/api/v1", dependencies=[Depends(verify_api_key)])

# /api/optimize-route (protected by API key)
app.include_router(optimize.router, prefix="/api", tags=["routing"], dependencies=[Depends(verify_api_key)])

@app.get("/health")
def health_check():
    """Health check endpoint for Docker / load balancer"""
    return {
        "status": "ok", 
        "service": "route-optimizer",
        "version": app.version
    }

if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8000, reload=True)
