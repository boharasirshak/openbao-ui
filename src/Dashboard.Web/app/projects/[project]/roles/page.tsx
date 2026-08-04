"use client";

import { useParams } from "next/navigation";
import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  Checkbox,
  Chip,
  FormControlLabel,
  IconButton,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/AddOutlined";
import DeleteIcon from "@mui/icons-material/DeleteOutlineOutlined";
import EditIcon from "@mui/icons-material/EditOutlined";
import {
  AccessDenied,
  EmptyState,
  LoadingRow,
  PageHeader,
  isAdmin,
  useSession,
} from "@/components/AppShell";
import EnvironmentChip from "@/components/EnvironmentChip";
import FormDialog from "@/components/FormDialog";
import { mono } from "@/lib/theme";
import {
  deleteAccessRole,
  errorMessage,
  listAccessRoles,
  listAdminProjects,
  saveAccessRole,
  type AccessRole,
  type RolePermissions,
} from "@/lib/client";
import { keys } from "@/lib/queryKeys";

const NONE: RolePermissions = {
  describe: false,
  readValues: false,
  writeSecrets: false,
  deleteSecrets: false,
  manageDetails: false,
  rollBack: false,
  destroy: false,
};

/** Order matters: the list reads from least to most dangerous. */
const PERMISSIONS: {
  key: keyof RolePermissions;
  label: string;
  hint: string;
  dangerous?: boolean;
}[] = [
  {
    key: "describe",
    label: "See that secrets exist",
    hint: "Names, tags, comments and version history — never a value.",
  },
  { key: "readValues", label: "See secret values", hint: "Read the values themselves." },
  { key: "writeSecrets", label: "Add and change secrets", hint: "Create keys and edit values." },
  { key: "deleteSecrets", label: "Delete secrets", hint: "Recoverable: older versions survive." },
  {
    key: "manageDetails",
    label: "Manage tags and retention",
    hint: "Edit annotations and history limits.",
  },
  { key: "rollBack", label: "Roll back and undelete", hint: "Restore an earlier version." },
  {
    key: "destroy",
    label: "Destroy permanently",
    hint: "Erases version data. Nothing can bring it back.",
    dangerous: true,
  },
];

const PRESETS: { label: string; permissions: RolePermissions }[] = [
  { label: "Auditor", permissions: { ...NONE, describe: true } },
  { label: "Viewer", permissions: { ...NONE, describe: true, readValues: true } },
  {
    label: "Editor",
    permissions: {
      describe: true,
      readValues: true,
      writeSecrets: true,
      deleteSecrets: true,
      manageDetails: true,
      rollBack: true,
      destroy: false,
    },
  },
];

export default function ProjectRolesPage() {
  const project = String(useParams<{ project: string }>().project);
  const session = useSession();
  const queryClient = useQueryClient();

  const roles = useQuery({
    queryKey: keys.accessRoles(project),
    queryFn: () => listAccessRoles(project),
  });
  const projects = useQuery({ queryKey: keys.projects, queryFn: listAdminProjects, retry: false });
  const environments = (projects.data?.find((p) => p.id === project)?.environments ?? []).map(
    (environment) => environment.id,
  );

  const [error, setError] = useState("");
  const [editing, setEditing] = useState<AccessRole | null>(null);
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [chosenEnvironments, setChosenEnvironments] = useState<string[]>([]);
  const [permissions, setPermissions] = useState<RolePermissions>(NONE);

  if (!isAdmin(session)) return <AccessDenied what="project roles" />;

  const refresh = () => queryClient.invalidateQueries({ queryKey: keys.accessRoles(project) });
  const grantsNothing = PERMISSIONS.every((entry) => !permissions[entry.key]);

  function open(role: AccessRole | null) {
    setError("");
    if (role) {
      setEditing(role);
      setName(role.name);
      setDescription(role.description ?? "");
      setChosenEnvironments(role.environments);
      setPermissions(role.permissions);
    } else {
      setCreating(true);
      setName("");
      setDescription("");
      setChosenEnvironments(environments.slice(0, 1));
      setPermissions({ ...NONE, describe: true, readValues: true });
    }
  }

  return (
    <>
      <PageHeader
        title={`${project} roles`}
        description="A role is a set of environments plus what may be done in them. The matching OpenBao policy is generated for you."
        actions={
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => open(null)}>
            New role
          </Button>
        }
      />

      {error && (
        <Alert severity="error" onClose={() => setError("")} sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}

      <Paper sx={{ overflow: "auto" }}>
        {roles.isLoading ? (
          <LoadingRow label="Loading roles…" />
        ) : (roles.data ?? []).length === 0 ? (
          <EmptyState
            title="No custom roles yet"
            hint="Start with an auditor who can see what exists but never a value."
          />
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Role</TableCell>
                <TableCell>Environments</TableCell>
                <TableCell>Grants</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(roles.data ?? []).map((role) => (
                <TableRow key={role.name} hover>
                  <TableCell>
                    <Typography sx={{ fontFamily: mono, fontSize: 13 }}>{role.name}</Typography>
                    {role.description && (
                      <Typography variant="caption" color="text.secondary">
                        {role.description}
                      </Typography>
                    )}
                  </TableCell>
                  <TableCell>
                    <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
                      {role.environments.map((environment) => (
                        <EnvironmentChip key={environment} environment={environment} />
                      ))}
                    </Stack>
                  </TableCell>
                  <TableCell>
                    <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
                      {PERMISSIONS.filter((entry) => role.permissions[entry.key]).map((entry) => (
                        <Chip
                          key={entry.key}
                          size="small"
                          variant="outlined"
                          color={entry.dangerous ? "error" : "default"}
                          label={entry.label}
                        />
                      ))}
                    </Stack>
                  </TableCell>
                  <TableCell align="right">
                    <Stack direction="row" justifyContent="flex-end">
                      <Tooltip title="Edit role">
                        <IconButton size="small" onClick={() => open(role)}>
                          <EditIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                      <Tooltip title="Delete role">
                        <IconButton
                          size="small"
                          color="error"
                          onClick={async () => {
                            if (
                              !window.confirm(
                                `Delete "${role.name}"? Anyone holding it loses that access.`,
                              )
                            ) {
                              return;
                            }
                            setError("");
                            try {
                              await deleteAccessRole(project, role.name);
                              await refresh();
                            } catch (deleteError) {
                              setError(errorMessage(deleteError, "The role could not be deleted."));
                            }
                          }}
                        >
                          <DeleteIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                    </Stack>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Paper>

      <FormDialog
        open={creating || editing !== null}
        title={editing ? `Edit ${editing.name}` : "New role"}
        submitLabel="Save role"
        disabled={!name.trim() || chosenEnvironments.length === 0 || grantsNothing}
        onClose={() => {
          setCreating(false);
          setEditing(null);
        }}
        onSubmit={async () => {
          await saveAccessRole(project, name.trim(), {
            environments: chosenEnvironments,
            permissions,
            description: description.trim() || undefined,
          });
          await refresh();
        }}
      >
        <TextField
          label="Role name"
          value={name}
          onChange={(event) => setName(event.target.value)}
          disabled={editing !== null}
          helperText={
            editing
              ? "The name is part of the policy, so it cannot change. Delete and recreate to rename."
              : "Letters, digits, dashes and underscores."
          }
          autoFocus={!editing}
          required
        />
        <TextField
          label="Description"
          value={description}
          onChange={(event) => setDescription(event.target.value)}
        />
        <Autocomplete
          multiple
          options={environments}
          value={chosenEnvironments}
          onChange={(_, next) => setChosenEnvironments(next)}
          renderValue={(value, getItemProps) =>
            value.map((environment, index) => (
              <Chip
                size="small"
                label={environment}
                {...getItemProps({ index })}
                key={environment}
              />
            ))
          }
          renderInput={(params) => (
            <TextField {...params} label="Environments" helperText="Where this role applies." />
          )}
        />

        <Stack direction="row" spacing={1} alignItems="center">
          <Typography variant="caption" color="text.secondary">
            Start from:
          </Typography>
          {PRESETS.map((preset) => (
            <Button
              key={preset.label}
              size="small"
              variant="outlined"
              onClick={() => setPermissions(preset.permissions)}
            >
              {preset.label}
            </Button>
          ))}
        </Stack>

        <Box>
          {PERMISSIONS.map((entry) => (
            <FormControlLabel
              key={entry.key}
              sx={{ display: "flex", alignItems: "flex-start", mb: 0.5 }}
              control={
                <Checkbox
                  size="small"
                  checked={permissions[entry.key]}
                  color={entry.dangerous ? "error" : "primary"}
                  onChange={(event) =>
                    setPermissions((current) => ({
                      ...current,
                      [entry.key]: event.target.checked,
                      // Seeing a value implies seeing that the secret exists; without it
                      // the UI could not list the secret it is about to show you.
                      ...(entry.key === "readValues" && event.target.checked
                        ? { describe: true }
                        : {}),
                    }))
                  }
                />
              }
              label={
                <Box>
                  <Typography variant="body2">{entry.label}</Typography>
                  <Typography variant="caption" color="text.secondary">
                    {entry.hint}
                  </Typography>
                </Box>
              }
            />
          ))}
        </Box>

        {grantsNothing && (
          <Alert severity="warning">Pick at least one thing this role can do.</Alert>
        )}
      </FormDialog>
    </>
  );
}
