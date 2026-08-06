import type { components } from "./generated";

type Schemas = components["schemas"];

export type ProjectResponse = Schemas["ProjectResponse"];
export type SecretDocumentResponse = Schemas["SecretDocumentResponse"];
export type SecretVersionResponse = Schemas["SecretVersionResponse"];
export type SessionResponse = Schemas["SessionResponse"];
export type MemberResponse = Schemas["MemberResponse"];
export type MachineIdentityResponse = Schemas["MachineIdentityResponse"];
export type SecretEntry = Schemas["SecretEntryResponse"];
export type DatabaseCredentialResponse = Schemas["DatabaseCredentialResponse"];
export type ActivityEntry = Schemas["ActivityEntryResponse"];

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

/**
 * The environment is protected, so this write has to go through review. Distinct from
 * a 403: the caller is allowed to make the change, just not in one step.
 */
export function needsApproval(error: unknown): boolean {
  return error instanceof ApiError && error.status === 409;
}

export function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error && error.message ? error.message : fallback;
}

async function csrfToken(): Promise<string> {
  const response = await fetch("/api/auth/csrf", { credentials: "include" });
  if (!response.ok) throw new ApiError(response.status, "Could not start a secure session.");
  const body = (await response.json()) as Schemas["CsrfTokenResponse"];
  return body.token;
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
        : ((await problemDetail(response)) ?? failure ?? "The request failed."),
    );
  }
  return response;
}

/**
 * Endpoints that can explain themselves answer with a problem document. Showing that
 * sentence beats a generic failure line, which is all the caller could otherwise say.
 */
async function problemDetail(response: Response): Promise<string | null> {
  if (!response.headers.get("content-type")?.includes("json")) return null;
  try {
    const body = (await response.clone().json()) as { detail?: string };
    return typeof body.detail === "string" && body.detail.length > 0 ? body.detail : null;
  } catch {
    return null;
  }
}

// The single place an untyped JSON body becomes a typed one. Response shapes come
// from the generated OpenAPI types, so this cast is only as good as that document —
// keeping it here means exactly one place to add runtime validation later.
const read = async <T>(path: string, failure: string): Promise<T> =>
  (await call(path, { failure })).json() as Promise<T>;

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
  return response.json() as Promise<SessionResponse>;
}

export const logout = () => send("/api/auth/logout", "POST", "Sign out failed.");

export async function getSession(): Promise<SessionResponse | null> {
  const response = await fetch("/api/auth/session", { credentials: "include" });
  if (response.status === 401) return null;
  if (!response.ok) throw new ApiError(response.status, "Session lookup failed.");
  return response.json() as Promise<SessionResponse>;
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
  return response.json() as Promise<SecretDocumentResponse>;
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
  return response.json() as Promise<ProjectResponse>;
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
  return response.json() as Promise<MachineIdentityResponse>;
}

export async function generateMachineSecretId(roleName: string): Promise<string> {
  const response = await call(
    `/api/admin/machine-identities/${encodeURIComponent(roleName)}/secret-id`,
    { method: "POST", failure: "A secret ID could not be generated." },
  );
  const body = (await response.json()) as Schemas["SecretIdResponse"];
  return body.secretId;
}

export const revokeMachineSecretIds = (roleName: string) =>
  send(
    `/api/admin/machine-identities/${encodeURIComponent(roleName)}/secret-id/revoke`,
    "POST",
    "The secret IDs could not be revoked.",
  );

/* ---------- activity ---------- */

export const listActivity = (project: string, days = 14): Promise<ActivityEntry[]> =>
  read(
    `/api/projects/${encodeURIComponent(project)}/activity?days=${days}`,
    "The activity feed is unavailable.",
  );

/* ---------- dynamic database credentials ---------- */

export const getDatabaseCredential = (role: string): Promise<DatabaseCredentialResponse> =>
  read(
    `/api/database/credentials/${encodeURIComponent(role)}`,
    "No credential was issued for that role.",
  );

/** Every policy that can be handed to a member or a team. */
export const listAssignablePolicies = async (): Promise<string[]> => {
  const body = await read<Schemas["AssignablePoliciesResponse"]>(
    "/api/admin/assignable-policies",
    "The role list is unavailable.",
  );
  return body.policies;
};

/* ---------- annotations, retention and destructive version actions ---------- */

export type SecretMetadata = Schemas["SecretMetadataResponse"];
export type SecretAnnotations = Schemas["SecretAnnotationsPayload"];
export type SecretRetention = Schemas["SecretRetentionPayload"];

export const readSecretMetadata = (
  project: string,
  environment: string,
  path: string,
): Promise<SecretMetadata> =>
  read(
    `${secretsBase(project, environment)}/metadata/${segments(path)}`,
    "The secret details could not be loaded.",
  );

export const updateSecretMetadata = (
  project: string,
  environment: string,
  path: string,
  body: { annotations?: SecretAnnotations; retention?: SecretRetention },
) =>
  send(
    `${secretsBase(project, environment)}/metadata/${segments(path)}`,
    "PATCH",
    "Those details could not be saved.",
    body,
  );

/** Irreversible: erases the version's data rather than hiding it. */
export const destroyVersions = (
  project: string,
  environment: string,
  path: string,
  versions: number[],
) =>
  send(
    `${secretsBase(project, environment)}/destroy/${segments(path)}`,
    "POST",
    "Those versions could not be destroyed.",
    { versions },
  );

/** Removes the secret and its entire history. Not recoverable. */
export const purgeSecret = (project: string, environment: string, path: string) =>
  send(
    `${secretsBase(project, environment)}/purge/${segments(path)}`,
    "DELETE",
    "The secret could not be removed.",
  );

export const deleteFolder = (
  project: string,
  environment: string,
  path: string,
  purge: boolean,
): Promise<Schemas["FolderOperationResponse"]> =>
  read(
    `${secretsBase(project, environment)}/folders/${segments(path)}?purge=${purge}`,
    "The folder could not be deleted.",
  );

/* ---------- search and compare ---------- */

export type SecretSearchResponse = Schemas["SecretSearchResponse"];
export type CompareResponse = Schemas["CompareResponse"];
export type EnvironmentSnapshot = Schemas["EnvironmentSnapshot"];

export const searchSecrets = (project: string, query: string): Promise<SecretSearchResponse> =>
  read(
    `/api/projects/${encodeURIComponent(project)}/search?q=${encodeURIComponent(query)}`,
    "The search failed.",
  );

export const compareEnvironments = (
  project: string,
  path: string,
  environments?: string[],
): Promise<CompareResponse> => {
  const scope = environments?.length ? `&environments=${environments.join(",")}` : "";
  return read(
    `/api/projects/${encodeURIComponent(project)}/compare?path=${encodeURIComponent(path)}${scope}`,
    "The comparison could not be loaded.",
  );
};

/* ---------- one-time share links ---------- */

export async function createShare(
  values: Record<string, string>,
  ttlSeconds: number,
): Promise<Schemas["CreateShareResponse"]> {
  const response = await call("/api/shares", {
    method: "POST",
    body: { values, ttlSeconds },
    failure: "The share link could not be created.",
  });
  return response.json() as Promise<Schemas["CreateShareResponse"]>;
}

/** Consumes the link. A second call always fails — that is the point. */
export async function openShare(token: string): Promise<Record<string, string> | null> {
  try {
    const response = await call(`/api/shares/${encodeURIComponent(token)}/open`, {
      method: "POST",
      failure: "This link is no longer valid.",
    });
    const body = (await response.json()) as Schemas["ShareResponse"];
    return body.values;
  } catch {
    return null;
  }
}

/* ---------- environments ---------- */

export type EnvironmentSummary = Schemas["EnvironmentResponse"];

export async function addEnvironment(
  project: string,
  body: { id: string; displayName: string; protected: boolean },
): Promise<ProjectResponse> {
  const response = await call(`/api/admin/projects/${encodeURIComponent(project)}/environments`, {
    method: "POST",
    body,
    failure: "The environment could not be created.",
  });
  return response.json() as Promise<ProjectResponse>;
}

export async function updateEnvironment(
  project: string,
  environment: string,
  body: { displayName?: string; protected?: boolean; position?: number },
): Promise<ProjectResponse> {
  const response = await call(
    `/api/admin/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(environment)}`,
    { method: "PATCH", body, failure: "The environment could not be updated." },
  );
  return response.json() as Promise<ProjectResponse>;
}

export async function removeEnvironment(
  project: string,
  environment: string,
  purge: boolean,
): Promise<ProjectResponse> {
  const response = await call(
    `/api/admin/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(environment)}?purge=${purge}`,
    { method: "DELETE", failure: "The environment could not be removed." },
  );
  return response.json() as Promise<ProjectResponse>;
}

/* ---------- teams ---------- */

export type TeamSummary = Schemas["TeamResponse"];

export const listTeams = (): Promise<TeamSummary[]> =>
  read("/api/admin/teams", "The team list is unavailable.");

export async function createTeam(name: string, roles: string[]): Promise<TeamSummary> {
  const response = await call("/api/admin/teams", {
    method: "POST",
    body: { name, roles },
    failure: "The team could not be created.",
  });
  return response.json() as Promise<TeamSummary>;
}

export const setTeamRoles = (name: string, roles: string[]) =>
  send(
    `/api/admin/teams/${encodeURIComponent(name)}/roles`,
    "PUT",
    "The team roles could not be saved.",
    { roles },
  );

export const setTeamMembers = (name: string, memberEntityIds: string[]) =>
  send(
    `/api/admin/teams/${encodeURIComponent(name)}/members`,
    "PUT",
    "The team membership could not be saved.",
    { memberEntityIds },
  );

export const deleteTeam = (name: string) =>
  send(`/api/admin/teams/${encodeURIComponent(name)}`, "DELETE", "The team could not be deleted.");

/* ---------- custom project roles ---------- */

export type AccessRole = Schemas["AccessRoleResponse"];
export type RolePermissions = Schemas["RolePermissionsPayload"];

export const listAccessRoles = (project: string): Promise<AccessRole[]> =>
  read(`/api/admin/projects/${encodeURIComponent(project)}/roles`, "The role list is unavailable.");

export async function saveAccessRole(
  project: string,
  name: string,
  body: { environments: string[]; permissions: RolePermissions; description?: string },
): Promise<AccessRole> {
  const response = await call(
    `/api/admin/projects/${encodeURIComponent(project)}/roles/${encodeURIComponent(name)}`,
    { method: "PUT", body, failure: "The role could not be saved." },
  );
  return response.json() as Promise<AccessRole>;
}

export const deleteAccessRole = (project: string, name: string) =>
  send(
    `/api/admin/projects/${encodeURIComponent(project)}/roles/${encodeURIComponent(name)}`,
    "DELETE",
    "The role could not be deleted.",
  );

/* ---------- change requests ---------- */

export type ChangeRequest = Schemas["ChangeRequestResponse"];
export type ChangeReview = Schemas["ChangeReviewPayload"];

export const listChanges = (project: string): Promise<ChangeRequest[]> =>
  read(`/api/projects/${encodeURIComponent(project)}/changes`, "The change list is unavailable.");

export async function proposeChange(
  project: string,
  body: {
    environment: string;
    path: string;
    values: Record<string, string>;
    reason?: string;
    expectedVersion?: number;
    description?: string;
    delete?: boolean;
  },
): Promise<ChangeRequest> {
  const response = await call(`/api/projects/${encodeURIComponent(project)}/changes`, {
    method: "POST",
    body,
    failure: "The change could not be sent for review.",
  });
  return response.json() as Promise<ChangeRequest>;
}

/**
 * The proposed values, read through the reviewer's own token. A 404 here means the
 * change is closed or was a deletion, not that something went wrong.
 */
export async function readProposedValues(
  project: string,
  id: string,
): Promise<SecretDocumentResponse | null> {
  const url = `/api/projects/${encodeURIComponent(project)}/changes/${encodeURIComponent(id)}/values`;
  const response = await fetch(url, { credentials: "include" });
  if (response.status === 404) return null;
  if (!response.ok) {
    throw new ApiError(
      response.status,
      response.status === 403
        ? "You cannot read the proposed values for this change."
        : "The proposed values could not be loaded.",
    );
  }
  return response.json() as Promise<SecretDocumentResponse>;
}

async function reviewChange(
  project: string,
  id: string,
  action: "approve" | "reject",
  note: string | undefined,
  failure: string,
): Promise<ChangeRequest> {
  const response = await call(
    `/api/projects/${encodeURIComponent(project)}/changes/${encodeURIComponent(id)}/${action}`,
    { method: "POST", body: { note: note ?? null }, failure },
  );
  return response.json() as Promise<ChangeRequest>;
}

export const approveChange = (project: string, id: string, note?: string) =>
  reviewChange(project, id, "approve", note, "The change could not be approved.");

export const rejectChange = (project: string, id: string, note?: string) =>
  reviewChange(project, id, "reject", note, "The change could not be rejected.");

export async function withdrawChange(project: string, id: string): Promise<ChangeRequest> {
  const response = await call(
    `/api/projects/${encodeURIComponent(project)}/changes/${encodeURIComponent(id)}`,
    { method: "DELETE", failure: "The change could not be withdrawn." },
  );
  return response.json() as Promise<ChangeRequest>;
}

/* ---------- per-project members ---------- */

export type ProjectMember = Schemas["ProjectMemberResponse"];
export type ProjectRoleOption = Schemas["ProjectRoleOption"];
export type ProjectMemberOptions = Schemas["ProjectMemberOptions"];

export const listProjectMembers = (project: string): Promise<ProjectMember[]> =>
  read(`/api/projects/${encodeURIComponent(project)}/members`, "The member list is unavailable.");

export const projectMemberOptions = (project: string): Promise<ProjectMemberOptions> =>
  read(
    `/api/projects/${encodeURIComponent(project)}/members/options`,
    "The role options are unavailable.",
  );

/** An empty list removes the member from the project. Other projects are untouched. */
export const setProjectRoles = (project: string, username: string, policies: string[]) =>
  send(
    `/api/projects/${encodeURIComponent(project)}/members/${encodeURIComponent(username)}`,
    "PUT",
    "The member's access could not be changed.",
    { policies },
  );
