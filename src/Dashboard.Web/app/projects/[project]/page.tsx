"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { Alert, Box, Paper, Stack, Typography } from "@mui/material";
import ArrowIcon from "@mui/icons-material/ChevronRightOutlined";
import { LoadingRow, PageHeader } from "@/components/AppShell";
import EnvironmentChip from "@/components/EnvironmentChip";
import { errorMessage, isForbidden, listAdminProjects } from "@/lib/client";

const FALLBACK_ENVIRONMENTS = ["development", "staging", "production"];

export default function ProjectPage() {
  const project = String(useParams<{ project: string }>().project);
  const projects = useQuery({ queryKey: ["projects"], queryFn: listAdminProjects, retry: false });

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
              key={environment}
              component={Link}
              href={`/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(environment)}/secrets`}
              sx={{
                p: 2.5,
                display: "flex",
                alignItems: "center",
                gap: 2,
                "&:hover": { borderColor: "primary.main" },
              }}
            >
              <EnvironmentChip environment={environment} />
              <Box sx={{ flex: 1 }}>
                <Typography variant="body2" color="text.secondary">
                  {environment === "production"
                    ? "Live values. Changes take effect immediately."
                    : `Secrets for the ${environment} environment.`}
                </Typography>
              </Box>
              <ArrowIcon fontSize="small" sx={{ color: "text.secondary" }} />
            </Paper>
          ))}
        </Stack>
      )}
    </>
  );
}
