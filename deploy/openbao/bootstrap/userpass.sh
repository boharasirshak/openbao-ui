#!/bin/sh
set -eu
until curl -fsS 'http://openbao:8200/v1/sys/health?uninitcode=200&sealedcode=200' >/dev/null; do sleep 1; done

BAO=http://openbao:8200
ROOT_TOKEN_FILE=/bootstrap-state/root_token
UNSEAL_KEY_FILE=/bootstrap-state/unseal_key

sealed() {
  curl -fsS "$BAO/v1/sys/seal-status" | grep -q '"sealed":true'
}

# Development only, and the reason this file exists at all: a real deployment must
# never keep the unseal key beside the data it unseals. Here it is the difference
# between `docker compose restart` working and the local stack being unrecoverable —
# the key is issued once at init, so if it is not saved, a restart bricks the volume.
INIT_RESPONSE=$(curl -fsS "$BAO/v1/sys/init")
if echo "$INIT_RESPONSE" | grep -q '"initialized":false'; then
  echo "Initialising OpenBao"
  INIT_RESPONSE=$(curl -fsS -X POST "$BAO/v1/sys/init" -d '{"secret_shares":1,"secret_threshold":1}')
  ROOT_TOKEN=$(echo "$INIT_RESPONSE" | sed -n 's/.*"root_token":"\([^"]*\)".*/\1/p')
  UNSEAL_KEY=$(echo "$INIT_RESPONSE" | sed -n 's/.*"keys_base64":\["\([^"]*\)".*/\1/p')
  printf '%s' "$ROOT_TOKEN" > "$ROOT_TOKEN_FILE"
  printf '%s' "$UNSEAL_KEY" > "$UNSEAL_KEY_FILE"
  chmod 600 "$ROOT_TOKEN_FILE" "$UNSEAL_KEY_FILE"
else
  if [ ! -s "$ROOT_TOKEN_FILE" ]; then
    echo "OpenBao is initialised but the disposable bootstrap token is missing." >&2
    echo "Reset the local stack: docker compose -f deploy/docker-compose/docker-compose.yml down -v" >&2
    exit 1
  fi
  ROOT_TOKEN=$(cat "$ROOT_TOKEN_FILE")
fi

# Runs on every start, not only the first: raft storage comes back sealed after a
# restart, and nothing below works against a sealed instance.
if sealed; then
  if [ ! -s "$UNSEAL_KEY_FILE" ]; then
    echo "OpenBao is sealed and no unseal key was saved, so this volume cannot be opened." >&2
    echo "Reset the local stack: docker compose -f deploy/docker-compose/docker-compose.yml down -v" >&2
    exit 1
  fi

  echo "Unsealing OpenBao"
  curl -fsS -X POST "$BAO/v1/sys/unseal" -d "{\"key\":\"$(cat "$UNSEAL_KEY_FILE")\"}" >/dev/null
fi

# Unsealed is not the same as ready: for a moment after unsealing the node is still
# becoming active and answers 500. Waiting on plain /sys/health covers both.
until curl -fsS "$BAO/v1/sys/health" >/dev/null 2>&1; do sleep 1; done

AUTH_MOUNTS=$(curl -fsS -H "X-Vault-Token: $ROOT_TOKEN" "$BAO/v1/sys/auth")
if ! echo "$AUTH_MOUNTS" | grep -q '"userpass/"'; then
  echo "Enabling Userpass auth"
  curl -fsS -H "X-Vault-Token: $ROOT_TOKEN" -X POST "$BAO/v1/sys/auth/userpass" -d '{"type":"userpass"}' >/dev/null
fi

# A glob such as sys/mounts/* does not match the bare sys/mounts path, so the
# list endpoints need their own entries or every administrative page returns 403.
# The activity prefix deliberately grants create without update: KV-v2 then accepts
# the first write to a path and refuses every later one, so entries cannot be edited.
echo "Writing wrapper-admin policy"
curl -fsS -H "X-Vault-Token: $ROOT_TOKEN" -X PUT "$BAO/v1/sys/policies/acl/wrapper-admin" \
  -d '{"policy":"path \"sys/mounts\" { capabilities = [\"read\", \"list\"] }\npath \"sys/mounts/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"list\"] }\npath \"sys/auth\" { capabilities = [\"read\", \"list\"] }\npath \"sys/auth/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"sudo\"] }\npath \"sys/policies/acl\" { capabilities = [\"list\"] }\npath \"sys/policies/acl/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"list\"] }\npath \"sys/capabilities-self\" { capabilities = [\"update\"] }\npath \"sys/wrapping/wrap\" { capabilities = [\"update\"] }\npath \"identity/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"list\"] }\npath \"wrapper-metadata/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"list\", \"scan\"] }\npath \"wrapper-metadata/data/activity/*\" { capabilities = [\"create\", \"read\", \"list\"] }\npath \"auth/userpass/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"list\"] }\npath \"auth/approle/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"list\"] }\npath \"auth/token/accessors\" { capabilities = [\"list\"] }\npath \"auth/token/lookup-accessor\" { capabilities = [\"update\"] }\npath \"auth/token/revoke-accessor\" { capabilities = [\"update\"] }"}' >/dev/null

# Only create the admin if it is missing. Writing it unconditionally replaced the
# policy list on every restart, silently undoing any role assigned since.
if curl -fsS -H "X-Vault-Token: $ROOT_TOKEN" "$BAO/v1/auth/userpass/users/admin" >/dev/null 2>&1; then
  echo "Local admin user already exists, leaving its roles alone"
else
  echo "Creating local admin user"
  curl -fsS -H "X-Vault-Token: $ROOT_TOKEN" -X POST "$BAO/v1/auth/userpass/users/admin" \
    -d '{"password":"admin-only-change-me","policies":["wrapper-admin"],"token_ttl":"8h"}' >/dev/null
fi

echo "Bootstrap complete"
