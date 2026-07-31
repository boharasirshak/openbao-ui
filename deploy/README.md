# Local OpenBao

Run `docker compose -f deploy/docker-compose/docker-compose.yml up`. This disposable development setup creates user `dev` with password `dev-only-change-me`; it exposes HTTP only, so set the API's `OpenBao:Address` to `http://localhost:8200` for local development. Never use this bootstrap process outside local development.
