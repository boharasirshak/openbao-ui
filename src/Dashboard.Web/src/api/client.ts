import type { components } from "./generated";

type ProjectResponse = components["schemas"]["ProjectResponse"];
type SecretDocumentResponse = components["schemas"]["SecretDocumentResponse"];
type SecretVersionResponse = components["schemas"]["SecretVersionResponse"];
type SessionResponse = components["schemas"]["SessionResponse"];
type MemberResponse = components["schemas"]["MemberResponse"];
type RoleResponse = components["schemas"]["RoleResponse"];
type MachineIdentityResponse = components["schemas"]["MachineIdentityResponse"];
type SecretEntry = components["schemas"]["SecretEntry"];

async function csrfToken(): Promise<string> {
  const response = await fetch("/api/auth/csrf", { credentials: "include" });
  if (!response.ok) throw new Error("Could not start a secure session.");
  return (await response.json()).token;
}

async function unsafeRequest(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set("X-CSRF-TOKEN", await csrfToken());
  return fetch(path, { ...init, headers, credentials: "include" });
}

export async function login(username: string, password: string): Promise<SessionResponse> {
  const response = await unsafeRequest("/api/auth/login", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ username, password }),
  });
  if (!response.ok) throw new Error("Sign in failed.");
  return response.json();
}

export async function logout(): Promise<void> {
  const response = await unsafeRequest("/api/auth/logout", { method: "POST" });
  if (!response.ok) throw new Error("Sign out failed.");
}

export async function getSession(): Promise<SessionResponse | null> {
  const response = await fetch("/api/auth/session", { credentials: "include" });
  if (response.status === 401) return null;
  if (!response.ok) throw new Error("Session lookup failed.");
  return response.json();
}

function secretUrl(project: string, environment: string, path: string): string {
  return `/api/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(environment)}/secrets/${path
    .split("/")
    .map(encodeURIComponent)
    .join("/")}`;
}

export async function readSecret(
  project: string,
  environment: string,
  path: string,
): Promise<SecretDocumentResponse> {
  const response = await fetch(secretUrl(project, environment, path), { credentials: "include" });
  if (!response.ok)
    throw new Error(response.status === 403 ? "Access denied." : "Secret not found.");
  return response.json();
}

export async function writeSecret(
  project: string,
  environment: string,
  path: string,
  values: Record<string, string>,
  expectedVersion: number,
  description?: string,
): Promise<void> {
  const response = await unsafeRequest(secretUrl(project, environment, path), {
    method: "PUT",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ values, expectedVersion, description }),
  });
  if (!response.ok)
    throw new Error(response.status === 403 ? "Access denied." : "Secret update failed.");
}

export async function deleteSecret(
  project: string,
  environment: string,
  path: string,
): Promise<void> {
  const response = await unsafeRequest(secretUrl(project, environment, path), { method: "DELETE" });
  if (!response.ok) throw new Error("Secret delete failed.");
}

export async function listVersions(
  project: string,
  environment: string,
  path: string,
): Promise<SecretVersionResponse[]> {
  const response = await fetch(
    `/api/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(environment)}/secrets/versions/${path.split("/").map(encodeURIComponent).join("/")}`,
    {
      credentials: "include",
    },
  );
  if (!response.ok) throw new Error("Version history is unavailable.");
  return response.json();
}

export async function listSecretEntries(
  project: string,
  environment: string,
  folder: string,
): Promise<SecretEntry[]> {
  const suffix = folder.split("/").filter(Boolean).map(encodeURIComponent).join("/");
  const response = await fetch(
    `/api/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(environment)}/secrets/list/${suffix}`,
    { credentials: "include" },
  );
  if (!response.ok) throw new Error("Folder listing unavailable.");
  return response.json();
}

export async function restoreSecret(
  project: string,
  environment: string,
  path: string,
  version: number,
): Promise<void> {
  const response = await unsafeRequest(
    `/api/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(environment)}/secrets/restore/${path.split("/").map(encodeURIComponent).join("/")}`,
    {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ version }),
    },
  );
  if (!response.ok) throw new Error("Secret restore failed.");
}

export async function undeleteSecret(
  project: string,
  environment: string,
  path: string,
  version: number,
): Promise<void> {
  const response = await unsafeRequest(
    `/api/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(environment)}/secrets/undelete/${path.split("/").map(encodeURIComponent).join("/")}`,
    {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ version }),
    },
  );
  if (!response.ok) throw new Error("Secret undelete failed.");
}

export async function importSecrets(
  project: string,
  environment: string,
  path: string,
  values: Record<string, string>,
  expectedVersion?: number,
  description?: string,
): Promise<void> {
  const response = await unsafeRequest(
    `/api/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(environment)}/secrets/import/${path.split("/").map(encodeURIComponent).join("/")}`,
    {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ values, expectedVersion, description }),
    },
  );
  if (!response.ok) throw new Error("Secret import failed.");
}

export async function listAdminProjects(): Promise<ProjectResponse[]> {
  const response = await fetch("/api/admin/projects", { credentials: "include" });
  if (!response.ok) throw new Error("Project list unavailable.");
  return response.json();
}

export async function createProject(id: string, description: string): Promise<ProjectResponse> {
  const response = await unsafeRequest(`/api/admin/projects/${encodeURIComponent(id)}`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ description }),
  });
  if (!response.ok) throw new Error("Project creation failed.");
  return response.json();
}

export async function deleteProject(id: string): Promise<void> {
  const response = await unsafeRequest(`/api/admin/projects/${encodeURIComponent(id)}`, {
    method: "DELETE",
  });
  if (!response.ok) throw new Error("Project deletion failed.");
}

export async function listMembers(): Promise<MemberResponse[]> {
  const response = await fetch("/api/admin/members", { credentials: "include" });
  if (!response.ok) throw new Error("Member list unavailable.");
  return response.json();
}

export async function createMember(
  username: string,
  password: string,
  policies: string[],
): Promise<void> {
  const response = await unsafeRequest("/api/admin/members", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ username, password, policies }),
  });
  if (!response.ok) throw new Error("Member creation failed.");
}

export async function disableMember(username: string): Promise<void> {
  const response = await unsafeRequest(
    `/api/admin/members/${encodeURIComponent(username)}/disable`,
    {
      method: "POST",
    },
  );
  if (!response.ok) throw new Error("Member disable failed.");
}

export async function listRoles(): Promise<RoleResponse[]> {
  const response = await fetch("/api/admin/roles", { credentials: "include" });
  if (!response.ok) throw new Error("Role list unavailable.");
  return response.json();
}

export async function listMachineIdentities(): Promise<MachineIdentityResponse[]> {
  const response = await fetch("/api/admin/machine-identities", { credentials: "include" });
  if (!response.ok) throw new Error("Machine identity list unavailable.");
  return response.json();
}

export async function listAuditEvents(): Promise<unknown[]> {
  const response = await fetch("/api/admin/audit/recent", { credentials: "include" });
  if (!response.ok) throw new Error("Audit list unavailable.");
  return response.json();
}
