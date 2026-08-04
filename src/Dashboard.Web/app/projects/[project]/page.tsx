"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { Alert, Box, Button, Chip, Paper, Stack, Typography } from "@mui/material";
import ArrowIcon from "@mui/icons-material/ChevronRightOutlined";
import SearchIcon from "@mui/icons-material/SearchOutlined";
import CompareIcon from "@mui/icons-material/DifferenceOutlined";
import ApprovalIcon from "@mui/icons-material/RuleOutlined";
import HistoryIcon from "@mui/icons-material/HistoryOutlined";
import SettingsIcon from "@mui/icons-material/SettingsOutlined";
import ShieldIcon from "@mui/icons-material/VerifiedUserOutlined";
import { LoadingRow, PageHeader } from "@/components/AppShell";
import EnvironmentChip from "@/components/EnvironmentChip";
import {
  errorMessage,
  isForbidden,
  listAdminProjects,
  type EnvironmentSummary,
} from "@/lib/client";
import { keys } from "@/lib/queryKeys";

// Shown when the caller cannot list projects, so the environment list is unknown.
const FALLBACK_ENVIRONMENTS: EnvironmentSummary[] = [
  { id: "development", displayName: "Development", protected: false, position: 0 },
  { id: "staging", displayName: "Staging", protected: false, position: 1 },
  { id: "production", displayName: "Production", protected: true, position: 2 },
];

export default function ProjectPage() {
  const project = String(useParams<{ project: string }>().project);
  const projects = useQuery({ queryKey: keys.projects, queryFn: listAdminProjects, retry: false });

  const known = projects.data?.find((candidate) => candidate.id === project);
  const forbidden = projects.isError && isForbidden(projects.error);
  // Without the administrator project list, fall back to the environments the
  // control plane always creates.
  const environments = known?.environments ?? (forbidden ? FALLBACK_ENVIRONMENTS : []);

  return (
    <>
      <PageHeader
        title={project}
        description={known?.description || "Choose an environment to browse its secrets."}
        actions={
          <>
            <Button
              component={Link}
              href={`/projects/${encodeURIComponent(project)}/search`}
              startIcon={<SearchIcon />}
            >
              Search
            </Button>
            <Button
              component={Link}
              href={`/projects/${encodeURIComponent(project)}/compare`}
              startIcon={<CompareIcon />}
            >
              Compare
            </Button>
            <Button
              component={Link}
              href={`/projects/${encodeURIComponent(project)}/changes`}
              startIcon={<ApprovalIcon />}
            >
              Changes
            </Button>
            <Button
              component={Link}
              href={`/projects/${encodeURIComponent(project)}/activity`}
              startIcon={<HistoryIcon />}
            >
              Activity
            </Button>
            <Button
              component={Link}
              href={`/projects/${encodeURIComponent(project)}/roles`}
              startIcon={<ShieldIcon />}
            >
              Roles
            </Button>
            <Button
              component={Link}
              href={`/projects/${encodeURIComponent(project)}/settings`}
              startIcon={<SettingsIcon />}
            >
              Settings
            </Button>
          </>
        }
      />

      {projects.isError && !forbidden && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errorMessage(projects.error, "The project could not be loaded.")}
        </Alert>
      )}

      {projects.isLoading ? (
        <LoadingRow label="Loading environments…" />
      ) : (
        <Stack spacing={1.5} sx={{ maxWidth: 640 }}>
          {environments.map((environment) => (
            <Paper
              key={environment.id}
              component={Link}
              href={`/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(environment.id)}/secrets`}
              sx={{
                p: 2.5,
                display: "flex",
                alignItems: "center",
                gap: 2,
                "&:hover": { borderColor: "primary.main" },
              }}
            >
              <EnvironmentChip environment={environment.id} />
              <Box sx={{ flex: 1 }}>
                <Typography variant="body2">{environment.displayName}</Typography>
                <Typography variant="caption" color="text.secondary">
                  {environment.protected
                    ? "Protected. Changes here need approval."
                    : `Secrets for ${environment.displayName.toLowerCase()}.`}
                </Typography>
              </Box>
              {environment.protected && (
                <Chip size="small" variant="outlined" color="warning" label="protected" />
              )}
              <ArrowIcon fontSize="small" sx={{ color: "text.secondary" }} />
            </Paper>
          ))}
        </Stack>
      )}
    </>
  );
}
