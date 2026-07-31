import type {
  ProjectResponse,
  SecretDocumentResponse,
  SecretVersionResponse,
  SessionResponse,
} from "./generated";

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
): Promise<void> {
  const response = await unsafeRequest(secretUrl(project, environment, path), {
    method: "PUT",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ values, expectedVersion }),
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

export async function importSecrets(
  project: string,
  environment: string,
  path: string,
  values: Record<string, string>,
  expectedVersion?: number,
): Promise<void> {
  const response = await unsafeRequest(
    `/api/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(environment)}/secrets/import/${path.split("/").map(encodeURIComponent).join("/")}`,
    {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ values, expectedVersion }),
    },
  );
  if (!response.ok) throw new Error("Secret import failed.");
}

export async function listAdminProjects(): Promise<ProjectResponse[]> {
  const response = await fetch("/api/admin/projects", { credentials: "include" });
  if (!response.ok) throw new Error("Project list unavailable.");
  return response.json();
}
