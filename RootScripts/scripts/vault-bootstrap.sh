#!/bin/sh
export VAULT_ADDR='http://vault:8200'

# Wait for vault to be ready
echo "Waiting for Vault to start..."
until vault status > /dev/null 2>&1 || [ $? -eq 2 ]; do
  sleep 1
done

echo "Vault is up. Configuring..."

export VAULT_ADDR='http://vault:8200'
: "${VAULT_TOKEN:?VAULT_TOKEN must be provided}"

# Enable KV v2 at secret/
vault secrets enable -path=secret -version=2 kv || true

# Put delivery secrets (reading from env injected by docker-compose)
vault kv put secret/delivery/backend \
  PostgresPassword="${POSTGRES_PASSWORD}" \
  Jwt__CurrentKeyId="current" \
  Jwt__Keys__current="${JWT_SECRET}" \
  RabbitMqPassword="${RABBITMQ_PASSWORD}" \
  AiServiceApiKey="${AI_SERVICE_API_KEY}"

vault kv put secret/delivery/ai \
  PostgresPassword="${POSTGRES_PASSWORD}" \
  AiServiceApiKey="${AI_SERVICE_API_KEY}"

# Enable AppRole auth
vault auth enable approle || true

# Create policies
cat <<EOF > /tmp/backend-policy.hcl
path "secret/data/delivery/backend" {
  capabilities = ["read"]
}
EOF
vault policy write backend-policy /tmp/backend-policy.hcl

cat <<EOF > /tmp/ai-policy.hcl
path "secret/data/delivery/ai" {
  capabilities = ["read"]
}
EOF
vault policy write ai-policy /tmp/ai-policy.hcl

# Create AppRoles
vault write auth/approle/role/backend-role policies="backend-policy" token_ttl=1h token_max_ttl=4h
vault write auth/approle/role/ai-role policies="ai-policy" token_ttl=1h token_max_ttl=4h

# Inject RoleID and SecretID into shared volume
mkdir -p /vault/approle
vault read -field=role_id auth/approle/role/backend-role/role-id > /vault/approle/backend_role_id
vault write -f -field=secret_id auth/approle/role/backend-role/secret-id > /vault/approle/backend_secret_id

vault read -field=role_id auth/approle/role/ai-role/role-id > /vault/approle/ai_role_id
vault write -f -field=secret_id auth/approle/role/ai-role/secret-id > /vault/approle/ai_secret_id

echo "Vault Bootstrap Complete! AppRole credentials written to /vault/approle"
