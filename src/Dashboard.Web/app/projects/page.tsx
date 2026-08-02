"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Box,
  Button,
  IconButton,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/AddOutlined";
import DeleteIcon from "@mui/icons-material/DeleteOutlineOutlined";
import ArrowIcon from "@mui/icons-material/ArrowForwardOutlined";
import { EmptyState, LoadingRow, PageHeader, isAdmin, useSession } from "@/components/AppShell";
import EnvironmentChip from "@/components/EnvironmentChip";
import FormDialog from "@/components/FormDialog";
import {
  createProject,
  deleteProject,
  errorMessage,
  isForbidden,
  listAdminProjects,
} from "@/lib/client";

const secretsHref = (project: string, environment: string) =>
  `/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(environment)}/secrets`;

export default function ProjectsPage() {
  const session = useSession();
  const admin = isAdmin(session);
  const queryClient = useQueryClient();
  const [creating, setCreating] = useState(false);
  const [id, setId] = useState("");
  const [description, setDescription] = useState("");
  const [error, setError] = useState("");

  const projects = useQuery({ queryKey: ["projects"], queryFn: listAdminProjects, retry: false });

  const removeProject = useMutation({
    mutationFn: deleteProject,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["projects"] }),
    onError: (deleteError) =>
      setError(errorMessage(deleteError, "The project could not be deleted.")),
  });

  // Listing projects is an administrator endpoint, so everyone else navigates by path.
  if (projects.isError && isForbidden(projects.error)) return <DirectAccess />;

  return (
    <>
      <PageHeader
        title="Projects"
        description="Pick a project, then an environment, to browse its secrets."
        actions={
          admin ? (
            <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
              New project
            </Button>
          ) : null
        }
      />

      {error && (
        <Alert severity="error" onClose={() => setError("")} sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}
      {projects.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errorMessage(projects.error, "The project list is unavailable.")}
        </Alert>
      )}

      {projects.isLoading ? (
        <LoadingRow label="Loading projects…" />
      ) : (projects.data ?? []).length === 0 ? (
        <Paper>
          <EmptyState
            title="No projects yet"
            hint={admin ? "Create one to get started." : "Ask an administrator to create one."}
          />
        </Paper>
      ) : (
        <Stack spacing={1.5}>
          {(projects.data ?? []).map((project) => (
            <Paper key={project.id} sx={{ p: 2.5 }}>
              <Stack
                direction={{ xs: "column", sm: "row" }}
                spacing={2}
                justifyContent="space-between"
                alignItems={{ sm: "center" }}
              >
                <Box sx={{ minWidth: 0 }}>
                  <Stack direction="row" spacing={1} alignItems="center">
                    <Typography sx={{ fontWeight: 600 }}>{project.id}</Typography>
                    <IconButton
                      size="small"
                      component={Link}
                      href={`/projects/${encodeURIComponent(project.id)}`}
                      aria-label={`Open ${project.id}`}
                    >
                      <ArrowIcon fontSize="small" />
                    </IconButton>
                  </Stack>
                  <Typography variant="body2" color="text.secondary">
                    {project.description || "No description"}
                  </Typography>
                </Box>

                <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                  {project.environments.map((environment) => (
                    <Link key={environment} href={secretsHref(project.id, environment)}>
                      <EnvironmentChip environment={environment} clickable />
                    </Link>
                  ))}
                  {admin && (
                    <Tooltip title="Delete project">
                      <IconButton
                        size="small"
                        color="error"
                        disabled={removeProject.isPending}
                        onClick={() => {
                          if (
                            window.confirm(
                              `Delete project "${project.id}"? Its secrets stay in OpenBao but the project entry and its policies are removed.`,
                            )
                          ) {
                            setError("");
                            removeProject.mutate(project.id);
                          }
                        }}
                      >
                        <DeleteIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  )}
                </Stack>
              </Stack>
            </Paper>
          ))}
        </Stack>
      )}

      <FormDialog
        open={creating}
        title="New project"
        submitLabel="Create project"
        disabled={!id.trim()}
        onClose={() => {
          setCreating(false);
          setId("");
          setDescription("");
        }}
        onSubmit={async () => {
          await createProject(id.trim(), description.trim());
          await queryClient.invalidateQueries({ queryKey: ["projects"] });
        }}
      >
        <TextField
          label="Project ID"
          value={id}
          onChange={(event) => setId(event.target.value)}
          helperText="Lowercase letters, digits and dashes. This becomes part of every secret path."
          autoFocus
          required
        />
        <TextField
          label="Description"
          value={description}
          onChange={(event) => setDescription(event.target.value)}
        />
        <Alert severity="info">
          Development, staging and production environments are created automatically.
        </Alert>
      </FormDialog>
    </>
  );
}

/** Fallback for members who cannot list projects: open a known path directly. */
function DirectAccess() {
  const router = useRouter();
  const [project, setProject] = useState("");
  const [environment, setEnvironment] = useState("development");
  const [path, setPath] = useState("");

  function open(event: FormEvent) {
    event.preventDefault();
    const suffix = path.split("/").filter(Boolean).map(encodeURIComponent).join("/");
    router.push(`${secretsHref(project.trim(), environment)}${suffix ? `/${suffix}` : ""}`);
  }

  return (
    <>
      <PageHeader
        title="Open a project"
        description="Your account cannot list every project, so enter the one you have access to."
      />
      <Paper sx={{ p: 3, maxWidth: 560 }}>
        <Stack component="form" onSubmit={open} spacing={2}>
          <TextField
            label="Project"
            value={project}
            onChange={(event) => setProject(event.target.value)}
            required
            autoFocus
          />
          <TextField
            label="Environment"
            select
            value={environment}
            onChange={(event) => setEnvironment(event.target.value)}
          >
            {["development", "staging", "production"].map((option) => (
              <MenuItem key={option} value={option}>
                {option}
              </MenuItem>
            ))}
          </TextField>
          <TextField
            label="Folder or secret path"
            value={path}
            onChange={(event) => setPath(event.target.value)}
            helperText="Optional. For example backend or services/api."
          />
          <Button type="submit" variant="contained" disabled={!project.trim()}>
            Open secrets
          </Button>
        </Stack>
      </Paper>
    </>
  );
}
