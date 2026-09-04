#!/bin/sh

export VAULT_ADDR="${VAULT_ADDR:-http://vault:8200}"
: "${VAULT_TOKEN:?VAULT_TOKEN must be provided}"

APPROLE_DIR=/vault/approle
READY_FILE="${APPROLE_DIR}/ready"
CHECK_INTERVAL_SECONDS="${VAULT_BOOTSTRAP_CHECK_INTERVAL_SECONDS:-10}"

wait_for_vault() {
  echo "Waiting for Vault to start..."
  until vault status >/dev/null 2>&1 || [ "$?" -eq 2 ]; do
    sleep 1
  done
}

credentials_match_current_vault() {
    [ -s "${READY_FILE}" ] &&
    [ -s "${APPROLE_DIR}/backend_role_id" ] &&
    [ -s "${APPROLE_DIR}/route_optimizer_role_id" ] &&
    [ "$(cat "${APPROLE_DIR}/backend_role_id")" = "$(vault read -field=role_id auth/approle/role/backend-role/role-id 2>/dev/null)" ] &&
    [ "$(cat "${APPROLE_DIR}/route_optimizer_role_id")" = "$(vault read -field=role_id auth/approle/role/route-optimizer-role/role-id 2>/dev/null)" ]
}

configure_vault() {
  echo "Vault is up. Configuring delivery secrets and AppRoles..."
  rm -f "${READY_FILE}"

  vault secrets enable -path=secret -version=2 kv >/dev/null 2>&1 || true

  vault kv put secret/delivery/backend \
    PostgresPassword="${POSTGRES_PASSWORD}" \
    Jwt__CurrentKeyId="current" \
    Jwt__Keys__current="${JWT_SECRET}" \
    RabbitMqPassword="${RABBITMQ_PASSWORD}" \
    RouteOptimizerApiKey="${ROUTE_OPTIMIZER_API_KEY:-$AI_SERVICE_API_KEY}" \
    AiServiceApiKey="${AI_SERVICE_API_KEY}" || return 1

  vault kv put secret/delivery/route-optimizer \
    PostgresPassword="${POSTGRES_PASSWORD}" \
    RouteOptimizerApiKey="${ROUTE_OPTIMIZER_API_KEY:-$AI_SERVICE_API_KEY}" \
    AiServiceApiKey="${AI_SERVICE_API_KEY}" || return 1

  vault kv put secret/delivery/ai \
    PostgresPassword="${POSTGRES_PASSWORD}" \
    RouteOptimizerApiKey="${ROUTE_OPTIMIZER_API_KEY:-$AI_SERVICE_API_KEY}" \
    AiServiceApiKey="${AI_SERVICE_API_KEY}" >/dev/null 2>&1 || true

  vault auth enable approle >/dev/null 2>&1 || true

  cat <<EOF > /tmp/backend-policy.hcl
path "secret/data/delivery/backend" {
  capabilities = ["read"]
}
EOF
  vault policy write backend-policy /tmp/backend-policy.hcl || return 1

  cat <<EOF > /tmp/route-optimizer-policy.hcl
path "secret/data/delivery/route-optimizer" {
  capabilities = ["read"]
}
path "secret/data/delivery/ai" {
  capabilities = ["read"]
}
EOF
  vault policy write route-optimizer-policy /tmp/route-optimizer-policy.hcl || return 1

  vault write auth/approle/role/backend-role \
    policies="backend-policy" token_ttl=1h token_max_ttl=4h || return 1
  vault write auth/approle/role/route-optimizer-role \
    policies="route-optimizer-policy" token_ttl=1h token_max_ttl=4h || return 1

  mkdir -p "${APPROLE_DIR}"
  umask 077

  backend_role_tmp="${APPROLE_DIR}/backend_role_id.tmp.$$"
  backend_secret_tmp="${APPROLE_DIR}/backend_secret_id.tmp.$$"
  route_optimizer_role_tmp="${APPROLE_DIR}/route_optimizer_role_id.tmp.$$"
  route_optimizer_secret_tmp="${APPROLE_DIR}/route_optimizer_secret_id.tmp.$$"

  vault read -field=role_id auth/approle/role/backend-role/role-id >"${backend_role_tmp}" || return 1
  vault write -f -field=secret_id auth/approle/role/backend-role/secret-id >"${backend_secret_tmp}" || return 1
  vault read -field=role_id auth/approle/role/route-optimizer-role/role-id >"${route_optimizer_role_tmp}" || return 1
  vault write -f -field=secret_id auth/approle/role/route-optimizer-role/secret-id >"${route_optimizer_secret_tmp}" || return 1

  [ -s "${backend_role_tmp}" ] &&
    [ -s "${backend_secret_tmp}" ] &&
    [ -s "${route_optimizer_role_tmp}" ] &&
    [ -s "${route_optimizer_secret_tmp}" ] || return 1

  mv "${backend_role_tmp}" "${APPROLE_DIR}/backend_role_id"
  mv "${backend_secret_tmp}" "${APPROLE_DIR}/backend_secret_id"
  mv "${route_optimizer_role_tmp}" "${APPROLE_DIR}/route_optimizer_role_id"
  mv "${route_optimizer_secret_tmp}" "${APPROLE_DIR}/route_optimizer_secret_id"
  printf '%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" >"${READY_FILE}"

  echo "Vault bootstrap complete. AppRole credentials are current."
}

while true; do
  wait_for_vault

  if ! credentials_match_current_vault; then
    until configure_vault; do
      echo "Vault bootstrap failed; retrying in 2 seconds."
      rm -f "${READY_FILE}"
      sleep 2
      wait_for_vault
    done
  fi

  sleep "${CHECK_INTERVAL_SECONDS}"
done
