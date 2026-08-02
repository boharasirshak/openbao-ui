#!/bin/sh
set -eu
until curl -fsS 'http://openbao:8200/v1/sys/health?uninitcode=200&sealedcode=200' >/dev/null; do sleep 1; done

ROOT_TOKEN_FILE=/bootstrap-state/root_token
INIT_RESPONSE=$(curl -fsS http://openbao:8200/v1/sys/init)
if echo "$INIT_RESPONSE" | grep -q '"initialized":false'; then
  # Development only: replace this bootstrap process before any non-local deployment.
  INIT_RESPONSE=$(curl -fsS -X POST http://openbao:8200/v1/sys/init -d '{"secret_shares":1,"secret_threshold":1}')
  ROOT_TOKEN=$(echo "$INIT_RESPONSE" | sed -n 's/.*"root_token":"\([^"]*\)".*/\1/p')
  UNSEAL_KEY=$(echo "$INIT_RESPONSE" | sed -n 's/.*"keys_base64":\["\([^"]*\)".*/\1/p')
  printf '%s' "$ROOT_TOKEN" > "$ROOT_TOKEN_FILE"
  chmod 600 "$ROOT_TOKEN_FILE"
  curl -fsS -X POST http://openbao:8200/v1/sys/unseal -d "{\"key\":\"$UNSEAL_KEY\"}" >/dev/null
else
  if [ ! -s "$ROOT_TOKEN_FILE" ]; then
    echo "OpenBao is initialized but the disposable bootstrap token is missing; remove the local volumes and retry." >&2
    exit 1
  fi
  ROOT_TOKEN=$(cat "$ROOT_TOKEN_FILE")
fi

AUTH_MOUNTS=$(curl -fsS -H "X-Vault-Token: $ROOT_TOKEN" http://openbao:8200/v1/sys/auth)
if ! echo "$AUTH_MOUNTS" | grep -q '"userpass/"'; then
  echo "Enabling Userpass auth"
  curl -fsS -H "X-Vault-Token: $ROOT_TOKEN" -X POST http://openbao:8200/v1/sys/auth/userpass -d '{"type":"userpass"}' >/dev/null
fi
# A glob such as sys/mounts/* does not match the bare sys/mounts path, so the
# list endpoints need their own entries or every administrative page returns 403.
echo "Writing wrapper-admin policy"
curl -fsS -H "X-Vault-Token: $ROOT_TOKEN" -X PUT http://openbao:8200/v1/sys/policies/acl/wrapper-admin \
  -d '{"policy":"path \"sys/mounts\" { capabilities = [\"read\", \"list\"] }\npath \"sys/mounts/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"list\"] }\npath \"sys/auth\" { capabilities = [\"read\", \"list\"] }\npath \"sys/auth/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"sudo\"] }\npath \"sys/policies/acl/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"list\"] }\npath \"identity/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"list\"] }\npath \"wrapper-metadata/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"list\"] }\npath \"auth/userpass/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"list\"] }\npath \"auth/approle/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"list\"] }\npath \"auth/token/accessors\" { capabilities = [\"list\"] }\npath \"auth/token/lookup-accessor\" { capabilities = [\"update\"] }\npath \"auth/token/revoke-accessor\" { capabilities = [\"update\"] }"}' >/dev/null
echo "Creating local admin user"
curl -fsS -H "X-Vault-Token: $ROOT_TOKEN" -X POST http://openbao:8200/v1/auth/userpass/users/admin \
  -d '{"password":"admin-only-change-me","policies":["wrapper-admin"],"token_ttl":"30m"}' >/dev/null
