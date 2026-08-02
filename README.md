# OpenBao Developer Secrets Dashboard

Self-hosted internal secrets management for one organization. OpenBao is the only persistent backend; the ASP.NET API and Next.js dashboard do not use an application database.

## Local development

```bash
docker compose -f deploy/docker-compose/docker-compose.yml up
dotnet run --project src/ControlPlane.Api/ControlPlane.Api.csproj --launch-profile local
cd src/Dashboard.Web && npm install && npm run dev
```

Then open `http://localhost:3000`. The `local` launch profile runs the API on `http://localhost:5000` with `ASPNETCORE_ENVIRONMENT=LocalDevelopment`, and the dashboard proxies `/api` to it so the session cookie and antiforgery token stay same-origin. Point the proxy elsewhere with `API_ORIGIN`. This profile is only for disposable local data; normal development and production retain HTTPS-only cookies.

## Dashboard

The dashboard covers every control-plane endpoint: browsing folders and secrets, editing keys with masked values, importing and exporting `.env`/JSON, version history with roll back and undelete, projects, members, roles, machine identities, the audit log, and short-lived database credentials.

The disposable OpenBao bootstrap creates `admin` / `admin-only-change-me`. Change or remove these credentials before using any shared environment. The development listener is HTTP by design; production must use HTTPS and a separately managed control-plane identity.

## CLI

```bash
dotnet run --project src/ControlPlane.Cli -- login --username alice
# Or for automation: printf '%s' "$PASSWORD" | dotnet run --project src/ControlPlane.Cli -- login --username alice --password-stdin
dotnet run --project src/ControlPlane.Cli -- run --project thorneai --env development --path backend -- pnpm dev
dotnet run --project src/ControlPlane.Cli -- export --project thorneai --env development --path backend
dotnet run --project src/ControlPlane.Cli -- import .env --project thorneai --env development --path backend
dotnet run --project src/ControlPlane.Cli -- set --project thorneai --env development --path backend KEY=value
```

`run` keeps values in memory, injects them into the child process, forwards standard streams and termination signals, and never creates a `.env` file. The local token file is restricted to the current user; set `SECRETS_TOKEN_FILE` for an explicit path in managed environments.

CLI login reads the password from a hidden prompt; automation should pipe it with `--password-stdin`. Password command-line arguments are rejected because process listings can expose them.

## Tests and formatting

```bash
dotnet test OpenBaoDashboard.slnx
cd src/Dashboard.Web && npm run format:check && npm run build
```

Integration tests start a disposable OpenBao container and cover Userpass, KV-v2 authorization, CAS, revocation, offboarding, projects, AppRole and CLI process injection. No browser E2E suite is required.

## Security boundaries

- Browser sessions use encrypted, secure, HttpOnly, SameSite cookies. OpenBao tokens are never stored in browser storage.
- Normal secret operations use the current user's OpenBao token. The control token is only for explicitly admin-gated system operations and must be supplied outside source control.
- Roles are generated from structured fields; clients never submit arbitrary HCL.
- Secret values are masked in the dashboard and excluded from audit projections, exceptions and URLs.
- Dynamic database credentials are short-lived OpenBao leases; database permissions remain the database's responsibility.
- Do not put root, unseal or recovery keys into the wrapper. Use a separate break-glass process.

Before production deployment, pin and verify the supported OpenBao version, configure TLS, configure an audit device, provide `OpenBao:ControlToken` through a secret manager, and review dependency advisories.
