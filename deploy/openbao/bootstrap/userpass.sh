#!/bin/sh
set -eu
until curl -fsS 'http://openbao:8200/v1/sys/health?uninitcode=200&sealedcode=200' >/dev/null; do sleep 1; done

INIT_RESPONSE=$(curl -fsS http://openbao:8200/v1/sys/init)
if echo "$INIT_RESPONSE" | grep -q '"initialized":false'; then
  # Development only: replace this bootstrap process before any non-local deployment.
  INIT_RESPONSE=$(curl -fsS -X POST http://openbao:8200/v1/sys/init -d '{"secret_shares":1,"secret_threshold":1}')
  ROOT_TOKEN=$(echo "$INIT_RESPONSE" | sed -n 's/.*"root_token":"\([^"]*\)".*/\1/p')
  UNSEAL_KEY=$(echo "$INIT_RESPONSE" | sed -n 's/.*"keys_base64":\["\([^"]*\)".*/\1/p')
  curl -fsS -X POST http://openbao:8200/v1/sys/unseal -d "{\"key\":\"$UNSEAL_KEY\"}" >/dev/null
else
  echo "An initialized OpenBao requires an externally supplied bootstrap token." >&2
  exit 1
fi

curl -fsS -H "X-Vault-Token: $ROOT_TOKEN" -X POST http://openbao:8200/v1/sys/auth/userpass -d '{"type":"userpass"}' >/dev/null
curl -fsS -H "X-Vault-Token: $ROOT_TOKEN" -X PUT http://openbao:8200/v1/sys/policies/acl/wrapper-admin \
  -d '{"policy":"path \"sys/mounts/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"list\"] }\npath \"sys/policies/acl/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"list\"] }\npath \"auth/userpass/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"list\"] }\npath \"auth/approle/*\" { capabilities = [\"create\", \"read\", \"update\", \"delete\", \"list\"] }"}' >/dev/null
curl -fsS -H "X-Vault-Token: $ROOT_TOKEN" -X POST http://openbao:8200/v1/auth/userpass/users/admin \
  -d '{"password":"admin-only-change-me","policies":["wrapper-admin"],"token_ttl":"30m"}' >/dev/null
