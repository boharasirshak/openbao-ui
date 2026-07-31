#!/bin/sh
set -eu
until curl -fsS http://openbao:8200/v1/sys/health >/dev/null; do sleep 1; done
# Development only: replace this token before any non-local deployment.
curl -fsS -X POST http://openbao:8200/v1/sys/init -d '{"secret_shares":1,"secret_threshold":1}' >/tmp/init.json
ROOT_TOKEN=$(sed -n 's/.*"root_token":"\([^"]*\)".*/\1/p' /tmp/init.json)
UNSEAL_KEY=$(sed -n 's/.*"keys_base64":\["\([^"]*\)".*/\1/p' /tmp/init.json)
curl -fsS -X POST http://openbao:8200/v1/sys/unseal -d "{\"key\":\"$UNSEAL_KEY\"}"
curl -fsS -H "X-Vault-Token: $ROOT_TOKEN" -X POST http://openbao:8200/v1/sys/auth/userpass -d '{"type":"userpass"}'
curl -fsS -H "X-Vault-Token: $ROOT_TOKEN" -X POST http://openbao:8200/v1/auth/userpass/users/dev -d '{"password":"dev-only-change-me","policies":["default"],"token_ttl":"30m"}'
