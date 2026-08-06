"use client";

import { useQuery } from "@tanstack/react-query";
import { isForbidden, listAdminProjects, type EnvironmentSummary } from "@/lib/client";
import { keys } from "@/lib/queryKeys";

// Shown when the caller cannot list projects, so the real environment list is unknown.
// These are the three the control plane always creates.
const FALLBACK: EnvironmentSummary[] = [
  { id: "development", displayName: "Development", protected: false, position: 0 },
  { id: "staging", displayName: "Staging", protected: false, position: 1 },
  { id: "production", displayName: "Production", protected: true, position: 2 },
];

/**
 * One source for a project's environments. The sidebar, the project page and the
 * secrets header all need this list, and they used to disagree about what to show a
 * member who cannot list projects.
 */
export function useProjectEnvironments(project: string | null) {
  const projects = useQuery({
    queryKey: keys.projects,
    queryFn: listAdminProjects,
    retry: false,
    enabled: project !== null,
  });

  const known = projects.data?.find((candidate) => candidate.id === project);
  const forbidden = projects.isError && isForbidden(projects.error);

  return {
    description: known?.description ?? "",
    environments: known?.environments ?? (forbidden ? FALLBACK : []),
    isLoading: projects.isLoading,
    isError: projects.isError && !forbidden,
    error: projects.error,
  };
}
