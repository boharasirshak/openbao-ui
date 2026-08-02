import type { components } from "./generated";

type Schemas = components["schemas"];

export type ProjectResponse = Schemas["ProjectResponse"];
export type SecretDocumentResponse = Schemas["SecretDocumentResponse"];
export type SecretVersionResponse = Schemas["SecretVersionResponse"];
export type SessionResponse = Schemas["SessionResponse"];
export type MemberResponse = Schemas["MemberResponse"];
export type RoleResponse = Schemas["RoleResponse"];
export type MachineIdentityResponse = Schemas["MachineIdentityResponse"];
export type SecretEntry = Schemas["SecretEntry"];
export type DatabaseCredentialResponse = Schemas["DatabaseCredentialResponse"];

/** The audit endpoint declares no response type, so it is absent from generated.ts. */
export type AuditEvent = {
  time: string | null;
  type: string;
  operation: string;
  path: string;
  actor: string;
};

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

export function isForbidden(error: unknown): boolean {
  return error instanceof ApiError && (error.status === 403 || error.status === 401);
}

export function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error && error.message ? error.message : fallback;
}

async function csrfToken(): Promise<string> {
  const response = await fetch("/api/auth/csrf", { credentials: "include" });
  if (!response.ok) throw new ApiError(response.status, "Could not start a secure session.");
  return (await response.json()).token;
}

type Options = { method?: string; body?: unknown; failure?: string };

async function call(path: string, { method = "GET", body, failure }: Options = {}) {
  const headers = new Headers();
  if (method !== "GET") headers.set("X-CSRF-TOKEN", await csrfToken());
  if (body !== undefined) headers.set("content-type", "application/json");

  const response = await fetch(path, {
    method,
    headers,
    credentials: "include",
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (!response.ok) {
    throw new ApiError(
      response.status,
      response.status === 403 || response.status === 401
        ? "You do not have permission for this action."
        : (failure ?? "The request failed."),
    );
  }
  return response;
}

const read = async <T>(path: string, failure: string): Promise<T> =>
  (await call(path, { failure })).json();

const send = async (path: string, method: string, failure: string, body?: unknown) => {
  await call(path, { method, body, failure });
};

function segments(path: string): string {
  return path.split("/").filter(Boolean).map(encodeURIComponent).join("/");
}

function secretsBase(project: string, environment: string): string {
  return `/api/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(environment)}/secrets`;
}

/* ---------- auth ---------- */

export async function login(username: string, password: string): Promise<SessionResponse> {
  const response = await call("/api/auth/login", {
    method: "POST",
    body: { username, password },
    failure: "Sign in failed. Check the username and password.",
  });
  return response.json();
}

export const logout = () => send("/api/auth/logout", "POST", "Sign out failed.");

export async function getSession(): Promise<SessionResponse | null> {
  const response = await fetch("/api/auth/session", { credentials: "include" });
  if (response.status === 401) return null;
  if (!response.ok) throw new ApiError(response.status, "Session lookup failed.");
  return response.json();
}

/* ---------- secrets ---------- */

export function listSecretEntries(
  project: string,
  environment: string,
  folder: string,
): Promise<SecretEntry[]> {
  return read(
    `${secretsBase(project, environment)}/list/${segments(folder)}`,
    "This folder could not be listed.",
  );
}

/** Returns null when no document exists at the path, which is normal for a folder. */
export async function readSecret(
  project: string,
  environment: string,
  path: string,
): Promise<SecretDocumentResponse | null> {
  const url = `${secretsBase(project, environment)}/${segments(path)}`;
  const response = await fetch(url, { credentials: "include" });
  if (response.status === 404) return null;
  if (!response.ok) {
    throw new ApiError(
      response.status,
      response.status === 403 ? "You cannot read this secret." : "The secret could not be loaded.",
    );
  }
  return response.json();
}

export const writeSecret = (
  project: string,
  environment: string,
  path: string,
  values: Record<string, string>,
  expectedVersion: number,
  description?: string,
) =>
  send(
    `${secretsBase(project, environment)}/${segments(path)}`,
    "PUT",
    "The secret could not be saved. Someone else may have changed it, so reload and try again.",
    { values, expectedVersion, description },
  );

export const deleteSecret = (project: string, environment: string, path: string) =>
  send(
    `${secretsBase(project, environment)}/${segments(path)}`,
    "DELETE",
    "The secret could not be deleted.",
  );

export const listVersions = (
  project: string,
  environment: string,
  path: string,
): Promise<SecretVersionResponse[]> =>
  read(
    `${secretsBase(project, environment)}/versions/${segments(path)}`,
    "Version history is unavailable.",
  );

export const restoreSecret = (
  project: string,
  environment: string,
  path: string,
  version: number,
) =>
  send(
    `${secretsBase(project, environment)}/restore/${segments(path)}`,
    "POST",
    "That version could not be restored.",
    { version },
  );

export const undeleteSecret = (
  project: string,
  environment: string,
  path: string,
  version: number,
) =>
  send(
    `${secretsBase(project, environment)}/undelete/${segments(path)}`,
    "POST",
    "That version could not be undeleted.",
    { version },
  );

export const importSecrets = (
  project: string,
  environment: string,
  path: string,
  values: Record<string, string>,
  expectedVersion?: number,
  description?: string,
) =>
  send(
    `${secretsBase(project, environment)}/import/${segments(path)}`,
    "POST",
    "The import failed. Check that every key is a valid name.",
    { values, expectedVersion, description },
  );

export async function exportSecrets(
  project: string,
  environment: string,
  path: string,
  format: "env" | "json",
): Promise<string> {
  const response = await call(
    `${secretsBase(project, environment)}/export/${segments(path)}?format=${format}`,
    { failure: "The export failed." },
  );
  return format === "env" ? response.text() : JSON.stringify(await response.json(), null, 2);
}

/* ---------- projects ---------- */

export const listAdminProjects = (): Promise<ProjectResponse[]> =>
  read("/api/admin/projects", "The project list is unavailable.");

export async function createProject(id: string, description: string): Promise<ProjectResponse> {
  const response = await call(`/api/admin/projects/${encodeURIComponent(id)}`, {
    method: "POST",
    body: { description },
    failure: "The project could not be created. Use lowercase letters, digits and dashes.",
  });
  return response.json();
}

export const deleteProject = (id: string) =>
  send(
    `/api/admin/projects/${encodeURIComponent(id)}`,
    "DELETE",
    "The project could not be deleted.",
  );

/* ---------- members ---------- */

export const listMembers = (): Promise<MemberResponse[]> =>
  read("/api/admin/members", "The member list is unavailable.");

export const createMember = (username: string, password: string, policies: string[]) =>
  send("/api/admin/members", "POST", "The member could not be created.", {
    username,
    password,
    policies,
  });

export const updateMember = (username: string, password: string, policies: string[]) =>
  send(
    `/api/admin/members/${encodeURIComponent(username)}`,
    "PUT",
    "The member could not be updated.",
    { password, policies },
  );

export const assignMemberRoles = (username: string, roles: string[]) =>
  send(
    `/api/admin/members/${encodeURIComponent(username)}/roles`,
    "POST",
    "Those roles could not be assigned. Role names allow letters, digits, dashes and underscores.",
    { roles },
  );

export const disableMember = (username: string) =>
  send(
    `/api/admin/members/${encodeURIComponent(username)}/disable`,
    "POST",
    "The member could not be disabled.",
  );

export const deleteMember = (username: string) =>
  send(
    `/api/admin/members/${encodeURIComponent(username)}`,
    "DELETE",
    "The member could not be deleted.",
  );

/* ---------- roles ---------- */

export const listRoles = (): Promise<RoleResponse[]> =>
  read("/api/admin/roles", "The role list is unavailable.");

export const createRole = (name: string, project: string, environment: string, readOnly: boolean) =>
  send("/api/admin/roles", "POST", "The role could not be created.", {
    name,
    project,
    environment,
    readOnly,
  });

export const deleteRole = (name: string) =>
  send(`/api/admin/roles/${encodeURIComponent(name)}`, "DELETE", "The role could not be deleted.");

/* ---------- machine identities ---------- */

export const listMachineIdentities = (): Promise<MachineIdentityResponse[]> =>
  read("/api/admin/machine-identities", "The machine identity list is unavailable.");

export async function createMachineIdentity(input: {
  name: string;
  project: string;
  environment: string;
  readOnly: boolean;
  tokenTtlSeconds: number;
  tokenUses: number;
}): Promise<MachineIdentityResponse> {
  const response = await call("/api/admin/machine-identities", {
    method: "POST",
    body: input,
    failure: "The machine identity could not be created.",
  });
  return response.json();
}

export async function generateMachineSecretId(roleName: string): Promise<string> {
  const response = await call(
    `/api/admin/machine-identities/${encodeURIComponent(roleName)}/secret-id`,
    { method: "POST", failure: "A secret ID could not be generated." },
  );
  return (await response.json()).secretId;
}

export const revokeMachineSecretIds = (roleName: string) =>
  send(
    `/api/admin/machine-identities/${encodeURIComponent(roleName)}/secret-id/revoke`,
    "POST",
    "The secret IDs could not be revoked.",
  );

/* ---------- audit ---------- */

export const listAuditEvents = (limit = 100): Promise<AuditEvent[]> =>
  read(`/api/admin/audit/recent?limit=${limit}`, "The audit log is unavailable.");

/* ---------- dynamic database credentials ---------- */

export const getDatabaseCredential = (role: string): Promise<DatabaseCredentialResponse> =>
  read(
    `/api/database/credentials/${encodeURIComponent(role)}`,
    "No credential was issued for that role.",
  );
