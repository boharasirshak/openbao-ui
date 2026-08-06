"use client";

import Link from "next/link";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Box,
  Button,
  IconButton,
  ListItemIcon,
  Menu,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/AddOutlined";
import KeyIcon from "@mui/icons-material/KeyOutlined";
import DeleteIcon from "@mui/icons-material/DeleteOutlineOutlined";
import ArrowIcon from "@mui/icons-material/ArrowForwardOutlined";
import MoreIcon from "@mui/icons-material/MoreVertOutlined";
import SettingsIcon from "@mui/icons-material/SettingsOutlined";
import { EmptyState, LoadingRow, PageHeader, isAdmin, useSession } from "@/components/AppShell";
import EnvironmentChip from "@/components/EnvironmentChip";
import FormDialog from "@/components/FormDialog";
import RequestAccessDialog from "@/components/RequestAccessDialog";
import { keys } from "@/lib/queryKeys";
import { createProject, deleteProject, errorMessage, listAdminProjects } from "@/lib/client";

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
  const [menu, setMenu] = useState<{ project: string; at: HTMLElement } | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null);
  const [requesting, setRequesting] = useState(false);
  const [notice, setNotice] = useState("");

  const projects = useQuery({ queryKey: keys.projects, queryFn: listAdminProjects, retry: false });

  const removeProject = useMutation({
    mutationFn: deleteProject,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.projects }),
    onError: (deleteError) =>
      setError(errorMessage(deleteError, "The project could not be deleted.")),
  });

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
          ) : (
            // The way in when the project you need is not on your list — or the list
            // is empty because you have no roles anywhere yet.
            <Button variant="outlined" startIcon={<KeyIcon />} onClick={() => setRequesting(true)}>
              Request access
            </Button>
          )
        }
      />

      <RequestAccessDialog
        open={requesting}
        onClose={() => setRequesting(false)}
        onSent={() => setNotice("Request sent. An administrator will review it.")}
      />

      {notice && (
        <Alert severity="success" sx={{ mb: 2 }} onClose={() => setNotice("")}>
          {notice}
        </Alert>
      )}

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
            hint={
              admin
                ? "Create one to get started."
                : "Request access to the project your team uses, or ask an administrator."
            }
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
                {/* The whole name is the link. It used to be plain text beside a small
                    arrow icon, so the obvious thing to click did nothing. */}
                <Box sx={{ minWidth: 0 }}>
                  <Typography
                    component={Link}
                    href={`/projects/${encodeURIComponent(project.id)}`}
                    sx={{
                      fontWeight: 600,
                      display: "inline-flex",
                      alignItems: "center",
                      gap: 0.5,
                      "&:hover": { color: "primary.main" },
                    }}
                  >
                    {project.id}
                    <ArrowIcon fontSize="small" />
                  </Typography>
                  <Typography variant="body2" color="text.secondary">
                    {project.description || "No description"}
                  </Typography>
                </Box>

                <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                  {project.environments.map((environment) => (
                    <Link key={environment.id} href={secretsHref(project.id, environment.id)}>
                      <EnvironmentChip environment={environment.id} clickable />
                    </Link>
                  ))}
                  {admin && (
                    <Tooltip title="Project options">
                      <IconButton
                        size="small"
                        aria-label={`Options for ${project.id}`}
                        onClick={(event) =>
                          setMenu({ project: project.id, at: event.currentTarget })
                        }
                      >
                        <MoreIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  )}
                </Stack>
              </Stack>
            </Paper>
          ))}
        </Stack>
      )}

      <Menu
        anchorEl={menu?.at ?? null}
        open={menu !== null}
        onClose={() => setMenu(null)}
        anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
        transformOrigin={{ vertical: "top", horizontal: "right" }}
      >
        <MenuItem
          component={Link}
          href={`/projects/${encodeURIComponent(menu?.project ?? "")}/settings`}
          onClick={() => setMenu(null)}
        >
          <ListItemIcon>
            <SettingsIcon fontSize="small" />
          </ListItemIcon>
          Settings
        </MenuItem>
        <MenuItem
          sx={{ color: "error.main" }}
          onClick={() => {
            setConfirmDelete(menu?.project ?? null);
            setMenu(null);
          }}
        >
          <ListItemIcon>
            <DeleteIcon fontSize="small" color="error" />
          </ListItemIcon>
          Delete project
        </MenuItem>
      </Menu>

      <DeleteProjectDialog
        project={confirmDelete}
        busy={removeProject.isPending}
        onClose={() => setConfirmDelete(null)}
        onConfirm={(name) => {
          setError("");
          removeProject.mutate(name);
        }}
      />

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
          await queryClient.invalidateQueries({ queryKey: keys.projects });
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

/**
 * Deleting a project used to be a browser confirm() behind a red bin icon sitting right
 * next to the environment links. Typing the name is slower on purpose.
 */
function DeleteProjectDialog({
  project,
  busy,
  onClose,
  onConfirm,
}: {
  project: string | null;
  busy: boolean;
  onClose: () => void;
  onConfirm: (project: string) => void;
}) {
  const [typed, setTyped] = useState("");

  return (
    <FormDialog
      open={project !== null}
      title={`Delete ${project ?? ""}?`}
      submitLabel="Delete project"
      disabled={typed !== project || busy}
      onClose={() => {
        setTyped("");
        onClose();
      }}
      onSubmit={() => {
        if (project) onConfirm(project);
      }}
    >
      <Alert severity="warning">
        This removes the project and the access rules that go with it. The secrets themselves stay
        in OpenBao.
      </Alert>
      <TextField
        label="Type the project name to confirm"
        value={typed}
        onChange={(event) => setTyped(event.target.value)}
        placeholder={project ?? ""}
        autoFocus
        fullWidth
      />
    </FormDialog>
  );
}
