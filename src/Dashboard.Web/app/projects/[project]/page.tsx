"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { Alert, Box, Chip, Paper, Stack, Typography } from "@mui/material";
import ArrowIcon from "@mui/icons-material/ChevronRightOutlined";
import { LoadingRow, PageHeader } from "@/components/AppShell";
import EnvironmentChip from "@/components/EnvironmentChip";
import { errorMessage } from "@/lib/client";
import { useProjectEnvironments } from "@/lib/useProjectEnvironments";

export default function ProjectPage() {
  const project = String(useParams<{ project: string }>().project);
  const { description, environments, isLoading, isError, error } = useProjectEnvironments(project);

  return (
    <>
      {/* The project's tools used to be six identical links in this header. They are in
          the sidebar now, so they are reachable from inside a secret too. */}
      <PageHeader
        title={project}
        description={description || "Pick an environment to see its secrets."}
      />

      {isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errorMessage(error, "The project could not be loaded.")}
        </Alert>
      )}

      {isLoading ? (
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
                    ? "Someone else has to approve changes here."
                    : "Anyone with access can edit these."}
                </Typography>
              </Box>
              {environment.protected && (
                <Chip size="small" variant="outlined" color="warning" label="needs approval" />
              )}
              <ArrowIcon fontSize="small" sx={{ color: "text.secondary" }} />
            </Paper>
          ))}
        </Stack>
      )}
    </>
  );
}
