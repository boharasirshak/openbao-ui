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

Light and dark themes, following the operating system by default.

**Secrets** — browse folders, edit keys with masked values, import and export `.env` or
JSON, and see version history with roll back, undelete and destroy. Tags, a comment and
retention (how many versions to keep, when to expire them) are stored as OpenBao custom
metadata. Deleting hides a version; destroying erases it, and the two are worded so they
cannot be confused.

**Compare** — one secret path side by side across every environment, showing which
environments agree, which differ and which have no value at all. An environment you
cannot read is shown as locked and excluded from the comparison rather than reported as
a difference.

**Search** — find a secret by path across a whole project. Paths you could not have
listed yourself are never returned.

**Share once** — hand a value to someone without an account. The payload sits in
OpenBao behind a single-use wrapping token, so nothing is stored by this application and
the link dies the first time it is opened.

**Activity** — who changed what, per project. Records key names, never values, and is
append-only: the policy grants `create` without `update`, so an entry cannot be edited
after the fact.

**Access** — projects, members, roles, machine identities, and short-lived database
credentials.

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
cd src/Dashboard.Web && npm run lint && npm run typecheck && npm run test && npm run build && npm run format:check
./scripts/check-terms.sh
```

Integration tests share one disposable OpenBao container across the suite and cover
Userpass, KV-v2 authorization, CAS, revocation, offboarding, projects, AppRole and CLI
process injection. Anything a test mutates or deletes must use a unique name from the
fixture helpers, or it will break its siblings.

`check-terms.sh` fails the build if a banned product name appears in a tracked file or
in a commit message on the branch. The list lives in `.banned-terms`.

## Security boundaries

- Browser sessions use encrypted, secure, HttpOnly, SameSite cookies. OpenBao tokens are never stored in browser storage.
- Normal secret operations use the current user's OpenBao token. The control token is only for explicitly admin-gated system operations and must be supplied outside source control.
- Roles are generated from structured fields; clients never submit arbitrary HCL.
- Secret values are masked in the dashboard and excluded from the activity feed, exceptions and URLs.
- Annotations are merge-patched, so saving one never silently erases another. This needs `patch` on the metadata path; the generated project policies grant it.
- Share links are OpenBao wrapping tokens: single use, self-expiring, and never persisted by this application.
- Dynamic database credentials are short-lived OpenBao leases; database permissions remain the database's responsibility.
- Do not put root, unseal or recovery keys into the wrapper. Use a separate break-glass process.

Before production deployment, pin and verify the supported OpenBao version, configure
TLS, provide `OpenBao:ControlToken` through a secret manager, and review dependency
advisories.

Declare a compliance audit device in `deploy/openbao/config/openbao.hcl` rather than
pointing one at this API. Audit devices are synchronous and do not retry, so a slow web
application would take the control plane down with it. The in-app activity feed is a
product feature and is not a substitute for that device.
