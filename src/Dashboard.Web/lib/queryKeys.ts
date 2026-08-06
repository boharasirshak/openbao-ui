/**
 * Hierarchical cache keys. React Query matches a key as a prefix, so invalidating
 * `env(project, environment)` covers that environment's listing, documents and version
 * history in one call — and, unlike a flat `["entries"]`, leaves every other project
 * and environment alone.
 */
export const keys = {
  session: ["session"] as const,

  projects: ["projects"] as const,
  project: (project: string) => ["projects", project] as const,
  env: (project: string, environment: string) => ["projects", project, "env", environment] as const,
  entries: (project: string, environment: string, folder: string) =>
    [...keys.env(project, environment), "entries", folder] as const,
  secret: (project: string, environment: string, path: string) =>
    [...keys.env(project, environment), "secret", path] as const,
  versions: (project: string, environment: string, path: string) =>
    [...keys.env(project, environment), "versions", path] as const,

  members: ["members"] as const,
  roles: ["roles"] as const,
  machineIdentities: ["machine-identities"] as const,
  teams: ["teams"] as const,
  assignablePolicies: ["assignable-policies"] as const,
  accessRoles: (project: string) => ["projects", project, "access-roles"] as const,
  changes: (project: string) => ["projects", project, "changes"] as const,
  projectMembers: (project: string) => ["projects", project, "members"] as const,
};
